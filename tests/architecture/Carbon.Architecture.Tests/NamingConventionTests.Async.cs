// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>The <c>Async</c> suffix rule of the naming conventions (<c>csharp-style</c>, Framework Design Guidelines).</summary>
public sealed partial class NamingConventionTests
{
    private const BindingFlags DeclaredMethods =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void TaskReturningMethods_EndWithAsync()
    {
        List<string> violations = [];
        foreach ((ProductionProject _, Assembly assembly) in RepositoryLayout.Assemblies)
        {
            foreach (Type type in RepositoryLayout.GetTypesSafe(assembly).Where(t => !IsGenerated(t)))
            {
                violations.AddRange(type.GetMethods(DeclaredMethods)
                    .Where(m => IsAwaitable(m.ReturnType) && !m.IsSpecialName && !m.Name.StartsWith('<') &&
                        !m.Name.EndsWith("Async", StringComparison.Ordinal) && m.Name != "Main")
                    .Select(m => $"{type.FullName}.{m.Name}"));
            }
        }

        Violations.ShouldBeEmpty(violations.Distinct(StringComparer.Ordinal), "a method that returns Task or ValueTask ends in 'Async' so callers know to await it");
    }

    private static bool IsAwaitable(Type returnType)
    {
        Type definition = returnType.IsGenericType ? returnType.GetGenericTypeDefinition() : returnType;
        return definition == typeof(Task) || definition == typeof(Task<>) || definition == typeof(ValueTask) || definition == typeof(ValueTask<>);
    }

    private static bool IsGenerated(Type type)
    {
        for (Type? cursor = type; cursor is not null; cursor = cursor.DeclaringType)
        {
            if (cursor.IsDefined(typeof(CompilerGeneratedAttribute), false) || cursor.Name.StartsWith('<'))
            {
                return true;
            }
        }

        return false;
    }
}
