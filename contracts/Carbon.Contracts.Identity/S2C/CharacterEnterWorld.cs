// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Identity.S2C;

/// <summary>
/// Server reply to <see cref="C2S.CharacterSelect"/> carrying world bootstrap metadata and initial entity state.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CharacterEnterWorld : PacketBase<CharacterEnterWorld>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => IdentityOpCode.CharacterEnterWorld;

    /// <summary>Operation outcome (<see cref="OpStatus.Ok"/> when spawned into world).</summary>
    [SerializeOrder(0)]
    public OpStatus Status { get; set; }

    /// <summary>Authoritative network entity identifier bound to the player avatar.</summary>
    [SerializeOrder(1)]
    public ulong PlayerNetworkId { get; set; }

    /// <summary>Deterministic world generation seed.</summary>
    [SerializeOrder(2)]
    public ulong WorldSeed { get; set; }

    /// <summary>Generator version expected by the server.</summary>
    [SerializeOrder(3)]
    public ushort GeneratorVersion { get; set; }

    /// <summary>Physical tile dimension in world units.</summary>
    [SerializeOrder(4)]
    public float TileSize { get; set; }

    /// <summary>Macro terrain noise frequency.</summary>
    [SerializeOrder(5)]
    public float Frequency { get; set; }

    /// <summary>Initial authoritative spawn world X position.</summary>
    [SerializeOrder(6)]
    public float PositionX { get; set; }

    /// <summary>Initial authoritative spawn world Y position.</summary>
    [SerializeOrder(7)]
    public float PositionY { get; set; }

    /// <summary>Current authoritative simulation tick.</summary>
    [SerializeOrder(8)]
    public ulong ServerTick { get; set; }

    /// <summary>Fixed simulation tick rate in Hertz.</summary>
    [SerializeOrder(9)]
    public ushort TickRate { get; set; }

    /// <summary>Visual appearance preset index (0..255) of the entered character.</summary>
    [SerializeOrder(10)]
    public byte AppearancePreset { get; set; }

    /// <summary>Persisted active hotbar slot (<c>0..8</c>).</summary>
    [SerializeOrder(11)]
    public byte SelectedHotbarSlot { get; set; }

    /// <summary>Character progression level (<c>1..99</c>).</summary>
    [SerializeOrder(12)]
    public ushort Level { get; set; } = 1;

    /// <summary>Accumulated XP within the current level.</summary>
    [SerializeOrder(13)]
    public uint Experience { get; set; }

    /// <summary>XP required to reach the next level.</summary>
    [SerializeOrder(14)]
    public uint XpToNextLevel { get; set; } = 100U;

    /// <summary>Current authoritative health points.</summary>
    [SerializeOrder(15)]
    public float CurrentHealth { get; set; } = 100f;

    /// <summary>Derived maximum health points.</summary>
    [SerializeOrder(16)]
    public float MaxHealth { get; set; } = 100f;

    /// <summary>Current authoritative mana points.</summary>
    [SerializeOrder(17)]
    public float CurrentMana { get; set; } = 80f;

    /// <summary>Derived maximum mana points.</summary>
    [SerializeOrder(18)]
    public float MaxMana { get; set; } = 80f;

    /// <summary>Current authoritative stamina points.</summary>
    [SerializeOrder(19)]
    public float CurrentStamina { get; set; } = 100f;

    /// <summary>Derived maximum stamina points.</summary>
    [SerializeOrder(20)]
    public float MaxStamina { get; set; } = 100f;

    /// <summary>Derived armor defense rating.</summary>
    [SerializeOrder(21)]
    public float Defense { get; set; } = 2f;

    /// <summary>Character starting archetype class (<see cref="HeroArchetypeClass"/>).</summary>
    [SerializeOrder(22)]
    public byte ClassId { get; set; }

    /// <summary>Persistent character lifecycle/unlock traits.</summary>
    [SerializeOrder(23)]
    public CharacterTraits Traits { get; set; }

    /// <summary>Bitmask of unlocked character skill IDs (<c>1..64</c>).</summary>
    [SerializeOrder(24)]
    public ulong UnlockedSkillMask { get; set; }

    /// <summary>Versioned binary character progression payload (attributes + skill ranks).</summary>
    [SerializeOrder(25)]
    public byte[] ProgressionBlob { get; set; } = [];
}
