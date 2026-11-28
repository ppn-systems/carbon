// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Microsoft.Extensions.Logging;

namespace Carbon.Host;

/// <summary>How a tick run ended; the value is the host's process exit code.</summary>
internal enum WorldTickRunOutcome
{
    /// <summary>The host asked the run to stop (orderly shutdown).</summary>
    Stopped = 0,

    /// <summary>A tick threw: the run stopped the host so its supervisor restarts it (audit TS-5).</summary>
    Faulted = 1,
}

/// <summary>
/// Drives the authoritative fixed-tick simulation loop and periodic write-behind database flushes.
/// </summary>
internal sealed class WorldTickHostedRunner
{
    private readonly IWorldSimulationLoop _world;
    private readonly ILogger<WorldTickHostedRunner> _logger;
    private readonly Action _stopHost;
    private readonly TimeSpan _tickInterval;
    private readonly int _flushEveryTicks;

    private static readonly TimeSpan s_shutdownBuildTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldTickHostedRunner"/> class.
    /// </summary>
    /// <param name="world">The world to tick.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="stopHost">
    /// Stops the host (its network app) when a tick throws, so no player is accepted into a world that no longer ticks.
    /// </param>
    public WorldTickHostedRunner(IWorldSimulationLoop world, ILogger<WorldTickHostedRunner> logger, Action stopHost)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stopHost = stopHost ?? throw new ArgumentNullException(nameof(stopHost));
        _tickInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1, (int)world.TickRate));
        _flushEveryTicks = Math.Max(1, world.TickRate * 10);
    }

    /// <summary>
    /// Runs fixed simulation ticks until <paramref name="ct"/> is cancelled, then flushes dirty state. A tick that throws
    /// ends the run: it is logged with the tick number, the host is stopped, and the same shutdown work runs (pending
    /// commands answered, a best-effort final flush, background builds stopped). The world is never ticked again after
    /// a failed tick, because its state may be half-updated.
    /// </summary>
    /// <returns><see cref="WorldTickRunOutcome.Faulted"/> after a failed tick, else <see cref="WorldTickRunOutcome.Stopped"/>.</returns>
    public async Task<WorldTickRunOutcome> RunAsync(CancellationToken ct)
    {
        WorldTickRunOutcome outcome = WorldTickRunOutcome.Stopped;
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
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            outcome = WorldTickRunOutcome.Faulted;
            if (_logger.IsEnabled(LogLevel.Critical))
            {
                _logger.LogCritical(
                    ex,
                    "World tick {Tick} failed; stopping the host so its supervisor restarts it. Dirty state is flushed first, best effort.",
                    _world.CurrentTick);
            }

            _stopHost();
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

        return outcome;
    }
}
