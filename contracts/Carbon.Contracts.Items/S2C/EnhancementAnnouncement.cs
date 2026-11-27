// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// World-wide push (ADR-024) sent to every player in the world when someone reaches a high enhancement level or
/// destroys an item at the forge (thresholds in <c>enhancement.json</c> <c>announce</c>).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EnhancementAnnouncement : PacketBase<EnhancementAnnouncement>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.EnhancementAnnouncement;

    /// <summary>Network id of the player who forged.</summary>
    [SerializeOrder(0)]
    public ulong PlayerNetworkId { get; set; }

    /// <summary>Character name of the player who forged.</summary>
    [SerializeOrder(1)]
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>Item id that was enhanced or destroyed.</summary>
    [SerializeOrder(2)]
    public ushort ItemId { get; set; }

    /// <summary><see cref="EnhancementOutcome.Success"/> or <see cref="EnhancementOutcome.Destroyed"/>.</summary>
    [SerializeOrder(3)]
    public EnhancementOutcome Outcome { get; set; }

    /// <summary>Level reached (success) or the level the item had when it broke (destroyed).</summary>
    [SerializeOrder(4)]
    public byte Level { get; set; }
}
