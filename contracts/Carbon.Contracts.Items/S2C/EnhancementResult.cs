// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// Server reply to <see cref="C2S.UpgradeInventoryItem"/> (ADR-024): whether the attempt ran and, when it did, the
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

    // [SerializeOrder(1)] was SlotIndex, never read; removed by ADR-038.

    /// <summary>Item id that was enhanced (also set when it was destroyed).</summary>
    [SerializeOrder(2)]
    public ushort ItemId { get; set; }

    /// <summary>The rolled outcome (<see cref="EnhancementOutcome.None"/> when <see cref="Status"/> is not Ok).</summary>
    [SerializeOrder(3)]
    public EnhancementOutcome Outcome { get; set; }

    // [SerializeOrder(4)] was PreviousLevel, never read; removed by ADR-038.

    /// <summary>Level after the attempt (<c>0</c> when destroyed).</summary>
    [SerializeOrder(5)]
    public byte NewLevel { get; set; }

    // [SerializeOrder(6)] was IsDestroyed, never read; removed by ADR-038.

    /// <summary>Whether a protection scroll was spent and changed the outcome or guarded it.</summary>
    [SerializeOrder(7)]
    public bool ProtectionConsumed { get; set; }
}
