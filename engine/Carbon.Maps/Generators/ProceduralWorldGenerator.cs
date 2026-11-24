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
    /// Version of the generation algorithm, sent to clients in <c>MapWorldInfo.GeneratorVersion</c>.
    /// Bump it whenever any change alters generated output (the golden-hash test fails), so a client running
    /// a different generator refuses to play instead of silently desyncing.
    /// </summary>
    public const ushort GeneratorVersion = 3;

    /// <summary>Frequency of the major river field (feature size ≈ 900 tiles).</summary>
    public const float MajorRiverFrequency = 0.0011f;

    /// <summary>Frequency of the tributary field (feature size ≈ 300 tiles).</summary>
    public const float TributaryFrequency = 0.0034f;

    /// <summary>Relief above which no river exists (river sources).</summary>
    public const float MaxRiverRelief = 0.74f;

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
    private readonly FractalNoise2D _majorRiverField;
    private readonly FractalNoise2D _tributaryField;
    private readonly FractalNoise2D _tributarySideField;
    private readonly FractalNoise2D _ridgeNoise;

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

        _majorRiverField = new FractalNoise2D(seed ^ 0x0BADC0DE5EEDF00DUL);
        _tributaryField = new FractalNoise2D(seed ^ 0x7E1B0A2C4D6F8091UL);
        _tributarySideField = new FractalNoise2D(seed ^ 0x3C5A7E9F1B2D4F60UL);
        _ridgeNoise = new FractalNoise2D(seed ^ 0x52D6E1F0A3B49C87UL);
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
    /// Computes the distance in tiles to the nearest river channel, scaled by the river's local width
    /// (a value of 1 means "one half-width away"; the channel itself is ≤ 2.2).
    /// </summary>
    /// <remarks>
    /// Rivers are the zero iso-lines of domain-warped noise fields, so every river has its own organic
    /// shape instead of repeating one sine curve. Distance to the iso-line is the first-order estimate
    /// <c>|f| / |∇f|</c>. The terrain relief shapes the network: major rivers widen in lowlands, thin into
    /// streams in the uplands and vanish below the peaks (their sources); tributaries live in the uplands
    /// and exist on one side of their receiving river only, so they end in T-junctions instead of crossing it.
    /// </remarks>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>Width-scaled distance to the nearest river.</returns>
    public float GetRiverDistance(int worldX, int worldY) =>
        this.GetRiverDistance(worldX, worldY, this.SampleRelief(worldX * DefaultFrequency, worldY * DefaultFrequency));

    private float GetRiverDistance(int worldX, int worldY, float relief)
    {
        // Rivers never rise above the high mountains: those are where they spring from.
        if (relief > MaxRiverRelief)
        {
            return float.MaxValue;
        }

        // Shared meander warp (tiles)
        float warpX = (_riverNoise.Evaluate(worldX * 0.010f, worldY * 0.010f, octaves: 2) - 0.5f) * 36f;
        float warpY = (_riverNoise.Evaluate((worldY * 0.010f) + 71.3f, (worldX * 0.010f) + 19.7f, octaves: 2) - 0.5f) * 36f;
        float wx = worldX + warpX;
        float wy = worldY + warpY;

        // Width: broad lowland rivers, narrowing upstream into streams.
        float upland = Math.Clamp((relief - 0.40f) / (MaxRiverRelief - 0.40f), 0f, 1f);
        // Never thinner than ~0.9 so the 2.2 channel threshold stays ≥ 2 tiles wide and streams do not
        // break up into dashes.
        float majorWidth = 1.6f - (0.7f * upland);
        float major = IsoLineDistance(_majorRiverField, wx, wy, MajorRiverFrequency, octaves: 2, out float majorSign) / majorWidth;

        // Tributaries: upland streams, on one side of the major river field only.
        float tributary = float.MaxValue;
        if (relief > 0.48f)
        {
            // Only some upland basins carry tributaries (sparsity), and only on one side of the major
            // river field, so a tributary meets its river in a T-junction instead of crossing it.
            float basin = _tributarySideField.Evaluate(worldX * 0.0009f, worldY * 0.0009f, octaves: 2);
            float side = _tributarySideField.Evaluate((worldX * 0.0013f) + 37.1f, (worldY * 0.0013f) + 11.9f, octaves: 1) - 0.5f;
            if (basin > 0.54f && side * majorSign > 0f)
            {
                tributary = IsoLineDistance(_tributaryField, wx, wy, TributaryFrequency, octaves: 2, out _) / 0.9f;
            }
        }

        return MathF.Min(major, tributary);
    }

    private static float IsoLineDistance(FractalNoise2D field, float x, float y, float frequency, int octaves, out float value)
    {
        const float H = 1.5f;
        float v = field.Evaluate(x * frequency, y * frequency, octaves) - 0.5f;
        value = v;
        float gx = (field.Evaluate((x + H) * frequency, y * frequency, octaves) - field.Evaluate((x - H) * frequency, y * frequency, octaves)) / (2f * H);
        float gy = (field.Evaluate(x * frequency, (y + H) * frequency, octaves) - field.Evaluate(x * frequency, (y - H) * frequency, octaves)) / (2f * H);
        float grad = MathF.Sqrt((gx * gx) + (gy * gy));
        return MathF.Abs(v) / MathF.Max(grad, 1e-5f);
    }

    /// <summary>
    /// Smooth, low-detail terrain relief used for discrete height levels and river routing. Built from the
    /// same warped continental field as <c>Elevation</c> but with fewer octaves, plus ridged mountain ranges,
    /// so plateau and mountain outlines are broad and clean instead of jagged.
    /// </summary>
    private float SampleRelief(float fx, float fy)
    {
        float relief = _elevationWarp.Evaluate(fx, fy, warpStrength: 0.15f, octaves: 2);
        return relief + this.RidgeLift(fx, fy, relief, octaves: 2);
    }

    // Ridged multifractal mountain chains, only raised where the continent is already high.
    private float RidgeLift(float fx, float fy, float baseElevation, int octaves)
    {
        float ridge = 1f - MathF.Abs((_ridgeNoise.Evaluate(fx * 2.2f, fy * 2.2f, octaves) * 2f) - 1f);
        float mask = Math.Clamp((baseElevation - 0.50f) / 0.14f, 0f, 1f);
        return ridge * ridge * mask * 0.10f;
    }

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
        elevation = Math.Clamp(elevation + this.RidgeLift(fx, fy, elevation, octaves: 4), 0f, 1f);

        // Smooth relief drives the discrete 2.5D height levels and the river network.
        float relief = this.SampleRelief(fx, fy);

        // Elevation before river valleys are carved: decides what counts as coast for height levels.
        float coastElevation = elevation;

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
            float riverDist = this.GetRiverDistance(worldX, worldY, relief);
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
                // Flowing freshwater river channel or inland lake: continuous, un-severed water surface.
                // Rivers and lakes sit on the terrain they cross; where they leave a plateau the
                // renderer draws a waterfall over the cliff instead of a carved canyon.
                byte riverVariant = (byte)(SpatialHash.Hash32(_seed, worldX, worldY, 0x0A71UL) & 3);
                return new MapTile(
                    BiomeType.ShallowWater,
                    Elevation: 0.28f,
                    temperature,
                    Moisture: 0.85f,
                    IsSolid: false,
                    riverVariant,
                    HeightLevel: TopographyHelper.ResolveTerrainHeightLevel(relief, coastElevation, BiomeType.ShallowWater, isInlandWater: true));
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
        // River valleys turn banks into beach sand; that is inland, so it keeps the terrain's level.
        bool isCoast = coastElevation < 0.38f;
        byte heightLevel = TopographyHelper.ResolveTerrainHeightLevel(relief, coastElevation, isCoast ? biome : BiomeType.LushPlains, isInlandWater: false);

        return new MapTile(biome, elevation, temperature, moisture, isSolid, variant, heightLevel);
    }
}
