// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// ECS and type-design rules for <c>engine/</c> (see the carbon-engine-ecs and csharp-style skills).
/// </summary>
public sealed class EngineDesignTests
{
    /// <summary>
    /// Instance methods compilers synthesize for records/structs; they are not "behavior".
    /// </summary>
    private static readonly HashSet<string> s_synthesizedMethodNames = new(StringComparer.Ordinal)
    {
        "ToString", "Equals", "GetHashCode", "PrintMembers", "Deconstruct", "<Clone>$",
    };

    /// <summary>
    /// Existing ECS component types that carry instance methods (behavior).
    /// Entry format: <c>Namespace.Type.Method</c>. Do NOT add new entries: move logic into a system or a static helper.
    /// </summary>
    private static readonly HashSet<string> s_componentBehaviorAllowlist = new(StringComparer.Ordinal)
    {
        // Pure geometry helper (position + offset -> AABB). Audit: move to a static Collider2DMath helper.
        "Carbon.Simulation.ECS.Components.Collider2D.GetWorldAabb",

        // Fixed-capacity mood-memory buffer accessors, added in #14 (survival, owned by a parallel agent).
        // Audit: move to a static NeedsMemory helper operating on ref NeedsComponent.
        "Carbon.Survival.Components.NeedsComponent.AddMemory",
        "Carbon.Survival.Components.NeedsComponent.GetMemory",
    };

    /// <summary>
    /// Existing public, non-sealed, non-abstract engine classes that are not inherited anywhere.
    /// Entry format: full type name. Do NOT add new entries: seal the class (or make it abstract/static).
    /// </summary>
    private static readonly HashSet<string> s_unsealedPublicClassAllowlist = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void EcsComponents_ShouldBeUnmanagedStructs()
    {
        List<string> violations = [.. EcsComponentTypes()
            .Where(t => !t.IsValueType || IsReferenceOrContainsReferences(t))
            .Select(t => $"{t.FullName} ({(t.IsValueType ? "struct containing managed references" : "reference type")})")];

        Violations.ShouldBeEmpty(violations, "ECS components must be unmanaged structs (EntityManager stores them in ComponentPool<T> where T : unmanaged)");
    }

    [Fact]
    public void EcsComponents_ShouldNotDeclareBehaviorMethods()
    {
        List<string> violations = [];
        foreach (Type type in EcsComponentTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName || s_synthesizedMethodNames.Contains(method.Name) || method.IsDefined(typeof(CompilerGeneratedAttribute), false))
                {
                    continue;
                }

                string key = $"{type.FullName}.{method.Name}";
                if (!s_componentBehaviorAllowlist.Contains(key))
                {
                    violations.Add(key);
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "components are plain data; logic belongs in stateless systems (derived read-only properties are fine)");
    }

    [Fact]
    public void PublicEngineClasses_ShouldBeSealedAbstractStaticOrInherited()
    {
        HashSet<Type> baseTypes = [.. RepositoryLayout.Assemblies
            .SelectMany(a => RepositoryLayout.GetTypesSafe(a.Assembly))
            .Select(t => t.BaseType)
            .Where(b => b is not null)
            .Select(b => b!.IsGenericType ? b.GetGenericTypeDefinition() : b)];

        List<string> violations = [.. RepositoryLayout.AssembliesIn("engine")
            .SelectMany(a => a.Assembly.GetExportedTypes())
            .Where(t => t.IsClass && !t.IsSealed && !t.IsAbstract && !typeof(Delegate).IsAssignableFrom(t))
            .Where(t => !baseTypes.Contains(t.IsGenericType ? t.GetGenericTypeDefinition() : t))
            .Select(t => t.FullName!)
            .Where(name => !s_unsealedPublicClassAllowlist.Contains(name))
            .OrderBy(n => n, StringComparer.Ordinal)];

        Violations.ShouldBeEmpty(violations, "public engine classes must be sealed unless designed (and used) for inheritance");
    }

    [Fact]
    public void UnsealedPublicClassAllowlist_ShouldNotContainStaleEntries()
    {
        HashSet<string> exported = [.. RepositoryLayout.AssembliesIn("engine")
            .SelectMany(a => a.Assembly.GetExportedTypes())
            .Where(t => t.IsClass && !t.IsSealed && !t.IsAbstract)
            .Select(t => t.FullName!)];

        Violations.ShouldBeEmpty(s_unsealedPublicClassAllowlist.Where(e => !exported.Contains(e)), "fixed entries must be removed from the allowlist");
    }

    /// <summary>
    /// ECS components: engine types in a <c>*.Components</c> namespace, or whose name ends in <c>Component</c>.
    /// </summary>
    private static IEnumerable<Type> EcsComponentTypes() =>
        RepositoryLayout.AssembliesIn("engine")
            .SelectMany(a => RepositoryLayout.GetTypesSafe(a.Assembly))
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false) && !t.IsInterface && !t.IsEnum && !t.IsAbstract)
            .Where(t => (t.Namespace?.EndsWith(".Components", StringComparison.Ordinal) ?? false) ||
                        (t.Name.EndsWith("Component", StringComparison.Ordinal) && !t.IsNested))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static bool IsReferenceOrContainsReferences(Type type)
    {
        if (type.ContainsGenericParameters)
        {
            return false;
        }

        MethodInfo method = typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!.MakeGenericMethod(type);
        return (bool)method.Invoke(null, null)!;
    }
}
