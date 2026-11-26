// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Items.Catalog;

/// <summary>
/// Immutable baseline specification for a wilderness monster archetype.
/// </summary>
/// <param name="ArchetypeId">Visual &amp; protocol archetype ID.</param>
/// <param name="Name">Display name.</param>
/// <param name="BaseLevel">Baseline monster level in Tier 0 starter zones.</param>
/// <param name="BaseMaxHealth">Baseline maximum HP.</param>
/// <param name="MoveSpeed">Movement speed in world units/sec.</param>
/// <param name="Radius">Collider radius in world units.</param>
/// <param name="BaseAttackDamage">Baseline raw melee strike damage.</param>
/// <param name="WindupTicks">AI windup delay ticks before strike lands.</param>
/// <param name="DropItemId">Primary ground loot drop item ID.</param>
/// <param name="DropMinQuantity">Minimum stack quantity dropped on kill.</param>
/// <param name="DropMaxQuantity">Maximum stack quantity dropped on kill.</param>
/// <param name="AttackRange">Engagement distance before entering <c>Attack</c> state.</param>
/// <param name="ProjectileSpeed">Projectile velocity in world units/sec (<c>0</c> = melee).</param>
/// <param name="ProjectileArchetype">Visual projectile archetype ID for ranged monsters.</param>
public readonly record struct MonsterArchetypeSpec(
    ushort ArchetypeId,
    string Name,
    byte BaseLevel,
    float BaseMaxHealth,
    float MoveSpeed,
    float Radius,
    float BaseAttackDamage,
    uint WindupTicks,
    uint DropItemId,
    int DropMinQuantity,
    int DropMaxQuantity,
    float AttackRange = 44f,
    float ProjectileSpeed = 0f,
    byte ProjectileArchetype = 0);

/// <summary>
/// Zone-scaled monster combat parameters computed cleanly without hardcoded server switches.
/// </summary>
public readonly record struct ScaledMonsterStats(
    ushort ArchetypeId,
    string Name,
    ushort Level,
    float MaxHealth,
    float MoveSpeed,
    float Radius,
    float AttackDamage,
    uint WindupTicks,
    uint DropItemId,
    int DropQuantity,
    uint ExperienceReward,
    float AttackRange = 44f,
    float ProjectileSpeed = 0f,
    byte ProjectileArchetype = 0);

/// <summary>
/// Immutable specification for a friendly town NPC archetype.
/// </summary>
public readonly record struct NpcArchetypeSpec(
    ushort ArchetypeId,
    string Name,
    byte Level,
    float Radius);

/// <summary>
/// Centralized Native-AOT catalog defining monster archetypes, zone scaling, and friendly NPC metadata.
/// </summary>
public static class WorldActorCatalog
{
    /// <summary>Wild Slime archetype identifier.</summary>
    public const ushort WildSlime = 101;

    /// <summary>Rock Golem archetype identifier.</summary>
    public const ushort RockGolem = 102;

    /// <summary>Frost Wolf archetype identifier.</summary>
    public const ushort FrostWolf = 103;

    /// <summary>Ash Wraith archetype identifier.</summary>
    public const ushort AshWraith = 104;

    /// <summary>Skeleton Archer ranged monster archetype identifier.</summary>
    public const ushort SkeletonArcher = 105;

    /// <summary>Orc Shaman tribal hex caster monster archetype identifier.</summary>
    public const ushort OrcShaman = 106;

    /// <summary>Crimson Demon heavy abyssal cleaver monster archetype identifier.</summary>
    public const ushort CrimsonDemon = 107;

    /// <summary>Village Guard NPC archetype identifier.</summary>
    public const ushort VillageGuard = 201;

    /// <summary>Harbour Master NPC archetype identifier.</summary>
    public const ushort HarbourMaster = 202;

    /// <summary>Shrine Keeper NPC archetype identifier.</summary>
    public const ushort ShrineKeeper = 203;

    /// <summary>Master Blacksmith NPC archetype identifier.</summary>
    public const ushort MasterBlacksmith = 204;

    private static readonly MonsterArchetypeSpec[] s_monsters =
    [
        new(WildSlime, "Wild Slime", BaseLevel: 1, BaseMaxHealth: 75f, MoveSpeed: 130f, Radius: 8f, BaseAttackDamage: 9f, WindupTicks: 7U, StarterGameCatalog.BerryHerb, 1, 2),
        new(RockGolem, "Rock Golem", BaseLevel: 3, BaseMaxHealth: 150f, MoveSpeed: 105f, Radius: 11f, BaseAttackDamage: 16f, WindupTicks: 11U, StarterGameCatalog.IronOre, 2, 3),
        new(FrostWolf, "Frost Wolf", BaseLevel: 2, BaseMaxHealth: 95f, MoveSpeed: 168f, Radius: 8.5f, BaseAttackDamage: 12f, WindupTicks: 6U, StarterGameCatalog.StoneChunk, 2, 3),
        new(AshWraith, "Ash Wraith", BaseLevel: 4, BaseMaxHealth: 110f, MoveSpeed: 148f, Radius: 8.5f, BaseAttackDamage: 14f, WindupTicks: 10U, StarterGameCatalog.ManaCrystal, 1, 2, AttackRange: 215f, ProjectileSpeed: 340f, ProjectileArchetype: 4),
        new(SkeletonArcher, "Skeleton Archer", BaseLevel: 2, BaseMaxHealth: 85f, MoveSpeed: 142f, Radius: 8f, BaseAttackDamage: 13f, WindupTicks: 9U, StarterGameCatalog.WoodLog, 2, 4, AttackRange: 235f, ProjectileSpeed: 390f, ProjectileArchetype: 5),
        new(OrcShaman, "Orc Shaman", BaseLevel: 5, BaseMaxHealth: 165f, MoveSpeed: 140f, Radius: 9.0f, BaseAttackDamage: 22f, WindupTicks: 10U, StarterGameCatalog.ManaCrystal, 1, 2, AttackRange: 195f, ProjectileSpeed: 360f, ProjectileArchetype: 2),
        new(CrimsonDemon, "Crimson Demon", BaseLevel: 8, BaseMaxHealth: 310f, MoveSpeed: 158f, Radius: 10.5f, BaseAttackDamage: 34f, WindupTicks: 8U, StarterGameCatalog.RoastMeat, 1, 2, AttackRange: 44f),
    ];

    private static readonly NpcArchetypeSpec[] s_npcs =
    [
        new(VillageGuard, "Village Guard", Level: 10, Radius: 8f),
        new(HarbourMaster, "Harbour Master", Level: 8, Radius: 8f),
        new(ShrineKeeper, "Shrine Keeper", Level: 12, Radius: 8f),
        new(MasterBlacksmith, "Master Blacksmith", Level: 15, Radius: 8.5f),
    ];

    /// <summary>Resolves static baseline specs for <paramref name="archetypeId"/> (defaults to <see cref="WildSlime"/>).</summary>
    public static MonsterArchetypeSpec GetMonsterSpec(ushort archetypeId)
    {
        for (int i = 0; i < s_monsters.Length; i++)
        {
            if (s_monsters[i].ArchetypeId == archetypeId)
            {
                return s_monsters[i];
            }
        }

        return s_monsters[0];
    }

    /// <summary>Resolves friendly NPC specs for <paramref name="archetypeId"/>.</summary>
    public static NpcArchetypeSpec GetNpcSpec(ushort archetypeId)
    {
        for (int i = 0; i < s_npcs.Length; i++)
        {
            if (s_npcs[i].ArchetypeId == archetypeId)
            {
                return s_npcs[i];
            }
        }

        return s_npcs[0];
    }

    /// <summary>Computes level, HP, attack, loot quantity, and XP reward scaled by <paramref name="zoneTier"/>.</summary>
    public static ScaledMonsterStats ScaleMonsterForZone(ushort archetypeId, byte zoneTier)
    {
        MonsterArchetypeSpec baseSpec = GetMonsterSpec(archetypeId);
        int tier = Math.Clamp((int)zoneTier, 0, 20);
        ushort level = (ushort)Math.Clamp(baseSpec.BaseLevel + (tier * 2), 1, CharacterProgressionPolicy.MaxLevel);
        float hpMult = 1f + (tier * 0.25f);
        float dmgMult = 1f + (tier * 0.16f);
        float maxHp = MathF.Round(baseSpec.BaseMaxHealth * hpMult);
        float atk = MathF.Round(baseSpec.BaseAttackDamage * dmgMult);
        int qty = Math.Min(baseSpec.DropMaxQuantity, baseSpec.DropMinQuantity + (tier >= 2 ? 1 : 0));
        uint xp = (uint)Math.Clamp((int)(maxHp * 0.45f) + (level * 4), 25, 320);

        return new ScaledMonsterStats(
            baseSpec.ArchetypeId,
            baseSpec.Name,
            level,
            maxHp,
            baseSpec.MoveSpeed,
            baseSpec.Radius,
            atk,
            baseSpec.WindupTicks,
            baseSpec.DropItemId,
            qty,
            xp,
            baseSpec.AttackRange,
            baseSpec.ProjectileSpeed,
            baseSpec.ProjectileArchetype);
    }
}
