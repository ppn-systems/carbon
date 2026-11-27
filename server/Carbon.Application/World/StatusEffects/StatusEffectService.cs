// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Carbon.Abstractions.World;
using Carbon.Combat.Statuses;
using Carbon.Combat.Statuses.Config;
using Carbon.Creatures.Definitions;
using Carbon.Primitives.Random;
using Carbon.Simulation.ECS;
using Carbon.Simulation.World;
using Carbon.Survival.Components;
using Carbon.Survival.Config;

namespace Carbon.Application.World.StatusEffects;

/// <summary>
/// Server rules for player status effects (ADR-029): one <see cref="StatusEffectBuffer"/> per player, ticked by
/// <see cref="StatusEffectSystem"/> inside the world tick; monster procs and heavy hits; the food poisoning fold;
/// derived needs effects; the owner's replicated list; and persistence. Called only from inside the world lock.
/// </summary>
internal sealed class StatusEffectService
{
    /// <summary>Leading token of the persisted format with levels (ADR-036 D5).</summary>
    private const string FormatV2 = "v2";

    private readonly WorldSimulation _simulation;
    private readonly ushort _tickRate;
    private readonly IGameplayEntropy _entropy;
    private readonly MonsterCatalog _monsters;
    private readonly Dictionary<ushort, MonsterProcEntry[]> _procsByArchetype = [];

    /// <summary>Initializes a new instance of the <see cref="StatusEffectService"/> class and registers its system.</summary>
    public StatusEffectService(WorldSimulation simulation, ushort tickRate, StatusEffectCatalog? catalog, MonsterCatalog monsters, IGameplayEntropy entropy)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _monsters = monsters ?? throw new ArgumentNullException(nameof(monsters));
        _entropy = entropy ?? throw new ArgumentNullException(nameof(entropy));
        _tickRate = tickRate;
        this.Catalog = catalog ?? StatusEffectCatalog.LoadShippedDefaults();
        this.BuildProcTable();
        _simulation.RegisterSystem(new StatusEffectSystem());
    }

    /// <summary>Gets the definitions in effect.</summary>
    public StatusEffectCatalog Catalog { get; private set; }

    /// <summary>Switches to new definitions (ADR-014 hot reload); running effects keep the values they were applied with.</summary>
    public void Reconfigure(StatusEffectCatalog catalog)
    {
        this.Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.BuildProcTable();
    }

    /// <summary>Gives <paramref name="entity"/> its buffer (once, at world entry).</summary>
    public StatusEffectBuffer Attach(Entity entity) =>
        _simulation.Entities.GetAttachmentStore<StatusEffectBuffer>().GetOrAdd(entity, static () => new StatusEffectBuffer());

    /// <summary>Gets the buffer of <paramref name="entity"/>, if it has one.</summary>
    public bool TryGetBuffer(Entity entity, out StatusEffectBuffer buffer)
    {
        if (_simulation.Entities.GetAttachmentStore<StatusEffectBuffer>().TryGet(entity, out StatusEffectBuffer? found) && found is not null)
        {
            buffer = found;
            return true;
        }

        buffer = null!;
        return false;
    }

    /// <summary>Applies one application of <paramref name="definition"/>; returns whether the buffer changed.</summary>
    public bool Apply(
        Entity target,
        StatusEffectDefinition definition,
        Entity caster,
        StatusSource source,
        ushort sourceId,
        float durationSeconds = 0f,
        byte stacks = 1,
        byte level = 1)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Derived != StatusDerivation.None || !this.TryGetBuffer(target, out StatusEffectBuffer buffer))
        {
            return false;
        }

        ActiveStatusEffect active = StatusEffectCatalog.CreateActive(definition, _tickRate, caster, source, sourceId, durationSeconds, stacks, level);
        return buffer.ApplyOrRefresh(in active);
    }

    /// <summary>Applies the effect with <paramref name="key"/> at <paramref name="level"/>; returns whether the buffer changed.</summary>
    public bool Apply(Entity target, string key, StatusSource source, ushort sourceId = 0, float durationSeconds = 0f, byte level = 1) =>
        this.Catalog.TryGetByKey(key, out StatusEffectDefinition definition) &&
        this.Apply(target, definition, Entity.Null, source, sourceId, durationSeconds, level: level);

    /// <summary>Removes every effect (death).</summary>
    public void Clear(Entity entity)
    {
        if (this.TryGetBuffer(entity, out StatusEffectBuffer buffer))
        {
            buffer.Clear();
        }
    }

    /// <summary>Gets the movement speed scale from effects (0 while stunned).</summary>
    public float GetMoveScale(Entity entity) =>
        this.TryGetBuffer(entity, out StatusEffectBuffer buffer) ? buffer.MoveSpeedScale : 1f;

    /// <summary>Gets whether <paramref name="entity"/> is stunned.</summary>
    public bool IsStunned(Entity entity) =>
        this.TryGetBuffer(entity, out StatusEffectBuffer buffer) && (buffer.ActiveControlMask & CrowdControlMask.Stunned) != 0;

    /// <summary>Gets the stamina regeneration multiplier from effects.</summary>
    public float GetStaminaRegenMultiplier(Entity entity) =>
        this.TryGetBuffer(entity, out StatusEffectBuffer buffer) ? buffer.StaminaRegenMultiplier : 1f;

    /// <summary>Gets the health regeneration multiplier from timed effects and the derived well-fed buff.</summary>
    public float GetHealthRegenMultiplier(Entity entity, NeedsConfig needsConfig)
    {
        float scale = this.TryGetBuffer(entity, out StatusEffectBuffer buffer) ? buffer.HealthRegenMultiplier : 1f;
        if (this.Catalog.GetDerived(StatusDerivation.WellFed) is { } wellFed &&
            _simulation.Entities.TryGetComponent(entity, out NeedsComponent needs) &&
            IsWellFed(in needs, needsConfig))
        {
            scale *= wellFed.HealthRegenMultiplier;
        }

        return scale;
    }

    /// <summary>
    /// Mirrors the remaining time of <c>poisoned</c> into the needs, so the needs poison penalties apply to every poison
    /// source (ADR-029 D6). Call before the simulation tick.
    /// </summary>
    public void MirrorPoisonIntoNeeds(Entity entity)
    {
        if (!_simulation.Entities.HasComponent<NeedsComponent>(entity))
        {
            return;
        }

        float seconds = 0f;
        if (this.Catalog.TryGetByKey(StatusEffectKeys.Poisoned, out StatusEffectDefinition poisoned) &&
            this.TryGetBuffer(entity, out StatusEffectBuffer buffer) &&
            buffer.TryGet(poisoned.Id, out ActiveStatusEffect active))
        {
            seconds = active.RemainingTicks / (float)_tickRate;
        }

        _simulation.Entities.GetComponent<NeedsComponent>(entity).PoisonRemainingSeconds = seconds;
    }

    /// <summary>
    /// Applies the effects of one landed monster hit (ADR-029 D7): the heavy-hit effect when the mitigated damage reaches
    /// the configured share of max HP, and each proc of the attacker's monster type on the secret proc roll.
    /// </summary>
    /// <returns>Whether the victim's buffer changed.</returns>
    public bool OnMonsterHit(
        Entity victim,
        ulong victimNetworkId,
        Entity attacker,
        ulong attackerNetworkId,
        ushort attackerArchetypeId,
        float dealt,
        float victimMaxHealth,
        ulong tick)
    {
        if (dealt <= 0f)
        {
            return false;
        }

        bool changed = false;
        StatusEffectCatalog catalog = this.Catalog;
        if (catalog.HeavyHitEffect is { } heavy && victimMaxHealth > 0f && dealt >= catalog.HeavyHitMinDamageFraction * victimMaxHealth)
        {
            changed |= this.Apply(victim, heavy, attacker, StatusSource.HeavyHit, attackerArchetypeId);
        }

        if (!_procsByArchetype.TryGetValue(attackerArchetypeId, out MonsterProcEntry[]? procs))
        {
            return changed;
        }

        GameplayKey key = _entropy.KeyAt(tick);
        for (int i = 0; i < procs.Length; i++)
        {
            MonsterProcEntry proc = procs[i];
            if (StatusProcRoll.Sample(in key, tick, victimNetworkId, attackerNetworkId, i) < proc.Chance)
            {
                changed |= this.Apply(victim, proc.Effect, attacker, StatusSource.Monster, attackerArchetypeId);
            }
        }

        return changed;
    }

    /// <summary>Grants the wake-up buff when the sleeper's rest reached the configured fraction.</summary>
    public bool TryGrantRested(Entity entity)
    {
        StatusEffectCatalog catalog = this.Catalog;
        return catalog.RestedEffect is { } rested &&
            _simulation.Entities.TryGetComponent(entity, out NeedsComponent needs) &&
            needs.RestFraction >= catalog.RestedMinRestFraction &&
            this.Apply(entity, rested, Entity.Null, StatusSource.Sleep, 0);
    }

    /// <summary>
    /// Packs the derived conditions (hunger, fatigue, well fed) into one value, so replication can tell when they change
    /// without building the list.
    /// </summary>
    public int ComputeDerivedSignature(Entity entity, NeedsConfig needsConfig)
    {
        if (!_simulation.Entities.TryGetComponent(entity, out NeedsComponent needs))
        {
            return 0;
        }

        int fatigue = needsConfig.RestEnabled ? (int)needs.Fatigue : 0;
        return (int)needs.Hunger | (fatigue << 4) | ((IsWellFed(in needs, needsConfig) ? 1 : 0) << 8);
    }

    /// <summary>Appends the owner's replicated effects (timed, then derived) to <paramref name="output"/>.</summary>
    public void CollectEntries(Entity entity, NeedsConfig needsConfig, List<StatusEffectEntry> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (this.TryGetBuffer(entity, out StatusEffectBuffer buffer))
        {
            ReadOnlySpan<ActiveStatusEffect> active = buffer.ActiveEffects;
            for (int i = 0; i < active.Length; i++)
            {
                ActiveStatusEffect effect = active[i];
                float magnitude = effect.MagnitudePerStack > 0f ? effect.MagnitudePerStack * effect.Stacks
                    : effect.MoveSpeedMultiplier != 1f ? effect.MoveSpeedMultiplier
                    : effect.HealthRegenMultiplier != 1f ? effect.HealthRegenMultiplier
                    : effect.StaminaRegenMultiplier;
                output.Add(new StatusEffectEntry(
                    (ushort)effect.EffectId,
                    effect.Stacks,
                    this.TicksToMs(effect.RemainingTicks),
                    this.TicksToMs(effect.DurationTicks),
                    magnitude,
                    (byte)effect.Source,
                    effect.SourceId,
                    effect.Level));
            }
        }

        if (!_simulation.Entities.TryGetComponent(entity, out NeedsComponent needs))
        {
            return;
        }

        switch (needs.Hunger)
        {
            case HungerState.Hungry:
                this.AddDerived(output, StatusDerivation.Hungry, 1, 1f);
                break;
            case HungerState.UrgentlyHungry:
                this.AddDerived(output, StatusDerivation.Hungry, 2, needsConfig.UrgentlyHungrySpeedScale);
                break;
            case HungerState.Starving:
                this.AddDerived(output, StatusDerivation.Starving, 1, needsConfig.StarvingSpeedScale);
                break;
            case HungerState.Fed:
            default:
                break;
        }

        if (needsConfig.RestEnabled)
        {
            switch (needs.Fatigue)
            {
                case RestState.Tired:
                    this.AddDerived(output, StatusDerivation.Tired, 1, 1f);
                    break;
                case RestState.VeryTired:
                    this.AddDerived(output, StatusDerivation.Tired, 2, needsConfig.VeryTiredSpeedScale);
                    break;
                case RestState.Exhausted:
                    this.AddDerived(output, StatusDerivation.Tired, 3, needsConfig.ExhaustedSpeedScale);
                    break;
                case RestState.Rested:
                default:
                    break;
            }
        }

        if (IsWellFed(in needs, needsConfig) && this.Catalog.GetDerived(StatusDerivation.WellFed) is { } wellFed)
        {
            this.AddDerived(output, StatusDerivation.WellFed, 1, wellFed.HealthRegenMultiplier);
        }
    }

    /// <summary>
    /// Serializes the effects marked <c>persist</c> as format <c>v2</c> (ADR-036 D5): a leading <c>v2</c> token, then
    /// <c>key:remainingMs:stacks:level</c> entries, all joined by <c>;</c>. Cold path (saves).
    /// </summary>
    public string Serialize(Entity entity)
    {
        if (!this.TryGetBuffer(entity, out StatusEffectBuffer buffer) || buffer.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder text = new();
        ReadOnlySpan<ActiveStatusEffect> active = buffer.ActiveEffects;
        for (int i = 0; i < active.Length; i++)
        {
            if (!this.Catalog.TryGet(active[i].EffectId, out StatusEffectDefinition def) || !def.Persist)
            {
                continue;
            }

            _ = text.Append(text.Length == 0 ? FormatV2 : string.Empty)
                .Append(';')
                .Append(def.Key)
                .Append(':')
                .Append(this.TicksToMs(active[i].RemainingTicks).ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(active[i].Stacks.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(active[i].Level.ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>
    /// Restores persisted effects at world entry (ADR-029 D8, ADR-036 D5). A value starting with the <c>v2</c> token holds
    /// <c>key:remainingMs:stacks:level</c> entries; any other value is the ADR-029 format <c>key:remainingMs:stacks</c> and
    /// loads at level 1. Unknown keys and malformed entries are skipped, durations and levels are clamped to the definition.
    /// A record saved before ADR-029 restores <c>poisoned</c> from the needs poison seconds.
    /// <paramref name="persistedOnly"/> false also accepts effects that are never saved, at any duration (the screenshot debug
    /// override).
    /// </summary>
    public void Restore(Entity entity, string persisted, float legacyPoisonSeconds, NeedsConfig needsConfig, bool persistedOnly = true)
    {
        ArgumentNullException.ThrowIfNull(needsConfig);
        _ = this.Attach(entity);
        bool restoredAny = false;
        string[] items = (persisted ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool v2 = items.Length > 0 && string.Equals(items[0], FormatV2, StringComparison.Ordinal);
        for (int index = v2 ? 1 : 0; index < items.Length; index++)
        {
            string[] parts = items[index].Split(':');
            byte level = 1;
            if (parts.Length != (v2 ? 4 : 3) ||
                (v2 && (!byte.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out level) || level == 0)) ||
                !this.Catalog.TryGetByKey(parts[0], out StatusEffectDefinition def) ||
                (persistedOnly && !def.Persist) ||
                def.Derived != StatusDerivation.None ||
                !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint remainingMs) ||
                !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out byte stacks) ||
                remainingMs == 0U)
            {
                continue;
            }

            float maxSeconds = MaxRestoreSeconds(def, needsConfig);
            // The screenshot debug override (persistedOnly false) may hold an effect longer than one application lasts.
            float seconds = persistedOnly ? MathF.Min(remainingMs / 1000f, maxSeconds) : remainingMs / 1000f;
            restoredAny |= this.Apply(entity, def, Entity.Null, StatusSource.None, 0, seconds, stacks, level);
        }

        if (!restoredAny && float.IsFinite(legacyPoisonSeconds) && legacyPoisonSeconds > 0f &&
            this.Catalog.TryGetByKey(StatusEffectKeys.Poisoned, out StatusEffectDefinition poisoned))
        {
            _ = this.Apply(entity, poisoned, Entity.Null, StatusSource.Food, 0, MathF.Min(legacyPoisonSeconds, MaxRestoreSeconds(poisoned, needsConfig)));
        }
    }

    private static bool IsWellFed(in NeedsComponent needs, NeedsConfig needsConfig) =>
        needs.Hunger == HungerState.Fed && needs.FoodMax > 0f && needs.FoodFraction >= needsConfig.FullThreshold;

    private static float MaxRestoreSeconds(StatusEffectDefinition def, NeedsConfig needsConfig) =>
        string.Equals(def.Key, StatusEffectKeys.Poisoned, StringComparison.Ordinal)
            ? MathF.Max(def.DurationSeconds, needsConfig.PoisonDurationSeconds)
            : def.DurationSeconds;

    private void AddDerived(List<StatusEffectEntry> output, StatusDerivation derivation, byte stacks, float magnitude)
    {
        if (this.Catalog.GetDerived(derivation) is { } def)
        {
            output.Add(new StatusEffectEntry(def.Id, Math.Min(stacks, def.MaxStacks), 0U, 0U, magnitude, (byte)StatusSource.Needs, 0));
        }
    }

    private uint TicksToMs(uint ticks) => (uint)Math.Min(uint.MaxValue, ticks * 1000UL / _tickRate);

    private void BuildProcTable()
    {
        _procsByArchetype.Clear();
        ReadOnlySpan<MonsterDefinition> monsters = _monsters.Definitions;
        IReadOnlyList<MonsterProcEntry> procs = this.Catalog.MonsterProcs;
        List<MonsterProcEntry> matches = [];
        for (int m = 0; m < monsters.Length; m++)
        {
            matches.Clear();
            for (int p = 0; p < procs.Count; p++)
            {
                if (string.Equals(procs[p].Monster, monsters[m].Id, StringComparison.Ordinal))
                {
                    matches.Add(procs[p]);
                }
            }

            if (matches.Count > 0)
            {
                _procsByArchetype[monsters[m].ArchetypeId] = [.. matches];
            }
        }
    }
}
