// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Survival.Stamina;

/// <summary>
/// One body's stamina (ADR-033): the current value, the regen delay since the last spend, and the exhausted lock.
/// The server owns the real meter; the client runs a copy of the same rules to predict between syncs.
/// </summary>
/// <remarks>
/// Rules:
/// <list type="bullet">
/// <item>An action that costs more than the meter holds is refused (nothing happens) and exhausts the body.</item>
/// <item>Reaching 0 exhausts the body. While exhausted, nothing that costs stamina is allowed and sprint is off.</item>
/// <item>Exhaustion ends once the meter is back to <see cref="StaminaConfig.ExhaustedRecoverFraction"/> of max.</item>
/// <item>Every spend restarts the <see cref="StaminaConfig.RegenDelaySeconds"/> delay; regen waits for it.</item>
/// </list>
/// </remarks>
public struct StaminaMeter
{
    /// <summary>Gets or sets the current stamina.</summary>
    public float Current { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the body is exhausted (no stamina actions, no sprint). Set it directly only to
    /// mirror the server's state on a client.
    /// </summary>
    public bool Exhausted { get; set; }

    /// <summary>Gets the seconds left before regen starts again.</summary>
    public float RegenDelayRemaining { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="StaminaMeter"/> struct.</summary>
    /// <param name="current">Starting stamina.</param>
    public StaminaMeter(float current)
    {
        this.Current = current;
        this.Exhausted = false;
        this.RegenDelayRemaining = 0f;
    }

    /// <summary>Gets whether the body may sprint this tick.</summary>
    public readonly bool CanSprint => !this.Exhausted && this.Current > 0f;

    /// <summary>Gets whether an action costing <paramref name="cost"/> is allowed now (free actions always are).</summary>
    /// <param name="cost">Stamina cost.</param>
    /// <returns><see langword="true"/> when the action may run.</returns>
    public readonly bool CanAfford(float cost) => cost <= 0f || (!this.Exhausted && this.Current >= cost);

    /// <summary>
    /// Pays for a discrete action. Refuses it, exhausting the body, when it cannot be afforded.
    /// </summary>
    /// <param name="cost">Stamina cost.</param>
    /// <param name="config">Tuning.</param>
    /// <returns><see langword="true"/> when paid (the action runs); <see langword="false"/> when refused.</returns>
    public bool TrySpend(float cost, StaminaConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (cost <= 0f)
        {
            return true;
        }

        if (!this.CanAfford(cost))
        {
            this.Exhausted = true;
            return false;
        }

        this.Current -= cost;
        this.AfterSpend(config);
        return true;
    }

    /// <summary>
    /// Drains a continuous effort (sprint, build work) for one tick. Takes what is left when less than
    /// <paramref name="amount"/> remains; refuses when exhausted or empty.
    /// </summary>
    /// <param name="amount">Stamina for this tick.</param>
    /// <param name="config">Tuning.</param>
    /// <returns><see langword="true"/> when the effort may continue this tick.</returns>
    public bool TryDrain(float amount, StaminaConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (amount <= 0f)
        {
            return true;
        }

        if (!this.CanSprint)
        {
            this.Exhausted = true;
            return false;
        }

        this.Current = MathF.Max(0f, this.Current - amount);
        this.AfterSpend(config);
        return true;
    }

    /// <summary>
    /// Advances regen by <paramref name="deltaSeconds"/>: first the regen delay, then <paramref name="ratePerSecond"/>
    /// up to <paramref name="max"/>. Ends exhaustion once the meter reaches the recover fraction.
    /// </summary>
    /// <param name="deltaSeconds">Elapsed seconds.</param>
    /// <param name="max">Maximum stamina.</param>
    /// <param name="ratePerSecond">Regen rate (<see cref="StaminaConfig.GetRegenPerSecond"/>).</param>
    /// <param name="config">Tuning.</param>
    public void Regenerate(float deltaSeconds, float max, float ratePerSecond, StaminaConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        float dt = deltaSeconds;
        if (this.RegenDelayRemaining > 0f)
        {
            this.RegenDelayRemaining -= dt;
            if (this.RegenDelayRemaining > 0f)
            {
                this.EndExhaustionIfRecovered(max, config);
                return;
            }

            dt = -this.RegenDelayRemaining;
            this.RegenDelayRemaining = 0f;
        }

        if (this.Current < max && dt > 0f && ratePerSecond > 0f)
        {
            this.Current = MathF.Min(max, this.Current + (ratePerSecond * dt));
        }

        this.EndExhaustionIfRecovered(max, config);
    }

    /// <summary>Refills the meter and clears exhaustion and delay (respawn).</summary>
    /// <param name="max">Maximum stamina.</param>
    public void Refill(float max)
    {
        this.Current = max;
        this.Exhausted = false;
        this.RegenDelayRemaining = 0f;
    }

    private void AfterSpend(StaminaConfig config)
    {
        this.RegenDelayRemaining = config.RegenDelaySeconds;
        if (this.Current <= 0f)
        {
            this.Current = 0f;
            this.Exhausted = true;
        }
    }

    private void EndExhaustionIfRecovered(float max, StaminaConfig config)
    {
        if (this.Exhausted && this.Current >= max * config.ExhaustedRecoverFraction)
        {
            this.Exhausted = false;
        }
    }
}
