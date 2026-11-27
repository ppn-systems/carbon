// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World.Needs;
using Carbon.Application.World.Players;
using Carbon.Application.World.StatusEffects;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Carbon.Survival.Components;

namespace Carbon.Application.World.Persistence;

/// <summary>
/// Character persistence for the world (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>): turns live player
/// state into <see cref="CharacterRecord"/>s, keeps the records waiting for the next save (dirty) and the ones handed to
/// the store whose save has not committed yet (in flight), and reads a record's inventory and progression back on join.
/// Called only under the world lock.
/// </summary>
internal sealed class CharacterPersistence
{
    private readonly WorldSimulation _simulation;
    private readonly StatusEffectService _statusEffects;
    private readonly NeedsService _needs;
    private readonly TimeProvider _time;
    private readonly Dictionary<Guid, CharacterRecord> _dirty = new();

    /// <summary>
    /// Records handed to <see cref="ICharacterStore.SaveBatchAsync"/> whose save has not committed yet. They stay visible
    /// to <see cref="TryGetPending"/> so a re-select during the save never loads the stale row.
    /// </summary>
    private readonly Dictionary<Guid, CharacterRecord> _inFlight = new();

    /// <summary>Initializes a new instance of the <see cref="CharacterPersistence"/> class.</summary>
    public CharacterPersistence(WorldSimulation simulation, NeedsService needs, StatusEffectService statusEffects, TimeProvider time)
    {
        _statusEffects = statusEffects ?? throw new ArgumentNullException(nameof(statusEffects));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _needs = needs ?? throw new ArgumentNullException(nameof(needs));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Loads the record's inventory into <paramref name="inventory"/>; a record without a readable inventory gets the
    /// starter loadout of its class (nothing for a classless character, ADR-030) and is returned flagged
    /// <see cref="CharacterTraits.StarterLoadoutGranted"/>.
    /// </summary>
    public static CharacterRecord LoadInventory(CharacterRecord character, InventoryContainer inventory)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (InventoryBinaryCodec.TryDeserializeInto(character.InventoryBlob, inventory))
        {
            return character;
        }

        if (character.Class == HeroArchetypeClass.None)
        {
            inventory.Clear();
        }
        else
        {
            StarterGameCatalog.GrantStarterLoadout(inventory, (byte)character.Class);
        }

        return character with { Flags = character.Flags | CharacterTraits.StarterLoadoutGranted };
    }

    /// <summary>
    /// Reads the record's progression (empty when unreadable); skills unlocked only in the legacy
    /// <see cref="CharacterRecord.UnlockedSkills"/> mask are carried over.
    /// </summary>
    public static CharacterProgressionState LoadProgression(CharacterRecord character)
    {
        ArgumentNullException.ThrowIfNull(character);
        CharacterProgressionState progression = new();
        if (!CharacterProgressionBinaryCodec.TryDeserializeInto(character.ProgressionBlob ?? [], progression))
        {
            progression.Clear();
        }

        if (character.UnlockedSkills != CharacterSkillMask.None && progression.UnlockedSkillMask == CharacterSkillMask.None)
        {
            progression.SetUnlockedSkillMask(character.UnlockedSkills);
        }

        return progression;
    }

    /// <summary>
    /// Flags the player for saving and, while its entity is in the world, snapshots its record now (serializing the
    /// inventory when it changed since the last snapshot).
    /// </summary>
    public void MarkDirty(PlayerState state, bool inventoryChanged)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (inventoryChanged)
        {
            state.InventoryDirty = true;
        }

        state.PersistenceDirty = true;
        if (!_simulation.Entities.IsAlive(state.Entity) || !_simulation.Entities.HasTransform(state.Entity))
        {
            return;
        }

        this.Snapshot(state, state.InventoryDirty);
    }

    /// <summary>Builds the player's current record (position, health, stats, inventory, progression, wallet, needs).</summary>
    public CharacterRecord BuildRecord(PlayerState state, bool serializeInventory)
    {
        ArgumentNullException.ThrowIfNull(state);
        Vector2 pos = _simulation.Entities.IsAlive(state.Entity) && _simulation.Entities.HasTransform(state.Entity)
            ? _simulation.Entities.GetTransform(state.Entity).Position
            : new Vector2(state.SourceCharacter.PositionX, state.SourceCharacter.PositionY);

        HealthComponent health = _simulation.Entities.IsAlive(state.Entity) && _simulation.Entities.HasHealth(state.Entity)
            ? _simulation.Entities.GetHealth(state.Entity)
            : new HealthComponent(state.SourceCharacter.CurrentHealth, state.SourceCharacter.MaxHealth);

        byte[] blob = serializeInventory
            ? InventoryBinaryCodec.Serialize(state.Inventory)
            : state.SourceCharacter.InventoryBlob;

        NeedsComponent needs = _needs.GetForPersistence(state.Entity);
        return state.SourceCharacter with
        {
            Level = state.Level,
            Experience = state.Experience,
            PositionX = pos.X,
            PositionY = pos.Y,
            CurrentHealth = health.Current,
            MaxHealth = health.Max,
            CurrentMana = state.CurrentMana,
            MaxMana = state.Stats.MaxMana,
            CurrentStamina = state.CurrentStamina,
            MaxStamina = state.Stats.MaxStamina,
            SelectedHotbarSlot = state.Session.SelectedHotbarSlot,
            InventoryBlob = blob,
            UpdatedAtUtc = _time.GetUtcNow(),
            UnlockedSkills = state.Progression.UnlockedSkillMask,
            ProgressionBlob = CharacterProgressionBinaryCodec.Serialize(state.Progression),
            Gold = state.Wallet.Gold,
            Diamonds = state.Wallet.Diamonds,
            WalletSequence = state.Wallet.Sequence,
            DiamondPity = state.LootPity.KillsSinceDiamond,
            NeedsFood = needs.Food,
            NeedsMalnutrition = needs.Malnutrition,
            NeedsPoisonSeconds = needs.PoisonRemainingSeconds,
            NeedsRest = needs.Rest,
            StatusEffects = _statusEffects.Serialize(state.Entity),
        };
    }

    /// <summary>
    /// Snapshots every player that is flagged, changed its inventory or moved at least 4px since its last save, then moves
    /// all dirty records into <paramref name="batch"/> and marks them in flight.
    /// </summary>
    public void CollectBatch(Dictionary<ulong, PlayerState>.ValueCollection players, List<CharacterRecord> batch)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(batch);
        foreach (PlayerState state in players)
        {
            bool movedSinceSave = _simulation.Entities.IsAlive(state.Entity) &&
                _simulation.Entities.HasTransform(state.Entity) &&
                Vector2.DistanceSquared(_simulation.Entities.GetTransform(state.Entity).Position, state.LastSavedPosition) >= 16f;

            if (state.PersistenceDirty || state.InventoryDirty || movedSinceSave)
            {
                this.Snapshot(state, state.InventoryDirty);
                state.PersistenceDirty = false;
            }
        }

        if (_dirty.Count > 0)
        {
            foreach (KeyValuePair<Guid, CharacterRecord> kvp in _dirty)
            {
                batch.Add(kvp.Value);
                _inFlight[kvp.Key] = kvp.Value;
            }

            _dirty.Clear();
        }
    }

    /// <summary>Drops the in-flight entries of a finished save. An entry replaced by a newer flush meanwhile is kept.</summary>
    public void Release(List<CharacterRecord> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        for (int i = 0; i < batch.Count; i++)
        {
            CharacterRecord saved = batch[i];
            if (_inFlight.TryGetValue(saved.Id, out CharacterRecord? current) && ReferenceEquals(current, saved))
            {
                _ = _inFlight.Remove(saved.Id);
            }
        }
    }

    /// <summary>A save failed: queues its records again (unless a newer snapshot is already waiting) and releases them.</summary>
    public void Requeue(List<CharacterRecord> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        for (int i = 0; i < batch.Count; i++)
        {
            CharacterRecord failed = batch[i];
            _ = _dirty.TryAdd(failed.Id, failed);
        }

        this.Release(batch);
    }

    /// <summary>
    /// Gets the newest unsaved state of a character: built live when the character is in the world, otherwise the record
    /// waiting for the next save, otherwise the one being saved.
    /// </summary>
    public bool TryGetPending(Dictionary<ulong, PlayerState>.ValueCollection players, Guid characterId, out CharacterRecord? character)
    {
        ArgumentNullException.ThrowIfNull(players);
        foreach (PlayerState state in players)
        {
            if (state.SourceCharacter.Id == characterId)
            {
                character = this.BuildRecord(state, serializeInventory: true);
                return true;
            }
        }

        if (_dirty.TryGetValue(characterId, out CharacterRecord? cached))
        {
            character = cached;
            return true;
        }

        if (_inFlight.TryGetValue(characterId, out CharacterRecord? inFlight))
        {
            character = inFlight;
            return true;
        }

        character = null;
        return false;
    }

    private void Snapshot(PlayerState state, bool serializeInventory)
    {
        CharacterRecord updated = this.BuildRecord(state, serializeInventory);
        state.SourceCharacter = updated;
        state.LastSavedPosition = new Vector2(updated.PositionX, updated.PositionY);
        state.InventoryDirty = false;
        _dirty[updated.Id] = updated;
    }
}
