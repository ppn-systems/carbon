// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.Batching.Common;

/// <summary>
/// Specifies visual mirror transformations applied to a rendered sprite.
/// </summary>
[Flags]
public enum SpriteEffects
{
    /// <summary>
    /// No visual flip applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// Flips the sprite horizontally along the vertical axis.
    /// </summary>
    FlipHorizontally = 1,

    /// <summary>
    /// Flips the sprite vertically along the horizontal axis.
    /// </summary>
    FlipVertically = 2
}
