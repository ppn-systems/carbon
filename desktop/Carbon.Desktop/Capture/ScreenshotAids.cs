// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Capture;

/// <summary>
/// Client-only screenshot aids driven by <see cref="DesktopOptions"/> (<c>CARBON_CAPTURE_MOUSE</c>,
/// <c>CARBON_OPEN_PANEL</c>, <c>CARBON_UI_PREVIEW</c>). Purely visual: nothing here sends a request to the server.
/// </summary>
internal sealed class ScreenshotAids(DesktopOptions options)
{
    private bool _openedPanel;
    private bool _breakToastShown;
    private long _nextShatterMs;

    /// <summary>Pins the cursor when <c>CARBON_CAPTURE_MOUSE</c> is set, so hover states render deterministically.</summary>
    public DesktopInputFrame PinMouse(DesktopInputFrame input) =>
        options.CaptureMouse is (float x, float y) ? input with { MouseScreenPosition = new Vector2(x, y) } : input;

    /// <summary>
    /// Applies the per-frame UI aids: opens the <c>CARBON_OPEN_PANEL</c> tab once, then the <c>CARBON_UI_PREVIEW</c>
    /// state. Returns the mouse position to use for a tooltip preview, or <see langword="null"/> to keep the real one.
    /// </summary>
    public Vector2? Apply(HudOverlayRenderer? hud, IWorldSession session, int viewportWidth, int viewportHeight, CombatFeedbackTracker? feedback = null)
    {
        if (hud is not null)
        {
            this.OpenRequestedPanel(hud, session);
            this.PreviewItemBreak(hud, session, feedback);
        }

        return this.ApplyUiPreview(hud, session, viewportWidth, viewportHeight);
    }

    private void OpenRequestedPanel(HudOverlayRenderer hud, IWorldSession session)
    {
        if (!_openedPanel && string.Equals(options.OpenPanel, "character", StringComparison.OrdinalIgnoreCase))
        {
            _openedPanel = true;
            _ = hud.State.OpenOrTogglePlayerTab(session, PlayerModalTab.Character);
        }
    }

    // CARBON_UI_PREVIEW=item-break:N (ADR-035): the break toast for the item in slot N once, and its shatter shards every 0.4 s
    // so any capture frame shows them. Visual only: the item is not touched and nothing is sent.
    private void PreviewItemBreak(HudOverlayRenderer hud, IWorldSession session, CombatFeedbackTracker? feedback)
    {
        const string prefix = "item-break:";
        if (options.UiPreview is not { } preview ||
            !preview.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(preview.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) ||
            slot < 0 || slot >= PlayerInventoryLayout.TotalCapacity)
        {
            return;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        session.CopyInventorySlots(slots);
        ItemSlot item = slots[slot];
        if (item.IsEmpty)
        {
            return;
        }

        if (!_breakToastShown)
        {
            _breakToastShown = true;
            hud.PushNotification(GameText.Format("durability.broke", new TextArg("item", session.Items.GetDisplayName(item.ItemId))));
        }

        long now = Environment.TickCount64;
        if (feedback is not null && now >= _nextShatterMs)
        {
            _nextShatterMs = now + 400L;
            feedback.SpawnItemBreakShatter(new ItemBreakEvent(session.LocalPlayerInterpolatedPosition, item.ItemId, item.UpgradeLevel), now / 1000f);
        }
    }

    private Vector2? ApplyUiPreview(HudOverlayRenderer? hud, IWorldSession session, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (hud is not null && options.UiPreview is { Length: > 0 } statusPreview && statusPreview.StartsWith("status-", StringComparison.Ordinal))
        {
            return ApplyStatusPreview(hud, session, statusPreview, viewportWidth);
        }

        if (hud is not null && string.Equals(options.UiPreview, "death", StringComparison.Ordinal))
        {
            // ADR-031 death screen review: a sample slime kill (visual only, nothing is sent).
            hud.State.DeathPreview = new DeathView(DeathCause.Monster, 0, "monster.wild_slime.name");
            return null;
        }

        if (hud is null || options.UiPreview is not { Length: > 0 } preview || !preview.StartsWith("inventory", StringComparison.Ordinal))
        {
            return null;
        }

        hud.State.ShowPlayerModal = true;
        hud.State.ActivePlayerTab = PlayerModalTab.Inventory;
        int colon = preview.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 ||
            !int.TryParse(preview.AsSpan(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) ||
            slot < 0 || slot >= PlayerInventoryLayout.TotalCapacity)
        {
            return null;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        session.CopyInventorySlots(slots);
        string mode = preview[..colon];
        if (mode == "inventory-tooltip")
        {
            return hud.GetInventorySlotCenter(slot, viewportWidth, viewportHeight);
        }

        if (!hud.InventoryDialog.IsOpen && !slots[slot].IsEmpty)
        {
            if (mode == "inventory-split")
            {
                _ = hud.InventoryDialog.OpenSplit(slot, in slots[slot]);
            }
            else if (mode == "inventory-destroy")
            {
                hud.InventoryDialog.OpenConfirmDestroy(slot, in slots[slot]);
            }
        }

        return null;
    }

    /// <summary>
    /// <c>status-hover:N</c> hovers the N-th status icon; <c>status-details:N</c> opens its details panel (ADR-029).
    /// </summary>
    private static Vector2? ApplyStatusPreview(HudOverlayRenderer hud, IWorldSession session, string preview, int viewportWidth)
    {
        int colon = preview.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 ||
            !int.TryParse(preview.AsSpan(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
            index < 0 || index >= session.LocalStatusEffects.Count)
        {
            return null;
        }

        Vector2 centre = hud.GetStatusIconCenter(index, viewportWidth);
        if (preview.StartsWith("status-details", StringComparison.Ordinal))
        {
            if (!hud.IsStatusEffectDetailsOpen)
            {
                hud.OpenStatusEffectDetails(session.LocalStatusEffects[index].EffectId);
            }

            return null;
        }

        return centre;
    }
}
