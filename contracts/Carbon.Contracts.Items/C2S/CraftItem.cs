// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client requests crafting one or more outputs of a recipe. Server responds with <see cref="Common.MutationResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CraftItem : PacketBase<CraftItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.CraftItem;

    /// <summary>Deterministic recipe identifier.</summary>
    [SerializeOrder(0)]
    public ushort RecipeId { get; set; }

    /// <summary>Craft repeat count (>= 1).</summary>
    [SerializeOrder(1)]
    public ushort Quantity { get; set; } = 1;
}
