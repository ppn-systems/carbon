// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Application.World.Actors;
using Carbon.Application.World.Commerce;
using Carbon.Application.World.Actions;
using Carbon.Application.World.Players;
using Carbon.Application.World.Projectiles;
using Carbon.Combat.Attacks;
using Carbon.Contracts.Entities;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Primitives.Random;
using Carbon.Simulation.ECS;

namespace Carbon.Application.World;

public sealed partial class AuthoritativeWorld
{
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
            if (!_commerce.TryRollLoot(attacker, meta.LootTableId ?? EconomyService.ResolveLootTableId(meta.ArchetypeId), meta.Level, monsterNetworkId, position))
            {
                ItemSlot loot = new(meta.DropItemId, Math.Max(1, meta.DropQuantity));
                this.SpawnGroundLootLocked(position, in loot);

                MonsterArchetypeSpec spec = WorldActorCatalog.GetMonsterSpec(meta.ArchetypeId);
                DeterministicRandom dropRandom = _entropy.KeyAt(_currentTick).CreateRandom(GameplayDomain.MonsterDrop, _currentTick, monsterNetworkId);
                float roll = dropRandom.NextFloat();
                if (spec.SecondaryDropItemId != 0U && spec.SecondaryDropQuantity > 0 && roll < spec.SecondaryDropChance)
                {
                    ItemSlot secondaryLoot = new(spec.SecondaryDropItemId, spec.SecondaryDropQuantity);
                    this.SpawnGroundLootLocked(position + new Vector2(10f, 4f), in secondaryLoot);
                }

                float rareRoll = dropRandom.NextFloat();
                if (spec.RareDropItemId != 0U && rareRoll < spec.RareDropChance)
                {
                    float dur = _itemRules.Items.TryGet(spec.RareDropItemId, out ItemArchetype? rareDef)
                        ? rareDef.MaxDurability
                        : 0f;
                    ItemSlot rareLoot = new(spec.RareDropItemId, 1, 0, dur);
                    this.SpawnGroundLootLocked(position + new Vector2(-10f, 6f), in rareLoot);
                }
            }

            this.GrantExperienceLocked(attacker, _progressionService.Config.ScaleKillExperience(meta.ExperienceReward, meta.Level, attacker.Level), out _);
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
        bool isKilled,
        ushort statusEffectId = 0,
        byte statusEffectLevel = 0) =>
        _combat.QueueCombatHit(worldPos, attackerNetId, defenderNetId, damage, remainingHealth, isCritical, isKilled, statusEffectId, statusEffectLevel);

    private void GrantExperienceLocked(PlayerState state, uint amount, out bool leveledUp)
    {
        leveledUp = false;
        if (amount == 0U || state.Level >= _progressionService.Config.MaxLevel)
        {
            return;
        }

        state.Experience += amount;
        while (state.Level < _progressionService.Config.MaxLevel)
        {
            uint needed = _progressionService.Config.GetXpToNextLevel(state.Level);
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
            // A level-up never refills vitals (killing a monster must not heal): only the max-pool growth is added.
            (float Health, float Mana, float Stamina) previousMax = VitalPools.Capture(state);
            this.RecomputeDerivedStatsLocked(state);
            VitalPools.AddGrowth(_simulation, state, previousMax);

            if (_simulation.Entities.HasTransform(state.Entity))
            {
                this.EnqueueEquipmentBroadcastLocked(state, _simulation.Entities.GetTransform(state.Entity).Position);
            }
        }

        state.StatsDirty = true;
        state.PersistenceDirty = true;
    }

    private bool DegradeActiveSlotDurabilityLocked(PlayerState state, in ItemSlot activeSlot, float loss, out ItemSlot updatedSlot) =>
        _actions.WearActiveTool(state, in activeSlot, loss, out updatedSlot);

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
                    _ = owner.ApplyDamageToPlayerLocked(
                        player,
                        projectile.OwnerNetworkId,
                        player.Entity,
                        player.Session.PlayerNetworkId,
                        playerPos,
                        projectile.Damage,
                        owner.ResolveMonsterDamageKindLocked(projectile.OwnerNetworkId, AttackKind.Projectile));
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary><see cref="IPlayerActionHost"/> for <see cref="PlayerActionService"/>. Invoked inside the locked simulation tick.</summary>
    private sealed class PlayerActionHost(AuthoritativeWorld owner) : IPlayerActionHost
    {
        public void MarkCharacterDirty(PlayerState state, bool inventoryChanged) => owner.MarkCharacterDirtyLocked(state, inventoryChanged);

        public void Wake(PlayerState state) => owner.WakeLocked(state);

        public bool TryBeginEating(PlayerState state, in ItemSlot activeSlot) => owner.TryBeginEatingActiveSlotLocked(state, in activeSlot);

        public void UsePlaceable(PlayerState state, Vector2 attackerPos, ItemSlot activeSlot, ItemArchetype archetype) =>
            owner.ProcessPlaceablePrimaryActionLocked(state, attackerPos, activeSlot, archetype);

        public void RecomputeDerivedStats(PlayerState state) => owner.RecomputeDerivedStatsLocked(state);
    }
}
