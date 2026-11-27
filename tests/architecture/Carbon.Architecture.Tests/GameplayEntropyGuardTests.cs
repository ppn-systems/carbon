// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Carbon.Primitives.Random;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// ADR-026 guards. The world seed is public (clients receive it to mirror terrain), so it must never seed a gameplay
/// outcome. Gameplay roll APIs take the server's secret key (<see cref="GameplayKey"/> / <see cref="IGameplayEntropy"/>),
/// no value named like a seed may flow into them, and no packet contract may carry anything secret-shaped.
/// Terrain and world generation (<c>engine/Carbon.Maps</c>) keep using the public seed and are out of scope.
/// </summary>
public sealed class GameplayEntropyGuardTests
{
    /// <summary>Projects that legitimately use the public world seed: procedural terrain the client mirrors.</summary>
    private static readonly HashSet<string> s_terrainAssemblies = new(StringComparer.Ordinal) { "Carbon.Maps" };

    /// <summary>Gameplay roll APIs (type name → member names; <c>.ctor</c> for constructors).</summary>
    private static readonly Dictionary<string, string[]> s_rollApis = new(StringComparer.Ordinal)
    {
        ["ForgeEnhancer"] = ["CreateRandom"],
        ["LootRoller"] = ["CreateRandom"],
        ["TraderStockRoller"] = ["Roll"],
        ["TraderBook"] = [".ctor"],
        ["CombatRoll"] = ["Roll"],
        ["CombatService"] = ["RollCriticalHit"],
        ["FoodPoisoningRoll"] = ["Sample"],
        ["EatActionSystem"] = [".ctor"],
        ["MonsterSpawnerSystem"] = [".ctor"],
        ["CreatureSystems"] = ["Register"],
        ["EconomySimulator"] = ["Simulate"],
        ["InventoryService"] = [".ctor"],
        ["EconomyService"] = [".ctor"],
        ["NeedsService"] = [".ctor"],
        ["TradeService"] = [".ctor"],
        ["PlayerActionService"] = [".ctor"],
        ["CharacterProgressionService"] = [".ctor"],
        ["StatusProcRoll"] = ["Sample"],
        ["StatusEffectService"] = [".ctor"],
    };

    /// <summary>Calls whose arguments feed a gameplay roll or the gameplay key itself (source scan).</summary>
    private static readonly HashSet<string> s_rollCallNames = new(StringComparer.Ordinal)
    {
        "CreateRandom", "RollCriticalHit", "Simulate", "FromBytes",
        "TraderBook", "EatActionSystem", "MonsterSpawnerSystem", "InventoryService", "EconomyService", "NeedsService",
        "TradeService", "PlayerActionService", "CharacterProgressionService", "GameplayEntropy", "StatusEffectService",
    };

    /// <summary>Receivers whose <c>Roll</c> / <c>Register</c> are gameplay rolls (other types have unrelated <c>Roll</c> methods).</summary>
    private static readonly HashSet<string> s_rollReceivers = new(StringComparer.Ordinal)
    {
        "ForgeEnhancer", "LootRoller", "TraderStockRoller", "CombatRoll", "FoodPoisoningRoll", "CombatService", "CreatureSystems",
    };

    /// <summary>Key and PRF calls (<c>key.Hash</c>, <c>secret.Derive</c>, <c>entropy.KeyAt</c>, <c>FoodPoisoningRoll.Sample</c>), member calls only.</summary>
    private static readonly HashSet<string> s_keyMemberCalls = new(StringComparer.Ordinal) { "Hash", "Derive", "KeyAt", "Sample" };

    /// <summary>Terrain hashing helpers whose <c>Hash</c> legitimately takes the public seed.</summary>
    private static readonly HashSet<string> s_terrainHashReceivers = new(StringComparer.Ordinal) { "SpatialHash", "Fnv1a64", "HashCode" };

    private static readonly string[] s_secretMemberWords = ["secret", "entropy", "gameplaykey", "rollkey", "subkey", "bootsalt"];

    [Fact]
    public void GameplayRollApis_TakeTheGameplaySecret_NeverASeed()
    {
        List<string> violations = [];
        HashSet<string> found = new(StringComparer.Ordinal);
        foreach ((ProductionProject project, Assembly assembly) in GameplayAssemblies())
        {
            foreach (Type type in RepositoryLayout.GetTypesSafe(assembly))
            {
                if (!s_rollApis.TryGetValue(type.Name, out string[]? members))
                {
                    continue;
                }

                const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                IEnumerable<MethodBase> candidates = type.GetConstructors(All).Cast<MethodBase>().Concat(type.GetMethods(All));
                foreach (MethodBase method in candidates.Where(m => members.Contains(m.IsConstructor ? ".ctor" : m.Name, StringComparer.Ordinal)))
                {
                    if (method.IsConstructor && method.IsStatic)
                    {
                        continue;
                    }

                    _ = found.Add($"{type.Name}.{(method.IsConstructor ? ".ctor" : method.Name)}");
                    violations.AddRange(CheckRollSignature(project, type, method));
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "gameplay rolls must be keyed by the server secret (ADR-026), never by the public world seed");
        found.Should().HaveCount(s_rollApis.Values.Sum(m => m.Length), "every guarded roll API must still exist (rename the guard with the API)");
    }

    [Fact]
    public void DeterministicRandomFactories_OutsideTerrain_TakeTheGameplaySecret()
    {
        List<string> violations = [];
        foreach ((ProductionProject project, Assembly assembly) in GameplayAssemblies())
        {
            foreach (Type type in RepositoryLayout.GetTypesSafe(assembly))
            {
                const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (MethodInfo method in type.GetMethods(All).Where(m => m.ReturnType == typeof(DeterministicRandom)))
                {
                    if (type == typeof(GameplayKey) || type == typeof(DeterministicRandom))
                    {
                        continue; // the keyed PRF itself, and the generator's own API
                    }

                    violations.AddRange(CheckRollSignature(project, type, method));
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "a gameplay roll stream outside terrain generation is created from the gameplay secret");
    }

    [Fact]
    public void WorldSeed_NeverFlowsIntoAGameplayRollCall()
    {
        List<string> violations = [];
        int guardedCalls = 0;
        foreach (ProductionProject project in RepositoryLayout.Projects.Where(IsGameplayProject))
        {
            foreach (string file in RepositoryLayout.SourceFiles(project))
            {
                string relative = RepositoryLayout.Relative(file);
                CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: relative).GetCompilationUnitRoot();
                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    (string? name, ArgumentListSyntax? args) = DescribeCall(node);
                    if (name is null || args is null)
                    {
                        continue;
                    }

                    guardedCalls++;
                    foreach (ArgumentSyntax argument in args.Arguments)
                    {
                        string? seedName = argument.Expression.DescendantNodesAndSelf()
                            .OfType<IdentifierNameSyntax>()
                            .Select(id => id.Identifier.ValueText)
                            .FirstOrDefault(IsSeedName);
                        if (seedName is not null)
                        {
                            int line = argument.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            violations.Add($"{relative}:{line}: '{seedName}' flows into {name}(...)");
                        }
                    }
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "the public world seed must never reach a gameplay roll or the gameplay key (ADR-026)");
        guardedCalls.Should().BeGreaterThan(20, "the scan must actually see the gameplay roll call sites");
    }

    [Fact]
    public void PacketContracts_HaveNoSecretShapedMembers_AndCannotReferenceTheGameplayKey()
    {
        List<string> violations = [];
        foreach ((ProductionProject project, Assembly assembly) in RepositoryLayout.AssembliesIn("contracts"))
        {
            if (assembly.GetReferencedAssemblies().Any(a => string.Equals(a.Name, typeof(GameplayKey).Assembly.GetName().Name, StringComparison.Ordinal)))
            {
                violations.Add($"{project.AssemblyName} references {typeof(GameplayKey).Assembly.GetName().Name}");
            }

            foreach (Type type in RepositoryLayout.GetTypesSafe(assembly))
            {
                const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (MemberInfo member in type.GetProperties(All).Cast<MemberInfo>().Concat(type.GetFields(All)))
                {
                    string lower = member.Name.ToLowerInvariant();
                    if (s_secretMemberWords.Any(word => lower.Contains(word, StringComparison.Ordinal)))
                    {
                        violations.Add($"{type.FullName}.{member.Name}");
                    }
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "no packet may carry the gameplay secret, a subkey or the boot salt (ADR-026)");
    }

    [Fact]
    public void ServerAbstractions_NeverExposeTheGameplayKeyToHandlers()
    {
        // Handlers see the world through Carbon.Abstractions only; nothing there may hand them a key or the entropy source.
        Type[] secretTypes = [typeof(GameplayKey), typeof(IGameplayEntropy)];
        List<string> violations = [];
        foreach ((ProductionProject project, Assembly assembly) in RepositoryLayout.AssembliesIn("server")
            .Where(a => a.Project.AssemblyName is "Carbon.Abstractions" or "Carbon.Handlers"))
        {
            foreach (Type type in RepositoryLayout.GetTypesSafe(assembly))
            {
                const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (PropertyInfo property in type.GetProperties(Public).Where(p => secretTypes.Contains(Unwrap(p.PropertyType))))
                {
                    violations.Add($"{project.AssemblyName}: {type.Name}.{property.Name}");
                }

                foreach (MethodInfo method in type.GetMethods(Public).Where(m => secretTypes.Contains(Unwrap(m.ReturnType))))
                {
                    violations.Add($"{project.AssemblyName}: {type.Name}.{method.Name}()");
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "the gameplay key stays inside Carbon.Application and the host boot (ADR-026)");
    }

    private static IEnumerable<string> CheckRollSignature(ProductionProject project, Type type, MethodBase method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string signature = $"{project.AssemblyName}: {type.Name}.{(method.IsConstructor ? ".ctor" : method.Name)}({string.Join(", ", parameters.Select(p => p.Name))})";
        bool keyed = parameters.Any(p => Unwrap(p.ParameterType) == typeof(GameplayKey) || p.ParameterType == typeof(IGameplayEntropy)
            || typeof(IGameplayEntropy).IsAssignableFrom(p.ParameterType));
        if (!keyed)
        {
            yield return $"{signature} takes no GameplayKey / IGameplayEntropy";
        }

        foreach (ParameterInfo parameter in parameters.Where(p => p.Name is not null && IsSeedName(p.Name)))
        {
            yield return $"{signature} has a seed parameter '{parameter.Name}'";
        }
    }

    private static (string? Name, ArgumentListSyntax? Args) DescribeCall(SyntaxNode node)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                {
                    (string? receiver, string? method) = invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax access => (LastName(access.Expression), access.Name.Identifier.ValueText),
                        IdentifierNameSyntax id => (null, id.Identifier.ValueText),
                        GenericNameSyntax g => (null, g.Identifier.ValueText),
                        _ => (null, null),
                    };

                    if (method is null)
                    {
                        return (null, null);
                    }

                    bool isMemberCall = invocation.Expression is MemberAccessExpressionSyntax;
                    bool guarded = s_rollCallNames.Contains(method)
                        || (method is "Roll" or "Register" && receiver is not null && s_rollReceivers.Contains(receiver))
                        || (isMemberCall && s_keyMemberCalls.Contains(method) && (receiver is null || !s_terrainHashReceivers.Contains(receiver)));
                    return guarded ? ($"{(receiver is null ? string.Empty : receiver + ".")}{method}", invocation.ArgumentList) : (null, null);
                }

            case ObjectCreationExpressionSyntax creation:
                {
                    string? typeName = LastName(creation.Type);
                    return typeName is not null && s_rollCallNames.Contains(typeName) ? ($"new {typeName}", creation.ArgumentList) : (null, null);
                }

            case ImplicitObjectCreationExpressionSyntax implicitCreation:
                {
                    // `Foo x = new(...)` / `_x = new(...)`: resolve the target type from the declaration when it is spelled out.
                    string? typeName = implicitCreation.Parent?.Parent?.Parent is VariableDeclarationSyntax declaration ? LastName(declaration.Type) : null;
                    return typeName is not null && s_rollCallNames.Contains(typeName) ? ($"new {typeName}", implicitCreation.ArgumentList) : (null, null);
                }

            default:
                return (null, null);
        }
    }

    private static string? LastName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        QualifiedNameSyntax q => q.Right.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        _ => null,
    };

    private static bool IsSeedName(string name) => name.Contains("seed", StringComparison.OrdinalIgnoreCase);

    private static Type Unwrap(Type type)
    {
        Type element = type.IsByRef ? type.GetElementType()! : type;
        return Nullable.GetUnderlyingType(element) ?? element;
    }

    private static bool IsGameplayProject(ProductionProject project) =>
        project.Layer is "engine" or "server" && !s_terrainAssemblies.Contains(project.AssemblyName);

    private static IEnumerable<(ProductionProject Project, Assembly Assembly)> GameplayAssemblies() =>
        RepositoryLayout.AssembliesIn("engine", "server").Where(a => IsGameplayProject(a.Project));
}
