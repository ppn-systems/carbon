// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World.Replication;
using Carbon.Application.World.Commands;
using Carbon.Combat.Events;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
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
            !_needs.IsIntactBed(state.RestBedTileX, state.RestBedTileY))
        {
            this.WakeLocked(state);
        }

        _needs.SetActivity(state.Entity, state.RestMode, isFighting, isSprinting, isMoving);

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

        if (!_needs.TryGetFoodCode(activeSlot.ItemId, out string code))
        {
            return false;
        }

        OpStatus status = _needs.BeginEat(state.Entity, code);
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
            _spoiledSlotsScratch.Clear();
            _needs.AdvanceSpoilage(state.Inventory, elapsedSeconds, _spoiledSlotsScratch);
            for (int i = 0; i < _spoiledSlotsScratch.Count; i++)
            {
                (ushort slotIndex, ItemSlot slot) = _spoiledSlotsScratch[i];
                _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, slotIndex, slot));
            }

            if (_spoiledSlotsScratch.Count > 0)
            {
                this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            }
        }
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
        _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, (ushort)slotIndex, updated));
        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
    }

    private void InterruptMealOnDamageLocked(ActivePlayerState victim)
    {
        _ = EatActionSystem.CancelEat(_simulation, victim.Entity, MealInterruptReason.Damaged);
        this.WakeLocked(victim);
    }

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitSetRestingAsync(ulong connectionId, bool rest) =>
        _commands.Submit(WorldCommandKind.SetResting, connectionId, rest ? (ushort)1 : (ushort)0);

    /// <summary>
    /// Starts resting (sleeping in a bed within reach, otherwise sitting) or stands the player up (ADR-018). Runs
    /// synchronously; packet handlers go through <see cref="SubmitSetRestingAsync"/> so it executes inside the tick.
    /// </summary>
    /// <param name="connectionId">Requesting connection.</param>
    /// <param name="rest"><see langword="true"/> to rest, <see langword="false"/> to stand up.</param>
    /// <returns><see cref="OpStatus.Ok"/>, or <see cref="OpStatus.Forbidden"/> when not in the world or dead.</returns>
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
            if (_needs.TryFindBed(pos, out int bedX, out int bedY))
            {
                state.RestMode = _needs.IsRoofed(bedX, bedY) ? RestMode.SleepingComfortable : RestMode.Sleeping;
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

    private void ResetNeedsAfterDeathLocked(ActivePlayerState state)
    {
        _ = EatActionSystem.CancelEat(_simulation, state.Entity, MealInterruptReason.Died);
        state.EatingSlot = -1;
        this.WakeLocked(state);
        _needs.ResetAfterDeath(state.Entity);
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
