// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Combat.Statuses.Config;
using Carbon.Desktop.Abstractions.World;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Feedback;

/// <summary>
/// Telegraph tints of monsters that apply status effects (ADR-036 D10): the colour of the monster's first proc in the synced
/// <c>status_effects.json</c>, so a poisoner's attack reads green. Resolved once per archetype and catalog; drawing looks it
/// up without allocating.
/// </summary>
internal sealed class MonsterEffectTints
{
    private const string MonsterKeyPrefix = "monster.";
    private const string MonsterKeySuffix = ".name";

    private readonly Dictionary<ushort, ColorRgba?> _byArchetype = [];
    private StatusEffectCatalog? _catalog;

    /// <summary>Gets the tint of <paramref name="archetypeId"/>, or <see langword="null"/> when it applies no effect.</summary>
    /// <param name="world">Read model: the synced definitions and the archetype's monster name key.</param>
    /// <param name="archetypeId">Monster archetype id.</param>
    /// <returns>The tint.</returns>
    public ColorRgba? Resolve(IWorldReadModel world, ushort archetypeId)
    {
        ArgumentNullException.ThrowIfNull(world);
        StatusEffectCatalog catalog = world.StatusEffectDefinitions;
        if (!ReferenceEquals(catalog, _catalog))
        {
            _byArchetype.Clear();
            _catalog = catalog;
        }

        if (_byArchetype.TryGetValue(archetypeId, out ColorRgba? cached))
        {
            return cached;
        }

        ColorRgba? tint = null;
        if (MonsterIdOf(world.ResolveMonsterNameKey(archetypeId)) is { } id)
        {
            IReadOnlyList<MonsterProcEntry> procs = catalog.MonsterProcs;
            for (int i = 0; i < procs.Count; i++)
            {
                if (string.Equals(procs[i].Monster, id, StringComparison.Ordinal))
                {
                    tint = StatusEffectTints.ForTelegraph(procs[i].Effect.Key);
                    break;
                }
            }
        }

        _byArchetype[archetypeId] = tint;
        return tint;
    }

    // The name key is "monster.<id>.name" (EntityNameKeys), so the monsters.json id is its middle.
    private static string? MonsterIdOf(string? nameKey) =>
        nameKey is { Length: > 13 } key && key.StartsWith(MonsterKeyPrefix, StringComparison.Ordinal) && key.EndsWith(MonsterKeySuffix, StringComparison.Ordinal)
            ? key[MonsterKeyPrefix.Length..^MonsterKeySuffix.Length]
            : null;
}
