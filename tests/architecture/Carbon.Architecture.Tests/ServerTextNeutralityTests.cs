// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// The server stays language-neutral (ADR-023 D4): server-to-client packets carry codes, enums and stable ids, and the
/// client localizes them. A new <c>string</c> field on an S2C packet is where translated prose would creep in.
/// </summary>
public sealed class ServerTextNeutralityTests
{
    /// <summary>S2C string fields that are not prose. Entry format: <c>Packet.Property</c>. Do NOT add prose fields.</summary>
    private static readonly HashSet<string> s_allowlist = new(StringComparer.Ordinal)
    {
        // Config document names (identifiers such as "items").
        "ConfigDocument.Name",
        "ConfigManifest.Names",

        // Names players chose.
        "CharacterSummary.Name",
        "EnhancementAnnouncement.PlayerName",

        // Player names; for monsters, NPCs and loot only the English fallback, since the client localizes those from ArchetypeId.
        "EntitySpawn.Name",
    };

    [Fact]
    public void ServerToClientPackets_ShouldNotGainTextFields()
    {
        Violations.ShouldBeEmpty(
            FindStringFields().Where(f => !s_allowlist.Contains(f)),
            "send a code, enum or stable id and localize it on the client (ADR-023 D4)");
    }

    [Fact]
    public void Allowlist_ShouldNotContainStaleEntries()
    {
        HashSet<string> found = [.. FindStringFields()];
        Violations.ShouldBeEmpty(s_allowlist.Where(e => !found.Contains(e)), "removed fields must be removed from the allowlist");
    }

    private static IEnumerable<string> FindStringFields()
    {
        foreach ((string path, Microsoft.CodeAnalysis.SyntaxTree tree) in LocalizationRepository.ParseSources("contracts"))
        {
            if (!path.Contains("/S2C/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (PropertyDeclarationSyntax property in tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                string type = property.Type.ToString().TrimEnd('?');
                if (type is "string" or "string[]" or "String" or "IReadOnlyList<string>" or "List<string>")
                {
                    string owner = property.Ancestors().OfType<TypeDeclarationSyntax>().First().Identifier.Text;
                    yield return $"{owner}.{property.Identifier.Text}";
                }
            }
        }
    }
}
