// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Unilateral server push synchronizing the local player's level, XP, HP, Mana, Stamina, Defense,
/// flags, binary progression blob, active skill cooldowns, and survival needs (hunger, mood, eating).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlayerStatsSync : PacketBase<PlayerStatsSync>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.PlayerStatsSync;

    /// <summary>Current character level.</summary>
    [SerializeOrder(0)]
    public ushort Level { get; set; } = 1;

    /// <summary>Accumulated XP within the current level.</summary>
    [SerializeOrder(1)]
    public uint Experience { get; set; }

    /// <summary>XP required to reach the next level.</summary>
    [SerializeOrder(2)]
    public uint XpToNextLevel { get; set; } = 100U;

    /// <summary>Current authoritative health points.</summary>
    [SerializeOrder(3)]
    public float CurrentHealth { get; set; }

    /// <summary>Derived maximum health points.</summary>
    [SerializeOrder(4)]
    public float MaxHealth { get; set; }

    /// <summary>Current authoritative mana points.</summary>
    [SerializeOrder(5)]
    public float CurrentMana { get; set; }

    /// <summary>Derived maximum mana points.</summary>
    [SerializeOrder(6)]
    public float MaxMana { get; set; }

    /// <summary>Current authoritative stamina points.</summary>
    [SerializeOrder(7)]
    public float CurrentStamina { get; set; }

    /// <summary>Derived maximum stamina points.</summary>
    [SerializeOrder(8)]
    public float MaxStamina { get; set; }

    /// <summary>Derived armor defense rating.</summary>
    [SerializeOrder(9)]
    public float Defense { get; set; }

    /// <summary>Indicates this sync accompanied a level-up celebration event.</summary>
    [SerializeOrder(10)]
    public bool LeveledUp { get; set; }

    /// <summary>Persistent character traits bitmask.</summary>
    [SerializeOrder(11)]
    public CharacterTraits Traits { get; set; }

    /// <summary>Bitmask of unlocked character skill IDs (<c>1..64</c>).</summary>
    [SerializeOrder(12)]
    public CharacterSkillMask UnlockedSkillMask { get; set; }

    /// <summary>Unspent character attribute points available to allocate.</summary>
    [SerializeOrder(13)]
    public ushort UnspentStatPoints { get; set; }

    /// <summary>Unspent skill mastery points available to unlock or rank up skills.</summary>
    [SerializeOrder(14)]
    public ushort UnspentSkillPoints { get; set; }

    /// <summary>Versioned binary progression payload encoding attribute points and skill ranks.</summary>
    [SerializeOrder(15)]
    public byte[] ProgressionBlob { get; set; } = [];

    /// <summary>Skill IDs currently on cooldown (parallel to <see cref="CooldownRemainingSeconds"/>).</summary>
    [SerializeOrder(16)]
    public ushort[] CooldownSkillIds { get; set; } = [];

    /// <summary>Remaining cooldown duration in seconds matching <see cref="CooldownSkillIds"/>.</summary>
    [SerializeOrder(17)]
    public float[] CooldownRemainingSeconds { get; set; } = [];

    /// <summary>Current food level (hunger meter), <c>0..MaxFood</c>.</summary>
    [SerializeOrder(18)]
    public float Food { get; set; }

    /// <summary>Food capacity.</summary>
    [SerializeOrder(19)]
    public float MaxFood { get; set; }

    /// <summary>Hunger state: <c>0</c>=Fed, <c>1</c>=Hungry, <c>2</c>=UrgentlyHungry, <c>3</c>=Starving.</summary>
    [SerializeOrder(20)]
    public byte HungerState { get; set; }

    /// <summary>Malnutrition severity, <c>0..1</c> (1 is lethal).</summary>
    [SerializeOrder(21)]
    public float Malnutrition { get; set; }

    /// <summary>Mood value, <c>0..100</c>.</summary>
    [SerializeOrder(22)]
    public float Mood { get; set; }

    /// <summary>Whether the player currently has food poisoning.</summary>
    [SerializeOrder(23)]
    public bool FoodPoisoned { get; set; }

    /// <summary>Progress of the meal being eaten, <c>0..1</c>, or a negative value when not eating.</summary>
    [SerializeOrder(24)]
    public float EatProgress { get; set; } = -1f;

    /// <summary>Current rest level, <c>0..MaxRest</c> (ADR-017).</summary>
    [SerializeOrder(25)]
    public float Rest { get; set; }

    /// <summary>Rest capacity.</summary>
    [SerializeOrder(26)]
    public float MaxRest { get; set; }

    /// <summary>Rest state: <c>0</c>=Rested, <c>1</c>=Tired, <c>2</c>=VeryTired, <c>3</c>=Exhausted.</summary>
    [SerializeOrder(27)]
    public byte RestState { get; set; }

    /// <summary>How the player is resting right now.</summary>
    [SerializeOrder(28)]
    public RestMode RestMode { get; set; }
}
