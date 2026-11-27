// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Roads;
using Carbon.Maps.Streaming;

namespace Carbon.Maps.Generators;

/// <summary>
/// High-performance deterministic procedural world generator.
/// Synthesizes continuous multi-layered coherent noise fields (Elevation, Temperature, Moisture, Hydrology)
/// with domain warping to produce massive macro-continental 10-biome landscapes.
/// </summary>
public sealed class ProceduralWorldGenerator : ITileSource
{
    /// <summary>
    /// Version of the generation algorithm, sent to clients in <c>CharacterEnterWorld.GeneratorVersion</c>.
    /// Bump it whenever any change alters generated output (the golden-hash test fails), so a client running
    /// a different generator refuses to play instead of silently desyncing.
    /// </summary>
    public const ushort GeneratorVersion = 21;

    /// <summary>Width-scaled distance over which rivers and lakes lower and moisten the land around them.</summary>
    public const float RiparianReach = WorldConstants.RiparianReach;


    /// <summary>
    /// Default macro-scale spatial frequency producing expansive continental biomes (wavelength ~1250 tiles).
    /// </summary>
    public const float DefaultFrequency = 0.0008f;

    /// <summary>The shipped catalog, for generators built without one (tools and tests).</summary>
    private static readonly Lazy<StructureCatalog> s_shippedStructures = new(StructureCatalog.LoadEmbedded);

    [ThreadStatic]
    private static TileSample[]? s_chunkSamples;

    private readonly ulong _seed;
    private readonly RiverNetwork _rivers;
    private readonly PointOfInterestResolver _landmarks;
    private readonly RoadNetwork _roads;
    private readonly Lazy<(int X, int Y)> _spawn;
    private readonly ITileStage[] _tileStages;
    private readonly IChunkStage[] _chunkStages;

    /// <summary>
    /// Gets the deterministic generation seed.
    /// </summary>
    public ulong Seed => _seed;

    /// <summary>Gets the world settings (seed, frequency, tile size).</summary>
    public WorldGenSettings Settings { get; }

    /// <summary>
    /// Gets the building catalog (<c>structures.json</c>) village planning sizes furniture with. The server passes the one it
    /// loaded and the client its synced copy, so both lay villages out identically.
    /// </summary>
    public StructureCatalog Structures { get; }

    /// <summary>Gets the resolver that places villages, harbours, camps and shrines.</summary>
    public PointOfInterestResolver Landmarks => _landmarks;

    /// <summary>Gets the road network linking settlements.</summary>
    public RoadNetwork Roads => _roads;

    /// <summary>Gets the world spawn tile: the nearest dry LushPlains tile to the origin.</summary>
    public (int X, int Y) SpawnTile => _spawn.Value;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProceduralWorldGenerator"/> class with the standard
    /// pipeline: Elevation → Climate → Hydrology → Biome per tile, then Landmarks → Roads → Props → Spawn clearing
    /// per chunk.
    /// </summary>
    /// <param name="seed">64-bit deterministic world seed (default frequency and tile size).</param>
    /// <param name="extraTileStages">Optional tile stages run after the biome stage (e.g. local overrides).</param>
    /// <param name="extraChunkStages">Optional chunk stages run after props (e.g. caves, roads). Give each its own
    /// seed with <see cref="StageSeed.Derive"/> so it never disturbs the existing stages.</param>
    /// <param name="structures">Building catalog for village planning; the shipped one when <see langword="null"/>.</param>
    public ProceduralWorldGenerator(
        ulong seed,
        IReadOnlyList<ITileStage>? extraTileStages = null,
        IReadOnlyList<IChunkStage>? extraChunkStages = null,
        StructureCatalog? structures = null)
        : this(new WorldGenSettings(seed), extraTileStages, extraChunkStages, structures)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProceduralWorldGenerator"/> class from world settings
    /// (as sent to clients in <c>CharacterEnterWorld</c>).
    /// </summary>
    /// <param name="settings">World settings.</param>
    /// <param name="extraTileStages">Optional tile stages run after the biome stage.</param>
    /// <param name="extraChunkStages">Optional chunk stages run after props.</param>
    /// <param name="structures">Building catalog for village planning; the shipped one when <see langword="null"/>.</param>
    public ProceduralWorldGenerator(
        WorldGenSettings settings,
        IReadOnlyList<ITileStage>? extraTileStages = null,
        IReadOnlyList<IChunkStage>? extraChunkStages = null,
        StructureCatalog? structures = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        this.Settings = settings;
        this.Structures = structures ?? s_shippedStructures.Value;
        ulong seed = settings.Seed;
        _seed = seed;
        ElevationStage elevation = new(seed, settings.Frequency);
        _rivers = new RiverNetwork(seed, elevation);
        _landmarks = new PointOfInterestResolver(seed, this, _rivers, this.Structures);
        _roads = new RoadNetwork(StageSeed.Derive(seed, "roads"), _landmarks, this, _rivers);
        _landmarks.Roads = _roads;
        _spawn = new Lazy<(int X, int Y)>(() => SpawnLocator.Locate(this, settings.Frequency));

        List<ITileStage> tileStages = [elevation, new ClimateStage(seed), new HydrologyStage(seed, _rivers), new BiomeStage(seed)];
        if (extraTileStages != null)
        {
            tileStages.AddRange(extraTileStages);
        }

        List<IChunkStage> chunkStages =
        [
            new LandmarkStage(_landmarks),
            new RoadStage(_roads),
            new PropStage(seed, _landmarks),
            new SpawnClearingStage(_spawn),
        ];
        if (extraChunkStages != null)
        {
            chunkStages.AddRange(extraChunkStages);
        }

        _tileStages = [.. tileStages];
        _chunkStages = [.. chunkStages];
    }

    /// <summary>
    /// Generates a complete continuous world tilemap of the given dimensions.
    /// </summary>
    /// <param name="width">Map width in tiles.</param>
    /// <param name="height">Map height in tiles.</param>
    /// <param name="frequency">Spatial frequency scale of terrain features (default: the world settings).</param>
    /// <returns>Populated <see cref="TilemapData"/>.</returns>
    public TilemapData GenerateTilemap(int width, int height, float? frequency = null)
    {
        TilemapData map = new(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                MapTile tile = this.SampleTile(x, y, frequency ?? this.Settings.Frequency);
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
    /// <param name="frequency">Spatial frequency scale of terrain features (default: the world settings).</param>
    /// <param name="tileSize">Physical dimension of each tile in world units.</param>
    /// <returns>Populated <see cref="Chunk2D"/>.</returns>
    public Chunk2D GenerateChunk(
        int chunkX,
        int chunkY,
        float? frequency = null,
        float? tileSize = null)
    {
        Chunk2D chunk = new(chunkX, chunkY);
        this.PopulateChunk(chunk, chunkX, chunkY, frequency ?? this.Settings.Frequency, tileSize ?? this.Settings.TileSize);
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
        float? frequency = null,
        float? tileSize = null)
    {
        ArgumentNullException.ThrowIfNull(chunk, nameof(chunk));

        chunk.Reset(chunkX, chunkY);
        float freq = frequency ?? this.Settings.Frequency;
        float size = tileSize ?? this.Settings.TileSize;

        // Terrain first, for the whole chunk, then the chunk stages decorate it from the same samples.
        TileSample[] samples = s_chunkSamples ??= new TileSample[Chunk2D.Size * Chunk2D.Size];
        int startX = chunkX * Chunk2D.Size;
        int startY = chunkY * Chunk2D.Size;
        for (int localY = 0; localY < Chunk2D.Size; localY++)
        {
            for (int localX = 0; localX < Chunk2D.Size; localX++)
            {
                ref TileSample sample = ref samples[(localY * Chunk2D.Size) + localX];
                this.Synthesize(ref sample, startX + localX, startY + localY, freq);
                chunk.SetTile(localX, localY, sample.Tile);
            }
        }

        ChunkContext context = new(chunkX, chunkY, size, samples);
        for (int i = 0; i < _chunkStages.Length; i++)
        {
            _chunkStages[i].Populate(chunk, context);
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

    /// <summary>Gets the river network (tests: region builds and cache state).</summary>
    internal RiverNetwork Rivers => _rivers;

    /// <summary>
    /// Builds the river regions around a point on the thread pool ahead of time, so streaming chunks in
    /// does not stall on a region build (~0.3 s each). Safe to call every frame.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="radiusTiles">Radius in tiles.</param>
    /// <param name="stream">Who is streaming (one per player on the server); its regions stay cached while it is near them.</param>
    public void PrewarmHydrology(int worldTileX, int worldTileY, int radiusTiles, ulong stream = 0) =>
        _rivers.Prewarm(worldTileX, worldTileY, radiusTiles, stream);

    /// <summary>Gets how road sites are planned ahead of a streaming player (ADR-045 D1, D5).</summary>
    public RoadPrewarmOptions RoadPrewarm
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
            field = value;
        }
    } = RoadPrewarmOptions.Default;

    /// <summary>
    /// Plans, in the background, the road sites the chunks of a tile rectangle need (ADR-045 D1), with at most
    /// <see cref="RoadPrewarmOptions.Workers"/> builds at once; the river regions their routes cross are built there too.
    /// </summary>
    /// <param name="minTileX">Rectangle left, in world tiles.</param>
    /// <param name="minTileY">Rectangle top, in world tiles.</param>
    /// <param name="maxTileX">Rectangle right, in world tiles.</param>
    /// <param name="maxTileY">Rectangle bottom, in world tiles.</param>
    /// <param name="stream">Who is streaming (one per player on the server).</param>
    public void PrewarmRoads(int minTileX, int minTileY, int maxTileX, int maxTileY, ulong stream = 0) =>
        _roads.Prewarm(minTileX, minTileY, maxTileX, maxTileY, stream, this.RoadPrewarm.Workers);

    /// <summary>Releases what <paramref name="stream"/> kept cached or wanted planned (the player left).</summary>
    /// <param name="stream">The stream passed to <see cref="PrewarmHydrology"/> and <see cref="PrewarmRoads"/>.</param>
    public void ReleaseStream(ulong stream)
    {
        _rivers.ReleaseStream(stream);
        _roads.ReleaseStream(stream);
    }

    /// <summary>Stops the generator's background builds, roads and rivers, on shutdown (ADR-045 D10).</summary>
    /// <param name="timeout">How long to wait for running builds, for each network.</param>
    /// <returns><see langword="true"/> if every build finished within <paramref name="timeout"/>.</returns>
    public bool StopBackgroundBuilds(TimeSpan timeout)
    {
        // Roads first: a road build reads river regions, so the river builds it waits on must still run.
        bool roads = _roads.StopBackgroundBuilds(timeout);
        return _rivers.StopBackgroundBuilds(timeout) && roads;
    }

    /// <summary>
    /// Samples a single map tile at world tile coordinates with continuous multi-scale coherent noise.
    /// </summary>
    /// <param name="worldX">World X tile coordinate.</param>
    /// <param name="worldY">World Y tile coordinate.</param>
    /// <param name="frequency">Spatial frequency scale.</param>
    /// <returns>Evaluated <see cref="MapTile"/>.</returns>
    public MapTile SampleTile(int worldX, int worldY, float? frequency = null)
    {
        TileSample sample = default;
        this.Synthesize(ref sample, worldX, worldY, frequency ?? this.Settings.Frequency);
        return sample.Tile;
    }

    /// <inheritdoc/>
    MapTile ITileSource.SampleTile(int worldX, int worldY) => this.SampleTile(worldX, worldY);

    private void Synthesize(ref TileSample sample, int worldX, int worldY, float frequency)
    {
        sample = default;
        sample.WorldX = worldX;
        sample.WorldY = worldY;
        sample.Fx = worldX * frequency;
        sample.Fy = worldY * frequency;
        for (int i = 0; i < _tileStages.Length; i++)
        {
            if (!_tileStages[i].Apply(ref sample))
            {
                break;
            }
        }
    }
}
