// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using Carbon.Combat.Attacks;
using Carbon.Creatures.Serialization;
using Carbon.Data.Registry;

namespace Carbon.Creatures.Definitions;

/// <summary>
/// Validated, immutable monster catalog. Assigns each definition a stable <see cref="ushort"/> index (file order)
/// and flattens every attack into one contiguous <see cref="AttackProfile"/> table shared with <see cref="AttackSystem"/>.
/// </summary>
public sealed class MonsterCatalog
{
    private readonly DefinitionRegistry<string, MonsterDefinition> _registry;
    private readonly FrozenDictionary<string, ushort> _indexById;
    private readonly AttackProfile[] _profiles;
    private readonly ushort[] _firstProfile;
    private readonly byte[] _profileCount;
    private readonly int[] _eliteVariantIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonsterCatalog"/> class.
    /// </summary>
    /// <param name="definitions">Monster definitions in index order.</param>
    /// <exception cref="ArgumentException">Thrown when a definition fails validation.</exception>
    public MonsterCatalog(IEnumerable<MonsterDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _registry = new DefinitionRegistry<string, MonsterDefinition>(definitions, StringComparer.Ordinal);
        ReadOnlySpan<MonsterDefinition> all = _registry.All;
        if (all.Length > ushort.MaxValue)
        {
            throw new ArgumentException("Too many monster definitions.", nameof(definitions));
        }

        Dictionary<string, ushort> indexById = new(all.Length, StringComparer.Ordinal);
        List<AttackProfile> profiles = new(all.Length * 2);
        _firstProfile = new ushort[all.Length];
        _profileCount = new byte[all.Length];
        _eliteVariantIndex = new int[all.Length];

        for (int i = 0; i < all.Length; i++)
        {
            MonsterDefinition def = all[i];
            Validate(def);
            indexById.Add(def.Id, (ushort)i);

            _firstProfile[i] = (ushort)profiles.Count;
            _profileCount[i] = (byte)def.Attacks.Count;
            for (int a = 0; a < def.Attacks.Count; a++)
            {
                try
                {
                    profiles.Add(def.Attacks[a].ToProfile());
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException($"Monster '{def.Id}' attack #{a}: {ex.Message}", nameof(definitions), ex);
                }
            }
        }

        if (profiles.Count > ushort.MaxValue)
        {
            throw new ArgumentException("Too many attack profiles.", nameof(definitions));
        }

        _indexById = indexById.ToFrozenDictionary(StringComparer.Ordinal);
        _profiles = [.. profiles];

        for (int i = 0; i < all.Length; i++)
        {
            string? elite = all[i].EliteVariant;
            _eliteVariantIndex[i] = -1;
            if (string.IsNullOrEmpty(elite))
            {
                continue;
            }

            if (!_indexById.TryGetValue(elite, out ushort eliteIndex) || !all[eliteIndex].IsElite || eliteIndex == i)
            {
                throw new ArgumentException($"Monster '{all[i].Id}' references unknown or non-elite variant '{elite}'.", nameof(definitions));
            }

            _eliteVariantIndex[i] = eliteIndex;
        }
    }

    /// <summary>Gets the number of monster definitions.</summary>
    public int Count => _registry.Count;

    /// <summary>Gets all definitions in index order.</summary>
    public ReadOnlySpan<MonsterDefinition> Definitions => _registry.All;

    /// <summary>Gets the flattened attack profile table (pass to <see cref="AttackSystem"/>).</summary>
    public ReadOnlySpan<AttackProfile> AttackProfiles => _profiles;

    /// <summary>
    /// Parses and validates a <c>monsters.json</c> document.
    /// </summary>
    /// <param name="json">JSON document text.</param>
    /// <returns>The validated catalog.</returns>
    /// <exception cref="JsonException">Thrown for malformed JSON, unknown members, or invalid enum names.</exception>
    public static MonsterCatalog FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        MonsterCatalogDocument document = JsonSerializer.Deserialize(json, CreaturesJsonContext.Default.MonsterCatalogDocument)
            ?? throw new JsonException("monsters.json is empty.");
        return new MonsterCatalog(document.Monsters);
    }

    /// <summary>
    /// Gets the definition at <paramref name="monsterIndex"/>.
    /// </summary>
    /// <param name="monsterIndex">Monster index.</param>
    /// <returns>The definition.</returns>
    public MonsterDefinition GetDefinition(ushort monsterIndex) => _registry.All[monsterIndex];

    /// <summary>
    /// Looks up the index of monster <paramref name="id"/>.
    /// </summary>
    /// <param name="id">Monster identifier.</param>
    /// <param name="monsterIndex">Resolved index.</param>
    /// <returns><see langword="true"/> if found.</returns>
    public bool TryGetIndex(string id, out ushort monsterIndex)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _indexById.TryGetValue(id, out monsterIndex);
    }

    /// <summary>
    /// Gets the index of monster <paramref name="id"/>.
    /// </summary>
    /// <param name="id">Monster identifier.</param>
    /// <returns>The monster index.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the monster is unknown.</exception>
    public ushort GetIndex(string id) =>
        this.TryGetIndex(id, out ushort index) ? index : throw new KeyNotFoundException($"Unknown monster '{id}'.");

    /// <summary>
    /// Gets the contiguous attack profile id range of a monster (priority order).
    /// </summary>
    /// <param name="monsterIndex">Monster index.</param>
    /// <param name="firstProfileId">First profile id.</param>
    /// <param name="count">Number of profiles.</param>
    public void GetAttackProfileRange(ushort monsterIndex, out ushort firstProfileId, out int count)
    {
        firstProfileId = _firstProfile[monsterIndex];
        count = _profileCount[monsterIndex];
    }

    /// <summary>
    /// Gets the elite variant index of a monster.
    /// </summary>
    /// <param name="monsterIndex">Monster index.</param>
    /// <param name="eliteIndex">Elite variant index.</param>
    /// <returns><see langword="true"/> if the monster has an elite variant.</returns>
    public bool TryGetEliteVariant(ushort monsterIndex, out ushort eliteIndex)
    {
        int idx = _eliteVariantIndex[monsterIndex];
        eliteIndex = idx < 0 ? (ushort)0 : (ushort)idx;
        return idx >= 0;
    }

    private static void Validate(MonsterDefinition def)
    {
        string id = def.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Monster id must be non-empty.");
        }

        if (def.ArchetypeId == 0)
        {
            throw new ArgumentException($"Monster '{id}' must declare a non-zero archetypeId.");
        }

        if (string.IsNullOrWhiteSpace(def.VisualArchetype))
        {
            throw new ArgumentException($"Monster '{id}' must declare a visualArchetype.");
        }

        if (string.IsNullOrWhiteSpace(def.LootTableId))
        {
            throw new ArgumentException($"Monster '{id}' must declare a lootTableId.");
        }

        MonsterBaseStats stats = def.Stats;
        if (stats is null || stats.Hp <= 0f || stats.Damage < 0f || stats.Armor < 0f || stats.Speed <= 0f)
        {
            throw new ArgumentException($"Monster '{id}' has invalid base stats.");
        }

        if (def.Radius <= 0f || def.AggroRange <= 0f || def.LeashRange < def.AggroRange || def.PatrolRadius < 0f)
        {
            throw new ArgumentException($"Monster '{id}' has invalid radius / aggro / leash / patrol ranges.");
        }

        if (def.EngageRange <= 0f || def.RetreatRange < 0f || def.RetreatRange >= def.EngageRange)
        {
            throw new ArgumentException($"Monster '{id}' must satisfy 0 <= retreatRange < engageRange.");
        }

        if (def.MinLevel < 1 || def.MaxLevel < def.MinLevel)
        {
            throw new ArgumentException($"Monster '{id}' must satisfy 1 <= minLevel <= maxLevel.");
        }

        if (def.Attacks is null || def.Attacks.Count == 0 || def.Attacks.Count > byte.MaxValue)
        {
            throw new ArgumentException($"Monster '{id}' must declare between 1 and 255 attacks.");
        }

        if (def.PreferredBiomes is null || def.PreferredBiomes.Count == 0)
        {
            throw new ArgumentException($"Monster '{id}' must declare at least one preferred biome.");
        }

        for (int i = 0; i < def.PreferredBiomes.Count; i++)
        {
            if (!Enum.IsDefined(def.PreferredBiomes[i]))
            {
                throw new ArgumentException($"Monster '{id}' references unknown biome '{def.PreferredBiomes[i]}'.");
            }
        }
    }
}
