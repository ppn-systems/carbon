// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Menu;

/// <summary>Whether a server answered its reachability check.</summary>
internal enum ServerHealth
{
    Checking,
    Online,
    Unreachable,
}

/// <summary>How good the connection to a server looks, as the player reads it.</summary>
internal enum ConnectionQuality
{
    Unknown,
    Excellent,
    Good,
    Fair,
    Poor,
}

/// <summary>
/// What a server card shows (docs/art/menu-ui.md rule 17): a status word, a connection word and 0–4 signal bars. Never
/// an address, a port or milliseconds.
/// </summary>
internal readonly record struct ServerStatus(ServerHealth Health, ConnectionQuality Quality)
{
    /// <summary>A server not checked yet.</summary>
    public static readonly ServerStatus Checking = new(ServerHealth.Checking, ConnectionQuality.Unknown);

    /// <summary>Gets the lit signal bars, 0–4.</summary>
    public int Bars => this.Quality switch
    {
        ConnectionQuality.Excellent => 4,
        ConnectionQuality.Good => 3,
        ConnectionQuality.Fair => 2,
        ConnectionQuality.Poor => 1,
        ConnectionQuality.Unknown => 0,
        _ => 0,
    };

    /// <summary>Gets the localization key of the status word.</summary>
    public string StatusKey => this.Health switch
    {
        ServerHealth.Online => "login.server_status.online",
        ServerHealth.Unreachable => "login.server_status.offline",
        ServerHealth.Checking => "login.server_status.checking",
        _ => "login.server_status.checking",
    };

    /// <summary>Gets the localization key of the connection word, or <see langword="null"/> when there is none.</summary>
    public string? QualityKey => this.Quality switch
    {
        ConnectionQuality.Excellent => "login.server_quality.excellent",
        ConnectionQuality.Good => "login.server_quality.good",
        ConnectionQuality.Fair => "login.server_quality.fair",
        ConnectionQuality.Poor => "login.server_quality.poor",
        ConnectionQuality.Unknown => null,
        _ => null,
    };

    /// <summary>
    /// Classifies a check: no answer is unreachable; otherwise the time to connect sets the quality (under 60 ms
    /// excellent, under 120 ms good, under 250 ms fair, slower poor).
    /// </summary>
    public static ServerStatus FromProbe(TimeSpan? connectTime)
    {
        if (connectTime is not { } time || time < TimeSpan.Zero)
        {
            return new ServerStatus(ServerHealth.Unreachable, ConnectionQuality.Unknown);
        }

        double ms = time.TotalMilliseconds;
        ConnectionQuality quality = ms < 60 ? ConnectionQuality.Excellent
            : ms < 120 ? ConnectionQuality.Good
            : ms < 250 ? ConnectionQuality.Fair
            : ConnectionQuality.Poor;
        return new ServerStatus(ServerHealth.Online, quality);
    }
}
