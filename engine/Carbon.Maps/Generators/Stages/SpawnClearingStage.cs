// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Props;

namespace Carbon.Maps.Generators.Stages;

/// <summary>
/// Keeps a clearing around the world spawn: no trees, ores or rocks block new players on arrival. The clearing then gets
/// the starter forage (loose stones, fallen branches and dry bushes) at fixed offsets from the spawn tile, so a character
/// who arrives with an empty bag can gather the stone, wood and fiber of a first tool by hand.
/// </summary>
public sealed class SpawnClearingStage : IChunkStage
{
    /// <summary>Clearing radius in tiles.</summary>
    public const int Radius = 9;

    /// <summary>
    /// Starter forage around the spawn: prop type, tile offset from the spawn tile (all inside <see cref="Radius"/>),
    /// draw radius and colors.
    /// </summary>
    private static readonly (MapPropType Type, int Dx, int Dy, float Radius, uint Primary, uint Secondary)[] s_forage =
    [
        (MapPropType.LooseStones, 3, 1, 9f, 0x8A8E94FF, 0x5E6268FF),
        (MapPropType.LooseStones, -2, 4, 9f, 0x8A8E94FF, 0x5E6268FF),
        (MapPropType.LooseStones, 4, -3, 9f, 0x8A8E94FF, 0x5E6268FF),
        (MapPropType.LooseStones, -4, -2, 9f, 0x8A8E94FF, 0x5E6268FF),
        (MapPropType.FallenBranch, 2, -3, 12f, 0x8A6A44FF, 0x5E4428FF),
        (MapPropType.FallenBranch, -3, 2, 12f, 0x8A6A44FF, 0x5E4428FF),
        (MapPropType.FallenBranch, 4, 3, 12f, 0x8A6A44FF, 0x5E4428FF),
        (MapPropType.FallenBranch, -5, 0, 12f, 0x8A6A44FF, 0x5E4428FF),
        (MapPropType.DryBush, 1, 4, 14f, 0x9C9A52FF, 0x5E4428FF),
        (MapPropType.DryBush, -1, -4, 14f, 0x9C9A52FF, 0x5E4428FF),
        (MapPropType.DryBush, 6, 0, 14f, 0x9C9A52FF, 0x5E4428FF),
        (MapPropType.DryBush, 0, -6, 14f, 0x9C9A52FF, 0x5E4428FF),
    ];

    private readonly Lazy<(int X, int Y)> _spawn;

    /// <summary>Initializes a new instance of the <see cref="SpawnClearingStage"/> class.</summary>
    /// <param name="spawn">The spawn tile, computed lazily once.</param>
    public SpawnClearingStage(Lazy<(int X, int Y)> spawn) => _spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));

    /// <inheritdoc/>
    public void Populate(Chunk2D chunk, ChunkContext context)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        int x0 = context.ChunkX * Chunk2D.Size;
        int y0 = context.ChunkY * Chunk2D.Size;

        // Cheap reject: only the few chunks near the origin can hold the spawn (it is searched outward from 0,0).
        if (Math.Max(Math.Abs(x0), Math.Abs(y0)) > 2400 + Chunk2D.Size)
        {
            return;
        }

        (int sx, int sy) = _spawn.Value;
        if (sx + Radius < x0 || sx - Radius >= x0 + Chunk2D.Size || sy + Radius < y0 || sy - Radius >= y0 + Chunk2D.Size)
        {
            return;
        }

        if (chunk.Props.Count > 0)
        {
            _ = chunk.RemoveProps(p =>
            {
                int dx = x0 + p.LocalTileX - sx;
                int dy = y0 + p.LocalTileY - sy;
                return (dx * dx) + (dy * dy) <= Radius * Radius;
            });
        }

        for (int i = 0; i < s_forage.Length; i++)
        {
            (MapPropType type, int dx, int dy, float radius, uint primary, uint secondary) = s_forage[i];
            int lx = sx + dx - x0;
            int ly = sy + dy - y0;
            if (lx < 0 || lx >= Chunk2D.Size || ly < 0 || ly >= Chunk2D.Size)
            {
                continue;
            }

            // The spawn tile is always dry ground on height level 0 (SpawnLocator); forage stays on the same level.
            MapTile tile = context.Tiles[(ly * Chunk2D.Size) + lx].Tile;
            if (tile.IsSolid || tile.Biome is BiomeType.DeepOcean or BiomeType.ShallowWater ||
                tile.HeightLevel != 0 ||
                chunk.IsRoad(lx, ly) || chunk.IsBridge(lx, ly) || chunk.FindPropIndexAt(lx, ly) >= 0)
            {
                continue;
            }

            float worldX = (x0 + lx + 0.5f) * context.TileSize;
            float worldY = (y0 + ly + 0.5f) * context.TileSize;
            chunk.AddProp(MapProp.Create(type, lx, ly, worldX, worldY, radius, isSolid: false, primary, secondary));
        }
    }
}
