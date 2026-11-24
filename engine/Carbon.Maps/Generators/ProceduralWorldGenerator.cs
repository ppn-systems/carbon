// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Noise;
using Carbon.Maps.Props;
using Carbon.Maps.Random;
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
    private readonly FractalNoise2D _foliageNoise;
    private readonly FractalNoise2D _mineralNoise;
    private readonly float _riverBaseX;
    private readonly float _riverBaseY;

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
        _foliageNoise = new FractalNoise2D(seed ^ 0xFEEDFACECAFEBEEFUL);
        _mineralNoise = new FractalNoise2D(seed ^ 0xDEADBEEF1337CAFEUL);

        // Seed-derived origin of the river lattice
        DeterministicRandom riverRng = new(seed ^ 0x0BADC0DE5EEDF00DUL);
        _riverBaseX = riverRng.NextFloat(0f, RiverSpacing);
        _riverBaseY = riverRng.NextFloat(0f, RiverSpacing);
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
                MapTile tile = this.SampleTileCore(worldX, worldY, frequency, out float riverDist);
                chunk.SetTile(localX, localY, tile);

                // riverDist is NaN only for open ocean, where Elevation < 0.38 already marks it near water.
                bool isNearWater = riverDist < 3.8f || tile.Elevation < 0.38f;

                // Coherent macro density fields (Foliage & Mineral Veins)
                float foliageDensity = _foliageNoise.Evaluate(worldX * 0.015f, worldY * 0.015f, octaves: 3);
                float mineralNoise = _mineralNoise.Evaluate(worldX * 0.008f, worldY * 0.008f, octaves: 2);

                // Natural wilderness procedural props & flora
                if (BiomePropResolver.TryResolveProp(
                    worldX,
                    worldY,
                    localX,
                    localY,
                    tile.Biome,
                    tile.Elevation,
                    foliageDensity,
                    mineralNoise,
                    isNearWater,
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
    /// Spacing in tiles between parallel continental river systems. Rivers repeat across the
    /// infinite world instead of existing only near the origin.
    /// </summary>
    public const float RiverSpacing = 768f;

    /// <summary>
    /// Computes the distance in tiles to the nearest meandering river channel.
    /// The world holds a repeating lattice of East-West continental rivers, each fed from the north by
    /// highland tributaries. Every river and tributary takes its offset and meander phase from the seed,
    /// so different seeds produce different hydrology. Uses <see cref="DeterministicMath"/> so the result
    /// is bit-identical on every platform.
    /// </summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>Distance to the nearest river centre line, in tiles.</returns>
    public float GetRiverDistance(int worldX, int worldY)
    {
        // Continuous domain-warped river meander
        float riverWarpX = (_riverNoise.Evaluate(worldX * 0.012f, worldY * 0.012f, octaves: 2) - 0.5f) * 28f;
        float riverWarpY = (_riverNoise.Evaluate(worldY * 0.012f, worldX * 0.012f, octaves: 2) - 0.5f) * 28f;

        // Primary East-West rivers: the two nearest bands always contain the closest one, because the
        // meander amplitude (≤ ~150 tiles) is well under half the band spacing.
        int band0 = (int)MathF.Floor((worldY - _riverBaseY) / RiverSpacing);
        float c0 = this.MainRiverCenterY(band0, worldX, riverWarpX);
        float c1 = this.MainRiverCenterY(band0 + 1, worldX, riverWarpX);
        float distMain = MathF.Min(MathF.Abs(worldY - c0), MathF.Abs(worldY - c1));

        // Highland feeder tributaries flow south into the river directly below them and start a third of
        // the way down from the river above, so they never cross a main channel.
        int southBand = worldY < c0 ? band0 : band0 + 1;
        float southCenter = worldY < c0 ? c0 : c1;
        float northCenter = worldY < c0 ? this.MainRiverCenterY(band0 - 1, worldX, riverWarpX) : c0;

        float distTrib = float.MaxValue;
        if (worldY < southCenter - 2f && worldY > northCenter + (RiverSpacing * 0.33f))
        {
            int col0 = (int)MathF.Floor((worldX - _riverBaseX) / RiverSpacing);
            for (int col = col0; col <= col0 + 1; col++)
            {
                float phase = HashPhase(_seed, col, southBand, 0x7A1BUL);
                float tributaryCenterX = _riverBaseX + (col * RiverSpacing)
                    + (DeterministicMath.Sin((worldY * 0.020f) + phase) * 30f)
                    + (DeterministicMath.Sin((worldY * 0.006f) + (phase * 1.7f)) * 70f)
                    + riverWarpY;
                distTrib = MathF.Min(distTrib, MathF.Abs(worldX - tributaryCenterX));
            }
        }

        return MathF.Min(distMain, distTrib);
    }

    private float MainRiverCenterY(int band, int worldX, float warp)
    {
        float phase = HashPhase(_seed, band, 0, 0x51F3UL);
        return _riverBaseY + (band * RiverSpacing)
            + (DeterministicMath.Sin((worldX * 0.024f) + phase) * 35f)
            + (DeterministicMath.Sin((worldX * 0.008f) + (phase * 2.3f)) * 75f)
            + (DeterministicMath.Cos((worldX * 0.055f) + (phase * 0.6f)) * 10f)
            + warp;
    }

    private static float HashPhase(ulong seed, int a, int b, ulong salt) =>
        SpatialHash.Hash32(seed, a, b, salt) * (6.2831853f / 4294967296f);

    /// <summary>
    /// Samples a single map tile at world tile coordinates with continuous multi-scale coherent noise.
    /// </summary>
    /// <param name="worldX">World X tile coordinate.</param>
    /// <param name="worldY">World Y tile coordinate.</param>
    /// <param name="frequency">Spatial frequency scale.</param>
    /// <returns>Evaluated <see cref="MapTile"/>.</returns>
    public MapTile SampleTile(int worldX, int worldY, float frequency = DefaultFrequency) =>
        this.SampleTileCore(worldX, worldY, frequency, out _);

    // riverDistance is NaN when the tile is ocean (elevation < 0.32) and the river field was not needed.
    private MapTile SampleTileCore(int worldX, int worldY, float frequency, out float riverDistance)
    {
        riverDistance = float.NaN;
        float fx = worldX * frequency;
        float fy = worldY * frequency;

        // 1. Continental Elevation with domain warping
        float elevation = _elevationWarp.Evaluate(fx, fy, warpStrength: 0.15f, octaves: 4);

        // 2. Coherent Continental Temperature with North-South Latitude Gradient
        // A gentle continental latitude band along Y (period ~2500 tiles)
        float latY = fy * 0.40f;
        float latGradient = 0.50f + (0.35f * DeterministicMath.Sin(latY));
        float tempTurbulence = (_temperatureNoise.Evaluate(fx * 0.45f, fy * 0.45f, octaves: 3) - 0.5f) * 0.20f;
        float temperature = Math.Clamp(latGradient + tempTurbulence, 0f, 1f);

        // Adiabatic lapse rate: High altitude mountain elevations cool down naturally
        if (elevation > 0.65f)
        {
            float altitudeCooling = (elevation - 0.65f) * 0.55f;
            temperature = Math.Clamp(temperature - altitudeCooling, 0f, 1f);
        }

        // 3. Moisture (Rainfall & Humidity)
        float rawMoisture = _moistureNoise.Evaluate(fx * 0.50f, fy * 0.50f, octaves: 3);
        if (elevation < 0.42f)
        {
            rawMoisture += (0.42f - elevation) * 0.35f; // Coastal humidity
        }

        float moisture = Math.Clamp(rawMoisture, 0f, 1f);

        // 4. Hydrology, Continuous Serpentine Rivers, Inland Lakes & Desert Oases
        // Rivers flow across continental landmasses and carve natural riverbeds down to sea level.
        // Once reaching the ocean (elevation < 0.32f), the river naturally discharges into the sea;
        // oceanic waters are never altered or elevated into artificial sandbanks.
        if (elevation >= 0.32f)
        {
            float riverDist = this.GetRiverDistance(worldX, worldY);
            riverDistance = riverDist;

            // Natural inland freshwater lakes and desert oases
            float lakeNoise = _riverNoise.Evaluate((fx * 3.6f) + 120f, (fy * 3.6f) + 120f, octaves: 2);
            bool isLakeOrOasis = false;

            if (temperature > 0.68f && rawMoisture < 0.28f && elevation >= 0.42f && elevation <= 0.62f && lakeNoise > 0.86f)
            {
                // Desert oasis spring in arid dunes
                isLakeOrOasis = true;
            }
            else if (elevation >= 0.45f && elevation <= 0.68f && rawMoisture >= 0.50f && riverDist > 14f && lakeNoise > 0.88f)
            {
                // Mountain / valley freshwater lake
                isLakeOrOasis = true;
            }

            if (riverDist <= 2.2f || isLakeOrOasis)
            {
                // Flowing freshwater river channel or inland lake: continuous, un-severed water surface
                byte riverVariant = (byte)(SpatialHash.Hash32(_seed, worldX, worldY, 0x0A71UL) & 3);
                return new MapTile(
                    BiomeType.ShallowWater,
                    Elevation: 0.28f,
                    temperature,
                    Moisture: 0.85f,
                    IsSolid: false,
                    riverVariant,
                    HeightLevel: 0);
            }

            if (riverDist < 6.0f)
            {
                // Riparian river valley zone: smooth continuous slope into the riverbed,
                // lush moist soil without artificial step cliffs
                float valleyT = Math.Clamp((riverDist - 2.2f) / 3.8f, 0f, 1f);
                float targetValleyElevation = (0.34f * (1f - valleyT)) + (MathF.Min(elevation, 0.50f) * valleyT);
                elevation = MathF.Min(elevation, targetValleyElevation);
                moisture = Math.Clamp(moisture + (0.25f * (1f - valleyT)), 0f, 1f);
            }
        }

        // 5. Resolve biome
        BiomeType biome = WhittakerBiomeResolver.Resolve(elevation, temperature, moisture);
        bool isSolid = BiomeProperties.Get(biome).IsSolid;

        // 6. Procedural variant for subtle visual variation
        byte variant = (byte)(SpatialHash.Hash32(_seed, worldX, worldY, 0x0A71UL) & 3);

        // 7. Resolve discrete 2.5D height level (0: Lowland, 1: Plateau, 2: Mountain)
        byte heightLevel = TopographyHelper.ResolveHeightLevel(elevation, biome);

        return new MapTile(biome, elevation, temperature, moisture, isSolid, variant, heightLevel);
    }
}
