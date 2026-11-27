// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Application.World.Inventory;

/// <summary>Kind of an <see cref="InventorySlotCommand"/>.</summary>
internal enum InventorySlotCommandKind : byte
{
    /// <summary>Move, swap, merge or equip <c>SlotA</c> to <c>SlotB</c>.</summary>
    Move = 0,

    /// <summary>Drop <c>Quantity</c> of <c>SlotA</c> on the ground (<c>&lt;= 0</c> = all).</summary>
    Drop = 1,

    /// <summary>Move <c>Quantity</c> units of bag slot <c>SlotA</c> to bag slot <c>SlotB</c> (ADR-020).</summary>
    Split = 2,

    /// <summary>Destroy <c>Quantity</c> of <c>SlotA</c> (<c>&lt;= 0</c> = all) (ADR-020).</summary>
    Destroy = 3,

    /// <summary>Merge and sort the backpack (ADR-020).</summary>
    Sort = 4,
}

/// <summary>
/// One player inventory slot command, run on the tick by <see cref="InventoryService.Execute"/>.
/// <see cref="ExpectedItemId"/> binds the command to the item the client saw in <see cref="SlotA"/> (<c>0</c> = unchecked).
/// </summary>
internal readonly record struct InventorySlotCommand(
    InventorySlotCommandKind Kind,
    ushort SlotA,
    ushort SlotB = 0,
    int Quantity = 0,
    ushort ExpectedItemId = 0);
