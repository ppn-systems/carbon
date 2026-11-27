// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// The local player's stamina state that <c>PlayerStatsSync</c> does not carry (ADR-033): whether the body is exhausted
/// and how many actions the server has refused for lack of stamina. Pushed when either changes. The stamina value itself
/// stays in <c>PlayerStatsSync</c>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class StaminaStatusSync : PacketBase<StaminaStatusSync>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.StaminaStatusSync;

    /// <summary>Whether the player is exhausted: no stamina actions and no sprint until the pool recovers.</summary>
    [SerializeOrder(0)]
    public bool Exhausted { get; set; }

    /// <summary>
    /// Running count of actions refused for lack of stamina this session. The client flashes the stamina bar when it grows.
    /// </summary>
    [SerializeOrder(1)]
    public uint RefusalCount { get; set; }
}
