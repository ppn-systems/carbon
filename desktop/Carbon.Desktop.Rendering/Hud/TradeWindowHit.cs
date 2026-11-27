// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>What the cursor is over in the trader window (ADR-025 D18).</summary>
public enum TradeWindowTarget : byte
{
    /// <summary>Nothing interactive.</summary>
    None = 0,

    /// <summary>A stock row (<see cref="TradeWindowHit.Index"/> = stock slot).</summary>
    Stock,

    /// <summary>An occupied bag cell (<see cref="TradeWindowHit.Index"/> = bag slot).</summary>
    Bag,

    /// <summary>The "To sell" card (a drop target for dragged bag items).</summary>
    SellArea,

    /// <summary>The cart card.</summary>
    BuyArea,

    /// <summary>The − of a basket line (<see cref="TradeWindowHit.Index"/> = line).</summary>
    SellMinus,

    /// <summary>The + of a basket line.</summary>
    SellPlus,

    /// <summary>The − of a cart line.</summary>
    BuyMinus,

    /// <summary>The + of a cart line.</summary>
    BuyPlus,

    /// <summary>The Sell button.</summary>
    SellButton,

    /// <summary>The Buy button.</summary>
    BuyButton,

    /// <summary>The basket's Clear button.</summary>
    ClearSell,

    /// <summary>The cart's Clear button.</summary>
    ClearBuy,

    /// <summary>The blacksmith's Repair button (opens the repair window, ADR-028).</summary>
    Repair,

    /// <summary>The blacksmith's Enhance button (hands off to the forge window, ADR-024).</summary>
    Enhance,
}

/// <summary>A hit-test result of the trader window.</summary>
/// <param name="Target">What was hit.</param>
/// <param name="Index">Stock slot, bag slot or line index, or -1.</param>
public readonly record struct TradeWindowHit(TradeWindowTarget Target, int Index = -1)
{
    /// <summary>Gets the empty hit.</summary>
    public static TradeWindowHit None => new(TradeWindowTarget.None);

    /// <summary>Gets whether the hit lands on the "To sell" card or one of its controls.</summary>
    public bool IsSellCard => this.Target is TradeWindowTarget.SellArea or TradeWindowTarget.SellMinus or TradeWindowTarget.SellPlus or
        TradeWindowTarget.SellButton or TradeWindowTarget.ClearSell;
}
