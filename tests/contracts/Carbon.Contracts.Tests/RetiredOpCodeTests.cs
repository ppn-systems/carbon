// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Carbon.Contracts.Common;
using Carbon.Contracts.Config;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Contracts.Items;
using Carbon.Contracts.Maps;
using Nalix.Abstractions.Networking.Packets;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>
/// ADR-038 D3: a retired opcode is never reused, so an old client that still sends it reaches no handler instead of a
/// different packet. ADR-028 retired <c>0x4009</c>; ADR-038 retired the rest.
/// </summary>
public sealed class RetiredOpCodeTests
{
    private static readonly Type[] s_opCodeClasses =
    [
        typeof(SharedOpCode), typeof(ConfigOpCode), typeof(IdentityOpCode), typeof(MapOpCode), typeof(EntityOpCode), typeof(ItemOpCode),
    ];

    public static TheoryData<ushort> Retired => new()
    {
        0x1101, // MapWorldInfoRequest
        0x1110, // MapWorldInfo
        0x1130, // MapTileChanged
        0x4007, // VendorBuyItem
        0x4008, // VendorSellItem
        0x4009, // RepairInventoryItem (ADR-028)
        0x4041, // TraderBuyRequest
        0x4042, // TraderSellRequest
    };

    [Theory]
    [MemberData(nameof(Retired))]
    public void RetiredOpCode_IsNeitherDeclaredNorBoundToAPacket(ushort opCode)
    {
        IEnumerable<string> constants = s_opCodeClasses
            .SelectMany(static t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (Type: t, Field: f)))
            .Where(c => c.Field.IsLiteral && c.Field.GetValue(null) is ushort value && value == opCode)
            .Select(static c => $"{c.Type.Name}.{c.Field.Name}");
        IEnumerable<string> packets = s_opCodeClasses
            .Select(static t => t.Assembly)
            .Distinct()
            .SelectMany(static a => a.GetTypes())
            .Where(static t => t is { IsClass: true, IsAbstract: false } && typeof(IPacketStaticOpcode).IsAssignableFrom(t))
            .Where(t => (ushort)t.GetProperty(nameof(IPacketStaticOpcode.StaticOpCode), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)! == opCode)
            .Select(static t => t.Name);

        Assert.Empty(constants);
        Assert.Empty(packets);
    }

    [Fact]
    public void Version_IsAtLeastFourSinceTheDeadOpCodesWereRetired() =>
        Assert.True(CarbonProtocol.Version >= 4, $"protocol {CarbonProtocol.Version} still accepts the opcodes ADR-038 retired");
}
