// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// ADR-044: an <c>IReusableAttachment</c> released by a despawn goes to the next entity, so nothing may keep one past the
/// call that fetched it from its store. A holder keeps the <c>Entity</c> handle instead: a stale handle fails the store's
/// generation check. This guard finds every field, property or closure that would keep a reusable attachment.
/// </summary>
public sealed class AttachmentReuseGuardTests
{
    private static readonly string[] s_sourceFolders = ["engine", "server", "desktop", "wasm", "graphics"];

    [Fact]
    public void ReusableAttachments_AreFound() =>
        ReusableTypeNames().Should().Contain(["StatSheet", "ThreatTable", "PathWaypointBuffer"]);

    [Fact]
    public void ReusableAttachments_ShouldNotBeKeptInAFieldPropertyOrClosure()
    {
        HashSet<string> reusable = ReusableTypeNames();
        List<string> violations = [];
        foreach ((string path, SyntaxTree tree) in LocalizationRepository.ParseSources(s_sourceFolders))
        {
            SyntaxNode root = tree.GetRoot();
            foreach (MemberDeclarationSyntax member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                TypeSyntax? type = member switch
                {
                    FieldDeclarationSyntax field => field.Declaration.Type,
                    PropertyDeclarationSyntax property => property.Type,
                    _ => null,
                };
                if (type is not null && NamesReusable(type, reusable))
                {
                    violations.Add($"{path}:{Line(member)}: {member.GetType().Name.Replace("DeclarationSyntax", string.Empty, StringComparison.Ordinal).ToLowerInvariant()} of type {type}");
                }
            }

            foreach (SyntaxNode closure in root.DescendantNodes().Where(n => n is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            {
                foreach (string captured in CapturedReusableLocals(closure, reusable))
                {
                    violations.Add($"{path}:{Line(closure)}: closure captures '{captured}'");
                }
            }
        }

        Violations.ShouldBeEmpty(violations,
            "a reusable attachment kept past its call may see the next entity's state: keep the Entity handle and fetch from the store (ADR-044)");
    }

    /// <summary>Classes whose base list names <c>IReusableAttachment</c>.</summary>
    private static HashSet<string> ReusableTypeNames() =>
    [
        .. LocalizationRepository.ParseSources(s_sourceFolders)
            .SelectMany(static s => s.Tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(static c => c.BaseList?.Types.Any(static t => t.Type.ToString() == "IReusableAttachment") == true)
            .Select(static c => c.Identifier.ValueText),
    ];

    private static bool NamesReusable(TypeSyntax type, HashSet<string> reusable) =>
        type.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().Any(n => reusable.Contains(n.Identifier.ValueText));

    // Locals (and out variables) of a reusable type declared in the enclosing member but outside the closure, which the
    // closure refers to. A static closure cannot capture.
    private static IEnumerable<string> CapturedReusableLocals(SyntaxNode closure, HashSet<string> reusable)
    {
        if ((closure is AnonymousFunctionExpressionSyntax lambda && lambda.Modifiers.Any(SyntaxKind.StaticKeyword)) ||
            (closure is LocalFunctionStatementSyntax local && local.Modifiers.Any(SyntaxKind.StaticKeyword)))
        {
            yield break;
        }

        SyntaxNode? body = closure.Ancestors().FirstOrDefault(static a => a is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax);
        if (body is null)
        {
            yield break;
        }

        HashSet<string> outside = new(StringComparer.Ordinal);
        foreach (SyntaxNode node in body.DescendantNodes().Where(n => !closure.Span.Contains(n.Span)))
        {
            switch (node)
            {
                case VariableDeclarationSyntax declaration when NamesReusable(declaration.Type, reusable):
                    outside.UnionWith(declaration.Variables.Select(static v => v.Identifier.ValueText));
                    break;
                case DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation } expression when NamesReusable(expression.Type, reusable):
                    _ = outside.Add(designation.Identifier.ValueText);
                    break;
                case ParameterSyntax { Type: { } parameterType } parameter when NamesReusable(parameterType, reusable):
                    _ = outside.Add(parameter.Identifier.ValueText);
                    break;
                default:
                    break;
            }
        }

        foreach (string name in closure.DescendantNodes().OfType<IdentifierNameSyntax>().Select(static i => i.Identifier.ValueText).Distinct(StringComparer.Ordinal))
        {
            if (outside.Contains(name))
            {
                yield return name;
            }
        }
    }

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
