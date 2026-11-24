// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Noise;
using Carbon.Maps.Props;
using Carbon.Maps.Topography;

namespace Carbon.Maps.Generators;

/// <summary>
/// High-performance deterministic procedural world generator.
/// Synthesizes continuous multi-layered coherent noise fields (Elevation, Temperature, Moisture, Hydrology)
/// with domain warping to produce massive macro-continental 10-biome landscapes.
/// </summary>
public sealed class ProceduralWorldGenerator
{
    /// <summary>
    /// Default macro-scale spatial frequency producing expansive continental biomes (wavelength ~1250 tiles).
    /// </summary>
    public const float DefaultFrequency = 0.0008f;

    private readonly ulong _seed;
    private readonly DomainWarp2D _elevationWarp;
    private readonly FractalNoise2D _temperatureNoise;
    private readonly FractalNoise2D _moistureNoise;
    private readonly FractalNoise2D _riverNoise;

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
        _riverNoise = new FractalNoise2D(seed ^ 0x9876543210ABCDEFUL);
    }

    /// <summary>
    /// Generates a complete continuous world tilemap of the given dimensions.
    /// </summary>
    /// <param name="width">Map width in tiles.</param>
    /// <param name="height">Map height in tiles.</param>
    /// <param name="frequency">Spatial frequency scale of terrain features (default 0.0008f).</param>
    /// <returns>Populated <see cref="TilemapData"/>.</returns>
    public TilemapData GenerateTilemap(int width, int height, float frequency = DefaultFrequency)
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
    /// Seamlessly continuous across chunk boundaries with natural flora and prop placement.
    /// </summary>
    /// <param name="chunkX">Chunk horizontal coordinate.</param>
    /// <param name="chunkY">Chunk vertical coordinate.</param>
    /// <param name="frequency">Spatial frequency scale of terrain features (default 0.0008f).</param>
    /// <param name="tileSize">Physical dimension of each tile in world units.</param>
    /// <returns>Populated <see cref="Chunk2D"/>.</returns>
    public Chunk2D GenerateChunk(
        int chunkX,
        int chunkY,
        float frequency = DefaultFrequency,
        float tileSize = 32f)
    {
        Chunk2D chunk = new(chunkX, chunkY);
        this.PopulateChunk(chunk, chunkX, chunkY, frequency, tileSize);
        return chunk;
    }

    /// <summary>
    /// Populates an existing (or recycled) chunk instance at the specified chunk grid coordinates.
    /// Zero allocation when used with chunk pooling.
    /// </summary>
    public void PopulateChunk(
        Chunk2D chunk,
        int chunkX,
        int chunkY,
        float frequency = DefaultFrequency,
        float tileSize = 32f)
    {
        ArgumentNullException.ThrowIfNull(chunk, nameof(chunk));

        chunk.Reset(chunkX, chunkY);
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

                // Natural wilderness procedural props & flora
                if (BiomePropResolver.TryResolveProp(
                    worldX,
                    worldY,
                    localX,
                    localY,
                    tile.Biome,
                    tileSize,
                    _seed,
                    out MapProp wildProp))
                {
                    chunk.AddProp(wildProp);
                }
            }
        }
    }

    /// <summary>
    /// Samples a single map tile at world tile coordinates with continuous multi-scale coherent noise.
    /// </summary>
    /// <param name="worldX">World X tile coordinate.</param>
    /// <param name="worldY">World Y tile coordinate.</param>
    /// <param name="frequency">Spatial frequency scale.</param>
    /// <returns>Evaluated <see cref="MapTile"/>.</returns>
    public MapTile SampleTile(int worldX, int worldY, float frequency = DefaultFrequency)
    {
        float fx = worldX * frequency;
        float fy = worldY * frequency;

        // 1. Continental Elevation with domain warping
        float elevation = _elevationWarp.Evaluate(fx, fy, warpStrength: 0.15f, octaves: 4);

        // 2. Temperature (gradient slightly by latitude Y plus noise)
        float rawTemp = _temperatureNoise.Evaluate(fx * 0.7f, fy * 0.7f, octaves: 3);
        float temperature = Math.Clamp(rawTemp, 0f, 1f);

        // 3. Moisture
        float moisture = _moistureNoise.Evaluate(fx * 0.8f, fy * 0.8f, octaves: 3);

        // 4. Hydrology & River carving (when on land, elevation > 0.38f)
        if (elevation is >= 0.38f and <= 0.82f)
        {
            float riverVal = MathF.Abs(_riverNoise.Evaluate(fx * 3.0f, fy * 3.0f, octaves: 2) - 0.5f);
            if (riverVal < 0.013f)
            {
                bool isCenter = riverVal < 0.007f;
                BiomeType riverBiome = isCenter ? BiomeType.ShallowWater : BiomeType.BeachSand;
                byte riverVariant = (byte)((worldX ^ (worldY * 31)) & 3);
                return new MapTile(riverBiome, isCenter ? 0.28f : 0.35f, temperature, 1.0f, false, riverVariant);
            }
        }

        // 5. Resolve biome
        BiomeType biome = WhittakerBiomeResolver.Resolve(elevation, temperature, moisture);
        bool isSolid = BiomeProperties.Get(biome).IsSolid;

        // 6. Procedural variant for subtle visual variation
        byte variant = (byte)((worldX ^ (worldY * 31)) & 3);

        // 7. Resolve discrete 2.5D height level (0: Lowland, 1: Plateau, 2: Mountain)
        byte heightLevel = TopographyHelper.ResolveHeightLevel(elevation, biome);

        return new MapTile(biome, elevation, temperature, moisture, isSolid, variant, heightLevel);
    }
}
