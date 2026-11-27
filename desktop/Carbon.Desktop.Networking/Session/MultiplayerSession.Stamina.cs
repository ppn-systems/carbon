// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Entities;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Items.Definitions;
using Carbon.Survival.Components;
using Carbon.Survival.Stamina;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Client stamina (ADR-033): the session's side of <see cref="StaminaPredictor"/>. It feeds the prediction the local
/// player's rest and hunger, takes the world lock where the HUD reads, and turns refusals into HUD messages.
/// </summary>
public sealed partial class MultiplayerSession
{
    private readonly StaminaPredictor _stamina = new();

    /// <inheritdoc/>
    public bool LocalPlayerStaminaExhausted => _stamina.Exhausted;

    /// <inheritdoc/>
    public float LocalPlayerStaminaFlash => _stamina.Flash;

    /// <summary>Starts a fresh meter (world entry, logout).</summary>
    private void ResetStaminaPrediction(float current) => _stamina.Reset(current);

    /// <summary>Predicts one frame and returns whether the player sprints (see <see cref="StaminaPredictor.Tick"/>).</summary>
    private bool PredictStaminaTick(float deltaSeconds, bool wantsSprint, bool isMoving)
    {
        StaminaRegenContext context = new(
            isMoving,
            _localRestMode != RestMode.None,
            (RestState)_localRestState,
            (HungerState)_localHungerState);
        return _stamina.Tick(deltaSeconds, wantsSprint, in context, _localMaxStamina);
    }

    /// <summary>Predicts paying <paramref name="cost"/>; flashes and tells the player when it would be refused.</summary>
    private bool TryPredictStaminaSpend(float cost)
    {
        bool paid = _stamina.TrySpend(cost, out string refusalKey);
        if (!paid)
        {
            this.FlashStaminaRefusal(refusalKey);
        }

        return paid;
    }

    /// <summary>
    /// Refuses a swing with <paramref name="held"/> while the player is exhausted (the server would refuse it too),
    /// flashing the bar. Free swings (staff casts) are never refused. Returns whether it refused.
    /// </summary>
    private bool RefuseSwingWhileExhausted(ItemArchetype? held)
    {
        if (!_stamina.RefusesSwing(held))
        {
            return false;
        }

        this.FlashStaminaRefusal("stamina.too_tired");
        return true;
    }

    private void FlashStaminaRefusal(string messageKey)
    {
        lock (_worldGate)
        {
            _stamina.StartFlash();
            _hudNotifications.Add(GameText.Get(messageKey));
        }
    }

    private void OnStaminaStatusSync(StaminaStatusSync pkt)
    {
        lock (_worldGate)
        {
            _stamina.OnStatusSync(pkt.Exhausted, pkt.RefusalCount);
        }
    }
}
