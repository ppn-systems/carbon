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
/// <param name="SecondaryDropItemId">Optional secondary drop item ID.</param>
/// <param name="SecondaryDropQuantity">Quantity of secondary drop when rolled.</param>
/// <param name="SecondaryDropChance">Probability [0..1] of secondary drop.</param>
/// <param name="RareDropItemId">Optional rare equipment/trophy drop item ID.</param>
/// <param name="RareDropChance">Probability [0..1] of rare drop.</param>
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
    byte ProjectileArchetype = 0,
    uint SecondaryDropItemId = 0,
    int SecondaryDropQuantity = 0,
    float SecondaryDropChance = 0f,
    uint RareDropItemId = 0,
    float RareDropChance = 0f);

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

/// <summary>Capability flags governing NPC services such as forging and healing shrines.</summary>
[Flags]
public enum NpcCapabilities : byte
{
    /// <summary>Standard dialogue or patrol NPC.</summary>
    None = 0,

    /// <summary>Town guard providing equipment mending.</summary>
    TownGuard = 1 << 0,

    /// <summary>Master artisan unlocking nearby Gear Enhancement Forging.</summary>
    ForgeArtisan = 1 << 1,

    /// <summary>Harbour logistics NPC.</summary>
    Harbour = 1 << 2,

    /// <summary>Sanctuary healing shrine keeper.</summary>
    Shrine = 1 << 3,

    /// <summary>Trades from a stock that the server restocks (ADR-025).</summary>
    Trader = 1 << 4,
}

/// <summary>
/// Immutable specification for a friendly town NPC archetype.
/// </summary>
public readonly record struct NpcArchetypeSpec(
    ushort ArchetypeId,
    string Name,
    byte Level,
    float Radius,
    NpcCapabilities Capabilities = NpcCapabilities.None);

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

    /// <summary>Orc Warlord elite (of <see cref="OrcShaman"/>) monster archetype identifier.</summary>
    public const ushort OrcWarlord = 108;

    /// <summary>Slime King elite (of <see cref="WildSlime"/>) monster archetype identifier.</summary>
    public const ushort SlimeKing = 109;

    /// <summary>Village Guard NPC archetype identifier.</summary>
    public const ushort VillageGuard = 201;

    /// <summary>Harbour Master NPC archetype identifier.</summary>
    public const ushort HarbourMaster = 202;

    /// <summary>Shrine Keeper NPC archetype identifier.</summary>
    public const ushort ShrineKeeper = 203;

    /// <summary>Master Blacksmith NPC archetype identifier.</summary>
    public const ushort MasterBlacksmith = 204;

    /// <summary>Village market-stall merchant NPC archetype identifier (ADR-025).</summary>
    public const ushort VillageMerchant = 205;

    /// <summary>Village general goods trader NPC archetype identifier (ADR-025).</summary>
    public const ushort GeneralTrader = 206;

    private static readonly MonsterArchetypeSpec[] s_monsters =
    [
        new(WildSlime, "Wild Slime", BaseLevel: 1, BaseMaxHealth: 75f, MoveSpeed: 130f, Radius: 8f, BaseAttackDamage: 9f, WindupTicks: 7U, StarterGameCatalog.BerryHerb, 1, 2, SecondaryDropItemId: StarterGameCatalog.ManaCrystal, SecondaryDropQuantity: 1, SecondaryDropChance: 0.22f),
        new(RockGolem, "Rock Golem", BaseLevel: 3, BaseMaxHealth: 150f, MoveSpeed: 105f, Radius: 11f, BaseAttackDamage: 16f, WindupTicks: 11U, StarterGameCatalog.IronOre, 2, 3, SecondaryDropItemId: StarterGameCatalog.StoneChunk, SecondaryDropQuantity: 2, SecondaryDropChance: 0.65f, RareDropItemId: StarterGameCatalog.StonePickaxe, RareDropChance: 0.10f),
        new(FrostWolf, "Frost Wolf", BaseLevel: 2, BaseMaxHealth: 95f, MoveSpeed: 168f, Radius: 8.5f, BaseAttackDamage: 12f, WindupTicks: 6U, StarterGameCatalog.BeastPelt, 1, 2, SecondaryDropItemId: StarterGameCatalog.RoastMeat, SecondaryDropQuantity: 1, SecondaryDropChance: 0.45f),
        new(AshWraith, "Ash Wraith", BaseLevel: 4, BaseMaxHealth: 110f, MoveSpeed: 148f, Radius: 8.5f, BaseAttackDamage: 14f, WindupTicks: 10U, StarterGameCatalog.ManaCrystal, 1, 2, AttackRange: 215f, ProjectileSpeed: 340f, ProjectileArchetype: 4, SecondaryDropItemId: StarterGameCatalog.ManaTonic, SecondaryDropQuantity: 1, SecondaryDropChance: 0.45f, RareDropItemId: StarterGameCatalog.ArcaneStaff, RareDropChance: 0.08f),
        new(SkeletonArcher, "Skeleton Archer", BaseLevel: 2, BaseMaxHealth: 85f, MoveSpeed: 142f, Radius: 8f, BaseAttackDamage: 13f, WindupTicks: 9U, StarterGameCatalog.BoneShard, 1, 3, AttackRange: 235f, ProjectileSpeed: 390f, ProjectileArchetype: 5, SecondaryDropItemId: StarterGameCatalog.WoodLog, SecondaryDropQuantity: 2, SecondaryDropChance: 0.35f, RareDropItemId: StarterGameCatalog.HuntingBow, RareDropChance: 0.08f),
        new(OrcShaman, "Orc Shaman", BaseLevel: 5, BaseMaxHealth: 165f, MoveSpeed: 140f, Radius: 9.0f, BaseAttackDamage: 22f, WindupTicks: 10U, StarterGameCatalog.ManaCrystal, 1, 3, AttackRange: 195f, ProjectileSpeed: 360f, ProjectileArchetype: 6, SecondaryDropItemId: StarterGameCatalog.BoneShard, SecondaryDropQuantity: 2, SecondaryDropChance: 0.55f, RareDropItemId: StarterGameCatalog.ShamanStaff, RareDropChance: 0.12f),
        new(CrimsonDemon, "Crimson Demon", BaseLevel: 8, BaseMaxHealth: 310f, MoveSpeed: 158f, Radius: 10.5f, BaseAttackDamage: 34f, WindupTicks: 8U, StarterGameCatalog.DemonEmber, 1, 2, AttackRange: 44f, SecondaryDropItemId: StarterGameCatalog.ManaCrystal, SecondaryDropQuantity: 2, SecondaryDropChance: 0.65f, RareDropItemId: StarterGameCatalog.DemonCleaver, RareDropChance: 0.15f),
    ];

    private static readonly NpcArchetypeSpec[] s_npcs =
    [
        new(VillageGuard, "Village Guard", Level: 10, Radius: 8f, NpcCapabilities.TownGuard),
        new(HarbourMaster, "Harbour Master", Level: 8, Radius: 8f, NpcCapabilities.Harbour),
        new(ShrineKeeper, "Shrine Keeper", Level: 12, Radius: 8f, NpcCapabilities.Shrine),
        new(MasterBlacksmith, "Master Blacksmith", Level: 15, Radius: 8.5f, NpcCapabilities.ForgeArtisan | NpcCapabilities.Trader),
        new(VillageMerchant, "Village Merchant", Level: 8, Radius: 8f, NpcCapabilities.Trader),
        new(GeneralTrader, "General Trader", Level: 8, Radius: 8f, NpcCapabilities.Trader),
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
    public static ScaledMonsterStats ScaleMonsterForZone(ushort archetypeId, byte zoneTier) =>
        ScaleMonster(archetypeId, zoneTier, distanceTilesFromSpawn: 0f, isNight: false);

    /// <summary>
    /// Computes level, HP, speed, attack, loot quantity, and XP reward scaled by
    /// <paramref name="zoneTier"/>, distance in tiles from initial player spawn, and night empowerment.
    /// </summary>
    public static ScaledMonsterStats ScaleMonster(
        ushort archetypeId,
        byte zoneTier,
        float distanceTilesFromSpawn,
        bool isNight)
    {
        MonsterArchetypeSpec baseSpec = GetMonsterSpec(archetypeId);
        float distanceTier = Math.Clamp(distanceTilesFromSpawn / 110f, 0f, 12f);
        float effectiveTier = MathF.Max(Math.Clamp((int)zoneTier, 0, 20), distanceTier);
        int tierInt = (int)MathF.Floor(effectiveTier);

        ushort level = (ushort)Math.Clamp(baseSpec.BaseLevel + (tierInt * 2), 1, CharacterProgressionPolicy.MaxLevel);
        float hpMult = 1f + (effectiveTier * 0.25f);
        float dmgMult = 1f + (effectiveTier * 0.17f);
        float speed = baseSpec.MoveSpeed;

        if (isNight)
        {
            hpMult *= 1.25f;
            dmgMult *= 1.28f;
            speed *= 1.12f;
        }

        float maxHp = MathF.Round(baseSpec.BaseMaxHealth * hpMult);
        float atk = MathF.Round(baseSpec.BaseAttackDamage * dmgMult);
        int qty = Math.Min(baseSpec.DropMaxQuantity + (isNight ? 1 : 0), baseSpec.DropMinQuantity + (tierInt >= 2 ? 1 : 0) + (isNight ? 1 : 0));
        uint xp = (uint)Math.Clamp((int)(maxHp * 0.45f) + (level * 4), 25, 650);
        if (isNight)
        {
            xp = (uint)MathF.Round(xp * 1.35f);
        }

        return new ScaledMonsterStats(
            baseSpec.ArchetypeId,
            baseSpec.Name,
            level,
            maxHp,
            speed,
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
