// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Landmarks;
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
    public const ushort GeneratorVersion = 10;

    /// <summary>Width-scaled distance over which rivers and lakes lower and moisten the land around them.</summary>
    public const float RiparianReach = 10f;


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
    private readonly RiverNetwork _rivers;
    private readonly PointOfInterestResolver _landmarks;
    private readonly FractalNoise2D _ridgeNoise;

    /// <summary>
    /// Gets the deterministic generation seed.
    /// </summary>
    public ulong Seed => _seed;

    /// <summary>Gets the resolver that places villages, harbours, camps and shrines.</summary>
    public PointOfInterestResolver Landmarks => _landmarks;

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

        _ridgeNoise = new FractalNoise2D(seed ^ 0x52D6E1F0A3B49C87UL);
        _rivers = new RiverNetwork(seed, this.SampleTerrainElevation);
        _landmarks = new PointOfInterestResolver(seed, (x, y) => this.SampleTile(x, y), _rivers.GetDistance);
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

        // Points of interest centred in this chunk (sites are larger than chunks, so check the 3x3 sites).
        int siteX = FloorDiv(chunkX * Chunk2D.Size, PointOfInterestResolver.SiteSize);
        int siteY = FloorDiv(chunkY * Chunk2D.Size, PointOfInterestResolver.SiteSize);
        for (int sy = siteY - 1; sy <= siteY + 1; sy++)
        {
            for (int sx = siteX - 1; sx <= siteX + 1; sx++)
            {
                PointOfInterest poi = _landmarks.GetSite(sx, sy);
                if (poi.Type != PointOfInterestType.None &&
                    Chunk2D.ToChunkCoord(poi.TileX) == chunkX && Chunk2D.ToChunkCoord(poi.TileY) == chunkY)
                {
                    chunk.AddPointOfInterest(poi);
                }
            }
        }
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

                // Settlements and landmarks keep their footprint clear of trees and rocks.
                if (_landmarks.TryGetAt(worldX, worldY, out _))
                {
                    continue;
                }

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
    /// Computes the width-scaled distance from a tile to the nearest river or lake (≤ 2.2 is water).
    /// Rivers come from <see cref="RiverNetwork"/>: they spring in the highlands, run down the steepest
    /// descent of the terrain, merge where they meet and end in the sea or in a lake.
    /// </summary>
    /// <param name="worldX">World tile X.</param>
    /// <param name="worldY">World tile Y.</param>
    /// <returns>Width-scaled distance to the nearest river or lake.</returns>
    public float GetRiverDistance(int worldX, int worldY) => _rivers.GetDistance(worldX, worldY);

    /// <summary>
    /// Builds the river regions around a point on the thread pool ahead of time, so streaming chunks in
    /// does not stall on a region build (~0.3 s each). Safe to call every frame.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="radiusTiles">Radius in tiles.</param>
    public void PrewarmHydrology(int worldTileX, int worldTileY, int radiusTiles) =>
        _rivers.Prewarm(worldTileX, worldTileY, radiusTiles);

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

    // Detailed terrain elevation (before river valleys are carved) at a world tile. Rivers are routed on it,
    // so they follow its valleys and converge the way real drainage does.
    private float SampleTerrainElevation(int worldX, int worldY)
    {
        float fx = worldX * DefaultFrequency;
        float fy = worldY * DefaultFrequency;
        float elevation = _elevationWarp.Evaluate(fx, fy, warpStrength: 0.15f, octaves: 4);
        return Math.Clamp(elevation + this.RidgeLift(fx, fy, elevation, octaves: 4), 0f, 1f);
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
            float riverDist = _rivers.GetDistance(worldX, worldY);
            riverDistance = riverDist;

            // Natural inland freshwater lakes and desert oases
            float lakeNoise = _riverNoise.Evaluate((fx * 3.6f) + 120f, (fy * 3.6f) + 120f, octaves: 2);
            bool isLakeOrOasis = false;

            if (temperature > 0.68f && rawMoisture < 0.28f && elevation >= 0.42f && elevation <= 0.62f && lakeNoise > 0.86f)
            {
                // Desert oasis spring in arid dunes
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

            if (riverDist < RiparianReach)
            {
                // Riparian zone: the ground eases down towards the water and gets wetter, with a smooth
                // falloff (no hard edge, no elevation cap), so bank biomes blend instead of forming a trench.
                // Distances are width-scaled, so big rivers get proportionally wider valleys than streams.
                float k = 1f - Math.Clamp((riverDist - 2.2f) / (RiparianReach - 2.2f), 0f, 1f);
                k = k * k * (3f - (2f * k));
                elevation = MathF.Max(elevation - (0.08f * k), MathF.Min(elevation, 0.36f));
                if (riverDist < 3.0f)
                {
                    elevation = MathF.Min(elevation, 0.37f); // narrow sand or gravel bank
                }

                moisture = Math.Clamp(moisture + (0.30f * k), 0f, 1f);
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

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;
}
