// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Application.World.Players;
using Carbon.Combat.Attacks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Creatures.Definitions;
using Carbon.Economy.Currency;
using Carbon.Items.Catalog;
using Carbon.Items.Progression;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Progression;

/// <summary>
/// Character progression on the server (ADR-021, extracted per audit SOLID-1): derived stats from
/// <c>progression.json</c>, attribute allocation and paid reset, over-budget refunds, and how a monster hit on a player
/// resolves (damage kind, deterministic dodge, mitigation). Called only under the world lock.
/// </summary>
internal sealed class CharacterProgressionService
{
    private readonly WorldSimulation _simulation;
    private readonly Func<ItemRules> _itemRules;
    private readonly Func<ulong> _currentTick;
    private readonly Action<PlayerState> _markCharacterDirty;
    private readonly Action<PlayerState> _queueWalletSync;
    private readonly HashSet<ushort> _casterArchetypes;

    /// <summary>Initializes a new instance of the <see cref="CharacterProgressionService"/> class.</summary>
    /// <param name="simulation">World simulation (player health components).</param>
    /// <param name="config">Initial progression tuning.</param>
    /// <param name="monsters">Monster catalog (casters are monsters with a spell attack).</param>
    /// <param name="itemRules">Returns the current item rules (gear stats).</param>
    /// <param name="currentTick">Returns the current tick (wallet ledger).</param>
    /// <param name="markCharacterDirty">Queues a character for persistence.</param>
    /// <param name="queueWalletSync">Queues a wallet sync after a paid reset.</param>
    public CharacterProgressionService(
        WorldSimulation simulation,
        ProgressionConfig config,
        MonsterCatalog monsters,
        Func<ItemRules> itemRules,
        Func<ulong> currentTick,
        Action<PlayerState> markCharacterDirty,
        Action<PlayerState> queueWalletSync)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        this.Config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(monsters);
        _itemRules = itemRules ?? throw new ArgumentNullException(nameof(itemRules));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        _markCharacterDirty = markCharacterDirty ?? throw new ArgumentNullException(nameof(markCharacterDirty));
        _queueWalletSync = queueWalletSync ?? throw new ArgumentNullException(nameof(queueWalletSync));
        _casterArchetypes = BuildCasterArchetypes(monsters);
    }

    /// <summary>Gets the progression tuning currently in use.</summary>
    public ProgressionConfig Config { get; private set; }

    /// <summary>
    /// Switches to a new tuning (hot reload) and re-evaluates <paramref name="players"/>: levels are clamped to the new
    /// cap, allocations the new budget no longer covers are refunded, and derived stats are recomputed.
    /// </summary>
    /// <param name="config">New tuning.</param>
    /// <param name="players">Connected players.</param>
    public void Reconfigure(ProgressionConfig config, IEnumerable<PlayerState> players)
    {
        this.Config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(players);
        foreach (PlayerState state in players)
        {
            state.Level = (ushort)Math.Min(state.Level, config.MaxLevel);
            _ = this.RefundIfOverBudget(state);
            this.RecomputeDerivedStats(state);
            state.StatsDirty = true;
        }
    }

    /// <summary>Recomputes <see cref="PlayerState.Stats"/> and clamps current health, mana and stamina to the new maxima.</summary>
    /// <param name="state">Player.</param>
    public void RecomputeDerivedStats(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        DerivedCharacterStats stats = CharacterStatCalculator.Compute(
            _itemRules(),
            this.Config,
            state.Level,
            state.Inventory,
            state.Session.SelectedHotbarSlot,
            state.Progression);
        state.Stats = stats;
        if (_simulation.Entities.HasHealth(state.Entity))
        {
            HealthComponent hp = _simulation.Entities.GetHealth(state.Entity);
            float clamped = MathF.Min(stats.MaxHealth, MathF.Max(1f, hp.Current));
            _simulation.Entities.SetHealth(state.Entity, new HealthComponent(clamped, stats.MaxHealth));
            _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
        }

        state.CurrentMana = Math.Clamp(state.CurrentMana, 0f, stats.MaxMana);
        state.CurrentStamina = Math.Clamp(state.CurrentStamina, 0f, stats.MaxStamina);
    }

    /// <summary>
    /// Spends up to <paramref name="requested"/> unspent points on <paramref name="attribute"/>.
    /// <see cref="OpStatus.Invalid"/> for an attribute without effects, <see cref="OpStatus.Conflict"/> with no point left.
    /// </summary>
    /// <param name="state">Player.</param>
    /// <param name="attribute">Attribute to raise.</param>
    /// <param name="requested">Points requested (already clamped by the caller).</param>
    /// <returns>The outcome.</returns>
    public OpStatus TryAllocateAttribute(PlayerState state, PrimaryAttributeId attribute, int requested)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (this.Config.Attributes.Get(attribute) is null)
        {
            return OpStatus.Invalid;
        }

        ushort unspent = this.Config.GetUnspentStatPoints(state.Level, state.Progression);
        if (unspent == 0 || requested <= 0)
        {
            return OpStatus.Conflict;
        }

        state.Progression.AddAttribute(attribute, (ushort)Math.Min(requested, unspent));
        this.ApplyAllocationChange(state);
        return OpStatus.Ok;
    }

    /// <summary>
    /// Refunds every attribute point (ADR-021 D6). Free through <c>respec.freeThroughLevel</c>, otherwise the gold price
    /// is debited first; a player who cannot pay, or has nothing allocated, gets <see cref="OpStatus.Conflict"/> and no change.
    /// </summary>
    /// <param name="state">Player.</param>
    /// <returns>The outcome.</returns>
    public OpStatus TryResetAttributes(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Progression.GetTotalSpentAttributePoints() == 0)
        {
            return OpStatus.Conflict;
        }

        long cost = this.Config.GetRespecCost(state.Level);
        if (cost > 0L)
        {
            if (!state.Wallet.TrySpend(CurrencyKind.Gold, cost, CurrencyReason.AttributeReset, _currentTick()).IsOk)
            {
                return OpStatus.Conflict;
            }

            _queueWalletSync(state);
        }

        ClearAttributes(state.Progression);
        this.ApplyAllocationChange(state);
        return OpStatus.Ok;
    }

    /// <summary>Refunds the allocation (for free) when the current budget no longer covers it (ADR-021 D7).</summary>
    /// <param name="state">Player.</param>
    /// <returns>Whether a refund happened.</returns>
    public bool RefundIfOverBudget(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!this.Config.IsOverBudget(state.Level, state.Progression))
        {
            return false;
        }

        ClearAttributes(state.Progression);
        _markCharacterDirty(state);
        return true;
    }

    /// <summary>Refunds a freshly loaded allocation (for free) when <paramref name="level"/>'s budget does not cover it.</summary>
    /// <param name="level">Character level.</param>
    /// <param name="progression">Loaded allocations.</param>
    /// <returns>Whether a refund happened (the caller persists it).</returns>
    public bool RefundIfOverBudget(int level, CharacterProgressionState progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        if (!this.Config.IsOverBudget(level, progression))
        {
            return false;
        }

        ClearAttributes(progression);
        return true;
    }

    /// <summary>
    /// Resolves a monster hit of <paramref name="rawDamage"/> on <paramref name="victim"/>: a deterministic dodge roll
    /// against the attacker's level, then defense or magic resist mitigation (ADR-021 §4).
    /// </summary>
    /// <param name="victim">Player hit.</param>
    /// <param name="rawDamage">Damage before mitigation.</param>
    /// <param name="kind">Damage kind.</param>
    /// <param name="attackerLevel">Attacker level.</param>
    /// <param name="dodged">Whether the hit was dodged (then the result is 0).</param>
    /// <returns>Damage to apply.</returns>
    public float ResolveIncomingDamage(PlayerState victim, float rawDamage, DamageKind kind, int attackerLevel, out bool dodged)
    {
        ArgumentNullException.ThrowIfNull(victim);
        DerivedCharacterStats stats = victim.Stats;
        dodged = CombatRoll.Roll(this.Config.GetEvasionChance(in stats, attackerLevel), ++victim.CombatRollNonce, CombatRoll.EvasionSalt);
        return dodged ? 0f : this.Config.Mitigate(rawDamage, kind, in stats, attackerLevel);
    }

    /// <summary>A caster's spells and bolts are magical; everything else a monster does is physical (ADR-021 D4).</summary>
    /// <param name="attackerArchetypeId">Attacking monster archetype.</param>
    /// <param name="kind">Attack kind.</param>
    /// <returns>The damage kind.</returns>
    public DamageKind ResolveMonsterDamageKind(ushort attackerArchetypeId, AttackKind kind) =>
        kind is AttackKind.Spell or AttackKind.Projectile && _casterArchetypes.Contains(attackerArchetypeId)
            ? DamageKind.Magical
            : DamageKind.Physical;

    /// <summary>Sets every attribute allocation to 0.</summary>
    /// <param name="progression">Allocations.</param>
    public static void ClearAttributes(CharacterProgressionState progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        for (int i = 0; i < CharacterProgressionState.MaxAttributeSlots; i++)
        {
            progression.SetAttribute((PrimaryAttributeId)i, 0);
        }
    }

    /// <summary>
    /// Recomputes stats after an allocation change. Raised maxima grant the difference (a Vitality point heals the HP it
    /// adds); lowered maxima clamp the current values.
    /// </summary>
    private void ApplyAllocationChange(PlayerState state)
    {
        (float Health, float Mana, float Stamina) previousMax = VitalPools.Capture(state);
        this.RecomputeDerivedStats(state);
        VitalPools.AddGrowth(_simulation, state, previousMax);
        state.StatsDirty = true;
        _markCharacterDirty(state);
    }

    private static HashSet<ushort> BuildCasterArchetypes(MonsterCatalog catalog)
    {
        HashSet<ushort> casters = [];
        foreach (MonsterDefinition def in catalog.Definitions)
        {
            foreach (AttackProfileDefinition attack in def.Attacks)
            {
                if (attack.Kind == AttackKind.Spell)
                {
                    _ = casters.Add(def.ArchetypeId);
                    break;
                }
            }
        }

        return casters;
    }
}
