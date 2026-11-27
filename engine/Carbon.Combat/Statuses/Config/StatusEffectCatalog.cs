// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Carbon.Simulation.ECS;

namespace Carbon.Combat.Statuses.Config;

/// <summary>
/// Validated status effect definitions, heavy-hit and wake-up rules and monster procs from
/// <c>resources/config/status_effects.json</c> (ADR-029 D1). Immutable; a hot reload builds a new catalog.
/// </summary>
public sealed class StatusEffectCatalog
{
    /// <summary>Config document name (ADR-014).</summary>
    public const string DocumentName = "status_effects";

    private const string FileName = "status_effects.json";

    private readonly Dictionary<ushort, StatusEffectDefinition> _byId = [];
    private readonly Dictionary<string, StatusEffectDefinition> _byKey = new(StringComparer.Ordinal);
    private readonly StatusEffectDefinition?[] _derived = new StatusEffectDefinition?[5];
    private readonly List<MonsterProcEntry> _procs = [];

    private StatusEffectCatalog(StatusEffectsDocument document)
    {
        this.Effects = document.Effects;
        foreach (StatusEffectDefinition def in document.Effects)
        {
            _byId.Add(def.Id, def);
            _byKey.Add(def.Key, def);
            if (def.Derived != StatusDerivation.None)
            {
                _derived[(int)def.Derived] = def;
            }
        }

        if (document.HeavyHit is { } heavy)
        {
            this.HeavyHitEffect = _byKey[heavy.Effect];
            this.HeavyHitMinDamageFraction = heavy.MinDamageFraction;
        }

        if (document.RestedOnWake is { } rested)
        {
            this.RestedEffect = _byKey[rested.Effect];
            this.RestedMinRestFraction = rested.MinRestFraction;
        }

        foreach (MonsterStatusProc proc in document.MonsterProcs)
        {
            _procs.Add(new MonsterProcEntry(proc.Monster, _byKey[proc.Effect], proc.Chance));
        }
    }

    /// <summary>Gets a catalog with no effects (nothing is ever applied).</summary>
    public static StatusEffectCatalog Empty { get; } = new(new StatusEffectsDocument { SchemaVersion = 1 });

    /// <summary>Gets every definition in document order.</summary>
    public IReadOnlyList<StatusEffectDefinition> Effects { get; }

    /// <summary>Gets the effect a heavy hit applies, or <see langword="null"/>.</summary>
    public StatusEffectDefinition? HeavyHitEffect { get; }

    /// <summary>Gets the minimum mitigated damage, as a fraction of max HP, that counts as a heavy hit.</summary>
    public float HeavyHitMinDamageFraction { get; } = 1f;

    /// <summary>Gets the buff granted on waking well rested, or <see langword="null"/>.</summary>
    public StatusEffectDefinition? RestedEffect { get; }

    /// <summary>Gets the minimum rest fraction at wake-up for <see cref="RestedEffect"/>.</summary>
    public float RestedMinRestFraction { get; } = 1f;

    /// <summary>Gets every monster proc in document order.</summary>
    public IReadOnlyList<MonsterProcEntry> MonsterProcs => _procs;

    /// <summary>Parses and validates a <c>status_effects.json</c> payload.</summary>
    /// <param name="json">JSON object text.</param>
    /// <returns>The validated catalog.</returns>
    /// <exception cref="InvalidDataException">Thrown when the payload is empty, malformed or inconsistent.</exception>
    public static StatusEffectCatalog LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        StatusEffectsDocument document;
        try
        {
            document = JsonSerializer.Deserialize(json, StatusEffectsJsonContext.Default.StatusEffectsDocument)
                ?? throw new InvalidDataException($"{FileName} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{FileName} is malformed: {ex.Message}", ex);
        }

        Validate(document);
        return new StatusEffectCatalog(document);
    }

    /// <summary>Loads the copy of <c>status_effects.json</c> embedded at build time (tests and config-less hosts).</summary>
    /// <returns>The shipped catalog.</returns>
    public static StatusEffectCatalog LoadShippedDefaults()
    {
        using Stream stream = typeof(StatusEffectCatalog).Assembly.GetManifestResourceStream("Carbon.Combat.Config.status_effects.json")
            ?? throw new InvalidOperationException($"Embedded default config '{FileName}' is missing.");
        using StreamReader reader = new(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    /// <summary>
    /// Converts seconds to whole simulation ticks, at least one tick for any positive value.
    /// </summary>
    /// <param name="seconds">Seconds.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <returns>Ticks.</returns>
    public static uint SecondsToTicks(float seconds, ushort tickRate) =>
        seconds <= 0f ? 0U : Math.Max(1U, (uint)MathF.Round(seconds * tickRate));

    /// <summary>Builds the active entry for one application of <paramref name="definition"/>.</summary>
    /// <param name="definition">Timed effect definition.</param>
    /// <param name="tickRate">Simulation ticks per second.</param>
    /// <param name="caster">Entity that applied it, or <see cref="Entity.Null"/>.</param>
    /// <param name="source">What applied it.</param>
    /// <param name="sourceId">Source detail (monster archetype id).</param>
    /// <param name="durationSeconds">Duration override; non-positive uses the definition's duration.</param>
    /// <param name="stacks">Initial stacks (clamped to the cap).</param>
    /// <returns>The active entry.</returns>
    public static ActiveStatusEffect CreateActive(
        StatusEffectDefinition definition,
        ushort tickRate,
        Entity caster,
        StatusSource source,
        ushort sourceId,
        float durationSeconds = 0f,
        byte stacks = 1)
    {
        ArgumentNullException.ThrowIfNull(definition);
        uint duration = SecondsToTicks(durationSeconds > 0f ? durationSeconds : definition.DurationSeconds, tickRate);
        return new ActiveStatusEffect(
            definition.Id,
            caster,
            ControlMaskOf(definition),
            definition.Magnitude,
            duration,
            PulseIntervalTicks: SecondsToTicks(definition.TickIntervalSeconds, tickRate),
            Stacks: Math.Clamp(stacks, (byte)1, definition.MaxStacks),
            MaxStacks: definition.MaxStacks,
            IsDamage: true,
            Stacking: definition.Stacking,
            MoveSpeedMultiplier: definition.MoveSpeedMultiplier,
            DurationTicks: duration,
            Lethal: definition.Lethal,
            HealthRegenMultiplier: definition.HealthRegenMultiplier,
            StaminaRegenMultiplier: definition.StaminaRegenMultiplier,
            Source: source,
            SourceId: sourceId);
    }

    /// <summary>Gets a definition by wire id.</summary>
    /// <param name="id">Wire id.</param>
    /// <param name="definition">The definition when found.</param>
    /// <returns><see langword="true"/> when the id exists.</returns>
    public bool TryGet(uint id, out StatusEffectDefinition definition)
    {
        if (id <= ushort.MaxValue && _byId.TryGetValue((ushort)id, out StatusEffectDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    /// <summary>Gets a definition by key.</summary>
    /// <param name="key">Effect key.</param>
    /// <param name="definition">The definition when found.</param>
    /// <returns><see langword="true"/> when the key exists.</returns>
    public bool TryGetByKey(string key, out StatusEffectDefinition definition)
    {
        if (_byKey.TryGetValue(key, out StatusEffectDefinition? found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    /// <summary>Gets the derived definition mirroring <paramref name="derivation"/>, or <see langword="null"/>.</summary>
    /// <param name="derivation">Needs condition.</param>
    /// <returns>The definition, or <see langword="null"/> when none is configured.</returns>
    public StatusEffectDefinition? GetDerived(StatusDerivation derivation) =>
        derivation is > StatusDerivation.None and <= StatusDerivation.WellFed ? _derived[(int)derivation] : null;

    private static CrowdControlMask ControlMaskOf(StatusEffectDefinition definition)
    {
        CrowdControlMask mask = definition.Stun ? CrowdControlMask.Stunned : CrowdControlMask.None;
        return definition.MoveSpeedMultiplier < 1f ? mask | CrowdControlMask.Slowed : mask;
    }

    private static void Validate(StatusEffectsDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException($"{FileName} schemaVersion {document.SchemaVersion} is not supported.");
        }

        HashSet<ushort> ids = [];
        Dictionary<string, StatusEffectDefinition> keys = new(StringComparer.Ordinal);
        HashSet<StatusDerivation> derivations = [];
        foreach (StatusEffectDefinition def in document.Effects ?? throw new InvalidDataException($"{FileName} must define effects."))
        {
            if (def is null || def.Id == 0 || !ids.Add(def.Id))
            {
                throw new InvalidDataException($"{FileName} has a missing, zero or duplicate effect id.");
            }

            RequireName(def.Key, $"effect {def.Id} key");
            RequireName(def.Icon, $"effect '{def.Key}' icon");
            if (!keys.TryAdd(def.Key, def))
            {
                throw new InvalidDataException($"{FileName} has a duplicate effect key '{def.Key}'.");
            }

            if (def.Derived != StatusDerivation.None)
            {
                if (!derivations.Add(def.Derived) || def.DurationSeconds != 0f || def.TickIntervalSeconds != 0f)
                {
                    throw new InvalidDataException($"{FileName} derived effect '{def.Key}' must be unique per condition and untimed.");
                }
            }
            else
            {
                RequireRange(def.DurationSeconds, 0.05f, 86_400f, def.Key, "durationSeconds");
            }

            RequireRange(def.MaxStacks, 1f, 20f, def.Key, "maxStacks");
            RequireRange(def.TickIntervalSeconds, 0f, 600f, def.Key, "tickIntervalSeconds");
            RequireRange(def.Magnitude, 0f, 10_000f, def.Key, "magnitude");
            RequireRange(def.MoveSpeedMultiplier, 0.05f, 3f, def.Key, "moveSpeedMultiplier");
            RequireRange(def.HealthRegenMultiplier, 0f, 10f, def.Key, "healthRegenMultiplier");
            RequireRange(def.StaminaRegenMultiplier, 0f, 10f, def.Key, "staminaRegenMultiplier");
            if (def.Magnitude > 0f && def.TickIntervalSeconds <= 0f)
            {
                throw new InvalidDataException($"{FileName} effect '{def.Key}' has a magnitude but no tickIntervalSeconds.");
            }
        }

        if (document.HeavyHit is { } heavy)
        {
            RequireTimed(keys, heavy.Effect, "heavyHit");
            RequireRange(heavy.MinDamageFraction, 0.01f, 1f, "heavyHit", "minDamageFraction");
        }

        if (document.RestedOnWake is { } rested)
        {
            RequireTimed(keys, rested.Effect, "restedOnWake");
            RequireRange(rested.MinRestFraction, 0.01f, 1f, "restedOnWake", "minRestFraction");
        }

        foreach (MonsterStatusProc proc in document.MonsterProcs ?? [])
        {
            if (proc is null || string.IsNullOrWhiteSpace(proc.Monster))
            {
                throw new InvalidDataException($"{FileName} has a monster proc without a monster.");
            }

            RequireTimed(keys, proc.Effect, $"monsterProcs[{proc.Monster}]");
            RequireRange(proc.Chance, 0f, 1f, proc.Monster, "chance");
        }
    }

    private static void RequireTimed(Dictionary<string, StatusEffectDefinition> keys, string key, string owner)
    {
        if (!keys.TryGetValue(key ?? string.Empty, out StatusEffectDefinition? def) || def.Derived != StatusDerivation.None)
        {
            throw new InvalidDataException($"{FileName} {owner} names '{key}', which is not a timed effect.");
        }
    }

    private static void RequireName(string value, string what)
    {
        bool valid = !string.IsNullOrEmpty(value) && value.Length <= 48;
        for (int i = 0; valid && i < value.Length; i++)
        {
            char c = value[i];
            valid = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_';
        }

        if (!valid)
        {
            throw new InvalidDataException($"{FileName} {what} '{value}' must be 1-48 characters of [a-z0-9_].");
        }
    }

    private static void RequireRange(float value, float min, float max, string owner, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new InvalidDataException($"{FileName} '{owner}.{name}' = {value} is outside [{min}, {max}].");
        }
    }
}

/// <summary>One resolved monster proc.</summary>
/// <param name="Monster">Monster id from <c>monsters.json</c>.</param>
/// <param name="Effect">Timed effect applied.</param>
/// <param name="Chance">Chance per landed hit.</param>
public sealed record MonsterProcEntry(string Monster, StatusEffectDefinition Effect, float Chance);
