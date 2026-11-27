// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.World;

/// <summary>One staged line of the trade window's sell basket or buy cart (ADR-025 D18).</summary>
/// <param name="Slot">Bag slot (basket) or stock slot (cart).</param>
/// <param name="ItemId">Item the player saw in that slot (the server refuses the batch if it changed).</param>
/// <param name="Quantity">Units staged.</param>
/// <param name="UnitPrice">Gold per unit, from the server's last <c>TraderStock</c>.</param>
/// <param name="UpgradeLevel">Forge enhancement level of a basket item (selling one asks for a confirm).</param>
public readonly record struct TradeBasketLine(int Slot, uint ItemId, int Quantity, int UnitPrice, byte UpgradeLevel = 0)
{
    /// <summary>Gets the line's gold (unit price × units).</summary>
    public long Subtotal => (long)this.UnitPrice * this.Quantity;
}
