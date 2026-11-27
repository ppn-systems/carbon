// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Minimap terrain cached per chunk in a toroidal atlas of <see cref="RingSize"/>² chunk slots (chunk <c>(cx, cy)</c>
/// lives in slot <c>(cx mod RingSize, cy mod RingSize)</c>). A chunk is baked once, when it first enters the view, and
/// again only after <see cref="Invalidate"/> marks it dirty (a terrain edit). Only chunks the client already streamed
/// are baked: nothing is sampled or generated for the minimap. Baking reuses one pixel buffer: no per-frame allocation.
/// </summary>
internal sealed class MinimapTerrainCache
{
    /// <summary>Chunk slots per atlas axis; must exceed the widest view in chunks (see <see cref="MinimapView"/>).</summary>
    internal const int RingSize = 8;

    /// <summary>Atlas side in texels (one texel per tile).</summary>
    internal const int AtlasSize = RingSize * Chunk2D.Size;

    private const long NoChunk = long.MinValue;

    private readonly long[] _slotKeys = new long[RingSize * RingSize];
    private readonly bool[] _slotDirty = new bool[RingSize * RingSize];
    private readonly byte[] _pixels = new byte[Chunk2D.Size * Chunk2D.Size * 4];

    public MinimapTerrainCache() => this.Clear();

    /// <summary>Gets how many chunks were baked since construction (diagnostics and tests).</summary>
    public int BakeCount { get; private set; }

    /// <summary>Forgets every cached chunk (new world or new art).</summary>
    public void Clear()
    {
        Array.Fill(_slotKeys, NoChunk);
        Array.Clear(_slotDirty);
    }

    /// <summary>Marks chunk <c>(chunkX, chunkY)</c> for a re-bake if it is cached; other chunks bake when first seen anyway.</summary>
    public void Invalidate(int chunkX, int chunkY)
    {
        int slot = SlotIndex(chunkX, chunkY);
        if (_slotKeys[slot] == Chunk2D.PackKey(chunkX, chunkY))
        {
            _slotDirty[slot] = true;
        }
    }

    /// <summary>Returns whether chunk <c>(chunkX, chunkY)</c> has pixels in its slot (possibly stale while a re-bake waits).</summary>
    public bool TryGetSlot(int chunkX, int chunkY, out int slotX, out int slotY)
    {
        slotX = chunkX & (RingSize - 1);
        slotY = chunkY & (RingSize - 1);
        return _slotKeys[(slotY * RingSize) + slotX] == Chunk2D.PackKey(chunkX, chunkY);
    }

    /// <summary>Returns whether chunk <c>(chunkX, chunkY)</c> is cached and waiting for a re-bake.</summary>
    public bool IsDirty(int chunkX, int chunkY)
    {
        int slot = SlotIndex(chunkX, chunkY);
        return _slotKeys[slot] == Chunk2D.PackKey(chunkX, chunkY) && _slotDirty[slot];
    }

    /// <summary>
    /// Bakes the missing or dirty chunks of the inclusive range that are loaded in <paramref name="world"/>, at most
    /// <paramref name="maxBakes"/> of them, and uploads each to <paramref name="sink"/>.
    /// </summary>
    /// <returns>The number of chunks baked by this call.</returns>
    public int Prepare(
        ChunkWorld2D world,
        IVectorArtRepository? art,
        (int MinX, int MinY, int MaxX, int MaxY) range,
        IMinimapTextureSink sink,
        int maxBakes)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sink);
        int baked = 0;
        for (int cy = range.MinY; cy <= range.MaxY && baked < maxBakes; cy++)
        {
            for (int cx = range.MinX; cx <= range.MaxX && baked < maxBakes; cx++)
            {
                int slot = SlotIndex(cx, cy);
                long key = Chunk2D.PackKey(cx, cy);
                if (_slotKeys[slot] == key && !_slotDirty[slot])
                {
                    continue;
                }

                if (!world.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk is null)
                {
                    continue; // not streamed yet: the slot keeps whatever it had and the map shows its background there
                }

                this.Bake(chunk, art);
                sink.UploadChunk(cx & (RingSize - 1), cy & (RingSize - 1), _pixels);
                _slotKeys[slot] = key;
                _slotDirty[slot] = false;
                this.BakeCount++;
                baked++;
            }
        }

        return baked;
    }

    private static int SlotIndex(int chunkX, int chunkY) => ((chunkY & (RingSize - 1)) * RingSize) + (chunkX & (RingSize - 1));

    private void Bake(Chunk2D chunk, IVectorArtRepository? art)
    {
        Span<byte> px = _pixels;
        for (int ly = 0; ly < Chunk2D.Size; ly++)
        {
            for (int lx = 0; lx < Chunk2D.Size; lx++)
            {
                ref readonly MapTile tile = ref chunk.GetTile(lx, ly);
                byte north = ly > 0 ? chunk.GetTile(lx, ly - 1).HeightLevel : tile.HeightLevel;
                ColorRgba c = MinimapPalette.GetTileColor(in tile, north, chunk.IsRoad(lx, ly), chunk.IsBridge(lx, ly), art);
                int o = ((ly * Chunk2D.Size) + lx) * 4;
                px[o] = c.R;
                px[o + 1] = c.G;
                px[o + 2] = c.B;
                px[o + 3] = 255;
            }
        }
    }
}
