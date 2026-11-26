// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;

namespace Carbon.Creatures.Events;

/// <summary>
/// Published when a monster is spawned. Hosts resolve the client visual archetype key and loot table from
/// <see cref="MonsterIndex"/> via the catalog; no sprite data is ever produced by the simulation.
/// </summary>
/// <param name="Monster">Spawned entity.</param>
/// <param name="MonsterIndex">Catalog index.</param>
/// <param name="Level">Scaled level.</param>
/// <param name="IsElite">Whether the elite variant was spawned.</param>
/// <param name="IsNight">Whether night empowerment applied.</param>
/// <param name="Position">Spawn position.</param>
public readonly record struct MonsterSpawnedEvent(
    Entity Monster,
    ushort MonsterIndex,
    int Level,
    bool IsElite,
    bool IsNight,
    Vector2 Position);
