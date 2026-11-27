// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Application.World.Combat;
using Carbon.Application.World.Inventory;
using Carbon.Application.World.Needs;
using Carbon.Application.World.Players;
using Carbon.Application.World.Projectiles;
using Carbon.Application.World.Props;
using Carbon.Application.World.Replication;
using Carbon.Application.World.Stamina;
using Carbon.Combat.Hitboxes;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Progression;
using Carbon.Primitives.Random;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Actions;

/// <summary>World-side effects of a player action that belong to other parts of the world.</summary>
internal interface IPlayerActionHost
{
    /// <summary>Flags the character for saving.</summary>
    void MarkCharacterDirty(PlayerState state, bool inventoryChanged);

    /// <summary>Stands a resting player up.</summary>
    void Wake(PlayerState state);

    /// <summary>Starts a meal when the active slot holds food; returns whether the item was handled as food.</summary>
    bool TryBeginEating(PlayerState state, in ItemSlot activeSlot);

    /// <summary>Uses a placeable kit (places its blueprint).</summary>
    void UsePlaceable(PlayerState state, Vector2 attackerPos, ItemSlot activeSlot, ItemArchetype archetype);

    /// <summary>Recomputes the player's derived stats from level, gear and progression.</summary>
    void RecomputeDerivedStats(PlayerState state);

    /// <summary>Broadcasts the player's visible armor and held weapon to nearby players (after an item broke, ADR-035).</summary>
    void BroadcastEquipment(PlayerState state);
}

/// <summary>
/// A player's primary action and skills (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>): switching to the
/// chosen hotbar slot, eating or drinking a consumable, placing a kit, shooting a ranged weapon (mana, wear, projectile),
/// a melee strike (arc query, explicit target fallback, crits, wear) or a tool swing at the world, and active skills
/// (cost, cooldown, self-restore or arc damage). Also wears tools and armor. Runs inside the world tick lock.
/// </summary>
internal sealed class PlayerActionService
{
    /// <summary>Cooldown slot of the primary attack / tool swing.</summary>
    public const uint AttackAbilityId = 1U;

    /// <summary>Cooldown slot base for skills (<c>SkillAbilityIdBase + skillId</c>).</summary>
    public const uint SkillAbilityIdBase = 100U;

    /// <summary>Cooldown slot of consumables.</summary>
    public const uint ConsumableAbilityId = 5U;

    /// <summary>Minimum melee reach; melee weapons may reach further (up to 96px).</summary>
    public const float MeleeStrikeRangePx = 56f;

    private readonly WorldSimulation _simulation;
    private readonly int _tickRate;
    private readonly uint _monsterLayer;
    private readonly Func<ItemRules> _itemRules;
    private readonly Func<ulong> _currentTick;
    private readonly OutboundTickBuffer _outbound;
    private readonly CombatService _combat;
    private readonly ProjectileService _projectiles;
    private readonly PropHarvestService _props;
    private readonly InventoryService _inventory;
    private readonly NeedsService _needs;
    private readonly StaminaService _stamina;
    private readonly IPlayerActionHost _host;
    private readonly IGameplayEntropy _entropy;
    private readonly Entity[] _hitScratch = new Entity[32];

    /// <summary>Initializes a new instance of the <see cref="PlayerActionService"/> class.</summary>
    public PlayerActionService(
        WorldSimulation simulation,
        int tickRate,
        uint monsterLayer,
        Func<ItemRules> itemRules,
        Func<ulong> currentTick,
        OutboundTickBuffer outbound,
        CombatService combat,
        ProjectileService projectiles,
        PropHarvestService props,
        InventoryService inventory,
        NeedsService needs,
        StaminaService stamina,
        IPlayerActionHost host,
        IGameplayEntropy entropy)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _tickRate = tickRate;
        _monsterLayer = monsterLayer;
        _itemRules = itemRules ?? throw new ArgumentNullException(nameof(itemRules));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _props = props ?? throw new ArgumentNullException(nameof(props));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _needs = needs ?? throw new ArgumentNullException(nameof(needs));
        _stamina = stamina ?? throw new ArgumentNullException(nameof(stamina));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _entropy = entropy ?? throw new ArgumentNullException(nameof(entropy));
    }

    private ItemRules Rules => _itemRules();

    private bool RollCritical(PlayerState state)
    {
        ulong tick = _currentTick();
        return CombatService.RollCriticalHit(state.Stats.CritChance, _entropy.KeyAt(tick), tick, state.Session.PlayerNetworkId, ++state.CombatRollNonce);
    }

    private ulong Tick => _currentTick();

    public void Attack(PlayerState state)
    {
        if (!_simulation.Entities.IsAlive(state.Entity) ||
            !_simulation.Entities.HasTransform(state.Entity))
        {
            return;
        }

        if (AbilityInputEncoding.TryDecodeSkill(state.PendingAttackAbility, out ushort skillSlot))
        {
            this.UseSkill(state, skillSlot);
            return;
        }

        if (state.PendingAttackAbility < 9 && state.PendingAttackAbility != state.Session.SelectedHotbarSlot)
        {
            byte tier = _inventory.ResolveHarvestTier(state.Inventory, state.PendingAttackAbility);
            state.Session = state.Session with { SelectedHotbarSlot = state.PendingAttackAbility, SelectedHarvestTier = tier };
            _host.MarkCharacterDirty(state, inventoryChanged: false);
        }

        ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;

        if (this.TryUseConsumable(state, in activeSlot))
        {
            return;
        }

        // Attacking, harvesting or building stands a resting player up (eating does not).
        _host.Wake(state);

        if (!state.Cooldowns.IsReady(AttackAbilityId, this.Tick))
        {
            return;
        }

        ItemArchetype? placeable = activeSlot.Quantity > 0 &&
            this.Rules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? archetype) &&
            archetype.Category == ItemCategory.Placeable
                ? archetype
                : null;

        Vector2 attackerPos = _simulation.Entities.GetTransform(state.Entity).Position;
        if (placeable is not null)
        {
            state.Cooldowns.StartCooldown(AttackAbilityId, this.Tick, this.AttackCooldownTicks(state));
            _host.UsePlaceable(state, attackerPos, activeSlot, placeable);
            return;
        }

        Vector2 rawAim = state.PendingAttackAim - attackerPos;
        Vector2 aimDir = rawAim.LengthSquared() > 1e-4f ? Vector2.Normalize(rawAim) : Vector2.UnitX;

        ItemArchetype? rangedDef = activeSlot.Quantity > 0 &&
            this.Rules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? weaponDef) &&
            weaponDef.ProjectileSpeed > 0f
                ? weaponDef
                : null;

        int hitCount = rangedDef is null ? this.FindMeleeTargets(state, in activeSlot, attackerPos, aimDir) : 0;

        // ADR-033 amendment: a melee, tool or bare-hand swing pays only when it reaches a target (a monster, or a prop or
        // structure in reach). A whiff is free but still starts the cooldown. A shot pays on release: the shot is spent.
        // A paid swing the player cannot afford is refused: no cooldown, no damage, no wear.
        bool connects = rangedDef is not null || hitCount > 0 || _props.HasStrikeTarget(state, attackerPos, aimDir);
        if (connects && !_stamina.TrySpend(state, _stamina.GetSwingCost(this.Rules, in activeSlot)))
        {
            return;
        }

        state.Cooldowns.StartCooldown(AttackAbilityId, this.Tick, this.AttackCooldownTicks(state));

        if (rangedDef is not null)
        {
            if (rangedDef.AttackManaCost > 0f)
            {
                if (state.CurrentMana < rangedDef.AttackManaCost)
                {
                    return;
                }

                state.CurrentMana = MathF.Max(0f, state.CurrentMana - rangedDef.AttackManaCost);
                state.StatsDirty = true;
            }

            CombatStrikeEvaluation rangedEval = ToolUsagePolicy.EvaluateCombatStrike(this.Rules, in activeSlot);
            bool rangedCrit = this.RollCritical(state);

            // Staffs (MagicFocus) scale with spell power, bows with physical attack (ADR-021 D4).
            float rangedScale = rangedDef.Capabilities.HasFlag(ToolCapabilities.MagicFocus) ? state.Stats.SpellPowerMultiplier : state.Stats.AttackMultiplier;
            float finalRangedDmg = CombatRoll.ApplyCritical(rangedEval.Damage * rangedScale, rangedCrit, state.Stats.CritDamage);

            if (this.WearActiveTool(state, in activeSlot, 0.8f, out ItemSlot degradedRanged))
            {
                _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(
                    state.Session.ConnectionId,
                    state.Session.SelectedHotbarSlot,
                    degradedRanged));
            }

            _combat.QueueSkillCast(attackerPos, aimDir, state.Session.PlayerNetworkId, AbilityInputEncoding.EncodePlayerProjectileCast(rangedDef.ProjectileArchetype));

            uint lifeTicks = (uint)Math.Max(12, MathF.Ceiling(rangedDef.AttackRange / rangedDef.ProjectileSpeed * _tickRate));
            _ = _projectiles.Spawn(
                state.Session.PlayerNetworkId,
                state.Session.ConnectionId,
                fromPlayer: true,
                attackerPos + (aimDir * 10f),
                aimDir * rangedDef.ProjectileSpeed,
                rangedDef.ProjectileArchetype,
                finalRangedDmg,
                rangedCrit,
                lifeTicks);
            return;
        }

        if (hitCount == 0)
        {
            _props.SwingAtWorld(state, attackerPos, aimDir, in activeSlot);
            return;
        }

        CombatStrikeEvaluation combatEval = ToolUsagePolicy.EvaluateCombatStrike(this.Rules, in activeSlot);
        bool isCrit = combatEval.Outcome == ToolStrikeOutcome.Optimal &&
            this.RollCritical(state);
        float finalDamage = CombatRoll.ApplyCritical(combatEval.Damage * state.Stats.AttackMultiplier, isCrit, state.Stats.CritDamage);

        bool anyDamaged = _combat.ApplyMonsterHits(state, attackerPos, aimDir, _hitScratch.AsSpan(0, hitCount), finalDamage, isCrit, knockbackSpeed: 135f);

        if (anyDamaged &&
            combatEval.DurabilityLoss > 0f &&
            this.WearActiveTool(state, in activeSlot, combatEval.DurabilityLoss, out ItemSlot degradedCombatSlot))
        {
            _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(
                state.Session.ConnectionId,
                state.Session.SelectedHotbarSlot,
                degradedCombatSlot));
        }
    }

    private uint AttackCooldownTicks(PlayerState state) =>
        _needs.ScaleAttackCooldown(state.Entity, GetAttackCooldownTicks(_tickRate, state.Stats.AttackSpeedMultiplier));

    /// <summary>Fills <c>_hitScratch</c> with the monsters a melee swing reaches; returns how many.</summary>
    private int FindMeleeTargets(PlayerState state, in ItemSlot activeSlot, Vector2 attackerPos, Vector2 aimDir)
    {
        float meleeRange = MeleeStrikeRangePx;
        if (!activeSlot.IsEmpty && this.Rules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? meleeDef) && meleeDef.Capabilities.HasFlag(ToolCapabilities.MeleeWeapon))
        {
            meleeRange = Math.Clamp(meleeDef.AttackRange, MeleeStrikeRangePx, 96f);
        }

        HitboxQuery.QueryArc(
            _simulation,
            attackerPos,
            aimDir,
            radius: meleeRange,
            halfAngleRadians: 1.05f,
            _hitScratch,
            out int hitCount,
            layerMask: _monsterLayer,
            ignoreEntity: state.Entity);

        if (hitCount == 0 && state.PendingAttackTargetId != 0UL &&
            _simulation.TryGetEntityByNetworkId(state.PendingAttackTargetId, out Entity explicitTarget) &&
            _simulation.Entities.HasTransform(explicitTarget) &&
            _simulation.Entities.HasCollider(explicitTarget) &&
            (_simulation.Entities.GetCollider(explicitTarget).Layer & _monsterLayer) != 0U &&
            Vector2.DistanceSquared(attackerPos, _simulation.Entities.GetTransform(explicitTarget).Position) <= (meleeRange * meleeRange))
        {
            _hitScratch[0] = explicitTarget;
            hitCount = 1;
        }

        return hitCount;
    }

    /// <summary>
    /// Attack and tool swing cooldown: the base <c>max(10, tickRate / 2)</c> ticks divided by the attack speed multiplier
    /// (capped by <c>progression.json</c>), never below 5 ticks.
    /// </summary>
    /// <param name="tickRate">Simulation tick rate.</param>
    /// <param name="attackSpeedMultiplier">Derived attack speed.</param>
    /// <returns>Cooldown ticks.</returns>
    public static uint GetAttackCooldownTicks(int tickRate, float attackSpeedMultiplier)
    {
        float baseTicks = Math.Max(10, tickRate / 2);
        float speed = attackSpeedMultiplier >= 1f ? attackSpeedMultiplier : 1f;
        return (uint)Math.Max(5, (int)MathF.Round(baseTicks / speed));
    }

    private bool TryUseConsumable(PlayerState state, in ItemSlot activeSlot)
    {
        if (_host.TryBeginEating(state, in activeSlot))
        {
            return true;
        }

        if (activeSlot.IsEmpty || activeSlot.Quantity <= 0 ||
            !this.Rules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? def) ||
            def.Category != ItemCategory.Consumable)
        {
            return false;
        }

        if (!state.Cooldowns.IsReady(ConsumableAbilityId, this.Tick))
        {
            return true;
        }

        if (!this.ApplyConsumableRestores(state, def))
        {
            return false;
        }

        state.Cooldowns.StartCooldown(ConsumableAbilityId, this.Tick, (uint)Math.Max(15, (int)MathF.Round(_tickRate * 1.5f)));
        state.StatsDirty = true;

        ItemSlot updatedSlot = activeSlot.Quantity > 1
            ? activeSlot with { Quantity = activeSlot.Quantity - 1 }
            : ItemSlot.Empty;
        state.Inventory.SetSlot(state.Session.SelectedHotbarSlot, in updatedSlot);
        _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, state.Session.SelectedHotbarSlot, updatedSlot));
        _host.MarkCharacterDirty(state, inventoryChanged: true);
        return true;
    }

    /// <summary>Applies a consumable's HP/MP/stamina restores; returns false when nothing could be restored.</summary>
    public bool ApplyConsumableRestores(PlayerState state, ItemArchetype def)
    {
        bool used = false;
        float combatTextAmount = 0f;
        float resultingHealth = 0f;

        if (def.HealHealth > 0f && _simulation.Entities.HasHealth(state.Entity))
        {
            HealthComponent currentHp = _simulation.Entities.GetHealth(state.Entity);
            if (currentHp.Current < currentHp.Max)
            {
                float healedHp = MathF.Min(currentHp.Max, currentHp.Current + def.HealHealth);
                _simulation.Entities.SetHealth(state.Entity, new HealthComponent(healedHp, currentHp.Max));
                _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
                combatTextAmount = -def.HealHealth;
                resultingHealth = healedHp;
                used = true;
            }
        }

        if (def.RestoreMana > 0f && state.CurrentMana < state.Stats.MaxMana)
        {
            state.CurrentMana = MathF.Min(state.Stats.MaxMana, state.CurrentMana + def.RestoreMana);
            used = true;
        }

        if (def.RestoreStamina > 0f && state.CurrentStamina < state.Stats.MaxStamina)
        {
            state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + def.RestoreStamina);
            used = true;
        }

        if (!used)
        {
            return false;
        }

        if (combatTextAmount < 0f && _simulation.Entities.HasTransform(state.Entity))
        {
            _combat.QueueCombatHit(
                _simulation.Entities.GetTransform(state.Entity).Position,
                state.Session.PlayerNetworkId,
                state.Session.PlayerNetworkId,
                combatTextAmount,
                resultingHealth,
                isCritical: false,
                isKilled: false);
        }

        return true;
    }

    private void UseSkill(PlayerState state, ushort skillId)
    {
        byte rank = state.Progression.GetSkillRank(skillId);
        if (rank == 0 || !SkillCatalog.Skills.TryGet(skillId, out SkillDefinition? skillDef))
        {
            return;
        }

        uint abilityId = SkillAbilityIdBase + skillId;
        if (!state.Cooldowns.IsReady(abilityId, this.Tick))
        {
            return;
        }

        SkillSpec spec = skillDef.EvaluateRank(rank);
        if (state.CurrentMana < spec.ManaCost)
        {
            return;
        }

        // ADR-033: a skill's stamina cost goes through the meter, so exhaustion blocks it and the spend delays regen.
        if (!_stamina.TrySpend(state, spec.StaminaCost))
        {
            return;
        }

        state.CurrentMana = MathF.Max(0f, state.CurrentMana - spec.ManaCost);
        state.StatsDirty = true;
        state.Cooldowns.StartCooldown(abilityId, this.Tick, (uint)Math.Max(1, MathF.Ceiling(spec.CooldownSeconds * _tickRate)));

        Vector2 pos = _simulation.Entities.GetTransform(state.Entity).Position;
        Vector2 rawAim = state.PendingAttackAim - pos;
        Vector2 aimDir = rawAim.LengthSquared() > 1e-4f ? Vector2.Normalize(rawAim) : Vector2.UnitX;

        _combat.QueueSkillCast(pos, aimDir, state.Session.PlayerNetworkId, (byte)skillId);

        if (skillDef.EffectKind == SkillEffectKind.SelfRestore)
        {
            if (_simulation.Entities.HasHealth(state.Entity))
            {
                HealthComponent hp = _simulation.Entities.GetHealth(state.Entity);
                float healAmt = MathF.Max(20f, hp.Max * spec.SelfHealRatio);
                float nextHp = MathF.Min(hp.Max, hp.Current + healAmt);
                _simulation.Entities.SetHealth(state.Entity, new HealthComponent(nextHp, hp.Max));
                _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
                state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + spec.StaminaRestore);
                _combat.QueueCombatHit(pos, state.Session.PlayerNetworkId, state.Session.PlayerNetworkId, -healAmt, nextHp, false, false);
            }

            _host.MarkCharacterDirty(state, inventoryChanged: false);
            return;
        }

        ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;
        float basePower = skillDef.ScalingMode == SkillScalingMode.ArcaneSpell
            ? 30f * state.Stats.SpellPowerMultiplier
            : ToolUsagePolicy.EvaluateCombatStrike(this.Rules, in activeSlot).Damage * state.Stats.AttackMultiplier;
        float skillDamage = basePower * spec.DamageMultiplier;

        HitboxQuery.QueryArc(_simulation, pos, aimDir, spec.Radius, spec.HalfAngleRad, _hitScratch, out int hitCount, _monsterLayer, state.Entity);
        if (hitCount > 0)
        {
            _ = _combat.ApplyMonsterHits(state, pos, aimDir, _hitScratch.AsSpan(0, hitCount), skillDamage, isCritical: false, knockbackSpeed: skillDef.KnockbackSpeed);
        }

        _host.MarkCharacterDirty(state, inventoryChanged: false);
    }

    /// <summary>
    /// Wears the held item by <paramref name="loss"/> (ADR-035). The step that takes it to 0 destroys it: the slot is
    /// emptied, stats are recomputed, the new look is broadcast and the owner gets a <see cref="DurabilityAlertKind.Broke"/>
    /// alert; reaching the critical share queues a one-time <see cref="DurabilityAlertKind.AboutToBreak"/>. The caller
    /// pushes <paramref name="updatedSlot"/> (<see cref="ItemSlot.Empty"/> after a break) to the owner.
    /// </summary>
    /// <returns><see langword="true"/> when the item changed.</returns>
    public bool WearActiveTool(
        PlayerState state,
        in ItemSlot activeSlot,
        float loss,
        out ItemSlot updatedSlot)
    {
        DurabilityWearResult wear = DurabilityWear.Apply(this.Rules, in activeSlot, loss);
        updatedSlot = wear.Slot;
        if (!wear.Changed)
        {
            return false;
        }

        ushort slotIndex = state.Session.SelectedHotbarSlot;
        state.Inventory.SetSlot(slotIndex, in updatedSlot);
        this.QueueWearAlert(state, in activeSlot, in wear);
        if (wear.Broke)
        {
            // The held weapon feeds the derived stats and the look other players see.
            _host.RecomputeDerivedStats(state);
            _host.BroadcastEquipment(state);
            state.StatsDirty = true;
        }

        _host.MarkCharacterDirty(state, inventoryChanged: true);
        return true;
    }

    /// <summary>
    /// Wears every equipped armor piece (slots <c>36..39</c>) by <paramref name="loss"/> and pushes each change to the
    /// owner. A piece taken to 0 is destroyed (ADR-035): its slot empties, the owner gets a break alert, and stats are
    /// recomputed and the new look broadcast once.
    /// </summary>
    public void WearArmor(PlayerState victim, float loss)
    {
        bool anyBroke = false;
        bool anyChanged = false;
        ItemRules rules = this.Rules;
        for (int sIdx = PlayerInventoryLayout.HeadSlot; sIdx <= PlayerInventoryLayout.FeetSlot; sIdx++)
        {
            if (sIdx >= victim.Inventory.Capacity)
            {
                break;
            }

            ItemSlot slot = victim.Inventory.GetSlot(sIdx);
            DurabilityWearResult wear = DurabilityWear.Apply(rules, in slot, loss);
            if (!wear.Changed)
            {
                continue;
            }

            anyChanged = true;
            ItemSlot updated = wear.Slot;
            victim.Inventory.SetSlot(sIdx, in updated);
            _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(victim.Session.ConnectionId, (ushort)sIdx, updated));
            this.QueueWearAlert(victim, in slot, in wear);
            anyBroke |= wear.Broke;
        }

        if (anyBroke)
        {
            _host.RecomputeDerivedStats(victim);
            _host.BroadcastEquipment(victim);
            victim.StatsDirty = true;
        }

        if (anyChanged)
        {
            _host.MarkCharacterDirty(victim, inventoryChanged: true);
        }
    }

    private void QueueWearAlert(PlayerState owner, in ItemSlot before, in DurabilityWearResult wear)
    {
        DurabilityAlertKind kind = wear.Broke
            ? DurabilityAlertKind.Broke
            : wear.ReachedCritical ? DurabilityAlertKind.AboutToBreak : DurabilityAlertKind.None;
        if (kind == DurabilityAlertKind.None)
        {
            return;
        }

        _outbound.Pending.DurabilityAlerts.Add(new OutboundDurabilityAlert(
            owner.Session.ConnectionId,
            kind,
            before.ItemId,
            ItemEnhancementPolicy.GetUpgradeLevel(in before)));
    }
}
