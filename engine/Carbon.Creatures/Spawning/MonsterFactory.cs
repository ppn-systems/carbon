// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.Combat.Attacks;
using Carbon.Combat.Stats;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Events;
using Carbon.Creatures.Scaling;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Spatial;
using Carbon.Simulation.World;

namespace Carbon.Creatures.Spawning;

/// <summary>
/// Assembles a fully configured monster entity (collider, health, stats, AI brain, attack state) from a definition.
/// </summary>
public static class MonsterFactory
{
    /// <summary>
    /// Spawns monster <paramref name="monsterIndex"/> at <paramref name="position"/> with <paramref name="stats"/>.
    /// </summary>
    /// <param name="world">Owning world simulation.</param>
    /// <param name="catalog">Monster catalog.</param>
    /// <param name="monsterIndex">Catalog index.</param>
    /// <param name="position">Spawn / home position.</param>
    /// <param name="stats">Scaled stats from <see cref="MonsterScaling.Compute"/>.</param>
    /// <param name="monsterLayer">Collider layer of the monster.</param>
    /// <param name="targetLayerMask">Collider layer mask of hostile targets.</param>
    /// <param name="spawnCell">Owning spawner cell.</param>
    /// <returns>The spawned entity.</returns>
    public static Entity Spawn(
        WorldSimulation world,
        MonsterCatalog catalog,
        ushort monsterIndex,
        Vector2 position,
        in ScaledMonsterStats stats,
        uint monsterLayer,
        uint targetLayerMask,
        CellCoord spawnCell = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);

        MonsterDefinition def = catalog.GetDefinition(monsterIndex);
        EntityManager entities = world.Entities;

        Entity monster = world.SpawnEntity(position, def.Radius);
        Collider2D collider = Collider2D.CreateCircle(def.Radius) with { Layer = monsterLayer };
        if (def.Habitat is { } habitat)
        {
            collider = collider with { Mask = habitat.ToTerrainMask(collider.Mask) }; // ADR-032 D14: never leaves its water
        }

        entities.SetCollider(monster, collider);
        entities.SetVelocity(monster, new Velocity2D(Vector2.Zero));
        entities.SetHealth(monster, new HealthComponent(stats.MaxHealth, stats.MaxHealth));

        // ADR-044: reuses a sheet released by a despawned monster before creating one.
        StatSheet sheet = entities.GetAttachmentStore<StatSheet>().GetOrAdd(monster, static () => new StatSheet());
        sheet.SetBase(StatType.MaxHealth, stats.MaxHealth);
        sheet.SetBase(StatType.MoveSpeed, stats.MoveSpeed);
        sheet.SetBase(StatType.AttackPower, stats.Damage);
        sheet.SetBase(StatType.Defense, stats.Armor);

        entities.SetComponent(monster, new AiBrainComponent(
            def.PatrolRadius > 0f ? AiState.Patrol : AiState.Idle,
            position,
            def.AggroRange,
            def.EngageRange,
            def.LeashRange,
            stats.MoveSpeed,
            TargetLayerMask: targetLayerMask,
            PatrolRadius: def.PatrolRadius,
            ExternalAttackControl: true));
        MonsterBehaviorDefinition behavior = def.Behavior;
        entities.SetComponent(monster, new AiTacticsComponent(
            PackId: catalog.GetPackId(monsterIndex),
            AssistRadius: behavior.AssistRadius,
            FleeHealthFraction: behavior.FleeHealthFraction,
            FleeTicks: behavior.FleeTicks,
            GiveUpTicks: behavior.GiveUpTicks,
            ThreatSwitchMargin: behavior.ThreatSwitchMargin,
            SurroundRadius: behavior.SurroundRadius,
            NightAggroMultiplier: behavior.NightAggroMultiplier,
            NightLeashMultiplier: behavior.NightLeashMultiplier));
        entities.SetComponent(monster, new MonsterComponent(monsterIndex, stats.Level, def.IsElite, stats.Damage, spawnCell));
        entities.SetComponent(monster, default(AttackStateComponent));

        world.PublishEvent(new MonsterSpawnedEvent(monster, monsterIndex, stats.Level, def.IsElite, stats.IsNight, position));
        return monster;
    }
}
