// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Player-facing text speaks the player's language: no transport or wire-protocol names in any shipped locale.
/// Logs and developer tooling are not locale text and are not covered.
/// </summary>
public sealed partial class PlayerTextJargonTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("vi")]
    public void LocaleValues_ContainNoNetworkJargon(string locale)
    {
        LocalizationRepository.Tables.Should().ContainKey(locale, $"{locale}.ini ships with the client");
        IReadOnlyDictionary<string, string> table = LocalizationRepository.Tables[locale];

        string[] offenders = [.. table
            .Where(entry => NetworkJargon().IsMatch(entry.Value))
            .Select(entry => $"{entry.Key} = {entry.Value}")];

        offenders.Should().BeEmpty($"{locale}.ini is shown to players; say \"server\" or \"connection\" instead of transport or protocol names");
    }

    [Theory]
    [InlineData("Reconnected (TCP)")]
    [InlineData("opening udp")]
    [InlineData("KCP link")]
    [InlineData("WebSocket closed")]
    [InlineData("socket error")]
    [InlineData("bad opcode")]
    [InlineData("dropped packets")]
    public void NetworkJargon_MatchesBannedTermsAsWholeWordsAnyCase(string text) =>
        NetworkJargon().IsMatch(text).Should().BeTrue($"'{text}' names a transport or wire detail");

    [Theory]
    [InlineData("Connection lost. Reconnecting...")]
    [InlineData("Ping 42 ms")]
    [InlineData("Pocket watch")]
    [InlineData("Server unreachable")]
    public void NetworkJargon_IgnoresPlayerLanguage(string text) =>
        NetworkJargon().IsMatch(text).Should().BeFalse($"'{text}' is plain player language");

    [GeneratedRegex(@"\b(udp|tcp|kcp|websockets?|sockets?|opcodes?|packets?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NetworkJargon();
}
