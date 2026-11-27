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
    private readonly IWorldSimulationLoop _world;
    private readonly ILogger<WorldTickHostedRunner> _logger;
    private readonly TimeSpan _tickInterval;
    private readonly int _flushEveryTicks;

    private static readonly TimeSpan s_shutdownBuildTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldTickHostedRunner"/> class.
    /// </summary>
    public WorldTickHostedRunner(IWorldSimulationLoop world, ILogger<WorldTickHostedRunner> logger)
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
                    try
                    {
                        await _world.FlushDirtyPersistenceAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        _logger.LogError(ex, "Periodic persistence flush failed; will retry on next interval.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on orderly shutdown.
        }
        finally
        {
            // No more ticks will run: answer queued handler commands instead of leaving them waiting (ADR-017 D4).
            _world.FailPendingCommands();
            try
            {
                await _world.FlushDirtyPersistenceAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Final persistence flush failed during host shutdown.");
            }

            // ADR-045 D10: no tick will need a region or road again, so the generator's background builds stop.
            if (!_world.StopBackgroundBuilds(s_shutdownBuildTimeout))
            {
                _logger.LogWarning("World generation builds were still running {Timeout} after shutdown.", s_shutdownBuildTimeout);
            }
        }
    }
}
