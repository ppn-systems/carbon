// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Scenes;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Gameplay.Input;

/// <summary>
/// Mouse and keyboard handling for the inventory tab of the player modal (ADR-020): click/hold drag-and-drop, quick
/// move, split/confirm dialogs, drop and destroy, sort, and hover-to-bind hotbar keys. Owns the carried-slot drag
/// state. Extracted from <see cref="WorldGameplayScene"/> (audit round 2, SRP-2); behaviour is unchanged.
/// </summary>
internal sealed class InventoryInputController
{
    private readonly IWorldSession _session;
    private IInventoryInputHud? _hud;
    private int _carriedSlot = -1;
    private Vector2 _pressStartMousePos;
    private bool _isHoldDragging;

    public InventoryInputController(IWorldSession session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Gets the bag slot picked up by the cursor, or <c>-1</c>.</summary>
    public int CarriedSlot => _carriedSlot;

    /// <summary>Binds the HUD the controller hit-tests and drives (<see langword="null"/> while the scene is unloaded).</summary>
    public void AttachHud(IInventoryInputHud? hud) => _hud = hud;

    public void HandleTabInput(in DesktopInputFrame input, int viewportWidth, int viewportHeight)
    {
        if (_hud is null)
        {
            return;
        }

        Vector2 mouseScreen = input.MouseScreenPosition;
        Span<ItemSlot> invSlots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        _session.CopyInventorySlots(invSlots);

        if (this.HandleInventoryDialogInput(in input, invSlots, viewportWidth, viewportHeight))
        {
            return;
        }

        if (input.DeletePressed)
        {
            int target = _carriedSlot >= 0
                ? _carriedSlot
                : (_hud.TryHitTestInventorySlot(mouseScreen, viewportWidth, viewportHeight, out ushort hoverSlot) ? hoverSlot : -1);
            if (target >= 0 && target < invSlots.Length && !invSlots[target].IsEmpty)
            {
                this.ResetDragState();
                _hud.InventoryDialog.OpenConfirmDestroy(target, in invSlots[target]);
            }

            return;
        }

        if (_carriedSlot < 0)
        {
            if (input.LeftClickPressed)
            {
                if (!_hud.IsPointInActiveModal(_session, mouseScreen, viewportWidth, viewportHeight))
                {
                    _hud.ShowPlayerModal = false;
                    return;
                }

                if (_hud.TryHitTestInventorySortButton(mouseScreen, viewportWidth, viewportHeight))
                {
                    if (_session.TrySortInventory())
                    {
                        _hud.PushNotification(GameText.Get("inventory.sorted"));
                    }

                    return;
                }

                if (_hud.TryHitTestInventorySlot(mouseScreen, viewportWidth, viewportHeight, out ushort clickedSlot))
                {
                    if (input.CtrlHeld && clickedSlot < invSlots.Length)
                    {
                        if (!_hud.InventoryDialog.OpenSplit(clickedSlot, in invSlots[clickedSlot]))
                        {
                            _hud.PushNotification(GameText.Get("inventory.split.needs_stack"));
                        }
                    }
                    else if (input.ShiftHeld)
                    {
                        _ = _session.TryQuickMoveInventorySlot(clickedSlot);
                    }
                    else if (clickedSlot < invSlots.Length && !invSlots[clickedSlot].IsEmpty)
                    {
                        _carriedSlot = clickedSlot;
                        _pressStartMousePos = mouseScreen;
                        _isHoldDragging = true;
                    }
                }
            }
            else if (input.RightClickPressed &&
                     _hud.TryHitTestInventorySlot(mouseScreen, viewportWidth, viewportHeight, out ushort rightSlot))
            {
                _ = _session.TryQuickMoveInventorySlot(rightSlot);
            }

            return;
        }

        if (_isHoldDragging)
        {
            if (!input.LeftMouseDown)
            {
                _isHoldDragging = false;
                if (!_hud.IsPointInActiveModal(_session, mouseScreen, viewportWidth, viewportHeight))
                {
                    int dropSlot = _carriedSlot;
                    this.ResetDragState();
                    _ = this.RequestDrop(dropSlot, -1);
                }
                else if (_hud.TryHitTestInventorySlot(mouseScreen, viewportWidth, viewportHeight, out ushort releaseSlot) &&
                         releaseSlot != _carriedSlot)
                {
                    _ = _session.TryMoveInventorySlot((ushort)_carriedSlot, releaseSlot);
                    this.ResetDragState();
                }
                else if (Vector2.DistanceSquared(mouseScreen, _pressStartMousePos) > 36f)
                {
                    this.ResetDragState();
                }
            }

            return;
        }

        // Click-pickup carried item mode: place into target slot or drop outside modal window
        if (input.LeftClickPressed)
        {
            if (!_hud.IsPointInActiveModal(_session, mouseScreen, viewportWidth, viewportHeight))
            {
                int dropSlot = _carriedSlot;
                this.ResetDragState();
                _ = this.RequestDrop(dropSlot, -1);
            }
            else if (_hud.TryHitTestInventorySlot(mouseScreen, viewportWidth, viewportHeight, out ushort targetSlot))
            {
                if (targetSlot != _carriedSlot)
                {
                    _ = _session.TryMoveInventorySlot((ushort)_carriedSlot, targetSlot);
                }

                this.ResetDragState();
            }
            else
            {
                this.ResetDragState();
            }
        }
        else if (input.RightClickPressed)
        {
            if (!_hud.IsPointInActiveModal(_session, mouseScreen, viewportWidth, viewportHeight))
            {
                int currentQty = _carriedSlot < invSlots.Length ? invSlots[_carriedSlot].Quantity : 0;
                if (this.RequestDrop(_carriedSlot, 1))
                {
                    this.ResetDragState();
                }
                else if (currentQty <= 1)
                {
                    this.ResetDragState();
                }
            }
            else
            {
                this.ResetDragState();
            }
        }
    }

    /// <summary>
    /// Drops <paramref name="quantity"/> of <paramref name="slotIndex"/>, or opens the confirmation first for rare,
    /// enhanced or equipped items (ADR-020). Returns <see langword="true"/> when the confirmation opened.
    /// </summary>
    public bool RequestDrop(int slotIndex, int quantity)
    {
        if (_hud is null || slotIndex < 0 || slotIndex >= PlayerInventoryLayout.TotalCapacity)
        {
            return false;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        _session.CopyInventorySlots(slots);
        ItemSlot item = slots[slotIndex];
        if (InventoryActionPolicy.RequiresDropConfirmation(_session.Items.Rules, in item, slotIndex))
        {
            _hud.InventoryDialog.OpenConfirmDrop(slotIndex, in item, quantity);
            return true;
        }

        _ = _session.TryDropInventoryItem((ushort)slotIndex, quantity);
        return false;
    }

    /// <summary>Routes input to the open split/confirm dialog. Returns <see langword="true"/> when the dialog took the frame.</summary>
    private bool HandleInventoryDialogInput(in DesktopInputFrame input, ReadOnlySpan<ItemSlot> slots, int viewportWidth, int viewportHeight)
    {
        InventoryDialogState dialog = _hud!.InventoryDialog;
        if (!dialog.IsOpen)
        {
            return false;
        }

        if (dialog.SlotIndex < 0 || dialog.SlotIndex >= slots.Length || dialog.IsStale(in slots[dialog.SlotIndex]))
        {
            dialog.Close();
            return true;
        }

        InventoryDialogButton button = input.LeftClickPressed
            ? _hud.HitTestInventoryDialog(input.MouseScreenPosition, viewportWidth, viewportHeight)
            : InventoryDialogButton.Body;
        if (input.ConfirmPressed)
        {
            button = InventoryDialogButton.Confirm;
        }

        switch (button)
        {
            case InventoryDialogButton.Minus:
                dialog.Adjust(input.ShiftHeld ? -10 : -1);
                break;
            case InventoryDialogButton.Plus:
                dialog.Adjust(input.ShiftHeld ? 10 : 1);
                break;
            case InventoryDialogButton.Confirm:
                ushort slot = (ushort)dialog.SlotIndex;
                _ = dialog.Kind switch
                {
                    InventoryDialogKind.Split => _session.TrySplitInventorySlot(slot, dialog.Quantity),
                    InventoryDialogKind.ConfirmDrop => _session.TryDropInventoryItem(slot, dialog.Quantity),
                    InventoryDialogKind.ConfirmDestroy => _session.TryDestroyInventoryItem(slot, dialog.Quantity),
                    InventoryDialogKind.None or _ => false,
                };
                dialog.Close();
                break;
            case InventoryDialogButton.Cancel:
            case InventoryDialogButton.None when input.LeftClickPressed:
                dialog.Close();
                break;
            case InventoryDialogButton.None:
            case InventoryDialogButton.Body:
            default:
                break;
        }

        return true;
    }

    /// <summary>With the inventory open, "hover a slot and press 1..9" puts that item on the hotbar slot (ADR-020).</summary>
    public bool TryBindHoveredSlotToHotbar(byte hotbarIndex, Vector2 mouseScreen, int viewportWidth, int viewportHeight)
    {
        if (_hud is null ||
            _hud.InventoryDialog.IsOpen ||
            !_hud.TryHitTestInventorySlot(mouseScreen, viewportWidth, viewportHeight, out ushort hovered))
        {
            return false;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        _session.CopyInventorySlots(slots);
        if (!InventoryActionPolicy.TryResolveHotbarBind(slots, hovered, hotbarIndex, out ushort from, out ushort to))
        {
            return hovered < slots.Length && !slots[hovered].IsEmpty;
        }

        this.ResetDragState();
        _ = _session.TryMoveInventorySlot(from, to);
        return true;
    }

    public void ResetDragState()
    {
        _carriedSlot = -1;
        _isHoldDragging = false;
        _hud?.CarriedInventorySlot = -1;
    }
}
