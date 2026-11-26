// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Components;

/// <summary>
/// Unmanaged tag marking an entity (typically a player) around which <see cref="Systems.MonsterSpawnerSystem"/>
/// keeps nearby density cells populated.
/// </summary>
public readonly record struct SpawnAnchorComponent;
