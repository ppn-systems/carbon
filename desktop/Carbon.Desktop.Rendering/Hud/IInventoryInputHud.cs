// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Desktop.Abstractions.World;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// The slice of <see cref="HudOverlayRenderer"/> that inventory input reads and drives: hit tests, the split/confirm
/// dialog, the carried-slot highlight and toasts. Lets the input controller be exercised without an OpenGL device.
/// </summary>
public interface IInventoryInputHud
{
    /// <summary>Gets or sets whether the unified player window is open.</summary>
    bool ShowPlayerModal { get; set; }

    /// <summary>Gets or sets the inventory slot highlighted as carried by the cursor, or <c>-1</c>.</summary>
    int CarriedInventorySlot { get; set; }

    /// <summary>Gets the inventory split/confirm dialog state (ADR-020).</summary>
    InventoryDialogState InventoryDialog { get; }

    /// <summary>Queues a notification toast.</summary>
    void PushNotification(string message);

    /// <summary>Returns whether <paramref name="mousePos"/> is inside the open modal window.</summary>
    bool IsPointInActiveModal(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight);

    /// <summary>Hit-tests the inventory tab's Sort button.</summary>
    bool TryHitTestInventorySortButton(Vector2 mousePos, int viewportWidth, int viewportHeight);

    /// <summary>Hit-tests the inventory tab's slots.</summary>
    bool TryHitTestInventorySlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex);

    /// <summary>Hit-tests the open split/confirm dialog's buttons.</summary>
    InventoryDialogButton HitTestInventoryDialog(Vector2 mousePos, int viewportWidth, int viewportHeight);
}
