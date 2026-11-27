// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;
using Carbon.Maps.Settlements;

namespace Carbon.Maps.Landmarks;

/// <summary>
/// The structures and fields of one point of interest. Built deterministically from the point of interest and
/// the pure terrain sampler, so the server (collision, navigation) and every client (rendering) agree on it.
/// </summary>
public sealed class LandmarkLayout
{
    private readonly LandmarkPiece[] _pieces;
    private readonly LandmarkField[] _fields;
    private readonly VillagePlot[] _plots;
    private readonly LandmarkPath[] _paths;
    private readonly (int X, int Y)[] _paved;

    internal LandmarkLayout(LandmarkPiece[] pieces, LandmarkField[] fields, VillagePlot[] plots, LandmarkPath[] paths, (int X, int Y)[] paved)
    {
        _pieces = pieces;
        _fields = fields;
        _plots = plots;
        _paths = paths;
        _paved = paved;
    }

    /// <summary>Gets the empty layout.</summary>
    public static LandmarkLayout Empty { get; } = new([], [], [], [], []);

    /// <summary>
    /// Gets the village buildings. They are not drawn or collided as landmark pieces: the server materializes them as
    /// real building-grid structures owned by the village faction (ADR-025).
    /// </summary>
    public ReadOnlySpan<VillagePlot> Plots => _plots;

    /// <summary>Gets the centre lines of the village streets, lanes and the spurs to front doors (ADR-027).</summary>
    public ReadOnlySpan<LandmarkPath> Paths => _paths;

    /// <summary>
    /// Gets the world tiles of the square, the streets and the door paths that the server paves with a player-buildable
    /// floor structure (ADR-027 revision 3), in a deterministic order.
    /// </summary>
    public ReadOnlySpan<(int X, int Y)> PavedTiles => _paved;

    /// <summary>Gets the upright structures.</summary>
    public ReadOnlySpan<LandmarkPiece> Pieces => _pieces;

    /// <summary>Gets the crop fields.</summary>
    public ReadOnlySpan<LandmarkField> Fields => _fields;

    /// <summary>Builds the layout of a point of interest.</summary>
    /// <param name="poi">The point of interest.</param>
    /// <param name="tileAt">Pure terrain sampler.</param>
    /// <returns>The layout.</returns>
    public static LandmarkLayout Build(in PointOfInterest poi, ITileSource tileAt) => Build(poi, tileAt, isRoad: null);

    /// <summary>Builds the layout of a point of interest, keeping village buildings off roads.</summary>
    /// <param name="poi">The point of interest.</param>
    /// <param name="tileAt">Pure terrain sampler.</param>
    /// <param name="isRoad">Pure road query, or <see langword="null"/>.</param>
    /// <returns>The layout.</returns>
    public static LandmarkLayout Build(in PointOfInterest poi, ITileSource tileAt, Func<int, int, bool>? isRoad)
    {
        ArgumentNullException.ThrowIfNull(tileAt);
        if (poi.Type == PointOfInterestType.None)
        {
            return Empty;
        }

        LandmarkPlanContext context = new(poi, tileAt, isRoad);
        LandmarkPlanners.Get(poi.Type)?.Plan(context);
        return context.ToLayout();
    }

    /// <summary>Builds the layout of a point of interest with the world's road network.</summary>
    internal static LandmarkLayout Build(in PointOfInterest poi, ITileSource tileAt, Roads.RoadNetwork? roads)
    {
        ArgumentNullException.ThrowIfNull(tileAt);
        if (poi.Type == PointOfInterestType.None)
        {
            return Empty;
        }

        Func<int, int, bool>? isRoad = roads is null
            ? null
            : (x, y) => roads.IsRoadOrBridgeAtTile(x, y, bridgeOnly: false) || roads.IsRoadOrBridgeAtTile(x, y, bridgeOnly: true);
        LandmarkPlanContext context = new(poi, tileAt, isRoad, roads);
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
