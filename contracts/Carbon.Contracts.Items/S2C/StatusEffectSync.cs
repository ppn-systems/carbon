// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// The local player's active status effects (ADR-029 D9), as parallel arrays. Sent to the owning connection only, at
/// world entry and whenever an effect is added, stacked, refreshed, replaced or removed; the client counts the remaining
/// time down locally between syncs. Carries ids and numbers only; names and descriptions are client-localized.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class StatusEffectSync : PacketBase<StatusEffectSync>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.StatusEffectSync;

    /// <summary>Effect id per entry (<c>status_effects.json</c> <c>id</c>).</summary>
    [SerializeOrder(0)]
    public ushort[] EffectIds { get; set; } = [];

    /// <summary>Stacks per entry (severity for derived effects).</summary>
    [SerializeOrder(1)]
    public byte[] Stacks { get; set; } = [];

    /// <summary>Remaining milliseconds per entry; 0 while a derived condition holds.</summary>
    [SerializeOrder(2)]
    public uint[] RemainingMs { get; set; } = [];

    /// <summary>Full duration of the latest application per entry in milliseconds; 0 for derived effects.</summary>
    [SerializeOrder(3)]
    public uint[] DurationMs { get; set; } = [];

    /// <summary>Damage per pulse (all stacks) for damage effects, otherwise the effect's multiplier, per entry.</summary>
    [SerializeOrder(4)]
    public float[] Magnitudes { get; set; } = [];

    /// <summary>Source kind per entry (none, monster, food, needs, heavy hit, sleep).</summary>
    [SerializeOrder(5)]
    public byte[] SourceKinds { get; set; } = [];

    /// <summary>Source detail per entry: the monster archetype id, or 0.</summary>
    [SerializeOrder(6)]
    public ushort[] SourceIds { get; set; } = [];
}
