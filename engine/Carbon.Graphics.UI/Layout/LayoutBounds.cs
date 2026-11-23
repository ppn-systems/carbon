// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.UI.Common;

namespace Carbon.Graphics.UI.Layout;

/// <summary>
/// Defines sizing, anchoring, pivot, and offset parameters for computing absolute UI element bounds.
/// Zero heap allocation struct.
/// </summary>
public readonly struct LayoutBounds : IEquatable<LayoutBounds>
{
    /// <summary>
    /// Gets the container anchoring preset.
    /// </summary>
    public Anchor Anchor { get; }

    /// <summary>
    /// Gets the normalized pivot point [0.0 - 1.0] within this element (e.g. (0.5, 0.5) for center).
    /// </summary>
    public Vector2 Pivot { get; }

    /// <summary>
    /// Gets the positional pixel offset relative to the anchor point.
    /// </summary>
    public Vector2 Offset { get; }

    /// <summary>
    /// Gets the explicit width and height dimensions in pixels.
    /// </summary>
    public Vector2 Size { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutBounds"/> struct.
    /// </summary>
    /// <param name="size">Width and height in pixels.</param>
    /// <param name="anchor">Anchor preset.</param>
    /// <param name="pivot">Normalized pivot point.</param>
    /// <param name="offset">Offset in pixels from anchor.</param>
    public LayoutBounds(
        Vector2 size,
        Anchor anchor = Anchor.TopLeft,
        Vector2? pivot = null,
        Vector2? offset = null)
    {
        this.Size = size;
        this.Anchor = anchor;
        this.Pivot = pivot ?? GetDefaultPivot(anchor);
        this.Offset = offset ?? Vector2.Zero;
    }

    /// <summary>
    /// Calculates the absolute rectangular screen boundary given the parent container bounds.
    /// </summary>
    /// <param name="parentBounds">The parent container rectangle.</param>
    /// <returns>The computed absolute <see cref="RectangleF"/> bounds.</returns>
    public RectangleF CalculateBounds(in RectangleF parentBounds)
    {
        if (this.Anchor == Anchor.Stretch)
        {
            float width = Math.Max(0f, parentBounds.Width - (this.Offset.X * 2f));
            float height = Math.Max(0f, parentBounds.Height - (this.Offset.Y * 2f));
            return new RectangleF(parentBounds.X + this.Offset.X, parentBounds.Y + this.Offset.Y, width, height);
        }

        Vector2 anchorPoint = this.GetAnchorPoint(parentBounds);
        float x = anchorPoint.X + this.Offset.X - (this.Size.X * this.Pivot.X);
        float y = anchorPoint.Y + this.Offset.Y - (this.Size.Y * this.Pivot.Y);

        return new RectangleF(x, y, this.Size.X, this.Size.Y);
    }

    private Vector2 GetAnchorPoint(in RectangleF parent) =>
        this.Anchor switch
        {
            Anchor.TopLeft => new Vector2(parent.X, parent.Y),
            Anchor.TopCenter => new Vector2(parent.X + (parent.Width * 0.5f), parent.Y),
            Anchor.TopRight => new Vector2(parent.Right, parent.Y),
            Anchor.CenterLeft => new Vector2(parent.X, parent.Y + (parent.Height * 0.5f)),
            Anchor.Center => new Vector2(parent.X + (parent.Width * 0.5f), parent.Y + (parent.Height * 0.5f)),
            Anchor.CenterRight => new Vector2(parent.Right, parent.Y + (parent.Height * 0.5f)),
            Anchor.BottomLeft => new Vector2(parent.X, parent.Bottom),
            Anchor.BottomCenter => new Vector2(parent.X + (parent.Width * 0.5f), parent.Bottom),
            Anchor.BottomRight => new Vector2(parent.Right, parent.Bottom),
            Anchor.Stretch => new Vector2(parent.X, parent.Y),
            _ => new Vector2(parent.X, parent.Y)
        };

    private static Vector2 GetDefaultPivot(Anchor anchor) =>
        anchor switch
        {
            Anchor.TopLeft => new Vector2(0f, 0f),
            Anchor.TopCenter => new Vector2(0.5f, 0f),
            Anchor.TopRight => new Vector2(1f, 0f),
            Anchor.CenterLeft => new Vector2(0f, 0.5f),
            Anchor.Center => new Vector2(0.5f, 0.5f),
            Anchor.CenterRight => new Vector2(1f, 0.5f),
            Anchor.BottomLeft => new Vector2(0f, 1f),
            Anchor.BottomCenter => new Vector2(0.5f, 1f),
            Anchor.BottomRight => new Vector2(1f, 1f),
            Anchor.Stretch => new Vector2(0f, 0f),
            _ => new Vector2(0f, 0f)
        };

    /// <inheritdoc/>
    public bool Equals(LayoutBounds other) =>
        this.Anchor == other.Anchor
        && this.Pivot.Equals(other.Pivot)
        && this.Offset.Equals(other.Offset)
        && this.Size.Equals(other.Size);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LayoutBounds other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(this.Anchor, this.Pivot, this.Offset, this.Size);

    /// <summary>
    /// Compares two <see cref="LayoutBounds"/> instances for equality.
    /// </summary>
    public static bool operator ==(LayoutBounds left, LayoutBounds right) => left.Equals(right);

    /// <summary>
    /// Compares two <see cref="LayoutBounds"/> instances for inequality.
    /// </summary>
    public static bool operator !=(LayoutBounds left, LayoutBounds right) => !left.Equals(right);
}
