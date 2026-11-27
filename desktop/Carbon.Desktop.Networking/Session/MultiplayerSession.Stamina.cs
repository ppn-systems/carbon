// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Items.Definitions;
using Carbon.Survival.Components;
using Carbon.Survival.Stamina;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Client stamina (ADR-033): predicts sprint drain and regen with the server's synced <c>stamina.json</c> and the same
/// <see cref="StaminaMeter"/> rules, mirrors the server's exhaustion, and flashes the bar when an action is refused.
/// The server validates every spend; <c>PlayerStatsSync</c> overwrites the predicted value.
/// </summary>
public sealed partial class MultiplayerSession
{
    /// <summary>How long the stamina bar flashes after a refused action.</summary>
    private const float StaminaFlashSeconds = 0.6f;

    private uint _staminaRefusals;
    private float _staminaFlashTimer;

    /// <inheritdoc/>
    public bool LocalPlayerStaminaExhausted => _stamina.Exhausted;

    /// <inheritdoc/>
    public float LocalPlayerStaminaFlash => _staminaFlashTimer / StaminaFlashSeconds;

    /// <summary>Starts a fresh meter (world entry, logout).</summary>
    private void ResetStaminaPrediction(float current)
    {
        _stamina = new StaminaMeter(current);
        _staminaRefusals = 0U;
        _staminaFlashTimer = 0f;
    }

    /// <summary>
    /// Predicts one frame: drains sprint when <paramref name="wantsSprint"/> and the meter allows it, otherwise regenerates.
    /// Returns whether the player sprints. Without a synced config nothing is predicted and sprint follows the meter only.
    /// </summary>
    private bool PredictStaminaTick(float deltaSeconds, bool wantsSprint, bool isMoving)
    {
        if (_staminaFlashTimer > 0f)
        {
            _staminaFlashTimer = MathF.Max(0f, _staminaFlashTimer - deltaSeconds);
        }

        StaminaConfig? config = SyncedStaminaConfig.Current;
        if (config is null)
        {
            return wantsSprint && _stamina.CanSprint;
        }

        if (wantsSprint && _stamina.TryDrain(config.SprintDrainPerSecond * deltaSeconds, config))
        {
            return true;
        }

        StaminaRegenContext context = new(
            isMoving,
            _localRestMode != RestMode.None,
            (RestState)_localRestState,
            (HungerState)_localHungerState);
        _stamina.Regenerate(deltaSeconds, _localMaxStamina, config.GetRegenPerSecond(in context), config);
        return false;
    }

    /// <summary>Predicts paying <paramref name="cost"/>; flashes and tells the player when it would be refused.</summary>
    private bool TryPredictStaminaSpend(float cost)
    {
        StaminaConfig? config = SyncedStaminaConfig.Current;
        bool paid = config is null ? _stamina.CanAfford(cost) : _stamina.TrySpend(cost, config);
        if (!paid)
        {
            this.FlashStaminaRefusal();
        }

        return paid;
    }

    /// <summary>
    /// Refuses a swing with <paramref name="held"/> while the player is exhausted (the server would refuse it too),
    /// flashing the bar. Free swings (staff casts) are never refused. Returns whether it refused.
    /// </summary>
    private bool RefuseSwingWhileExhausted(ItemArchetype? held)
    {
        StaminaConfig? config = SyncedStaminaConfig.Current;
        if (!_stamina.Exhausted || (config is not null && config.GetSwingCost(held) <= 0f))
        {
            return false;
        }

        this.FlashStaminaRefusal();
        return true;
    }

    private void FlashStaminaRefusal()
    {
        lock (_worldGate)
        {
            _staminaFlashTimer = StaminaFlashSeconds;
            _hudNotifications.Add(GameText.Get("stamina.too_tired"));
        }
    }

    private void OnStaminaStatusSync(StaminaStatusSync pkt)
    {
        lock (_worldGate)
        {
            _stamina.Exhausted = pkt.Exhausted;
            if (pkt.RefusalCount > _staminaRefusals)
            {
                _staminaFlashTimer = StaminaFlashSeconds;
            }

            _staminaRefusals = pkt.RefusalCount;
        }
    }
}
