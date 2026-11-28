// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Host;

/// <summary>
/// Runs the network app and the world's tick loop together and turns how they ended into the process exit code
/// (audit TS-5): a failed tick stops the app, and the host exits non-zero so its supervisor restarts it.
/// </summary>
internal static class HostLifetime
{
    /// <summary>
    /// Starts <paramref name="tickRunner"/>, runs <paramref name="runApp"/> until <paramref name="stop"/> is cancelled
    /// (Ctrl+C, or the runner after a failed tick), then stops the ticks and waits for the runner's shutdown work.
    /// </summary>
    /// <param name="runApp">Runs the network app until the token is cancelled.</param>
    /// <param name="tickRunner">The world's tick runner, built with <c>stop.Cancel</c> as its stop callback.</param>
    /// <param name="stop">Stops both.</param>
    /// <returns>The process exit code: 0 after an orderly stop, 1 after a failed tick.</returns>
    public static async Task<int> RunAsync(Func<CancellationToken, Task> runApp, WorldTickHostedRunner tickRunner, CancellationTokenSource stop)
    {
        ArgumentNullException.ThrowIfNull(runApp);
        ArgumentNullException.ThrowIfNull(tickRunner);
        ArgumentNullException.ThrowIfNull(stop);
        Task<WorldTickRunOutcome> ticks = tickRunner.RunAsync(stop.Token);
        await runApp(stop.Token).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        return (int)await ticks.ConfigureAwait(false);
    }
}
