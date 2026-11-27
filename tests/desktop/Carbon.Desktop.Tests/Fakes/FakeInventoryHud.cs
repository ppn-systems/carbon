// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;
using Carbon.Desktop.Rendering.Hud;

namespace Carbon.Desktop.Tests.Fakes;

/// <summary>
/// Screen-free stand-in for the HUD's inventory tab: slot <c>i</c> sits at <see cref="SlotAt"/>, the Sort button at
/// <see cref="SortButton"/>, dialog buttons at <see cref="DialogButtonAt"/>, and anything with a negative Y is
/// outside the modal window. The dialog state is the real <see cref="InventoryDialogState"/>.
/// </summary>
internal sealed class FakeInventoryHud : IInventoryInputHud
{
    public static readonly Vector2 SortButton = new(900f, 20f);
    public static readonly Vector2 OutsideModal = new(400f, -50f);
    public static readonly Vector2 EmptyModalArea = new(900f, 600f);

    public bool ShowPlayerModal { get; set; } = true;

    public int CarriedInventorySlot { get; set; } = -1;

    public InventoryDialogState InventoryDialog { get; } = new();

    public List<string> Notifications { get; } = [];

    public static Vector2 SlotAt(int slot) => new(10f + (slot * 20f), 100f);

    public static Vector2 DialogButtonAt(InventoryDialogButton button) => new(100f * (int)button, 400f);

    public void PushNotification(string message) => this.Notifications.Add(message);

    public bool IsPointInActiveModal(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.ShowPlayerModal && mousePos.Y >= 0f;

    public bool TryHitTestInventorySortButton(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.ShowPlayerModal && mousePos == SortButton;

    public bool TryHitTestInventorySlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex)
    {
        slotIndex = 0;
        if (!this.ShowPlayerModal || mousePos.Y != 100f)
        {
            return false;
        }

        float index = (mousePos.X - 10f) / 20f;
        if (index < 0f || index != (int)index || index >= PlayerInventoryLayout.TotalCapacity)
        {
            return false;
        }

        slotIndex = (ushort)index;
        return true;
    }

    public InventoryDialogButton HitTestInventoryDialog(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        if (mousePos.Y != 400f)
        {
            return InventoryDialogButton.None;
        }

        int value = (int)(mousePos.X / 100f);
        return value is >= 1 and <= 5 ? (InventoryDialogButton)value : InventoryDialogButton.None;
    }
}
