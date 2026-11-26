// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Combat.Events;
using Carbon.Contracts.Common;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
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

        NeedsActivity activity = isFighting ? NeedsActivity.Fighting
            : isSprinting ? NeedsActivity.Sprinting
            : isMoving ? NeedsActivity.Walking
            : NeedsActivity.Idle;
        _ = NeedsSystem.SetActivity(_simulation, state.Entity, activity);

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
        _itemRules.Items.TryGet(itemId, out ItemArchetype? builtIn)
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

            ReadOnlySpan<ItemArchetype> builtIn = _itemRules.Items.All;
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

        if (_itemRules.Items.TryGet(slot.ItemId, out ItemArchetype? def))
        {
            _ = this.ApplyConsumableRestoresLocked(state, def);
        }

        ItemSlot updated = slot.Quantity > 1 ? slot with { Quantity = slot.Quantity - 1 } : ItemSlot.Empty;
        state.Inventory.SetSlot(slotIndex, in updated);
        _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, (ushort)slotIndex, updated));
        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
    }

    private void InterruptMealOnDamageLocked(ActivePlayerState victim) =>
        _ = EatActionSystem.CancelEat(_simulation, victim.Entity, MealInterruptReason.Damaged);

    private void ResetNeedsAfterDeathLocked(ActivePlayerState state)
    {
        _ = EatActionSystem.CancelEat(_simulation, state.Entity, MealInterruptReason.Died);
        state.EatingSlot = -1;
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
            eatProgress);
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
