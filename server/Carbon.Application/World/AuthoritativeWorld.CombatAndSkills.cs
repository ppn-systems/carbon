// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Combat;
using Carbon.Application.World.Players;
using Carbon.Application.World.Projectiles;
using Carbon.Application.World.Replication;
using Carbon.Contracts.Entities;
using Carbon.Combat.Hitboxes;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;

namespace Carbon.Application.World;

public sealed partial class AuthoritativeWorld
{
    private void ProcessPlayerAttackLocked(PlayerState state)
    {
        if (!_simulation.Entities.IsAlive(state.Entity) ||
            !_simulation.Entities.HasTransform(state.Entity))
        {
            return;
        }

        if (AbilityInputEncoding.TryDecodeSkill(state.PendingAttackAbility, out ushort skillSlot))
        {
            this.ProcessActiveSkillLocked(state, skillSlot);
            return;
        }

        if (state.PendingAttackAbility < 9 && state.PendingAttackAbility != state.Session.SelectedHotbarSlot)
        {
            byte tier = _inventory.ResolveHarvestTier(state.Inventory, state.PendingAttackAbility);
            state.Session = state.Session with { SelectedHotbarSlot = state.PendingAttackAbility, SelectedHarvestTier = tier };
            this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
        }

        ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;

        if (this.TryConsumeActiveSlotPotionLocked(state, in activeSlot))
        {
            return;
        }

        // Attacking, harvesting or building stands a resting player up (eating does not).
        this.WakeLocked(state);

        if (!state.Cooldowns.IsReady(AttackAbilityId, _currentTick))
        {
            return;
        }

        state.Cooldowns.StartCooldown(AttackAbilityId, _currentTick, _needs.ScaleAttackCooldown(state.Entity, (uint)Math.Max(10, this.TickRate / 2)));

        Vector2 attackerPos = _simulation.Entities.GetTransform(state.Entity).Position;
        if (activeSlot.Quantity > 0 &&
            _itemRules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? archetype) &&
            archetype.Category == ItemCategory.Placeable)
        {
            this.ProcessPlaceablePrimaryActionLocked(state, attackerPos, activeSlot, archetype);
            return;
        }

        Vector2 rawAim = state.PendingAttackAim - attackerPos;
        Vector2 aimDir = rawAim.LengthSquared() > 1e-4f ? Vector2.Normalize(rawAim) : Vector2.UnitX;

        if (activeSlot.Quantity > 0 &&
            _itemRules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? weaponDef) &&
            weaponDef.ProjectileSpeed > 0f)
        {
            if (weaponDef.AttackManaCost > 0f)
            {
                if (state.CurrentMana < weaponDef.AttackManaCost)
                {
                    return;
                }

                state.CurrentMana = MathF.Max(0f, state.CurrentMana - weaponDef.AttackManaCost);
                state.StatsDirty = true;
            }

            CombatStrikeEvaluation rangedEval = ToolUsagePolicy.EvaluateCombatStrike(_itemRules, in activeSlot);
            bool rangedCrit = !ToolUsagePolicy.IsWornOut(_itemRules, in activeSlot) &&
                CombatService.RollCriticalHit(weaponDef.CritChance, ++state.CombatRollNonce);
            float rangedDmg = rangedEval.Damage * state.Stats.AttackMultiplier;
            float finalRangedDmg = rangedCrit ? rangedDmg * 1.5f : rangedDmg;

            if (this.DegradeActiveSlotDurabilityLocked(state, in activeSlot, 0.8f, out ItemSlot degradedRanged))
            {
                _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(
                    state.Session.ConnectionId,
                    state.Session.SelectedHotbarSlot,
                    degradedRanged));
            }

            _combat.QueueSkillCast(attackerPos, aimDir, state.Session.PlayerNetworkId, AbilityInputEncoding.EncodePlayerProjectileCast(weaponDef.ProjectileArchetype));

            uint lifeTicks = (uint)Math.Max(12, MathF.Ceiling(weaponDef.AttackRange / weaponDef.ProjectileSpeed * this.TickRate));
            _ = _projectiles.Spawn(
                state.Session.PlayerNetworkId,
                state.Session.ConnectionId,
                fromPlayer: true,
                attackerPos + (aimDir * 10f),
                aimDir * weaponDef.ProjectileSpeed,
                weaponDef.ProjectileArchetype,
                finalRangedDmg,
                rangedCrit,
                lifeTicks);
            return;
        }

        float meleeRange = MeleeStrikeRangePx;
        ItemArchetype? meleeDef = null;
        if (!activeSlot.IsEmpty && _itemRules.Items.TryGet(activeSlot.ItemId, out meleeDef) && meleeDef.Capabilities.HasFlag(ToolCapabilities.MeleeWeapon))
        {
            meleeRange = Math.Clamp(meleeDef.AttackRange, MeleeStrikeRangePx, 96f);
        }

        HitboxQuery.QueryArc(
            _simulation,
            attackerPos,
            aimDir,
            radius: meleeRange,
            halfAngleRadians: 1.05f,
            _combatHitScratch,
            out int hitCount,
            layerMask: MonsterLayer,
            ignoreEntity: state.Entity);

        if (hitCount == 0 && state.PendingAttackTargetId != 0UL &&
            _simulation.TryGetEntityByNetworkId(state.PendingAttackTargetId, out Entity explicitTarget) &&
            _simulation.Entities.HasTransform(explicitTarget) &&
            _simulation.Entities.HasCollider(explicitTarget) &&
            (_simulation.Entities.GetCollider(explicitTarget).Layer & MonsterLayer) != 0U &&
            Vector2.DistanceSquared(attackerPos, _simulation.Entities.GetTransform(explicitTarget).Position) <= (meleeRange * meleeRange))
        {
            _combatHitScratch[0] = explicitTarget;
            hitCount = 1;
        }

        if (hitCount == 0)
        {
            _props.SwingAtWorld(state, attackerPos, aimDir, in activeSlot);
            return;
        }

        CombatStrikeEvaluation combatEval = ToolUsagePolicy.EvaluateCombatStrike(_itemRules, in activeSlot);
        bool isCrit = meleeDef is not null &&
            combatEval.Outcome == ToolStrikeOutcome.Optimal &&
            CombatService.RollCriticalHit(meleeDef.CritChance, ++state.CombatRollNonce);
        float scaledDamage = combatEval.Damage * state.Stats.AttackMultiplier;
        float finalDamage = isCrit ? scaledDamage * 1.5f : scaledDamage;

        bool anyDamaged = this.ApplyMonsterHitsFromScratchLocked(state, attackerPos, aimDir, hitCount, finalDamage, isCrit, knockbackSpeed: 135f);

        if (anyDamaged &&
            combatEval.DurabilityLoss > 0f &&
            this.DegradeActiveSlotDurabilityLocked(state, in activeSlot, combatEval.DurabilityLoss, out ItemSlot degradedCombatSlot))
        {
            _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(
                state.Session.ConnectionId,
                state.Session.SelectedHotbarSlot,
                degradedCombatSlot));
        }
    }

    private bool TryConsumeActiveSlotPotionLocked(PlayerState state, in ItemSlot activeSlot)
    {
        if (this.TryBeginEatingActiveSlotLocked(state, in activeSlot))
        {
            return true;
        }

        if (activeSlot.IsEmpty || activeSlot.Quantity <= 0 ||
            !_itemRules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? def) ||
            def.Category != ItemCategory.Consumable)
        {
            return false;
        }

        if (!state.Cooldowns.IsReady(ConsumableAbilityId, _currentTick))
        {
            return true;
        }

        if (!this.ApplyConsumableRestoresLocked(state, def))
        {
            return false;
        }

        state.Cooldowns.StartCooldown(ConsumableAbilityId, _currentTick, (uint)Math.Max(15, (int)MathF.Round(this.TickRate * 1.5f)));
        state.StatsDirty = true;

        ItemSlot updatedSlot = activeSlot.Quantity > 1
            ? activeSlot with { Quantity = activeSlot.Quantity - 1 }
            : ItemSlot.Empty;
        state.Inventory.SetSlot(state.Session.SelectedHotbarSlot, in updatedSlot);
        _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, state.Session.SelectedHotbarSlot, updatedSlot));
        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
        return true;
    }

    /// <summary>Applies a consumable's HP/MP/stamina restores; returns false when nothing could be restored.</summary>
    private bool ApplyConsumableRestoresLocked(PlayerState state, ItemArchetype def)
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
            this.QueueCombatHitLocked(
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

    private void ProcessActiveSkillLocked(PlayerState state, ushort skillId)
    {
        byte rank = state.Progression.GetSkillRank(skillId);
        if (rank == 0 || !SkillCatalog.Skills.TryGet(skillId, out SkillDefinition? skillDef))
        {
            return;
        }

        uint abilityId = SkillAbilityIdBase + skillId;
        if (!state.Cooldowns.IsReady(abilityId, _currentTick))
        {
            return;
        }

        SkillSpec spec = skillDef.EvaluateRank(rank);
        if (state.CurrentMana < spec.ManaCost || state.CurrentStamina < spec.StaminaCost)
        {
            return;
        }

        state.CurrentMana = MathF.Max(0f, state.CurrentMana - spec.ManaCost);
        state.CurrentStamina = MathF.Max(0f, state.CurrentStamina - spec.StaminaCost);
        state.StatsDirty = true;
        state.Cooldowns.StartCooldown(abilityId, _currentTick, (uint)Math.Max(1, MathF.Ceiling(spec.CooldownSeconds * this.TickRate)));

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
                this.QueueCombatHitLocked(pos, state.Session.PlayerNetworkId, state.Session.PlayerNetworkId, -healAmt, nextHp, false, false);
            }

            this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            return;
        }

        ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;
        float basePower = skillDef.ScalingMode == SkillScalingMode.ArcaneSpell
            ? 30f * state.Stats.SpellPowerMultiplier
            : ToolUsagePolicy.EvaluateCombatStrike(_itemRules, in activeSlot).Damage * state.Stats.AttackMultiplier;
        float skillDamage = basePower * spec.DamageMultiplier;

        HitboxQuery.QueryArc(_simulation, pos, aimDir, spec.Radius, spec.HalfAngleRad, _combatHitScratch, out int hitCount, MonsterLayer, state.Entity);
        if (hitCount > 0)
        {
            _ = this.ApplyMonsterHitsFromScratchLocked(state, pos, aimDir, hitCount, skillDamage, isCritical: false, knockbackSpeed: skillDef.KnockbackSpeed);
        }

        this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
    }

    private bool ApplyMonsterHitsFromScratchLocked(
        PlayerState attacker,
        Vector2 attackerPos,
        Vector2 aimDir,
        int hitCount,
        float damage,
        bool isCritical,
        float knockbackSpeed) =>
        _combat.ApplyMonsterHits(attacker, attackerPos, aimDir, _combatHitScratch.AsSpan(0, hitCount), damage, isCritical, knockbackSpeed);

    /// <summary>A player killed a monster: drop its loot (economy loot table, else its catalog drops) and grant experience.</summary>
    private void OnMonsterKilledLocked(PlayerState attacker, ulong monsterNetworkId, Vector2 position)
    {
        if (_actorsByNetworkId.Remove(monsterNetworkId, out NpcActorMeta meta))
        {
            if (!this.TryRollEconomyLootLocked(attacker, meta.LootTableId ?? ResolveLootTableId(meta.ArchetypeId), meta.Level, monsterNetworkId, position))
            {
                ItemSlot loot = new(meta.DropItemId, Math.Max(1, meta.DropQuantity));
                this.SpawnGroundLootLocked(position, in loot);

                MonsterArchetypeSpec spec = WorldActorCatalog.GetMonsterSpec(meta.ArchetypeId);
                ulong rollHash = (_currentTick * 1103515245UL) + monsterNetworkId;
                float roll = rollHash % 1000UL / 1000f;
                if (spec.SecondaryDropItemId != 0U && spec.SecondaryDropQuantity > 0 && roll < spec.SecondaryDropChance)
                {
                    ItemSlot secondaryLoot = new(spec.SecondaryDropItemId, spec.SecondaryDropQuantity);
                    this.SpawnGroundLootLocked(position + new Vector2(10f, 4f), in secondaryLoot);
                }

                ulong rareHash = ((_currentTick + 17UL) * 2654435761UL) + monsterNetworkId;
                float rareRoll = rareHash % 1000UL / 1000f;
                if (spec.RareDropItemId != 0U && rareRoll < spec.RareDropChance)
                {
                    float dur = _itemRules.Items.TryGet(spec.RareDropItemId, out ItemArchetype? rareDef)
                        ? rareDef.MaxDurability
                        : 0f;
                    ItemSlot rareLoot = new(spec.RareDropItemId, 1, 0, dur);
                    this.SpawnGroundLootLocked(position + new Vector2(-10f, 6f), in rareLoot);
                }
            }

            this.GrantExperienceLocked(attacker, meta.ExperienceReward, out _);
            this.MarkCharacterDirtyLocked(attacker, inventoryChanged: false);
        }
    }

    private void QueueCombatHitLocked(
        Vector2 worldPos,
        ulong attackerNetId,
        ulong defenderNetId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled) =>
        _combat.QueueCombatHit(worldPos, attackerNetId, defenderNetId, damage, remainingHealth, isCritical, isKilled);

    private void GrantExperienceLocked(PlayerState state, uint amount, out bool leveledUp)
    {
        leveledUp = false;
        if (amount == 0U || state.Level >= CharacterProgressionPolicy.MaxLevel)
        {
            return;
        }

        state.Experience += amount;
        while (state.Level < CharacterProgressionPolicy.MaxLevel)
        {
            uint needed = CharacterProgressionPolicy.GetXpToNextLevel(state.Level);
            if (state.Experience < needed)
            {
                break;
            }

            state.Experience -= needed;
            state.Level++;
            leveledUp = true;
            state.LeveledUpPending = true;
        }

        if (leveledUp)
        {
            this.RecomputeDerivedStatsLocked(state);
            if (_simulation.Entities.HasHealth(state.Entity))
            {
                _simulation.Entities.SetHealth(state.Entity, new HealthComponent(state.Stats.MaxHealth, state.Stats.MaxHealth));
                _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
            }

            state.CurrentMana = state.Stats.MaxMana;
            state.CurrentStamina = state.Stats.MaxStamina;

            if (_simulation.Entities.HasTransform(state.Entity))
            {
                this.EnqueueEquipmentBroadcastLocked(state, _simulation.Entities.GetTransform(state.Entity).Position);
            }
        }

        state.StatsDirty = true;
        state.PersistenceDirty = true;
    }

    private bool DegradeActiveSlotDurabilityLocked(
        PlayerState state,
        in ItemSlot activeSlot,
        float loss,
        out ItemSlot updatedSlot)
    {
        updatedSlot = activeSlot;
        if (loss <= 0f ||
            activeSlot.IsEmpty ||
            !_itemRules.Items.TryGet(activeSlot.ItemId, out ItemArchetype? def) ||
            def.MaxDurability <= 0f)
        {
            return false;
        }

        float current = MathF.Max(0f, activeSlot.Durability);
        float next = MathF.Max(0f, current - loss);
        if (MathF.Abs(next - current) < 0.001f)
        {
            return false;
        }

        updatedSlot = activeSlot with { Durability = next };
        state.Inventory.SetSlot(state.Session.SelectedHotbarSlot, in updatedSlot);
        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
        return true;
    }

    private void DegradeEquippedArmorLocked(PlayerState victim, float loss)
    {
        bool anyBroke = false;
        for (int sIdx = PlayerInventoryLayout.HeadSlot; sIdx <= PlayerInventoryLayout.FeetSlot; sIdx++)
        {
            if (sIdx >= victim.Inventory.Capacity)
            {
                break;
            }

            ItemSlot slot = victim.Inventory.GetSlot(sIdx);
            if (slot.IsEmpty || slot.Durability <= 0f)
            {
                continue;
            }

            float nextDur = MathF.Max(0f, slot.Durability - loss);
            ItemSlot updated = slot with { Durability = nextDur };
            victim.Inventory.SetSlot(sIdx, in updated);
            _outbound.Pending.SlotChanges.Add(new OutboundSlotChange(victim.Session.ConnectionId, (ushort)sIdx, updated));
            if (nextDur <= 0f)
            {
                anyBroke = true;
            }
        }

        if (anyBroke)
        {
            this.RecomputeDerivedStatsLocked(victim);
        }

        this.MarkCharacterDirtyLocked(victim, inventoryChanged: true);
    }

    private bool HasNearbyFriendlyNpcLocked(Vector2 worldPos, float radiusSq) =>
        this.HasNearbyNpcWithCapabilityLocked(worldPos, radiusSq, NpcCapabilities.TownGuard | NpcCapabilities.Shrine | NpcCapabilities.Harbour | NpcCapabilities.ForgeArtisan);

    private bool HasNearbyNpcWithCapabilityLocked(Vector2 worldPos, float radiusSq, NpcCapabilities requiredCapabilities)
    {
        foreach (KeyValuePair<ulong, NpcActorMeta> kv in _actorsByNetworkId)
        {
            if (kv.Value.Kind != EntityKind.Npc)
            {
                continue;
            }

            NpcCapabilities caps = WorldActorCatalog.GetNpcSpec(kv.Value.ArchetypeId).Capabilities;
            if ((caps & requiredCapabilities) == 0)
            {
                continue;
            }

            if (_simulation.TryGetEntityByNetworkId(kv.Key, out Entity npcEntity) &&
                _simulation.Entities.HasTransform(npcEntity) &&
                Vector2.DistanceSquared(worldPos, _simulation.Entities.GetTransform(npcEntity).Position) <= radiusSq)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see cref="IProjectileHitHandler"/> for <see cref="ProjectileService"/>: routes hits into the world's damage
    /// pipeline. Invoked inside the locked simulation tick.
    /// </summary>
    private sealed class ProjectileHits(AuthoritativeWorld owner) : IProjectileHitHandler
    {
        public void OnMonsterHit(in ProjectileState projectile, Entity monster, Vector2 segmentStart, Vector2 shotDirection)
        {
            if (owner._players.ByConnection.TryGetValue(projectile.OwnerConnectionId, out PlayerState? ownerState))
            {
                owner._combatHitScratch[0] = monster;
                _ = owner.ApplyMonsterHitsFromScratchLocked(
                    ownerState,
                    segmentStart,
                    shotDirection,
                    hitCount: 1,
                    projectile.Damage,
                    projectile.IsCritical,
                    knockbackSpeed: 85f);
            }
        }

        public bool TryHitPlayer(in ProjectileState projectile, Vector2 segmentStart, Vector2 segmentEnd)
        {
            foreach (KeyValuePair<ulong, PlayerState> pkv in owner._players.ByConnection)
            {
                PlayerState player = pkv.Value;
                if (!owner._simulation.Entities.IsAlive(player.Entity) ||
                    !owner._simulation.Entities.HasTransform(player.Entity) ||
                    !owner._simulation.Entities.HasHealth(player.Entity))
                {
                    continue;
                }

                Vector2 playerPos = owner._simulation.Entities.GetTransform(player.Entity).Position;
                float hitR = DefaultEntityRadius + 5f;
                if (ProjectileService.DistancePointToSegmentSq(playerPos, segmentStart, segmentEnd, out _) <= (hitR * hitR))
                {
                    owner.ApplyDamageToPlayerLocked(
                        player,
                        projectile.OwnerNetworkId,
                        player.Entity,
                        player.Session.PlayerNetworkId,
                        playerPos,
                        projectile.Damage);
                    return true;
                }
            }

            return false;
        }
    }
}
