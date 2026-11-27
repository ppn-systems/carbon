// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Places minimap markers. Sources are only what the client already holds: villages from the streamed chunks'
/// points of interest (seed-generated, drawn in the world anyway) and actors from the replicated area-of-interest list.
/// Nothing outside the AoI is shown, so the minimap reveals no more than the screen around it.
/// </summary>
internal static class MinimapIconCollector
{
    /// <summary>Pixels kept clear of the map edge so markers never straddle the frame.</summary>
    internal const float EdgeInset = 3f;

    /// <summary>Fills <paramref name="destination"/> with markers inside <paramref name="view"/>; villages first (drawn under actors).</summary>
    /// <returns>The number of markers written.</returns>
    internal static int Collect(
        ChunkWorld2D? world,
        IReadOnlyList<RemoteEntityView> remotes,
        ulong lockedTargetNetworkId,
        in MinimapView view,
        float tileSize,
        Span<MinimapIcon> destination)
    {
        ArgumentNullException.ThrowIfNull(remotes);
        int count = 0;
        if (world is not null)
        {
            (int minX, int minY, int maxX, int maxY) = view.VisibleChunks();
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    if (!world.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk is null)
                    {
                        continue;
                    }

                    for (int i = 0; i < chunk.PointsOfInterest.Count && count < destination.Length; i++)
                    {
                        PointOfInterest poi = chunk.PointsOfInterest[i];

                        // A point of interest is listed by every chunk it overlaps: count it once, from its centre chunk.
                        if (!IsVillage(poi.Type) || Chunk2D.ToChunkCoord(poi.TileX) != cx || Chunk2D.ToChunkCoord(poi.TileY) != cy)
                        {
                            continue;
                        }

                        Vector2 p = view.Project(poi.TileX + 0.5f, poi.TileY + 0.5f);
                        if (view.IsInside(p, EdgeInset))
                        {
                            destination[count++] = new MinimapIcon(MinimapIconKind.Village, p);
                        }
                    }
                }
            }
        }

        for (int i = 0; i < remotes.Count && count < destination.Length; i++)
        {
            RemoteEntityView r = remotes[i];
            if (r.Kind is EntityKind.LootDrop or EntityKind.Projectile)
            {
                continue;
            }

            Vector2 p = view.ProjectWorld(r.Position, tileSize);
            if (view.IsInside(p, EdgeInset))
            {
                destination[count++] = new MinimapIcon(Classify(in r), p, r.NetworkId != 0 && r.NetworkId == lockedTargetNetworkId);
            }
        }

        return count;
    }

    /// <summary>Returns the marker kind of a replicated actor.</summary>
    internal static MinimapIconKind Classify(in RemoteEntityView entity)
    {
        switch (entity.Kind)
        {
            case EntityKind.Player:
                return MinimapIconKind.Player;
            case EntityKind.Npc:
                NpcCapabilities caps = WorldActorCatalog.GetNpcSpec(entity.ArchetypeId).Capabilities;
                return (caps & NpcCapabilities.ForgeArtisan) != 0
                    ? MinimapIconKind.Blacksmith
                    : (caps & NpcCapabilities.Trader) != 0 ? MinimapIconKind.Trader : MinimapIconKind.Npc;
            case EntityKind.Monster:
            case EntityKind.Projectile:
            case EntityKind.LootDrop:
            default:
                return MinimapIconKind.Monster;
        }
    }

    private static bool IsVillage(PointOfInterestType type) =>
        type is PointOfInterestType.RiverVillage or PointOfInterestType.DesertVillage or PointOfInterestType.SnowVillage or PointOfInterestType.SwampVillage;
}
