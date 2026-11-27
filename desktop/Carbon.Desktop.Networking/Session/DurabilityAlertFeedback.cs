// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Localization;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Turns the server's <c>ItemDurabilityAlert</c> (ADR-035) into client feedback: a break shows the localized
/// "{item} broke!" toast, the same text floating over the player and an <see cref="ItemBreakEvent"/> for the shatter
/// effect; the one-time warning shows "{item} is about to break, repair it". The server decides; this only shows it.
/// </summary>
internal static class DurabilityAlertFeedback
{
    /// <summary>Adds the feedback of one alert to the session's buffers.</summary>
    /// <param name="kind">What happened to the item.</param>
    /// <param name="itemId">Item archetype id.</param>
    /// <param name="upgradeLevel">Its enhancement level.</param>
    /// <param name="itemName">The item's localized name.</param>
    /// <param name="playerPosition">The local player's world position.</param>
    /// <param name="hudNotifications">HUD toast feed.</param>
    /// <param name="combatEvents">Floating world text feed.</param>
    /// <param name="breakEvents">Shatter effect feed.</param>
    public static void Apply(
        DurabilityAlertKind kind,
        uint itemId,
        byte upgradeLevel,
        string itemName,
        Vector2 playerPosition,
        List<string> hudNotifications,
        List<CombatFeedbackEvent> combatEvents,
        List<ItemBreakEvent> breakEvents)
    {
        ArgumentNullException.ThrowIfNull(hudNotifications);
        ArgumentNullException.ThrowIfNull(combatEvents);
        ArgumentNullException.ThrowIfNull(breakEvents);
        TextArg item = new("item", itemName ?? string.Empty);
        switch (kind)
        {
            case DurabilityAlertKind.Broke:
                string broke = GameText.Format("durability.broke", item);
                hudNotifications.Add(broke);
                combatEvents.Add(new CombatFeedbackEvent(playerPosition + new Vector2(0f, -14f), 0f, IsCritical: true, IsHeal: false, Label: broke));
                breakEvents.Add(new ItemBreakEvent(playerPosition, itemId, upgradeLevel));
                break;
            case DurabilityAlertKind.AboutToBreak:
                hudNotifications.Add(GameText.Format("durability.about_to_break", item));
                break;
            case DurabilityAlertKind.None:
            default:
                break;
        }
    }
}
