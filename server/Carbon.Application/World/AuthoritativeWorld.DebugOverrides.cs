// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using Carbon.Abstractions.Configuration;
using Carbon.Maps.Biomes;
using Carbon.Maps.Generators;

namespace Carbon.Application.World;

/// <content>
/// Developer-only overrides for the screenshot review tool: a forced spawn tile and a start hour. They come
/// from the host configuration (Debug builds only), never from a client packet.
/// </content>
public sealed partial class AuthoritativeWorld
{
    private const int DebugBiomeSearchRadius = 4000;

    // Where every character enters the world while a debug spawn override is active.
    private Vector2? _debugSpawnPosition;

    /// <summary>Gets the forced debug spawn position, if a debug override set one.</summary>
    public Vector2? DebugSpawnPosition
    {
        get
        {
            lock (_gate)
            {
                return _debugSpawnPosition;
            }
        }
    }

    /// <summary>
    /// Applies developer-only overrides: resolves the debug spawn tile (explicit tile, else the nearest
    /// representative tile of a biome, else the world spawn, plus an offset) and winds the world clock to a
    /// start hour. Call once at boot, before players connect.
    /// </summary>
    /// <param name="overrides">The overrides read from the host configuration.</param>
    /// <returns>A one-line description of what was applied, for the boot log.</returns>
    /// <exception cref="ArgumentException">The biome name is unknown or no tile of it was found.</exception>
    public string ApplyDebugOverrides(WorldDebugOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        if (!overrides.IsActive)
        {
            return "none";
        }

        lock (_gate)
        {
            (int X, int Y) spawn = _chunkWorld.SpawnTile;
            if (overrides.SpawnTile is { } tile)
            {
                spawn = tile;
            }
            else if (overrides.SpawnBiome is { } biomeName)
            {
                if (!Enum.TryParse(biomeName, ignoreCase: true, out BiomeType biome) || !Enum.IsDefined(biome))
                {
                    throw new ArgumentException($"Unknown biome '{biomeName}'.", nameof(overrides));
                }

                if (!BiomeLocator.TryFind(_generator, biome, spawn.X, spawn.Y, DebugBiomeSearchRadius, out int bx, out int by))
                {
                    throw new ArgumentException($"No {biome} tile within {DebugBiomeSearchRadius} tiles of spawn.", nameof(overrides));
                }

                spawn = (bx, by);
            }

            spawn = (spawn.X + overrides.SpawnOffset.X, spawn.Y + overrides.SpawnOffset.Y);
            bool movesSpawn = overrides.SpawnTile.HasValue || overrides.SpawnBiome is not null || overrides.SpawnOffset != default;
            if (movesSpawn)
            {
                _debugSpawnPosition = new Vector2((spawn.X + 0.5f) * this.TileSize, (spawn.Y + 0.5f) * this.TileSize);
            }

            if (overrides.StartHour is float hour)
            {
                // The clock is a pure function of the tick (WorldProgression), so the start hour is a tick offset.
                float hoursAhead = (((hour - WorldProgression.DefaultStartHour) % 24f) + 24f) % 24f;
                double seconds = hoursAhead / 24.0 * WorldProgression.DefaultDayLengthSeconds;
                _currentTick = (ulong)Math.Round(seconds * this.TickRate);
            }

            string spawnText = movesSpawn ? $"{spawn.X},{spawn.Y}" : "default";
            string hourText = overrides.StartHour?.ToString("0.##", CultureInfo.InvariantCulture) ?? "default";
            return $"spawn={spawnText}, startHour={hourText}";
        }
    }
}
