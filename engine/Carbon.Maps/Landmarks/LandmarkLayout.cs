// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;

namespace Carbon.Maps.Landmarks;

/// <summary>
/// The structures and fields of one point of interest. Built deterministically from the point of interest and
/// the pure terrain sampler, so the server (collision, navigation) and every client (rendering) agree on it.
/// </summary>
public sealed class LandmarkLayout
{
    private readonly LandmarkPiece[] _pieces;
    private readonly LandmarkField[] _fields;

    internal LandmarkLayout(LandmarkPiece[] pieces, LandmarkField[] fields)
    {
        _pieces = pieces;
        _fields = fields;
    }

    /// <summary>Gets the empty layout.</summary>
    public static LandmarkLayout Empty { get; } = new([], []);

    /// <summary>Gets the upright structures.</summary>
    public ReadOnlySpan<LandmarkPiece> Pieces => _pieces;

    /// <summary>Gets the crop fields.</summary>
    public ReadOnlySpan<LandmarkField> Fields => _fields;

    /// <summary>Builds the layout of a point of interest.</summary>
    /// <param name="poi">The point of interest.</param>
    /// <param name="tileAt">Pure terrain sampler.</param>
    /// <returns>The layout.</returns>
    public static LandmarkLayout Build(in PointOfInterest poi, ITileSource tileAt)
    {
        ArgumentNullException.ThrowIfNull(tileAt);
        if (poi.Type == PointOfInterestType.None)
        {
            return Empty;
        }

        LandmarkPlanContext context = new(poi, tileAt);
        LandmarkPlanners.Get(poi.Type)?.Plan(context);
        return context.ToLayout();
    }
    /// <summary>Returns whether the solid footprint of any structure overlaps a world tile.</summary>
    /// <param name="tileX">World tile X.</param>
    /// <param name="tileY">World tile Y.</param>
    /// <returns><c>true</c> when the tile is blocked.</returns>
    public bool BlocksTile(int tileX, int tileY)
    {
        Span<TileBox> boxes = stackalloc TileBox[LandmarkPiece.MaxColliders];
        for (int i = 0; i < _pieces.Length; i++)
        {
            int n = _pieces[i].GetNavFootprint(boxes);
            for (int k = 0; k < n; k++)
            {
                TileBox b = boxes[k];

                // Count the tile as blocked when the footprint covers a meaningful part of it.
                float ox = MathF.Min(b.MaxX, tileX + 1) - MathF.Max(b.MinX, tileX);
                float oy = MathF.Min(b.MaxY, tileY + 1) - MathF.Max(b.MinY, tileY);
                if (ox > 0.3f && oy > 0.3f)
                {
                    return true;
                }
            }
        }

        return false;
    }

}
