// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Cross-layer hygiene rules: no <c>async void</c>, no static mutable state in engine/server. Naming rules live in
/// <see cref="NamingConventionTests"/>.
/// </summary>
public sealed class CodeHygieneTests
{
    /// <summary>Generic collection types whose <c>static readonly</c> instances are still shared mutable state.</summary>
    private static readonly HashSet<string> s_mutableCollectionDefinitions = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.List`1",
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.Queue`1",
        "System.Collections.Generic.Stack`1",
        "System.Collections.Generic.SortedDictionary`2",
        "System.Collections.Generic.SortedSet`1",
        "System.Collections.Generic.LinkedList`1",
        "System.Collections.Concurrent.ConcurrentDictionary`2",
        "System.Collections.Concurrent.ConcurrentQueue`1",
        "System.Collections.Concurrent.ConcurrentBag`1",
    };

    /// <summary>
    /// Existing static mutable fields in engine/ and server/. Entry format: <c>Namespace.Type.field</c>.
    /// Do NOT add new entries: inject state through instances owned by the world/host instead.
    /// </summary>
    private static readonly HashSet<string> s_staticMutableStateAllowlist = new(StringComparer.Ordinal)
    {
        // --- Accepted by design ---
        // Process-wide generic type-id counters (Interlocked); ids are per-type, not per-world state.
        "Carbon.Simulation.ECS.EntityManager.s_nextAttachmentTypeId",
        "Carbon.Simulation.ECS.EntityManager.s_nextComponentTypeId",
        "Carbon.Simulation.World.WorldSimulation.s_nextEventTypeId",

        // [ThreadStatic] per-thread scratch buffers for world generation (no cross-thread sharing).
        // Audit: rename to t_ prefix per csharp-style.
        "Carbon.Maps.Building.RoomDetector.s_enclosureScratch",
        "Carbon.Maps.Generators.ProceduralWorldGenerator.s_chunkSamples",
        "Carbon.Maps.Generators.Stages.RoadStage.s_links",

        // Read-only after static construction. Audit: convert to FrozenDictionary and drop this entry.
        "Carbon.Maps.Landmarks.LandmarkPlanners.s_planners",

        // --- Debt (see docs/audit/2026-11-codebase-audit.md) ---
        // Nalix [Inject] static service-locator fields on packet handler classes. Audit: instance handlers + DI.
        "Carbon.Handlers.Config.ConfigHandlers.s_config",
        "Carbon.Handlers.Entities.EntityHandlers.s_transports",
        "Carbon.Handlers.Entities.EntityHandlers.s_world",
        "Carbon.Handlers.Identity.IdentityHandlers.s_authService",
        "Carbon.Handlers.Identity.IdentityHandlers.s_transports",
        "Carbon.Handlers.Identity.IdentityHandlers.s_world",
        "Carbon.Handlers.Items.ItemHandlers.s_transports",
        "Carbon.Handlers.Items.ItemHandlers.s_world",
        "Carbon.Handlers.Maps.MapHandlers.s_transports",
        "Carbon.Handlers.Maps.MapHandlers.s_world",

        // Process-wide authentication map. Audit: injected singleton or connection attribute.
        "Carbon.Handlers.Session.ConnectionAuthRegistry.s_accountsByConnection",

        // Process-wide river caches shared by every world/test. Audit: instance cache owned by the generator.
        "Carbon.Maps.Hydrology.RiverNetwork.s_blockOrder",
        "Carbon.Maps.Hydrology.RiverNetwork.s_regionOrder",
        "Carbon.Maps.Hydrology.RiverNetwork.s_sharedBlocks",
        "Carbon.Maps.Hydrology.RiverNetwork.s_sharedRegions",
    };

    /// <summary>
    /// Existing <c>async void</c> methods. Entry format: <c>Namespace.Type.Method</c>. Do NOT add new entries.
    /// </summary>
    private static readonly HashSet<string> s_asyncVoidAllowlist = new(StringComparer.Ordinal)
    {
    };

    [Fact]
    public void ProductionCode_ShouldNotDeclareAsyncVoidMethods()
    {
        List<string> violations = [];
        foreach ((ProductionProject _, Assembly assembly) in RepositoryLayout.Assemblies)
        {
            foreach (Type type in RepositoryLayout.GetTypesSafe(assembly))
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.ReturnType == typeof(void) && method.IsDefined(typeof(AsyncStateMachineAttribute), false))
                    {
                        string key = $"{type.FullName}.{method.Name}";
                        if (!s_asyncVoidAllowlist.Contains(key))
                        {
                            violations.Add(key);
                        }
                    }
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "async void swallows exceptions into the synchronization context and cannot be awaited; return Task/ValueTask");
    }

    [Fact]
    public void EngineAndServer_ShouldNotHoldStaticMutableState()
    {
        List<string> violations = [.. FindStaticMutableFields()
            .Where(key => !s_staticMutableStateAllowlist.Contains(key))];

        Violations.ShouldBeEmpty(violations, "static mutable state breaks multi-world hosting, test isolation, and thread safety");
    }

    [Fact]
    public void StaticMutableStateAllowlist_ShouldNotContainStaleEntries()
    {
        HashSet<string> found = [.. FindStaticMutableFields()];
        Violations.ShouldBeEmpty(s_staticMutableStateAllowlist.Where(e => !found.Contains(e)), "fixed entries must be removed from the allowlist");
    }

    private static IEnumerable<string> FindStaticMutableFields() => FindStaticMutableFields("engine", "server");

    /// <summary>Non-readonly statics and <c>static readonly</c> mutable collections declared in <paramref name="layers"/>.</summary>
    internal static IEnumerable<string> FindStaticMutableFields(params string[] layers)
    {
        foreach ((ProductionProject _, Assembly assembly) in RepositoryLayout.AssembliesIn(layers))
        {
            foreach (Type type in RepositoryLayout.GetTypesSafe(assembly))
            {
                if (IsCompilerOrToolGenerated(type))
                {
                    continue;
                }

                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral)
                    {
                        continue;
                    }

                    bool mutableReference = !field.IsInitOnly;
                    bool mutableCollection = field.IsInitOnly && field.FieldType.IsGenericType &&
                        s_mutableCollectionDefinitions.Contains(field.FieldType.GetGenericTypeDefinition().FullName!);

                    if (mutableReference || mutableCollection)
                    {
                        yield return $"{type.FullName}.{field.Name}";
                    }
                }
            }
        }
    }

    internal static bool IsCompilerOrToolGenerated(Type type)
    {
        for (Type? cursor = type; cursor is not null; cursor = cursor.DeclaringType)
        {
            if (cursor.IsDefined(typeof(CompilerGeneratedAttribute), false) ||
                cursor.IsDefined(typeof(GeneratedCodeAttribute), false) ||
                typeof(JsonSerializerContext).IsAssignableFrom(cursor) ||
                cursor.Name.StartsWith('<'))
            {
                return true;
            }
        }

        return false;
    }
}
