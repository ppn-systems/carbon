// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Input;
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

    /// <summary>Pins the cursor when <c>CARBON_CAPTURE_MOUSE</c> is set, so hover states render deterministically.</summary>
    public DesktopInputFrame PinMouse(DesktopInputFrame input) =>
        options.CaptureMouse is (float x, float y) ? input with { MouseScreenPosition = new Vector2(x, y) } : input;

    /// <summary>
    /// Applies the per-frame UI aids: opens the <c>CARBON_OPEN_PANEL</c> tab once, then the <c>CARBON_UI_PREVIEW</c>
    /// state. Returns the mouse position to use for a tooltip preview, or <see langword="null"/> to keep the real one.
    /// </summary>
    public Vector2? Apply(HudOverlayRenderer? hud, IWorldSession session, int viewportWidth, int viewportHeight)
    {
        if (hud is not null)
        {
            this.OpenRequestedPanel(hud, session);
        }

        return this.ApplyUiPreview(hud, session, viewportWidth, viewportHeight);
    }

    private void OpenRequestedPanel(HudOverlayRenderer hud, IWorldSession session)
    {
        if (!_openedPanel && string.Equals(options.OpenPanel, "character", StringComparison.OrdinalIgnoreCase))
        {
            _openedPanel = true;
            _ = hud.OpenOrTogglePlayerTab(session, PlayerModalTab.Character);
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
            // ADR-031 death screen review: a sample slime kill that dropped 1,250 gold (visual only, nothing is sent).
            hud.DeathPreview = new DeathView(DeathCause.Monster, 0, 1_250L, "monster.wild_slime.name");
            return null;
        }

        if (hud is null || options.UiPreview is not { Length: > 0 } preview || !preview.StartsWith("inventory", StringComparison.Ordinal))
        {
            return null;
        }

        hud.ShowPlayerModal = true;
        hud.ActivePlayerTab = PlayerModalTab.Inventory;
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
            return HudOverlayRenderer.GetInventorySlotCenter(slot, viewportWidth, viewportHeight);
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

        Vector2 centre = HudOverlayRenderer.GetStatusIconCenter(index, viewportWidth);
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
