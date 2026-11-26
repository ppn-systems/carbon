// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Combat.Events;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS;
using Carbon.Survival.Components;
using Carbon.Survival.Events;
using Carbon.Survival.Food;
using Carbon.Survival.Systems;

namespace Carbon.Application.World;

/// <summary>
/// Survival needs integration: feeds player activity into <see cref="NeedsSystem"/>, starts timed meals for
/// edible consumables, consumes the item when <see cref="EatActionSystem"/> completes the meal, applies the
/// needs' speed/regen effects, and routes starvation death through the normal respawn path.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    private const float FightingWindowSeconds = 6f;
    private const float RespawnFoodFraction = 0.5f;
    private const float SpoilageStepSeconds = 1f;

    private void PrepareNeedsForTickLocked(ActivePlayerState state, bool isSprinting)
    {
        if (!_simulation.Entities.HasComponent<NeedsComponent>(state.Entity))
        {
            return;
        }

        bool isMoving = _simulation.Entities.HasVelocity(state.Entity) &&
            _simulation.Entities.GetVelocity(state.Entity).Linear.LengthSquared() > 1f;
        bool isFighting = state.LastCombatDamageTick != 0UL &&
            (_currentTick - state.LastCombatDamageTick) < (ulong)(this.TickRate * FightingWindowSeconds);

        // A sleeper whose bed was destroyed or deconstructed wakes up.
        if (state.RestMode is RestMode.Sleeping or RestMode.SleepingComfortable &&
            !this.IsIntactBedLocked(state.RestBedTileX, state.RestBedTileY))
        {
            this.WakeLocked(state);
        }

        NeedsActivity activity = state.RestMode switch
        {
            RestMode.Sitting => NeedsActivity.Resting,
            RestMode.Sleeping or RestMode.SleepingComfortable => NeedsActivity.Sleeping,
            RestMode.None => isFighting ? NeedsActivity.Fighting
                : isSprinting ? NeedsActivity.Sprinting
                : isMoving ? NeedsActivity.Walking
                : NeedsActivity.Idle,
            _ => NeedsActivity.Idle,
        };
        ref NeedsComponent current = ref _simulation.Entities.GetComponent<NeedsComponent>(state.Entity);
        current.Activity = activity;
        current.SleepComfort = state.RestMode == RestMode.SleepingComfortable;

        // Cancel the meal if the food left its reserved slot (dropped, moved, traded) so nothing is eaten for free.
        if (state.EatingSlot >= 0 && EatActionSystem.IsEating(_simulation, state.Entity))
        {
            ItemSlot reserved = state.Inventory.GetSlot(state.EatingSlot);
            if (reserved.IsEmpty || reserved.ItemId != state.EatingItemId)
            {
                _ = EatActionSystem.CancelEat(_simulation, state.Entity);
            }
        }
    }

    /// <summary>
    /// Starts a meal when the active slot holds a food from <c>food.json</c>. The item code is resolved from the
    /// built-in catalog first, then from the data-driven economy catalog (<c>items.json</c>).
    /// </summary>
    private bool TryBeginEatingActiveSlotLocked(ActivePlayerState state, in ItemSlot activeSlot)
    {
        if (activeSlot.IsEmpty || activeSlot.Quantity <= 0)
        {
            return false;
        }

        string? code = this.ResolveItemCode(activeSlot.ItemId);
        if (code is null || !_foodCatalog.TryGetIndex(code, out _))
        {
            return false;
        }

        OpStatus status = _eatAction.BeginEat(_simulation, state.Entity, code);
        if (status == OpStatus.Ok)
        {
            state.EatingSlot = state.Session.SelectedHotbarSlot;
            state.EatingItemId = activeSlot.ItemId;
            state.StatsDirty = true;
        }

        // Edible items are always handled here (Conflict = already eating or too full) and never fall back to an
        // instant potion use.
        return true;
    }

    /// <summary>
    /// Ages perishable food in every online player's inventory (ADR-016). Stacks that are first seen are stamped with
    /// the food's spoil time; spoiled stacks turn into their <c>spoilsInto</c> item. Only stamps and spoils are sent to
    /// the client (it counts down locally between them), so the steady state sends no packets.
    /// </summary>
    private void AdvanceFoodSpoilageLocked(float elapsedSeconds)
    {
        foreach (KeyValuePair<ulong, ActivePlayerState> kvp in _playersByConnection)
        {
            ActivePlayerState state = kvp.Value;
            InventoryContainer inventory = state.Inventory;
            bool changed = false;
            for (int i = 0; i < inventory.Capacity; i++)
            {
                ItemSlot slot = inventory.GetSlot(i);
                if (slot.IsEmpty)
                {
                    continue;
                }

                string? code = this.ResolveItemCode(slot.ItemId);
                if (code is null || !_foodCatalog.TryGetIndex(code, out int foodIndex))
                {
                    continue;
                }

                SpoilageStep step = FoodSpoilage.Advance(in slot, _foodCatalog.Get(foodIndex), elapsedSeconds, _spoiledItemIdByFood[foodIndex]);
                if (step.Outcome == SpoilageOutcome.Unchanged)
                {
                    continue;
                }

                inventory.SetSlot(i, step.Slot);
                if (step.Outcome is SpoilageOutcome.Stamped or SpoilageOutcome.Spoiled)
                {
                    _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, (ushort)i, step.Slot));
                    changed = true;
                }
            }

            if (changed)
            {
                this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            }
        }
    }

    /// <summary>Resolves an item id to its code from the built-in catalog, then the data-driven economy catalog.</summary>
    private string? ResolveItemCode(uint itemId) =>
        StarterGameCatalog.Items.TryGet(itemId, out ItemArchetype? builtIn)
            ? builtIn.Code
            : _economy is not null && _economy.Items.TryGet(itemId, out ItemCatalogEntry? entry) ? entry.Code : null;

    /// <summary>Maps every food index to the item id it spoils into (0 = removed), resolved once at construction.</summary>
    private uint[] ResolveSpoiledItemIds()
    {
        uint[] ids = new uint[_foodCatalog.Count];
        for (int i = 0; i < ids.Length; i++)
        {
            string target = _foodCatalog.Get(i).SpoilsInto;
            if (target.Length == 0)
            {
                continue;
            }

            if (_economy is not null && _economy.Items.TryGetByCode(target, out ItemCatalogEntry? entry))
            {
                ids[i] = entry.Id;
                continue;
            }

            ReadOnlySpan<ItemArchetype> builtIn = StarterGameCatalog.Items.All;
            for (int b = 0; b < builtIn.Length; b++)
            {
                if (string.Equals(builtIn[b].Code, target, StringComparison.Ordinal))
                {
                    ids[i] = builtIn[b].Id;
                    break;
                }
            }
        }

        return ids;
    }

    private void ProcessSurvivalEventsLocked()
    {
        ReadOnlySpan<MealCompletedEvent> meals = _simulation.ReadEvents<MealCompletedEvent>();
        for (int i = 0; i < meals.Length; i++)
        {
            if (this.TryGetPlayerByEntityLocked(meals[i].Entity, out ActivePlayerState? state))
            {
                this.ConsumeEatenItemLocked(state);
            }
        }

        ReadOnlySpan<MealInterruptedEvent> interrupts = _simulation.ReadEvents<MealInterruptedEvent>();
        for (int i = 0; i < interrupts.Length; i++)
        {
            if (this.TryGetPlayerByEntityLocked(interrupts[i].Entity, out ActivePlayerState? state))
            {
                state.EatingSlot = -1;
                state.StatsDirty = true;
            }
        }

        ReadOnlySpan<NeedStateChangedEvent> changes = _simulation.ReadEvents<NeedStateChangedEvent>();
        for (int i = 0; i < changes.Length; i++)
        {
            if (this.TryGetPlayerByEntityLocked(changes[i].Entity, out ActivePlayerState? state))
            {
                state.StatsDirty = true;
                this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            }
        }

        // NeedsSystem publishes starvation pulses as self-inflicted DamageEvent / EntityKilledEvent.
        ReadOnlySpan<DamageEvent> damage = _simulation.ReadEvents<DamageEvent>();
        for (int i = 0; i < damage.Length; i++)
        {
            ref readonly DamageEvent hit = ref damage[i];
            if (hit.Attacker == hit.Target && this.TryGetPlayerByEntityLocked(hit.Target, out ActivePlayerState? state))
            {
                state.StatsDirty = true;
                this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            }
        }

        ReadOnlySpan<EntityKilledEvent> kills = _simulation.ReadEvents<EntityKilledEvent>();
        for (int i = 0; i < kills.Length; i++)
        {
            ref readonly EntityKilledEvent kill = ref kills[i];
            if (kill.Killer == kill.Victim &&
                this.TryGetPlayerByEntityLocked(kill.Victim, out ActivePlayerState? state) &&
                _simulation.Entities.HasTransform(state.Entity))
            {
                // Route starvation death through the standard death/respawn path (health is already 0).
                this.ApplyDamageToPlayerLocked(
                    null,
                    state.Session.PlayerNetworkId,
                    state.Entity,
                    state.Session.PlayerNetworkId,
                    _simulation.Entities.GetTransform(state.Entity).Position,
                    1f);
                this.ResetNeedsAfterDeathLocked(state);
                state.CurrentMana = state.Stats.MaxMana;
                state.CurrentStamina = state.Stats.MaxStamina;
                state.StatsDirty = true;
                this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            }
        }

        _simulation.ClearEvents<MealCompletedEvent>();
        _simulation.ClearEvents<MealInterruptedEvent>();
        _simulation.ClearEvents<NeedStateChangedEvent>();
        _simulation.ClearEvents<DamageEvent>();
        _simulation.ClearEvents<EntityKilledEvent>();
    }

    private void ConsumeEatenItemLocked(ActivePlayerState state)
    {
        int slotIndex = state.EatingSlot;
        state.EatingSlot = -1;
        state.StatsDirty = true;
        if (slotIndex < 0)
        {
            return;
        }

        ItemSlot slot = state.Inventory.GetSlot(slotIndex);
        if (slot.IsEmpty || slot.ItemId != state.EatingItemId)
        {
            return;
        }

        if (StarterGameCatalog.Items.TryGet(slot.ItemId, out ItemArchetype? def))
        {
            _ = this.ApplyConsumableRestoresLocked(state, def);
        }

        ItemSlot updated = slot.Quantity > 1 ? slot with { Quantity = slot.Quantity - 1 } : ItemSlot.Empty;
        state.Inventory.SetSlot(slotIndex, in updated);
        _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, (ushort)slotIndex, updated));
        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
    }

    private void InterruptMealOnDamageLocked(ActivePlayerState victim)
    {
        _ = EatActionSystem.CancelEat(_simulation, victim.Entity, MealInterruptReason.Damaged);
        this.WakeLocked(victim);
    }

    /// <inheritdoc/>
    public OpStatus TrySetResting(ulong connectionId, bool rest)
    {
        lock (_gate)
        {
            if (!_playersByConnection.TryGetValue(connectionId, out ActivePlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity) ||
                (_simulation.Entities.HasHealth(state.Entity) && _simulation.Entities.GetHealth(state.Entity).IsDead))
            {
                return OpStatus.Forbidden;
            }

            if (!rest)
            {
                this.WakeLocked(state);
                return OpStatus.Ok;
            }

            Vector2 pos = _simulation.Entities.GetTransform(state.Entity).Position;
            if (this.TryFindBedLocked(pos, out int bedX, out int bedY))
            {
                state.RestMode = _chunkWorld.Buildings.IsRoofedAt(bedX, bedY) ? RestMode.SleepingComfortable : RestMode.Sleeping;
                state.RestBedTileX = bedX;
                state.RestBedTileY = bedY;
            }
            else
            {
                state.RestMode = RestMode.Sitting;
            }

            if (_simulation.Entities.HasVelocity(state.Entity))
            {
                _simulation.Entities.SetVelocity(state.Entity, new Velocity2D(0f, 0f, 5f));
            }

            state.StatsDirty = true;
            return OpStatus.Ok;
        }
    }

    /// <summary>Stands a resting player up (no-op when awake).</summary>
    private void WakeLocked(ActivePlayerState state)
    {
        if (state.RestMode == RestMode.None)
        {
            return;
        }

        state.RestMode = RestMode.None;
        state.StatsDirty = true;
    }

    /// <summary>Finds an intact wood bed within <see cref="Carbon.Survival.Config.NeedsConfig.BedReachTiles"/> of <paramref name="pos"/>.</summary>
    private bool TryFindBedLocked(Vector2 pos, out int bedX, out int bedY)
    {
        float reach = _needsConfig.BedReachTiles * this.TileSize;
        int radius = (int)MathF.Ceiling(_needsConfig.BedReachTiles);
        int ptx = (int)MathF.Floor(pos.X / this.TileSize);
        int pty = (int)MathF.Floor(pos.Y / this.TileSize);
        float best = float.MaxValue;
        bedX = 0;
        bedY = 0;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int tx = ptx + dx;
                int ty = pty + dy;
                if (!this.IsIntactBedLocked(tx, ty))
                {
                    continue;
                }

                Vector2 center = new((tx + 0.5f) * this.TileSize, (ty + 0.5f) * this.TileSize);
                float dist = Vector2.Distance(center, pos);
                if (dist <= reach && dist < best)
                {
                    best = dist;
                    bedX = tx;
                    bedY = ty;
                }
            }
        }

        return best < float.MaxValue;
    }

    private bool IsIntactBedLocked(int tileX, int tileY) =>
        _chunkWorld.Buildings.TryGetStructure(tileX, tileY, out StructureTile tile) &&
        tile.Type == StructureType.WoodBed &&
        !tile.IsDestroyed;

    private void ResetNeedsAfterDeathLocked(ActivePlayerState state)
    {
        _ = EatActionSystem.CancelEat(_simulation, state.Entity, MealInterruptReason.Died);
        state.EatingSlot = -1;
        this.WakeLocked(state);
        if (!_simulation.Entities.HasComponent<NeedsComponent>(state.Entity))
        {
            return;
        }

        ref NeedsComponent needs = ref _simulation.Entities.GetComponent<NeedsComponent>(state.Entity);
        NeedsComponent fresh = NeedsComponent.CreateFull(_needsConfig);
        fresh.Food = MathF.Max(needs.Food, needs.FoodMax * RespawnFoodFraction);
        fresh.Hunger = _needsConfig.ClassifyHunger(fresh.FoodFraction);
        fresh.Activity = needs.Activity;
        needs = fresh;
    }

    /// <summary>
    /// Rebuilds a player's needs from the persisted record (ADR-015). A negative <c>NeedsFood</c> means the needs were
    /// never saved, so the character starts fully fed; stored values are clamped to the current config ranges.
    /// </summary>
    private NeedsComponent RestoreNeedsLocked(CharacterRecord character)
    {
        NeedsComponent needs = NeedsComponent.CreateFull(_needsConfig);
        if (_needsConfig.RestEnabled && float.IsFinite(character.NeedsRest) && character.NeedsRest >= 0f)
        {
            needs.Rest = Math.Clamp(character.NeedsRest, 0f, needs.RestMax);
            needs.Fatigue = _needsConfig.ClassifyRest(needs.RestFraction);
        }

        if (!float.IsFinite(character.NeedsFood) || character.NeedsFood < 0f)
        {
            return needs;
        }

        needs.Food = Math.Clamp(character.NeedsFood, 0f, needs.FoodMax);
        needs.Malnutrition = float.IsFinite(character.NeedsMalnutrition) ? Math.Clamp(character.NeedsMalnutrition, 0f, 1f) : 0f;
        needs.PoisonRemainingSeconds = float.IsFinite(character.NeedsPoisonSeconds)
            ? Math.Clamp(character.NeedsPoisonSeconds, 0f, _needsConfig.PoisonDurationSeconds)
            : 0f;
        needs.Hunger = _needsConfig.ClassifyHunger(needs.FoodFraction);
        return needs;
    }

    private NeedsComponent GetNeedsForPersistenceLocked(ActivePlayerState state) =>
        _simulation.Entities.TryGetComponent(state.Entity, out NeedsComponent needs)
            ? needs
            : NeedsComponent.CreateFull(_needsConfig);

    private float GetNeedsMoveScaleLocked(ActivePlayerState state) =>
        _simulation.Entities.TryGetComponent(state.Entity, out NeedsComponent needs) ? needs.MoveSpeedScale : 1f;

    private bool IsHealthRegenBlockedLocked(ActivePlayerState state) =>
        _simulation.Entities.TryGetComponent(state.Entity, out NeedsComponent needs) && needs.HealthRegenBlocked;

    private uint ScaleAttackCooldownLocked(ActivePlayerState state, uint baseTicks)
    {
        if (!_simulation.Entities.TryGetComponent(state.Entity, out NeedsComponent needs) || needs.AttackSpeedScale >= 1f)
        {
            return baseTicks;
        }

        return (uint)MathF.Ceiling(baseTicks / MathF.Max(0.1f, needs.AttackSpeedScale));
    }

    private PlayerNeedsSnapshot BuildNeedsSnapshotLocked(ActivePlayerState state)
    {
        if (!_simulation.Entities.TryGetComponent(state.Entity, out NeedsComponent needs))
        {
            return default;
        }

        float eatProgress = _simulation.Entities.TryGetComponent(state.Entity, out EatingComponent meal) ? meal.Progress : -1f;
        return new PlayerNeedsSnapshot(
            needs.Food,
            needs.FoodMax,
            (byte)needs.Hunger,
            needs.Malnutrition,
            needs.Mood,
            needs.IsPoisoned,
            eatProgress,
            needs.Rest,
            needs.RestMax,
            (byte)needs.Fatigue,
            state.RestMode);
    }

    private bool TryGetPlayerByEntityLocked(Entity entity, [NotNullWhen(true)] out ActivePlayerState? state)
    {
        if (_simulation.TryGetNetworkIdentity(entity, out NetworkIdentity identity) &&
            _connectionByNetworkId.TryGetValue(identity.NetworkId, out ulong connectionId) &&
            _playersByConnection.TryGetValue(connectionId, out state) &&
            state.Entity == entity)
        {
            return true;
        }

        state = null;
        return false;
    }
}
