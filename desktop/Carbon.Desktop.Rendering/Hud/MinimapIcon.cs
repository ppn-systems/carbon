// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>A marker placed on the minimap.</summary>
/// <param name="Kind">What the marker stands for.</param>
/// <param name="Position">Screen position of the marker centre.</param>
/// <param name="IsLockedTarget">Whether this is the locked combat target.</param>
internal readonly record struct MinimapIcon(MinimapIconKind Kind, Vector2 Position, bool IsLockedTarget = false);
