// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Building;

namespace Carbon.Maps.Settlements;

/// <summary>The structure types a style builds its walls, doors, floors and porches from.</summary>
/// <param name="Wall">Wall type (grid cell <c>W</c>).</param>
/// <param name="Door">Door type (grid cell <c>D</c>).</param>
/// <param name="Floor">Interior floor type (grid cell <c>.</c>).</param>
/// <param name="Porch">Porch/deck type (grid cell <c>p</c>); <see cref="StructureType.None"/> leaves the ground bare.</param>
public readonly record struct VillageStylePalette(StructureType Wall, StructureType Door, StructureType Floor, StructureType Porch);
