// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Carbon.Abstractions.World;
using Carbon.Combat.Events;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Survival.Components;
using Carbon.Survival.Events;
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

    private bool TryBeginEatingActiveSlotLocked(ActivePlayerState state, in ItemSlot activeSlot, ItemArchetype def)
    {
        if (!_foodCatalog.TryGetIndex(def.Code, out _))
        {
            return false;
        }

        OpStatus status = _eatAction.BeginEat(_simulation, state.Entity, def.Code);
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
