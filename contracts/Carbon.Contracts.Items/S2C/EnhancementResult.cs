// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// Server reply to <see cref="C2S.UpgradeInventoryItem"/> (ADR-020): whether the attempt ran and, when it did, the
/// server-rolled outcome and the item's level afterwards. The changed slots arrive as <see cref="InventorySlotChanged"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EnhancementResult : PacketBase<EnhancementResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.EnhancementResult;

    /// <summary><see cref="OpStatus.Ok"/> when the attempt ran (any outcome), otherwise why it was rejected (nothing spent).</summary>
    [SerializeOrder(0)]
    public OpStatus Status { get; set; }

    /// <summary>The slot the request named.</summary>
    [SerializeOrder(1)]
    public ushort SlotIndex { get; set; }

    /// <summary>Item id that was enhanced (also set when it was destroyed).</summary>
    [SerializeOrder(2)]
    public ushort ItemId { get; set; }

    /// <summary>The rolled outcome (<see cref="EnhancementOutcome.None"/> when <see cref="Status"/> is not Ok).</summary>
    [SerializeOrder(3)]
    public EnhancementOutcome Outcome { get; set; }

    /// <summary>Level before the attempt.</summary>
    [SerializeOrder(4)]
    public byte PreviousLevel { get; set; }

    /// <summary>Level after the attempt (<c>0</c> when destroyed).</summary>
    [SerializeOrder(5)]
    public byte NewLevel { get; set; }

    /// <summary>Whether the item was destroyed.</summary>
    [SerializeOrder(6)]
    public bool IsDestroyed { get; set; }

    /// <summary>Whether a protection scroll was spent and changed the outcome or guarded it.</summary>
    [SerializeOrder(7)]
    public bool ProtectionConsumed { get; set; }
}
