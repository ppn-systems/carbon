// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Which inventory dialog is open.</summary>
public enum InventoryDialogKind : byte
{
    /// <summary>No dialog.</summary>
    None = 0,

    /// <summary>Choose how many units to split off a stack.</summary>
    Split = 1,

    /// <summary>Confirm dropping a valuable item on the ground.</summary>
    ConfirmDrop = 2,

    /// <summary>Confirm destroying an item permanently.</summary>
    ConfirmDestroy = 3,
}

/// <summary>
/// State of the inventory split and confirmation dialogs (ADR-020 D7). It remembers the item the dialog was opened for,
/// so <see cref="IsStale"/> can close it when the slot changes underneath it (the server checks again with
/// <c>ExpectedItemId</c>).
/// </summary>
public sealed class InventoryDialogState
{
    /// <summary>Gets the open dialog.</summary>
    public InventoryDialogKind Kind { get; private set; }

    /// <summary>Gets the slot the dialog acts on.</summary>
    public int SlotIndex { get; private set; } = -1;

    /// <summary>Gets the item the slot held when the dialog opened.</summary>
    public ItemSlot Item { get; private set; }

    /// <summary>Gets the chosen quantity (<c>&lt;= 0</c> = the whole stack for drop and destroy).</summary>
    public int Quantity { get; private set; }

    /// <summary>Gets whether any dialog is open.</summary>
    public bool IsOpen => this.Kind != InventoryDialogKind.None;

    /// <summary>Opens the split dialog at half the stack. Returns <see langword="false"/> when the slot cannot be split.</summary>
    public bool OpenSplit(int slotIndex, in ItemSlot item)
    {
        if (!InventoryActionPolicy.CanSplit(in item, slotIndex))
        {
            return false;
        }

        this.Open(InventoryDialogKind.Split, slotIndex, in item, InventoryActionPolicy.DefaultSplitQuantity(item.Quantity));
        return true;
    }

    /// <summary>Opens the drop confirmation for <paramref name="quantity"/> units (<c>&lt;= 0</c> = all).</summary>
    public void OpenConfirmDrop(int slotIndex, in ItemSlot item, int quantity) =>
        this.Open(InventoryDialogKind.ConfirmDrop, slotIndex, in item, quantity);

    /// <summary>Opens the destroy confirmation for the whole stack.</summary>
    public void OpenConfirmDestroy(int slotIndex, in ItemSlot item) =>
        this.Open(InventoryDialogKind.ConfirmDestroy, slotIndex, in item, 0);

    /// <summary>Changes the split amount by <paramref name="delta"/>, kept within <c>1..stack-1</c>.</summary>
    public void Adjust(int delta)
    {
        if (this.Kind == InventoryDialogKind.Split)
        {
            this.Quantity = InventoryActionPolicy.ClampSplitQuantity(this.Quantity + delta, this.Item.Quantity);
        }
    }

    /// <summary>Gets the units the confirmed action affects.</summary>
    public int EffectiveQuantity => this.Quantity <= 0 || this.Quantity >= this.Item.Quantity ? this.Item.Quantity : this.Quantity;

    /// <summary>Gets whether <paramref name="current"/> (the slot now) is no longer the item the dialog opened for.</summary>
    public bool IsStale(in ItemSlot current) =>
        this.IsOpen && (current.ItemId != this.Item.ItemId || current.Quantity < this.EffectiveQuantity ||
            (this.Kind == InventoryDialogKind.Split && current.Quantity < 2));

    /// <summary>Closes the dialog.</summary>
    public void Close()
    {
        this.Kind = InventoryDialogKind.None;
        this.SlotIndex = -1;
        this.Item = ItemSlot.Empty;
        this.Quantity = 0;
    }

    private void Open(InventoryDialogKind kind, int slotIndex, in ItemSlot item, int quantity)
    {
        this.Kind = kind;
        this.SlotIndex = slotIndex;
        this.Item = item;
        this.Quantity = quantity;
    }
}
