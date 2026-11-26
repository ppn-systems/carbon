// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Abstractions.World;

/// <summary>
/// Result of attempting to enter the authoritative world simulation.
/// </summary>
/// <param name="Status">Outcome status.</param>
/// <param name="PlayerNetworkId">Assigned player entity network ID.</param>
/// <param name="WorldSeed">Deterministic world seed.</param>
/// <param name="GeneratorVersion">World generator version.</param>
/// <param name="TileSize">Tile size in world units.</param>
/// <param name="Frequency">Macro terrain frequency.</param>
/// <param name="PositionX">Spawned world X coordinate.</param>
/// <param name="PositionY">Spawned world Y coordinate.</param>
/// <param name="ServerTick">Current server tick.</param>
/// <param name="TickRate">Configured simulation tick rate.</param>
/// <param name="AppearancePreset">Saved character appearance preset identifier.</param>
/// <param name="SelectedHotbarSlot">Persisted active hotbar slot index.</param>
/// <param name="Level">Current character progression level.</param>
/// <param name="Experience">Accumulated XP within the current level.</param>
/// <param name="XpToNextLevel">XP required for the next level.</param>
/// <param name="CurrentHealth">Authoritative health points.</param>
/// <param name="MaxHealth">Derived maximum health points.</param>
/// <param name="CurrentMana">Authoritative mana points.</param>
/// <param name="MaxMana">Derived maximum mana points.</param>
/// <param name="CurrentStamina">Authoritative stamina points.</param>
/// <param name="MaxStamina">Derived maximum stamina points.</param>
/// <param name="Defense">Derived armor defense rating.</param>
public readonly record struct EnterWorldResult(
    OpStatus Status,
    ulong PlayerNetworkId,
    ulong WorldSeed,
    ushort GeneratorVersion,
    float TileSize,
    float Frequency,
    float PositionX,
    float PositionY,
    ulong ServerTick,
    ushort TickRate,
    byte AppearancePreset = 0,
    byte SelectedHotbarSlot = 0,
    ushort Level = 1,
    uint Experience = 0U,
    uint XpToNextLevel = 100U,
    float CurrentHealth = 100f,
    float MaxHealth = 100f,
    float CurrentMana = 80f,
    float MaxMana = 80f,
    float CurrentStamina = 100f,
    float MaxStamina = 100f,
    float Defense = 2f);

/// <summary>
/// Outcome of an authoritative world mutation returned to handlers without allocating packet wrappers.
/// </summary>
/// <param name="Status">Operation status.</param>
/// <param name="Revision">Updated chunk revision, or zero.</param>
public readonly record struct WorldMutationOutcome(OpStatus Status, uint Revision);

/// <summary>
/// Composite authoritative game world gateway uniting the role-segregated session, input, inventory,
/// map, and simulation-loop ports (ISP / DIP).
/// </summary>
public interface IAuthoritativeWorld :
    IWorldSessionGateway,
    IWorldEntityCommandSink,
    IWorldInventoryService,
    IWorldMapService,
    IWorldSimulationLoop;
