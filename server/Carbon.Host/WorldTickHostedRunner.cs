// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Microsoft.Extensions.Logging;

namespace Carbon.Host;

/// <summary>
/// Drives the authoritative fixed-tick simulation loop and periodic write-behind database flushes.
/// </summary>
internal sealed class WorldTickHostedRunner
{
    private readonly IAuthoritativeWorld _world;
    private readonly ILogger<WorldTickHostedRunner> _logger;
    private readonly TimeSpan _tickInterval;
    private readonly int _flushEveryTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldTickHostedRunner"/> class.
    /// </summary>
    public WorldTickHostedRunner(IAuthoritativeWorld world, ILogger<WorldTickHostedRunner> logger)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tickInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1, (int)world.TickRate));
        _flushEveryTicks = Math.Max(1, world.TickRate * 10);
    }

    /// <summary>Runs fixed simulation ticks until <paramref name="ct"/> is cancelled, then flushes dirty state.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        PeriodicTimer timer = new(_tickInterval);
        using PeriodicTimer timerScope = timer;
        int ticksSinceFlush = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                _world.StepFixedTick();
                ticksSinceFlush++;

                if (ticksSinceFlush >= _flushEveryTicks)
                {
                    ticksSinceFlush = 0;
                    await _world.FlushDirtyPersistenceAsync(ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on orderly shutdown.
        }
        finally
        {
            try
            {
                await _world.FlushDirtyPersistenceAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Final persistence flush failed during host shutdown.");
            }
        }
    }
}
