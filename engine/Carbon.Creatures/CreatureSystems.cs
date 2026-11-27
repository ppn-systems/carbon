// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Systems;
using Carbon.Combat.Attacks;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using Carbon.Creatures.Spawning;
using Carbon.Creatures.Systems;
using Carbon.Primitives.Random;
using Carbon.Simulation.World;

namespace Carbon.Creatures;

/// <summary>
/// Wires the creature pipeline into a <see cref="WorldSimulation"/> in the required order:
/// <c>AiBehaviorSystem -> MonsterBrainSystem -> AttackSystem</c> (PreUpdate) and <c>MonsterSpawnerSystem</c> (PostUpdate).
/// Projectile hit resolution needs <see cref="Combat.Projectiles.ProjectileSystem"/> registered once on the world.
/// </summary>
public sealed class CreatureSystems
{
    private CreatureSystems(AiBehaviorSystem ai, MonsterBrainSystem brain, AttackSystem attacks, MonsterSpawnerSystem spawner, BossLairSystem? bossLairs)
    {
        this.Ai = ai;
        this.Brain = brain;
        this.Attacks = attacks;
        this.Spawner = spawner;
        this.BossLairs = bossLairs;
    }

    /// <summary>Gets the AI behavior system.</summary>
    public AiBehaviorSystem Ai { get; }

    /// <summary>Gets the monster combat decision system.</summary>
    public MonsterBrainSystem Brain { get; }

    /// <summary>Gets the attack timeline system.</summary>
    public AttackSystem Attacks { get; }

    /// <summary>Gets the spawner (set <see cref="MonsterSpawnerSystem.TimeOfDayHours"/> each tick).</summary>
    public MonsterSpawnerSystem Spawner { get; }

    /// <summary>Gets the abyss boss lairs (ADR-032 D15), or <see langword="null"/> without a lair source or <c>bossLairs</c>.</summary>
    public BossLairSystem? BossLairs { get; }

    /// <summary>
    /// Creates and registers the creature systems on <paramref name="world"/>.
    /// </summary>
    /// <param name="world">Target world simulation.</param>
    /// <param name="catalog">Monster catalog.</param>
    /// <param name="config">Spawning configuration.</param>
    /// <param name="surface">Spawn surface (walkability + biome).</param>
    /// <param name="worldOrigin">World origin / first player spawn point.</param>
    /// <param name="entropy">Secret roll key source for spawn rolls (ADR-026; never the world seed).</param>
    /// <param name="ai">Optional pre-built AI system (e.g. with a walkability grid); must not already be registered.</param>
    /// <param name="effects">Optional host attack side effects (defaults to <see cref="DefaultAttackEffects"/>).</param>
    /// <param name="lairs">Optional boss lair source; with <c>spawning.json → bossLairs</c> it registers <see cref="BossLairSystem"/>.</param>
    /// <returns>The registered systems.</returns>
    public static CreatureSystems Register(
        WorldSimulation world,
        MonsterCatalog catalog,
        SpawningConfig config,
        ISpawnSurface surface,
        Vector2 worldOrigin,
        IGameplayEntropy entropy,
        AiBehaviorSystem? ai = null,
        IAttackEffects? effects = null,
        ILairSource? lairs = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(catalog);

        AiBehaviorSystem aiSystem = ai ?? new AiBehaviorSystem();
        AttackSystem attacks = new(catalog.AttackProfiles, effects);
        MonsterBrainSystem brain = new(catalog, attacks);
        MonsterSpawnerSystem spawner = new(catalog, config, surface, worldOrigin, entropy);

        world.RegisterSystem(aiSystem);
        world.RegisterSystem(brain);
        world.RegisterSystem(attacks);
        world.RegisterSystem(spawner);
        BossLairSystem? bossLairs = lairs is not null && config.BossLairs is not null
            ? new BossLairSystem(catalog, config, spawner.Scaling, lairs, worldOrigin)
            : null;
        if (bossLairs is not null)
        {
            world.RegisterSystem(bossLairs);
        }

        return new CreatureSystems(aiSystem, brain, attacks, spawner, bossLairs);
    }
}
