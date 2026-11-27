// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>One line of an item tooltip.</summary>
internal readonly record struct ItemTooltipLine(string Text, ColorRgba Color);
