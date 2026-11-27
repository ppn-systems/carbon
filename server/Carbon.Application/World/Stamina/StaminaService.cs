// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Simulation.World;
using Carbon.Survival.Components;
using Carbon.Survival.Stamina;

namespace Carbon.Application.World.Stamina;

/// <summary>
/// Server-authoritative stamina (ADR-033): validates every physical action against the player's
/// <see cref="StaminaMeter"/>, drains sprint and build work, regenerates after the delay (slower while moving, tired or
/// hungry), and pushes exhaustion and refusals to the owning client. Runs inside the world tick lock.
/// </summary>
internal sealed class StaminaService
{
    private readonly WorldSimulation _simulation;
    private readonly OutboundTickBuffer _outbound;

    /// <summary>Initializes a new instance of the <see cref="StaminaService"/> class.</summary>
    /// <param name="simulation">World simulation (needs components).</param>
    /// <param name="config">Stamina tuning.</param>
    /// <param name="outbound">Outbound buffer for status pushes.</param>
    public StaminaService(WorldSimulation simulation, StaminaConfig config, OutboundTickBuffer outbound)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        this.Config = config ?? throw new ArgumentNullException(nameof(config));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
    }

    /// <summary>Gets the tuning the world currently runs on.</summary>
    public StaminaConfig Config { get; private set; }

    /// <summary>Swaps in new tuning (ADR-014 hot reload). Current meters keep their values.</summary>
    /// <param name="config">New tuning.</param>
    public void Reconfigure(StaminaConfig config) => this.Config = config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>Gets the stamina cost of one primary swing with the item in <paramref name="activeSlot"/>.</summary>
    /// <param name="rules">Item rules.</param>
    /// <param name="activeSlot">Held slot.</param>
    /// <returns>The swing cost.</returns>
    public float GetSwingCost(ItemRules rules, in ItemSlot activeSlot)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ItemArchetype? item = !activeSlot.IsEmpty && activeSlot.Quantity > 0 && rules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? def)
            ? def
            : null;
        return this.Config.GetSwingCost(item);
    }

    /// <summary>
    /// Pays for a discrete action. A refused action is counted (the client flashes the bar) and must not run.
    /// </summary>
    /// <param name="state">Player.</param>
    /// <param name="cost">Stamina cost.</param>
    /// <returns><see langword="true"/> when paid.</returns>
    public bool TrySpend(PlayerState state, float cost)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Stamina.TrySpend(cost, this.Config))
        {
            if (cost > 0f)
            {
                state.StatsDirty = true;
            }

            return true;
        }

        state.StaminaRefusals++;
        state.StatsDirty = true;
        return false;
    }

    /// <summary>Drains one tick of sprinting; returns whether the player may sprint this tick.</summary>
    /// <param name="state">Player.</param>
    /// <param name="deltaSeconds">Tick length.</param>
    /// <returns><see langword="true"/> when sprinting.</returns>
    public bool TrySprint(PlayerState state, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Stamina.TryDrain(this.Config.SprintDrainPerSecond * deltaSeconds, this.Config))
        {
            return false;
        }

        state.StatsDirty = true;
        return true;
    }

    /// <summary>Gets whether a build or demolition job may make progress this tick.</summary>
    /// <param name="state">Player.</param>
    /// <returns><see langword="false"/> while exhausted or empty.</returns>
    public bool CanWork(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return this.Config.BuildWorkPerSecond <= 0f || state.Stamina.CanSprint;
    }

    /// <summary>Drains the stamina of one tick of build or demolition work that made progress.</summary>
    /// <param name="state">Player.</param>
    /// <param name="deltaSeconds">Tick length.</param>
    public void DrainWork(PlayerState state, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Stamina.TryDrain(this.Config.BuildWorkPerSecond * deltaSeconds, this.Config) && this.Config.BuildWorkPerSecond > 0f)
        {
            state.StatsDirty = true;
        }
    }

    /// <summary>
    /// Regenerates one tick: nothing during the regen delay, then the still or moving rate scaled by rest, fatigue and
    /// hunger.
    /// </summary>
    /// <param name="state">Player.</param>
    /// <param name="deltaSeconds">Tick length.</param>
    /// <param name="isMoving">Whether the player moved this tick.</param>
    public void Regenerate(PlayerState state, float deltaSeconds, bool isMoving)
    {
        ArgumentNullException.ThrowIfNull(state);
        RestState fatigue = RestState.Rested;
        HungerState hunger = HungerState.Fed;
        if (_simulation.Entities.TryGetComponent(state.Entity, out NeedsComponent needs))
        {
            fatigue = needs.Fatigue;
            hunger = needs.Hunger;
        }

        StaminaRegenContext context = new(isMoving, state.RestMode != Contracts.Entities.RestMode.None, fatigue, hunger);
        state.Stamina.Regenerate(deltaSeconds, state.Stats.MaxStamina, this.Config.GetRegenPerSecond(in context), this.Config);
    }

    /// <summary>Queues a <c>StaminaStatusSync</c> when exhaustion or the refusal count changed since the last push.</summary>
    /// <param name="state">Player.</param>
    public void QueueStatusIfChanged(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        (bool Exhausted, uint Refusals) current = (state.Stamina.Exhausted, state.StaminaRefusals);
        if (current == state.StaminaStatusSent)
        {
            return;
        }

        state.StaminaStatusSent = current;
        _outbound.Pending.StaminaStatuses.Add(new OutboundStaminaStatus(state.Session.ConnectionId, current.Exhausted, current.Refusals));
    }
}
