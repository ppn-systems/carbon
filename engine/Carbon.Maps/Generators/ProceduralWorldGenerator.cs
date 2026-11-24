// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Noise;

namespace Carbon.Maps.Generators;

/// <summary>
/// High-performance deterministic procedural world generator.
/// Synthesizes continuous multi-layered coherent noise fields (Elevation, Temperature, Moisture)
/// with domain warping to produce rich 10-biome landscapes.
/// </summary>
public sealed class ProceduralWorldGenerator
{
    private readonly ulong _seed;
    private readonly DomainWarp2D _elevationWarp;
    private readonly FractalNoise2D _temperatureNoise;
    private readonly FractalNoise2D _moistureNoise;

    /// <summary>
    /// Gets the deterministic generation seed.
    /// </summary>
    public ulong Seed => _seed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProceduralWorldGenerator"/> class.
    /// </summary>
    /// <param name="seed">64-bit deterministic world seed.</param>
    public ProceduralWorldGenerator(ulong seed)
    {
        _seed = seed;
        _elevationWarp = new DomainWarp2D(seed ^ 0x1234567890ABCDEFUL);
        _temperatureNoise = new FractalNoise2D(seed ^ 0xFEDCBA0987654321UL);
        _moistureNoise = new FractalNoise2D(seed ^ 0xCAFEBABE1337BEEFUL);
    }

    /// <summary>
    /// Generates a complete continuous world tilemap of the given dimensions.
    /// </summary>
    /// <param name="width">Map width in tiles.</param>
    /// <param name="height">Map height in tiles.</param>
    /// <param name="frequency">Spatial frequency scale of terrain features (default 0.015f).</param>
    /// <returns>Populated <see cref="TilemapData"/>.</returns>
    public TilemapData GenerateTilemap(int width, int height, float frequency = 0.015f)
    {
        TilemapData map = new(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                MapTile tile = this.SampleTile(x, y, frequency);
                map.SetTile(x, y, tile);
            }
        }

        return map;
    }

    /// <summary>
    /// Generates a single 32x32 world chunk at the given chunk grid coordinates.
    /// Seamlessly continuous across chunk boundaries.
    /// </summary>
    /// <param name="chunkX">Chunk horizontal coordinate.</param>
    /// <param name="chunkY">Chunk vertical coordinate.</param>
    /// <param name="frequency">Spatial frequency scale of terrain features (default 0.015f).</param>
    /// <returns>Populated <see cref="Chunk2D"/>.</returns>
    public Chunk2D GenerateChunk(int chunkX, int chunkY, float frequency = 0.015f)
    {
        Chunk2D chunk = new(chunkX, chunkY);
        int startX = chunkX * Chunk2D.Size;
        int startY = chunkY * Chunk2D.Size;

        for (int localY = 0; localY < Chunk2D.Size; localY++)
        {
            int worldY = startY + localY;
            for (int localX = 0; localX < Chunk2D.Size; localX++)
            {
                int worldX = startX + localX;
                MapTile tile = this.SampleTile(worldX, worldY, frequency);
                chunk.SetTile(localX, localY, tile);
            }
        }

        return chunk;
    }

    /// <summary>
    /// Samples a single map tile at world tile coordinates.
    /// </summary>
    /// <param name="worldX">World X tile coordinate.</param>
    /// <param name="worldY">World Y tile coordinate.</param>
    /// <param name="frequency">Spatial frequency scale.</param>
    /// <returns>Evaluated <see cref="MapTile"/>.</returns>
    public MapTile SampleTile(int worldX, int worldY, float frequency = 0.015f)
    {
        float fx = worldX * frequency;
        float fy = worldY * frequency;

        // 1. Elevation with domain warping
        float elevation = _elevationWarp.Evaluate(fx, fy, warpStrength: 0.15f, octaves: 4);

        // 2. Temperature (gradient slightly by latitude Y plus noise)
        float rawTemp = _temperatureNoise.Evaluate(fx * 0.7f, fy * 0.7f, octaves: 3);
        float temperature = Math.Clamp(rawTemp, 0f, 1f);

        // 3. Moisture
        float moisture = _moistureNoise.Evaluate(fx * 0.8f, fy * 0.8f, octaves: 3);

        // 4. Resolve biome
        BiomeType biome = WhittakerBiomeResolver.Resolve(elevation, temperature, moisture);
        bool isSolid = BiomeProperties.Get(biome).IsSolid;

        // 5. Procedural variant for subtle visual variation
        byte variant = (byte)((worldX ^ (worldY * 31)) & 3);

        return new MapTile(biome, elevation, temperature, moisture, isSolid, variant);
    }
}
