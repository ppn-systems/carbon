// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Items.Definitions;
using Carbon.Survival.Stamina;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Client stamina (ADR-033): predicts sprint drain and regen with the server's synced <c>stamina.json</c> and the same
/// <see cref="StaminaMeter"/> rules, mirrors the server's exhaustion, and times the bar flash after a refused action.
/// The server validates every spend; <c>PlayerStatsSync</c> overwrites <see cref="Current"/>. Not thread-safe: the
/// session calls it under its own lock where the old code did.
/// </summary>
internal sealed class StaminaPredictor
{
    /// <summary>HUD animation: how long the bar flashes after a refused action (presentation, not gameplay).</summary>
    private const float BarFlashSeconds = 0.6f;

    private StaminaMeter _meter = new(100f);
    private uint _refusals;
    private float _flashTimer;

    /// <summary>Gets the synced stamina tuning (null until the server's <c>stamina.json</c> arrives).</summary>
    public SyncedStaminaConfig Config { get; } = new();

    /// <summary>Gets or sets the predicted stamina.</summary>
    public float Current
    {
        get => _meter.Current;
        set => _meter.Current = value;
    }

    /// <summary>Gets whether the player is exhausted (predicted, or mirrored from the server).</summary>
    public bool Exhausted => _meter.Exhausted;

    /// <summary>Gets the bar flash, from 1 just after a refusal down to 0.</summary>
    public float Flash => _flashTimer / BarFlashSeconds;

    /// <summary>Starts a fresh meter (world entry, logout).</summary>
    /// <param name="current">The stamina to start from.</param>
    public void Reset(float current)
    {
        _meter = new StaminaMeter(current);
        _refusals = 0U;
        _flashTimer = 0f;
    }

    /// <summary>
    /// Predicts one frame: drains sprint when <paramref name="wantsSprint"/> and the meter allows it, otherwise regenerates.
    /// Without a synced config nothing is predicted and sprint follows the meter only.
    /// </summary>
    /// <param name="deltaSeconds">Frame seconds.</param>
    /// <param name="wantsSprint">Whether the player holds sprint while moving.</param>
    /// <param name="context">What the body is doing, for the regen rate.</param>
    /// <param name="max">The maximum stamina.</param>
    /// <returns>Whether the player sprints.</returns>
    public bool Tick(float deltaSeconds, bool wantsSprint, in StaminaRegenContext context, float max)
    {
        if (_flashTimer > 0f)
        {
            _flashTimer = MathF.Max(0f, _flashTimer - deltaSeconds);
        }

        StaminaConfig? config = this.Config.Current;
        if (config is null)
        {
            return wantsSprint && _meter.CanSprint;
        }

        if (wantsSprint && _meter.TryDrain(config.SprintDrainPerSecond * deltaSeconds, config))
        {
            return true;
        }

        _meter.Regenerate(deltaSeconds, max, config.GetRegenPerSecond(in context), config);
        return false;
    }

    /// <summary>Predicts paying <paramref name="cost"/>. Does not flash; the caller does that with the message.</summary>
    /// <param name="cost">The stamina cost.</param>
    /// <param name="refusalKey">When refused, the localization key that explains why.</param>
    /// <returns>Whether it was paid.</returns>
    public bool TrySpend(float cost, out string refusalKey)
    {
        bool wasExhausted = _meter.Exhausted;
        StaminaConfig? config = this.Config.Current;
        bool paid = config is null ? _meter.CanAfford(cost) : _meter.TrySpend(cost, config);
        refusalKey = wasExhausted ? "stamina.too_tired" : "hud.not_enough_stamina";
        return paid;
    }

    /// <summary>
    /// Gets whether a swing with <paramref name="held"/> is refused because the player is exhausted (the server would refuse
    /// it too). Free swings (staff casts) are never refused.
    /// </summary>
    /// <param name="held">The held item, or <see langword="null"/> for a bare hand.</param>
    /// <returns>Whether the swing is refused.</returns>
    public bool RefusesSwing(ItemArchetype? held)
    {
        StaminaConfig? config = this.Config.Current;
        return _meter.Exhausted && (config is null || config.GetSwingCost(held) > 0f);
    }

    /// <summary>Starts the bar flash.</summary>
    public void StartFlash() => _flashTimer = BarFlashSeconds;

    /// <summary>Mirrors the server's exhaustion and flashes when it counted a refusal the client did not predict.</summary>
    /// <param name="exhausted">The server's exhaustion.</param>
    /// <param name="refusalCount">The server's running count of refused actions.</param>
    public void OnStatusSync(bool exhausted, uint refusalCount)
    {
        _meter.Exhausted = exhausted;
        if (refusalCount > _refusals)
        {
            _flashTimer = BarFlashSeconds;
        }

        _refusals = refusalCount;
    }
}
