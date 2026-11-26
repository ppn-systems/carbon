// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.Spatial;

namespace Carbon.Creatures.Components;

/// <summary>
/// Unmanaged tag + runtime data of a data-driven monster instance.
/// </summary>
/// <param name="MonsterIndex">Index into <see cref="Definitions.MonsterCatalog"/>.</param>
/// <param name="Level">Scaled level.</param>
/// <param name="IsElite">Whether this is an elite variant.</param>
/// <param name="Damage">Scaled base damage (multiplied per attack profile).</param>
/// <param name="SpawnCell">Spawner density cell that owns this monster.</param>
public readonly record struct MonsterComponent(
    ushort MonsterIndex,
    int Level,
    bool IsElite,
    float Damage,
    CellCoord SpawnCell);
