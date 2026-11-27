// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Economy;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Building;
using Carbon.Primitives.Random;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using Carbon.Survival.Components;
using Carbon.Survival.Config;
using Carbon.Survival.Food;
using Carbon.Survival.Systems;

namespace Carbon.Application.World.Needs;

/// <summary>
/// Owns the survival-needs tuning and the entity-level needs rules (audit SOLID-1, extracted from
/// <see cref="AuthoritativeWorld"/>): the needs and meal systems, food lookup and spoilage, needs restore/reset/persist,
/// the needs' gameplay effects and snapshots, and bed lookup for resting. Player-session glue (events, commands,
/// replication) stays in the world. Called only from inside the world lock.
/// </summary>
internal sealed class NeedsService
{
    /// <summary>Share of max food a player respawns with at least.</summary>
    public const float RespawnFoodFraction = 0.5f;

    private readonly WorldSimulation _simulation;
    private readonly BuildingGrid _buildings;
    private readonly float _tileSize;
    private readonly Func<ItemRules> _itemRules;
    private readonly Func<GameEconomy?> _economy;
    private readonly EatActionSystem _eatAction;
    private readonly NeedsSystem _needsSystem;
    private uint[] _spoiledItemIdByFood;

    /// <summary>Initializes a new instance of the <see cref="NeedsService"/> class and registers its systems.</summary>
    public NeedsService(
        WorldSimulation simulation,
        BuildingGrid buildings,
        float tileSize,
        Func<ItemRules> itemRules,
        Func<GameEconomy?> economy,
        NeedsConfig? config,
        FoodCatalog? foodCatalog,
        IGameplayEntropy entropy)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
        _tileSize = tileSize;
        _itemRules = itemRules ?? throw new ArgumentNullException(nameof(itemRules));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        this.Config = config ?? new NeedsConfig();
        this.Food = foodCatalog ?? new FoodCatalog([], this.Config.FoodMax);
        _eatAction = new EatActionSystem(this.Food, this.Config, entropy);
        _spoiledItemIdByFood = this.ResolveSpoiledItemIds();
        _simulation.RegisterSystem(_eatAction);
        _needsSystem = new NeedsSystem(this.Config);
        _simulation.RegisterSystem(_needsSystem);
    }

    /// <summary>Gets the needs tuning in effect.</summary>
    public NeedsConfig Config { get; private set; }

    /// <summary>Gets the food catalog in effect.</summary>
    public FoodCatalog Food { get; private set; }

    /// <summary>
    /// Switches to new tuning and food (ADR-014 hot reload); meals in progress are remapped or cancelled. Call after the
    /// world has switched its item rules / economy, since spoil targets are resolved against them.
    /// </summary>
    public void Reconfigure(NeedsConfig config, FoodCatalog foodCatalog)
    {
        _eatAction.Reconfigure(_simulation, foodCatalog, config);
        _needsSystem.Reconfigure(config);
        this.Config = config;
        this.Food = foodCatalog;
        _spoiledItemIdByFood = this.ResolveSpoiledItemIds();
    }

    /// <summary>Gets the food code of <paramref name="itemId"/> when it is edible.</summary>
    public bool TryGetFoodCode(uint itemId, out string code)
    {
        string? resolved = this.ResolveItemCode(itemId);
        if (resolved is not null && this.Food.TryGetIndex(resolved, out _))
        {
            code = resolved;
            return true;
        }

        code = string.Empty;
        return false;
    }

    /// <summary>Starts a timed meal of <paramref name="foodCode"/> for <paramref name="eater"/>.</summary>
    public OpStatus BeginEat(Entity eater, string foodCode) => _eatAction.BeginEat(_simulation, eater, foodCode);

    /// <summary>
    /// Ages perishable food in <paramref name="inventory"/> by <paramref name="elapsedSeconds"/> (ADR-016) and appends
    /// every stamped or spoiled slot, which the client must be told about, to <paramref name="changed"/>.
    /// </summary>
    public void AdvanceSpoilage(InventoryContainer inventory, float elapsedSeconds, List<(ushort SlotIndex, ItemSlot Slot)> changed)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(changed);
        for (int i = 0; i < inventory.Capacity; i++)
        {
            ItemSlot slot = inventory.GetSlot(i);
            if (slot.IsEmpty)
            {
                continue;
            }

            string? code = this.ResolveItemCode(slot.ItemId);
            if (code is null || !this.Food.TryGetIndex(code, out int foodIndex))
            {
                continue;
            }

            SpoilageStep step = FoodSpoilage.Advance(in slot, this.Food.Get(foodIndex), elapsedSeconds, _spoiledItemIdByFood[foodIndex]);
            if (step.Outcome == SpoilageOutcome.Unchanged)
            {
                continue;
            }

            inventory.SetSlot(i, step.Slot);
            if (step.Outcome is SpoilageOutcome.Stamped or SpoilageOutcome.Spoiled)
            {
                changed.Add(((ushort)i, step.Slot));
            }
        }
    }

    /// <summary>Sets the activity that drives needs decay this tick, and whether a sleeper sleeps comfortably.</summary>
    public void SetActivity(Entity entity, RestMode restMode, bool isFighting, bool isSprinting, bool isMoving)
    {
        NeedsActivity activity = restMode switch
        {
            RestMode.Sitting => NeedsActivity.Resting,
            RestMode.Sleeping or RestMode.SleepingComfortable => NeedsActivity.Sleeping,
            RestMode.None => isFighting ? NeedsActivity.Fighting
                : isSprinting ? NeedsActivity.Sprinting
                : isMoving ? NeedsActivity.Walking
                : NeedsActivity.Idle,
            _ => NeedsActivity.Idle,
        };
        ref NeedsComponent current = ref _simulation.Entities.GetComponent<NeedsComponent>(entity);
        current.Activity = activity;
        current.SleepComfort = restMode == RestMode.SleepingComfortable;
    }

    /// <summary>Refills needs after a death, keeping at least <see cref="RespawnFoodFraction"/> of max food.</summary>
    public void ResetAfterDeath(Entity entity)
    {
        if (!_simulation.Entities.HasComponent<NeedsComponent>(entity))
        {
            return;
        }

        ref NeedsComponent needs = ref _simulation.Entities.GetComponent<NeedsComponent>(entity);
        NeedsComponent fresh = NeedsComponent.CreateFull(this.Config);
        fresh.Food = MathF.Max(needs.Food, needs.FoodMax * RespawnFoodFraction);
        fresh.Hunger = this.Config.ClassifyHunger(fresh.FoodFraction);
        fresh.Activity = needs.Activity;
        needs = fresh;
    }

    /// <summary>
    /// Rebuilds a player's needs from the persisted record (ADR-015). A negative <c>NeedsFood</c> means the needs were
    /// never saved, so the character starts fully fed; stored values are clamped to the current config ranges.
    /// </summary>
    public NeedsComponent Restore(CharacterRecord character)
    {
        ArgumentNullException.ThrowIfNull(character);
        NeedsConfig config = this.Config;
        NeedsComponent needs = NeedsComponent.CreateFull(config);
        if (config.RestEnabled && float.IsFinite(character.NeedsRest) && character.NeedsRest >= 0f)
        {
            needs.Rest = Math.Clamp(character.NeedsRest, 0f, needs.RestMax);
            needs.Fatigue = config.ClassifyRest(needs.RestFraction);
        }

        if (!float.IsFinite(character.NeedsFood) || character.NeedsFood < 0f)
        {
            return needs;
        }

        needs.Food = Math.Clamp(character.NeedsFood, 0f, needs.FoodMax);
        needs.Malnutrition = float.IsFinite(character.NeedsMalnutrition) ? Math.Clamp(character.NeedsMalnutrition, 0f, 1f) : 0f;
        needs.PoisonRemainingSeconds = float.IsFinite(character.NeedsPoisonSeconds)
            ? Math.Clamp(character.NeedsPoisonSeconds, 0f, config.PoisonDurationSeconds)
            : 0f;
        needs.Hunger = config.ClassifyHunger(needs.FoodFraction);
        return needs;
    }

    /// <summary>Gets the needs to persist (full needs when the entity has none).</summary>
    public NeedsComponent GetForPersistence(Entity entity) =>
        _simulation.Entities.TryGetComponent(entity, out NeedsComponent needs) ? needs : NeedsComponent.CreateFull(this.Config);

    /// <summary>Gets the needs' movement speed multiplier (1 without needs).</summary>
    public float GetMoveScale(Entity entity) =>
        _simulation.Entities.TryGetComponent(entity, out NeedsComponent needs) ? needs.MoveSpeedScale : 1f;

    /// <summary>Gets whether the needs block health regeneration.</summary>
    public bool IsHealthRegenBlocked(Entity entity) =>
        _simulation.Entities.TryGetComponent(entity, out NeedsComponent needs) && needs.HealthRegenBlocked;

    /// <summary>Lengthens <paramref name="baseTicks"/> by the needs' attack-speed penalty.</summary>
    public uint ScaleAttackCooldown(Entity entity, uint baseTicks)
    {
        if (!_simulation.Entities.TryGetComponent(entity, out NeedsComponent needs) || needs.AttackSpeedScale >= 1f)
        {
            return baseTicks;
        }

        return (uint)MathF.Ceiling(baseTicks / MathF.Max(0.1f, needs.AttackSpeedScale));
    }

    /// <summary>Builds the replicated needs snapshot (default without needs).</summary>
    public PlayerNeedsSnapshot BuildSnapshot(Entity entity, RestMode restMode)
    {
        if (!_simulation.Entities.TryGetComponent(entity, out NeedsComponent needs))
        {
            return default;
        }

        float eatProgress = _simulation.Entities.TryGetComponent(entity, out EatingComponent meal) ? meal.Progress : -1f;
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
            restMode);
    }

    /// <summary>Finds the closest intact wood bed within <see cref="NeedsConfig.BedReachTiles"/> of <paramref name="pos"/>.</summary>
    public bool TryFindBed(Vector2 pos, out int bedX, out int bedY)
    {
        float reach = this.Config.BedReachTiles * _tileSize;
        int radius = (int)MathF.Ceiling(this.Config.BedReachTiles);
        int ptx = (int)MathF.Floor(pos.X / _tileSize);
        int pty = (int)MathF.Floor(pos.Y / _tileSize);
        float best = float.MaxValue;
        bedX = 0;
        bedY = 0;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int tx = ptx + dx;
                int ty = pty + dy;
                if (!this.IsIntactBed(tx, ty))
                {
                    continue;
                }

                Vector2 center = new((tx + 0.5f) * _tileSize, (ty + 0.5f) * _tileSize);
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

    /// <summary>Gets whether an intact wood bed stands on the tile.</summary>
    public bool IsIntactBed(int tileX, int tileY) =>
        _buildings.TryGetStructure(tileX, tileY, out StructureTile tile) &&
        tile.Type == StructureType.WoodBed &&
        !tile.IsDestroyed;

    /// <summary>Gets whether the bed tile is under a roof (comfortable sleep).</summary>
    public bool IsRoofed(int tileX, int tileY) => _buildings.IsRoofedAt(tileX, tileY);

    /// <summary>Resolves an item id to its code from the built-in catalog, then the data-driven economy catalog.</summary>
    private string? ResolveItemCode(uint itemId)
    {
        GameEconomy? economy = _economy();
        return _itemRules().Items.TryGet(itemId, out ItemArchetype? builtIn)
            ? builtIn.Code
            : economy is not null && economy.Items.TryGet(itemId, out ItemCatalogEntry? entry) ? entry.Code : null;
    }

    /// <summary>Maps every food index to the item id it spoils into (0 = removed).</summary>
    private uint[] ResolveSpoiledItemIds()
    {
        GameEconomy? economy = _economy();
        ReadOnlySpan<ItemArchetype> builtIn = _itemRules().Items.All;
        uint[] ids = new uint[this.Food.Count];
        for (int i = 0; i < ids.Length; i++)
        {
            string target = this.Food.Get(i).SpoilsInto;
            if (target.Length == 0)
            {
                continue;
            }

            if (economy is not null && economy.Items.TryGetByCode(target, out ItemCatalogEntry? entry))
            {
                ids[i] = entry.Id;
                continue;
            }

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
}
