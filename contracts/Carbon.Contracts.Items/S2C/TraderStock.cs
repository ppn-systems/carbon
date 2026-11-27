// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// A trader's stock for one player (ADR-025): parallel arrays of item, unit price (gold, for this player's level), units
/// stocked this epoch and units left; the seconds until the server restocks; and the gold the trader pays for each item
/// the player carries. Sent on <see cref="C2S.TraderOpenRequest"/> and after every purchase or sale.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TraderStock : PacketBase<TraderStock>, IPacketStaticOpcode
{
    /// <summary>Trader kind: village market merchant.</summary>
    public const byte KindMerchant = 1;

    /// <summary>Trader kind: general goods trader.</summary>
    public const byte KindGeneral = 2;

    /// <summary>Trader kind: blacksmith (weapons, armor, tools, repair).</summary>
    public const byte KindBlacksmith = 3;

    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.TraderStock;

    /// <summary>Network id of the trader NPC.</summary>
    [SerializeOrder(0)]
    public ulong TraderNetworkId { get; set; }

    /// <summary>Trader kind (<see cref="KindMerchant"/>, <see cref="KindGeneral"/>, <see cref="KindBlacksmith"/>).</summary>
    [SerializeOrder(1)]
    public byte Kind { get; set; }

    /// <summary>Restock epoch the stock and prices belong to (echoed in <see cref="C2S.TraderBuyRequest.Epoch"/>).</summary>
    [SerializeOrder(2)]
    public long Epoch { get; set; }

    /// <summary>Seconds until the server restocks.</summary>
    [SerializeOrder(3)]
    public int SecondsUntilRestock { get; set; }

    /// <summary>Item id per stock slot.</summary>
    [SerializeOrder(4)]
    public ushort[] ItemIds { get; set; } = [];

    /// <summary>Gold per unit per stock slot.</summary>
    [SerializeOrder(5)]
    public int[] UnitPrices { get; set; } = [];

    /// <summary>Units stocked this epoch per stock slot.</summary>
    [SerializeOrder(6)]
    public ushort[] Stocked { get; set; } = [];

    /// <summary>Units still for sale per stock slot.</summary>
    [SerializeOrder(7)]
    public ushort[] Remaining { get; set; } = [];

    /// <summary>Items the player carries that the trader buys.</summary>
    [SerializeOrder(8)]
    public ushort[] SellItemIds { get; set; } = [];

    /// <summary>Gold the trader pays per unit of <see cref="SellItemIds"/>.</summary>
    [SerializeOrder(9)]
    public int[] SellPrices { get; set; } = [];

    /// <summary>Whether this trader also repairs gear at its forge (blacksmith).</summary>
    [SerializeOrder(10)]
    public bool CanRepair { get; set; }

    /// <summary>Retired by ADR-028: always empty (the repair window prices from the synced <c>repair.json</c>). Kept for the wire layout.</summary>
    [SerializeOrder(11)]
    public ushort[] RepairSlots { get; set; } = [];

    /// <summary>Retired by ADR-028: always empty. Kept for the wire layout.</summary>
    [SerializeOrder(12)]
    public int[] RepairCosts { get; set; } = [];
}
