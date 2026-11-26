// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.AI.Threat;
using Carbon.Contracts.Entities;
using Carbon.Combat.Hitboxes;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Physics.Shapes;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Networking;

namespace Carbon.Application.World;

public sealed partial class AuthoritativeWorld
{
    private void ProcessPlayerAttackLocked(ActivePlayerState state)
    {
        if (!_simulation.Entities.IsAlive(state.Entity) ||
            !_simulation.Entities.HasTransform(state.Entity))
        {
            return;
        }

        if (AbilityInputEncoding.TryDecodeSkill(state.PendingAttackAbility, out byte skillSlot))
        {
            this.ProcessActiveSkillLocked(state, skillSlot);
            return;
        }

        if (state.PendingAttackAbility < 9 && state.PendingAttackAbility != state.Session.SelectedHotbarSlot)
        {
            byte tier = ResolveSlotHarvestTier(state.Inventory, state.PendingAttackAbility);
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

        if (!state.Cooldowns.IsReady(AttackAbilityId, _currentTick))
        {
            return;
        }

        state.Cooldowns.StartCooldown(AttackAbilityId, _currentTick, (uint)Math.Max(10, this.TickRate / 2));

        Vector2 attackerPos = _simulation.Entities.GetTransform(state.Entity).Position;
        if (activeSlot.Quantity > 0 &&
            StarterGameCatalog.Items.TryGet(activeSlot.ItemId, out ItemArchetype? archetype) &&
            archetype.Category == ItemCategory.Placeable)
        {
            this.ProcessPlaceablePrimaryActionLocked(state, attackerPos, activeSlot, archetype);
            return;
        }

        Vector2 rawAim = state.PendingAttackAim - attackerPos;
        Vector2 aimDir = rawAim.LengthSquared() > 1e-4f ? Vector2.Normalize(rawAim) : Vector2.UnitX;

        HitboxQuery.QueryArc(
            _simulation,
            attackerPos,
            aimDir,
            radius: 72f,
            halfAngleRadians: 1.15f,
            _combatHitScratch,
            out int hitCount,
            layerMask: MonsterLayer,
            ignoreEntity: state.Entity);

        if (hitCount == 0 && state.PendingAttackTargetId != 0UL &&
            _simulation.TryGetEntityByNetworkId(state.PendingAttackTargetId, out Entity explicitTarget) &&
            _simulation.Entities.HasTransform(explicitTarget) &&
            _simulation.Entities.HasCollider(explicitTarget) &&
            (_simulation.Entities.GetCollider(explicitTarget).Layer & MonsterLayer) != 0U &&
            Vector2.DistanceSquared(attackerPos, _simulation.Entities.GetTransform(explicitTarget).Position) <= (72f * 72f))
        {
            _combatHitScratch[0] = explicitTarget;
            hitCount = 1;
        }

        if (hitCount == 0)
        {
            this.ProcessToolHarvestFallbackLocked(state, in activeSlot);
            return;
        }

        CombatStrikeEvaluation combatEval = ToolUsagePolicy.EvaluateCombatStrike(in activeSlot);
        bool isCrit = activeSlot.ItemId == StarterGameCatalog.IronSword &&
            combatEval.Outcome == ToolStrikeOutcome.Optimal &&
            ((_currentTick + state.Session.PlayerNetworkId) % 5UL == 0UL);
        float scaledDamage = combatEval.Damage * state.Stats.AttackMultiplier;
        float finalDamage = isCrit ? scaledDamage * 1.5f : scaledDamage;

        bool anyDamaged = this.ApplyMonsterHitsFromScratchLocked(state, attackerPos, aimDir, hitCount, finalDamage, isCrit, knockbackSpeed: 135f);

        if (anyDamaged &&
            combatEval.DurabilityLoss > 0f &&
            this.DegradeActiveSlotDurabilityLocked(state, in activeSlot, combatEval.DurabilityLoss, out ItemSlot degradedCombatSlot))
        {
            _pendingSlotChanges.Add(new OutboundSlotChange(
                state.Session.ConnectionId,
                state.Session.SelectedHotbarSlot,
                degradedCombatSlot));
        }
    }

    private bool TryConsumeActiveSlotPotionLocked(ActivePlayerState state, in ItemSlot activeSlot)
    {
        if (activeSlot.IsEmpty || activeSlot.Quantity <= 0 ||
            !StarterGameCatalog.Items.TryGet(activeSlot.ItemId, out ItemArchetype? def) ||
            def.Category != ItemCategory.Consumable)
        {
            return false;
        }

        if (!state.Cooldowns.IsReady(ConsumableAbilityId, _currentTick))
        {
            return true;
        }

        bool used = false;
        float combatTextAmount = 0f;
        float resultingHealth = 0f;

        if (activeSlot.ItemId == StarterGameCatalog.ManaTonic &&
            (state.CurrentMana < state.Stats.MaxMana || state.CurrentStamina < state.Stats.MaxStamina))
        {
            state.CurrentMana = MathF.Min(state.Stats.MaxMana, state.CurrentMana + 45f);
            state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + 30f);
            used = true;
        }
        else if (activeSlot.ItemId == StarterGameCatalog.BerryHerb && _simulation.Entities.HasHealth(state.Entity))
        {
            HealthComponent currentHp = _simulation.Entities.GetHealth(state.Entity);
            if (currentHp.Current < currentHp.Max)
            {
                float healedHp = MathF.Min(currentHp.Max, currentHp.Current + 25f);
                _simulation.Entities.SetHealth(state.Entity, new HealthComponent(healedHp, currentHp.Max));
                _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
                combatTextAmount = -25f;
                resultingHealth = healedHp;
                used = true;
            }
        }

        if (!used)
        {
            return false;
        }

        state.Cooldowns.StartCooldown(ConsumableAbilityId, _currentTick, (uint)Math.Max(15, (int)MathF.Round(this.TickRate * 1.5f)));
        state.StatsDirty = true;

        ItemSlot updatedSlot = activeSlot.Quantity > 1
            ? activeSlot with { Quantity = activeSlot.Quantity - 1 }
            : ItemSlot.Empty;
        state.Inventory.SetSlot(state.Session.SelectedHotbarSlot, in updatedSlot);
        _pendingSlotChanges.Add(new OutboundSlotChange(state.Session.ConnectionId, state.Session.SelectedHotbarSlot, updatedSlot));
        this.MarkCharacterDirtyLocked(state, inventoryChanged: true);

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

    private void ProcessActiveSkillLocked(ActivePlayerState state, byte skillSlot)
    {
        if (!CharacterProgressionPolicy.TryGetSkillSpec(skillSlot, out SkillSpec spec))
        {
            return;
        }

        uint abilityId = skillSlot switch
        {
            CharacterProgressionPolicy.SkillWhirlwind => Skill1AbilityId,
            CharacterProgressionPolicy.SkillArcaneNova => Skill2AbilityId,
            CharacterProgressionPolicy.SkillVitalSurge => Skill3AbilityId,
            _ => 0U,
        };
        if (abilityId == 0U || !state.Cooldowns.IsReady(abilityId, _currentTick))
        {
            return;
        }

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

        this.CollectNearbyConnectionsAroundWorldLocked(pos, _aoiRecipientsScratch);
        int castOffset = _flatRecipients.Count;
        for (int i = 0; i < _aoiRecipientsScratch.Count; i++)
        {
            _flatRecipients.Add(_aoiRecipientsScratch[i]);
        }

        if (_aoiRecipientsScratch.Count > 0)
        {
            _pendingSkillCasts.Add(new OutboundSkillCast(
                castOffset,
                _aoiRecipientsScratch.Count,
                state.Session.PlayerNetworkId,
                skillSlot,
                pos.X,
                pos.Y,
                pos.X + (aimDir.X * 64f),
                pos.Y + (aimDir.Y * 64f)));
        }

        if (skillSlot == CharacterProgressionPolicy.SkillVitalSurge)
        {
            if (_simulation.Entities.HasHealth(state.Entity))
            {
                HealthComponent hp = _simulation.Entities.GetHealth(state.Entity);
                float healAmt = MathF.Max(30f, hp.Max * 0.30f);
                float nextHp = MathF.Min(hp.Max, hp.Current + healAmt);
                _simulation.Entities.SetHealth(state.Entity, new HealthComponent(nextHp, hp.Max));
                _simulation.MarkDirty(state.Entity, EntityDirtyMask.Health);
                state.CurrentStamina = MathF.Min(state.Stats.MaxStamina, state.CurrentStamina + 45f);
                this.QueueCombatHitLocked(pos, state.Session.PlayerNetworkId, state.Session.PlayerNetworkId, -healAmt, nextHp, false, false);
            }

            this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
            return;
        }

        ItemSlot activeSlot = state.Session.SelectedHotbarSlot < state.Inventory.Capacity
            ? state.Inventory.GetSlot(state.Session.SelectedHotbarSlot)
            : ItemSlot.Empty;
        float weaponBase = ToolUsagePolicy.EvaluateCombatStrike(in activeSlot).Damage * state.Stats.AttackMultiplier;

        float radius = skillSlot == CharacterProgressionPolicy.SkillArcaneNova ? 170f : 96f;
        float halfAngle = skillSlot == CharacterProgressionPolicy.SkillArcaneNova ? 0.65f : MathF.PI;
        float dmgMult = skillSlot == CharacterProgressionPolicy.SkillArcaneNova ? 1.85f : 1.45f;
        float skillDamage = weaponBase * dmgMult;

        HitboxQuery.QueryArc(_simulation, pos, aimDir, radius, halfAngle, _combatHitScratch, out int hitCount, MonsterLayer, state.Entity);
        if (hitCount > 0)
        {
            _ = this.ApplyMonsterHitsFromScratchLocked(state, pos, aimDir, hitCount, skillDamage, isCritical: true, knockbackSpeed: 110f);
        }

        this.MarkCharacterDirtyLocked(state, inventoryChanged: false);
    }

    private bool ApplyMonsterHitsFromScratchLocked(
        ActivePlayerState attacker,
        Vector2 attackerPos,
        Vector2 aimDir,
        int hitCount,
        float damage,
        bool isCritical,
        float knockbackSpeed)
    {
        EntityAttachmentStore<ThreatTable> threatStore = _simulation.Entities.GetAttachmentStore<ThreatTable>();
        bool anyDamaged = false;

        for (int i = 0; i < hitCount; i++)
        {
            Entity target = _combatHitScratch[i];
            if (!_simulation.Entities.IsAlive(target) ||
                !_simulation.Entities.HasHealth(target) ||
                !_simulation.TryGetNetworkIdentity(target, out NetworkIdentity targetIdentity))
            {
                continue;
            }

            Vector2 targetPos = _simulation.Entities.HasTransform(target) ? _simulation.Entities.GetTransform(target).Position : attackerPos;
            if (!this.HasLineOfSightLocked(attackerPos, targetPos))
            {
                continue;
            }

            anyDamaged = true;
            HealthComponent hp = _simulation.Entities.GetHealth(target);
            float remaining = MathF.Max(0f, hp.Current - damage);
            bool killed = remaining <= 0f;

            _simulation.Entities.SetHealth(target, new HealthComponent(remaining, hp.Max));
            _simulation.MarkDirty(target, EntityDirtyMask.Health);

            if (!killed)
            {
                threatStore.GetOrAdd(target, static () => new ThreatTable()).AddThreat(attacker.Entity, damage * 2f);
                if (_simulation.Entities.HasComponent<AiBrainComponent>(target))
                {
                    ref AiBrainComponent brain = ref _simulation.Entities.GetComponent<AiBrainComponent>(target);
                    brain = brain with { State = AiState.Chase, TargetEntity = attacker.Entity, RepathCooldownTicks = 0 };
                }

                if (_simulation.Entities.HasVelocity(target) && knockbackSpeed > 0f)
                {
                    ref Velocity2D targetVel = ref _simulation.Entities.GetVelocity(target);
                    targetVel.Linear = aimDir * knockbackSpeed;
                    _simulation.MarkDirty(target, EntityDirtyMask.Velocity);
                }

                // Pack social aggro: alert nearby allied monsters within 160px to assist.
                _simulation.OverlapCircle(targetPos, 160f, _packAggroScratch, out int allyCount, MonsterLayer);
                for (int a = 0; a < allyCount; a++)
                {
                    Entity ally = _packAggroScratch[a];
                    if (ally == target || !_simulation.Entities.IsAlive(ally) || !_simulation.Entities.HasComponent<AiBrainComponent>(ally))
                    {
                        continue;
                    }

                    threatStore.GetOrAdd(ally, static () => new ThreatTable()).AddThreat(attacker.Entity, damage * 0.8f);
                    ref AiBrainComponent allyBrain = ref _simulation.Entities.GetComponent<AiBrainComponent>(ally);
                    if (allyBrain.State is AiState.Idle or AiState.Patrol)
                    {
                        allyBrain = allyBrain with { State = AiState.Chase, TargetEntity = attacker.Entity, RepathCooldownTicks = 0 };
                    }
                }
            }
            else
            {
                if (_actorsByNetworkId.Remove(targetIdentity.NetworkId, out NpcActorMeta meta))
                {
                    _pendingRespawns.Add(new PendingMonsterRespawn(_currentTick + (ulong)(this.TickRate * 45), meta));
                    ItemSlot loot = new(meta.DropItemId, Math.Max(1, meta.DropQuantity));
                    this.SpawnGroundLootLocked(targetPos, in loot);
                    this.GrantExperienceLocked(attacker, meta.ExperienceReward, out _);
                    this.MarkCharacterDirtyLocked(attacker, inventoryChanged: false);
                }

                _simulation.DespawnEntity(target);
            }

            this.QueueCombatHitLocked(
                targetPos,
                attacker.Session.PlayerNetworkId,
                targetIdentity.NetworkId,
                damage,
                remaining,
                isCritical,
                killed);
        }

        return anyDamaged;
    }

    private bool HasLineOfSightLocked(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float dist = delta.Length();
        if (dist <= 6f)
        {
            return true;
        }

        Ray2D ray = new(from, delta / dist, dist - 4f);
        return !_chunkWorld.Raycast(in ray, out _);
    }

    private void QueueCombatHitLocked(
        Vector2 worldPos,
        ulong attackerNetId,
        ulong defenderNetId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled)
    {
        this.CollectNearbyConnectionsAroundWorldLocked(worldPos, _aoiRecipientsScratch);
        if (_aoiRecipientsScratch.Count == 0)
        {
            return;
        }

        int offset = _flatRecipients.Count;
        for (int i = 0; i < _aoiRecipientsScratch.Count; i++)
        {
            _flatRecipients.Add(_aoiRecipientsScratch[i]);
        }

        _pendingCombatHits.Add(new OutboundCombatHit(
            offset,
            _aoiRecipientsScratch.Count,
            attackerNetId,
            defenderNetId,
            damage,
            remainingHealth,
            isCritical,
            isKilled));
    }

    private void GrantExperienceLocked(ActivePlayerState state, uint amount, out bool leveledUp)
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
        ActivePlayerState state,
        in ItemSlot activeSlot,
        float loss,
        out ItemSlot updatedSlot)
    {
        updatedSlot = activeSlot;
        if (loss <= 0f ||
            activeSlot.IsEmpty ||
            !StarterGameCatalog.Items.TryGet(activeSlot.ItemId, out ItemArchetype? def) ||
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

    private void DegradeEquippedArmorLocked(ActivePlayerState victim, float loss)
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
            _pendingSlotChanges.Add(new OutboundSlotChange(victim.Session.ConnectionId, (ushort)sIdx, updated));
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

    private bool HasNearbyFriendlyNpcLocked(Vector2 worldPos, float radiusSq)
    {
        foreach (KeyValuePair<ulong, NpcActorMeta> kv in _actorsByNetworkId)
        {
            if (kv.Value.Kind != EntityKind.Npc)
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
}
