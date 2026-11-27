// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// Server push to a player who just died (ADR-031). The player stays dead until they send a
/// <see cref="C2S.RespawnRequest"/>. Ids only: the client localizes the killer and the text.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlayerDeath : PacketBase<PlayerDeath>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.PlayerDeath;

    /// <summary>What killed the player.</summary>
    [SerializeOrder(0)]
    public DeathCause Cause { get; set; }

    /// <summary>The killer's monster archetype id when <see cref="Cause"/> is <see cref="DeathCause.Monster"/>, otherwise 0.</summary>
    [SerializeOrder(1)]
    public ushort KillerArchetypeId { get; set; }

    /// <summary>The killer's network id (0 when none).</summary>
    [SerializeOrder(2)]
    public ulong KillerNetworkId { get; set; }

    /// <summary>Gold taken from the wallet and dropped as piles at the death spot.</summary>
    [SerializeOrder(3)]
    public long GoldDropped { get; set; }
}
