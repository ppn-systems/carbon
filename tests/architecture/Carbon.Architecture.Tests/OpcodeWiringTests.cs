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
using Carbon.Handlers.Maps;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nalix.Abstractions.Networking.Packets;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// ADR-038 D2: every packet on the wire has a sender and a handler. A C2S packet is sent by the client and handled by
/// a server <c>[PacketOpcode]</c> handler. Every other packet (S2C, and the shared <c>MutationResult</c>) is sent by
/// the server and received by the client through <c>On&lt;T&gt;</c>, <c>RequestAsync&lt;T&gt;</c> or
/// <c>StreamAsync&lt;T&gt;</c>. A packet that loses either end is dead and is retired (opcode and type), not kept.
/// </summary>
/// <remarks>
/// A side "sends" <c>T</c> when its sources rent <c>PacketFactory&lt;T&gt;</c> or lease a <c>PacketLease&lt;T&gt;</c>
/// (ADR-043), call <c>T.Create()</c> or <c>new T</c>, or call one of its own generic helpers with <c>T</c>, where the
/// helper rents its own type parameter.
/// </remarks>
public sealed class OpcodeWiringTests
{
    private static readonly Assembly[] s_contractAssemblies =
    [
        typeof(SharedOpCode).Assembly,
        typeof(ConfigOpCode).Assembly,
        typeof(IdentityOpCode).Assembly,
        typeof(MapOpCode).Assembly,
        typeof(EntityOpCode).Assembly,
        typeof(ItemOpCode).Assembly,
    ];

    private static readonly string[] s_clientReceiveMethods = ["On", "RequestAsync", "StreamAsync"];

    [Fact]
    public void ClientToServerPackets_ShouldHaveAClientSenderAndAServerHandler()
    {
        HashSet<string> clientSends = Sent("desktop", "wasm");
        HashSet<ushort> serverHandles = ServerHandledOpCodes();

        Violations.ShouldBeEmpty(
            Packets(c2s: true).SelectMany(p => Missing(p, clientSends.Contains(p.Type.Name), "no client sender", serverHandles.Contains(p.OpCode), "no server handler")),
            "a C2S packet nobody sends, or nobody handles, is dead: retire its opcode and type (ADR-038)");
    }

    [Fact]
    public void ServerToClientPackets_ShouldHaveAServerSenderAndAClientHandler()
    {
        HashSet<string> serverSends = Sent("server");
        HashSet<string> clientReceives = ClientReceived();

        Violations.ShouldBeEmpty(
            Packets(c2s: false).SelectMany(p => Missing(p, serverSends.Contains(p.Type.Name), "no server sender", clientReceives.Contains(p.Type.Name), "no client handler")),
            "an S2C packet nobody sends, or nobody receives, is dead: retire its opcode and type (ADR-038)");
    }

    private static IEnumerable<string> Missing(PacketInfo packet, bool sent, string notSent, bool handled, string notHandled)
    {
        if (!sent)
        {
            yield return $"0x{packet.OpCode:X4} {packet.Type.Name}: {notSent}";
        }

        if (!handled)
        {
            yield return $"0x{packet.OpCode:X4} {packet.Type.Name}: {notHandled}";
        }
    }

    private static IEnumerable<PacketInfo> Packets(bool c2s) =>
        s_contractAssemblies
            .SelectMany(static a => a.GetTypes())
            .Where(static t => t is { IsClass: true, IsAbstract: false } && typeof(IPacketStaticOpcode).IsAssignableFrom(t))
            .Where(t => (t.Namespace?.EndsWith(".C2S", StringComparison.Ordinal) ?? false) == c2s)
            .Select(static t => new PacketInfo(t, (ushort)t.GetProperty(nameof(IPacketStaticOpcode.StaticOpCode), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!));

    /// <summary>Opcodes named by a <c>[PacketOpcode]</c> handler method in <c>Carbon.Handlers</c>.</summary>
    private static HashSet<ushort> ServerHandledOpCodes() =>
    [
        .. typeof(MapHandlers).Assembly.GetTypes()
            .SelectMany(static t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(static m => m.GetCustomAttributesData())
            .Where(static a => a.AttributeType.Name == "PacketOpcodeAttribute" && a.ConstructorArguments.Count > 0)
            .Select(static a => Convert.ToUInt16(a.ConstructorArguments[0].Value, System.Globalization.CultureInfo.InvariantCulture)),
    ];

    /// <summary>Packet type names a side's sources create, rent, or pass to one of their own renting generic helpers.</summary>
    private static HashSet<string> Sent(params string[] directories)
    {
        List<SyntaxNode> roots = [.. LocalizationRepository.ParseSources(directories).Select(static s => s.Tree.GetRoot())];
        HashSet<string> rentingHelpers = new(StringComparer.Ordinal);
        foreach (MethodDeclarationSyntax method in roots.SelectMany(static r => r.DescendantNodes().OfType<MethodDeclarationSyntax>()))
        {
            HashSet<string> typeParameters = [.. method.TypeParameterList?.Parameters.Select(static p => p.Identifier.ValueText) ?? []];
            if (typeParameters.Count > 0 && method.DescendantNodes().OfType<GenericNameSyntax>().Any(g => IsPacketSendSource(g) && typeParameters.Contains(TypeArgumentName(g))))
            {
                rentingHelpers.Add(method.Identifier.ValueText);
            }
        }

        HashSet<string> sent = new(StringComparer.Ordinal);
        foreach (SyntaxNode node in roots.SelectMany(static r => r.DescendantNodes()))
        {
            switch (node)
            {
                case GenericNameSyntax generic when IsPacketSendSource(generic) || rentingHelpers.Contains(generic.Identifier.ValueText):
                    sent.Add(TypeArgumentName(generic));
                    break;
                case ObjectCreationExpressionSyntax creation:
                    sent.Add(SimpleName(creation.Type));
                    break;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Create" } access }:
                    sent.Add(SimpleName(access.Expression));
                    break;
                default:
                    break;
            }
        }

        return sent;
    }

    /// <summary>Packet type names the client subscribes to, requests, or streams.</summary>
    private static HashSet<string> ClientReceived() =>
    [
        .. LocalizationRepository.ParseSources("desktop", "wasm")
            .SelectMany(static s => s.Tree.GetRoot().DescendantNodes().OfType<GenericNameSyntax>())
            .Where(static g => s_clientReceiveMethods.Contains(g.Identifier.ValueText, StringComparer.Ordinal))
            .Select(TypeArgumentName),
    ];

    private static bool IsPacketSendSource(GenericNameSyntax generic) => generic.Identifier.ValueText is "PacketFactory" or "PacketLease";

    private static string TypeArgumentName(GenericNameSyntax generic) =>
        generic.TypeArgumentList.Arguments.Count == 1 ? SimpleName(generic.TypeArgumentList.Arguments[0]) : string.Empty;

    private static string SimpleName(SyntaxNode node) => node switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => string.Empty,
    };

    private sealed record PacketInfo(Type Type, ushort OpCode);
}
