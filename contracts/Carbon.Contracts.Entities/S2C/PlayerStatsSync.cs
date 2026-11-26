// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Unilateral server push synchronizing the local player's level, XP, HP, Mana, Stamina, Defense, and skill cooldowns.
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

    /// <summary>Remaining cooldown in seconds for Skill 1 (Whirlwind).</summary>
    [SerializeOrder(10)]
    public float Skill1Cooldown { get; set; }

    /// <summary>Remaining cooldown in seconds for Skill 2 (Arcane Nova).</summary>
    [SerializeOrder(11)]
    public float Skill2Cooldown { get; set; }

    /// <summary>Remaining cooldown in seconds for Skill 3 (Vital Surge).</summary>
    [SerializeOrder(12)]
    public float Skill3Cooldown { get; set; }

    /// <summary>Indicates this sync accompanied a level-up celebration event.</summary>
    [SerializeOrder(13)]
    public bool LeveledUp { get; set; }
}
