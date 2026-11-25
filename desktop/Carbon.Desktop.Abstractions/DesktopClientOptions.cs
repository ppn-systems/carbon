// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.IO;

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Immutable startup configuration for the modular desktop client, loaded cleanly from environment variables.
/// </summary>
public sealed class DesktopClientOptions
{
    /// <summary>Gets or sets the offline deterministic world generation seed.</summary>
    public ulong Seed { get; init; } = 1337UL;

    /// <summary>Gets or sets the macro terrain noise frequency.</summary>
    public float MacroFrequency { get; init; } = 0.0008f;

    /// <summary>Gets or sets the physical tile dimension in world units.</summary>
    public float TileSize { get; init; } = 32f;

    /// <summary>Gets or sets the optional starting world tile X override.</summary>
    public int? FocusTileX { get; init; }

    /// <summary>Gets or sets the optional starting world tile Y override.</summary>
    public int? FocusTileY { get; init; }

    /// <summary>Gets or sets a value indicating whether to start zoomed out for macro inspection.</summary>
    public bool MacroView { get; init; }

    /// <summary>Gets or sets the initial in-game time of day hour [0..24).</summary>
    public float StartHour { get; init; } = 9f;

    /// <summary>Gets or sets the real seconds per full in-game day cycle.</summary>
    public float DayLengthSeconds { get; init; } = 600f;

    /// <summary>Gets or sets the optional atmosphere rain override (<c>rain</c> / <c>clear</c>).</summary>
    public bool? WeatherRainOverride { get; init; }

    /// <summary>Gets or sets the atmospheric time acceleration multiplier.</summary>
    public float TimeScale { get; init; } = 1f;

    /// <summary>Gets or sets the automated screenshot destination path.</summary>
    public string ScreenshotPath { get; init; } = Path.Combine(Environment.CurrentDirectory, "screenshots", "world_capture.bmp");

    /// <summary>Gets or sets the count of sequential animation frames to capture after frame 20.</summary>
    public int CaptureFrames { get; init; }

    /// <summary>Gets or sets a value indicating whether the runner closes automatically after capture.</summary>
    public bool AutoExit { get; init; }

    /// <summary>Gets or sets the remote Nalix WebSocket endpoint (<c>ws://...</c>), or <see langword="null"/> for offline mode.</summary>
    public string? ServerUrl { get; init; }

    /// <summary>Gets a value indicating whether multiplayer mode is configured via <see cref="ServerUrl"/>.</summary>
    public bool IsMultiplayer => !string.IsNullOrWhiteSpace(this.ServerUrl);

    /// <summary>Gets or sets the optional pinned X25519 server public key hex string.</summary>
    public string? ServerPublicKeyHex { get; init; }

    /// <summary>Gets or sets the account username used in online multiplayer mode.</summary>
    public string Username { get; init; } = "adventurer";

    /// <summary>Gets or sets the account password used in online multiplayer mode.</summary>
    public string Password { get; init; } = "carbon123";

    /// <summary>Gets or sets the preferred character display name when creating a starter character.</summary>
    public string CharacterName { get; init; } = "Hero";

    /// <summary>
    /// Parses desktop client options from standard <c>CARBON_*</c> environment variables.
    /// </summary>
    /// <param name="readEnv">Optional environment reader (defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>).</param>
    /// <returns>Configured <see cref="DesktopClientOptions"/>.</returns>
    public static DesktopClientOptions FromEnvironment(Func<string, string?>? readEnv = null)
    {
        Func<string, string?> get = readEnv ?? Environment.GetEnvironmentVariable;

        ulong seed = ulong.TryParse(get("CARBON_SEED"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedSeed)
            ? parsedSeed
            : 1337UL;

        int? focusX = null;
        int? focusY = null;
        if (get("CARBON_FOCUS_TILE") is { Length: > 0 } focus &&
            focus.Split(',') is [string fx, string fy] &&
            int.TryParse(fx, NumberStyles.Integer, CultureInfo.InvariantCulture, out int px) &&
            int.TryParse(fy, NumberStyles.Integer, CultureInfo.InvariantCulture, out int py))
        {
            focusX = px;
            focusY = py;
        }

        float startHour = float.TryParse(get("CARBON_TIME_OF_DAY"), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedHour)
            ? parsedHour
            : 9f;

        float dayLength = float.TryParse(get("CARBON_DAY_LENGTH"), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedLen)
            ? parsedLen
            : 600f;

        bool? rain = get("CARBON_WEATHER") switch
        {
            "rain" => true,
            "clear" => false,
            _ => null,
        };

        float timeScale = float.TryParse(get("CARBON_TIME_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedScale)
            ? Math.Clamp(parsedScale, 0.1f, 1000f)
            : 1f;

        string screenshotPath = get("CARBON_SCREENSHOT_PATH") is { Length: > 0 } customPath
            ? customPath
            : Path.Combine(Environment.CurrentDirectory, "screenshots", "world_capture.bmp");

        int captureFrames = int.TryParse(get("CARBON_CAPTURE_FRAMES"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedFrames)
            ? Math.Clamp(parsedFrames, 0, 240)
            : 0;

        string? serverUrl = get("CARBON_SERVER_URL") is { Length: > 0 } url ? url.Trim() : null;
        string? serverKey = get("CARBON_SERVER_PUBLIC_KEY") is { Length: > 0 } key ? key.Trim() : null;
        string username = get("CARBON_USERNAME") is { Length: > 0 } user ? user.Trim() : "adventurer";
        string password = get("CARBON_PASSWORD") is { Length: > 0 } pass ? pass : "carbon123";
        string charName = get("CARBON_CHARACTER") is { Length: > 0 } ch ? ch.Trim() : "Hero";

        return new DesktopClientOptions
        {
            Seed = seed,
            FocusTileX = focusX,
            FocusTileY = focusY,
            MacroView = string.Equals(get("CARBON_MACRO_VIEW"), "1", StringComparison.Ordinal),
            StartHour = startHour,
            DayLengthSeconds = dayLength,
            WeatherRainOverride = rain,
            TimeScale = timeScale,
            ScreenshotPath = screenshotPath,
            CaptureFrames = captureFrames,
            AutoExit = string.Equals(get("CARBON_AUTO_EXIT"), "1", StringComparison.Ordinal),
            ServerUrl = serverUrl,
            ServerPublicKeyHex = serverKey,
            Username = username,
            Password = password,
            CharacterName = charName,
        };
    }
}
