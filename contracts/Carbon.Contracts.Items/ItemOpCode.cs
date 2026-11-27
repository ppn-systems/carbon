// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Items;

/// <summary>
/// Carbon inventory, hotbar, and crafting protocol opcodes.
/// <para>Range <c>0x4000–0x40FF</c> is reserved for the Items module.</para>
/// </summary>
public static class ItemOpCode
{
    // ── C2S: Client → Server (0x4001–0x400F) ────────────────────────────────

    /// <summary>Client requests a full inventory slot sync.</summary>
    public const ushort InventoryRequest = 0x4001;

    /// <summary>Client selects an active hotbar tool/weapon slot.</summary>
    public const ushort SelectHotbarSlot = 0x4002;

    /// <summary>Client requests crafting a recipe.</summary>
    public const ushort CraftItem = 0x4003;

    /// <summary>Client requests swapping/moving/equipping an inventory or armor slot.</summary>
    public const ushort MoveInventorySlot = 0x4004;

    /// <summary>Client requests enhancing a tool, weapon, or armor piece in the Forge.</summary>
    public const ushort UpgradeInventoryItem = 0x4005;

    /// <summary>Client requests dropping an item stack onto the ground.</summary>
    public const ushort DropInventoryItem = 0x4006;

    /// <summary>Client requests buying items from a nearby NPC vendor for gold.</summary>
    public const ushort VendorBuyItem = 0x4007;

    /// <summary>Client requests selling a bag stack to a nearby NPC vendor for gold.</summary>
    public const ushort VendorSellItem = 0x4008;

    /// <summary>Client requests repairing an item's durability for gold at a forge station.</summary>
    public const ushort RepairInventoryItem = 0x4009;

    /// <summary>Client requests splitting part of a bag stack into another bag slot (ADR-020).</summary>
    public const ushort SplitInventorySlot = 0x400A;

    /// <summary>Client requests a server-side merge and sort of the backpack (ADR-020).</summary>
    public const ushort SortInventory = 0x400B;

    /// <summary>Client requests destroying items in a slot permanently (ADR-020).</summary>
    public const ushort DestroyInventoryItem = 0x400C;

    // ── S2C: Server → Client stream (0x4020–0x402F) ─────────────────────────

    /// <summary>Streamed inventory slot entry answering <see cref="InventoryRequest"/>.</summary>
    public const ushort InventorySlotItem = 0x4020;

    // ── S2C: Server → Client push (0x4030–0x40FF) ───────────────────────────

    /// <summary>Unilateral notification that a player inventory slot changed.</summary>
    public const ushort InventorySlotChanged = 0x4030;
}
