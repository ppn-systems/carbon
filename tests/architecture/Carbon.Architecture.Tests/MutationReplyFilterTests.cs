// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// ADR-038: every client wait for a <c>MutationResult</c> filters by <c>RequestOpCode</c>. Every write handler answers
/// with a <c>MutationResult</c> and map edits are sent without waiting, so an unfiltered wait can take another request's
/// reply as its own.
/// </summary>
public sealed class MutationReplyFilterTests
{
    [Fact]
    public void ClientWaitsForAMutationResult_ShouldPassAReplyFilter()
    {
        Violations.ShouldBeEmpty(
            LocalizationRepository.ParseSources("desktop", "wasm")
                .SelectMany(static s => s.Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(static call => call.Expression is MemberAccessExpressionSyntax
                    {
                        Name: GenericNameSyntax { Identifier.ValueText: "RequestAsync", TypeArgumentList.Arguments: [IdentifierNameSyntax { Identifier.ValueText: "MutationResult" }] },
                    })
                    .Where(static call => call.ArgumentList.Arguments.Count < 3 || call.ArgumentList.Arguments[2].Expression.IsKind(SyntaxKind.NullLiteralExpression))
                    .Select(call => $"{s.Path}:{call.GetLocation().GetLineSpan().StartLinePosition.Line + 1}")),
            "pass a MutationReplies filter so the wait takes only the reply to its own request");
    }
}
