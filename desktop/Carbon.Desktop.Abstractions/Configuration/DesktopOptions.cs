// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.IO;
using Carbon.Maps.Biomes;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// Immutable startup configuration for the modular desktop client, loaded cleanly from environment variables.
/// </summary>
public sealed class DesktopOptions
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

    /// <summary>
    /// Gets or sets an optional biome to frame on start (<c>CARBON_FOCUS_BIOME</c>): the camera starts on the
    /// nearest representative tile of it (see <c>BiomeLocator</c>). An explicit focus tile wins.
    /// </summary>
    public BiomeType? FocusBiome { get; init; }

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

    /// <summary>Gets or sets a player panel tab to open once in the world (<c>CARBON_OPEN_PANEL=character</c>), for screenshots.</summary>
    public string? OpenPanel { get; init; }

    /// <summary>Gets or sets a fixed mouse position in window pixels (<c>CARBON_CAPTURE_MOUSE=x,y</c>), for screenshots of hover states.</summary>
    public (float X, float Y)? CaptureMouse { get; init; }

    /// <summary>
    /// Gets or sets how many in-world frames to sample for the frame-stats log (<c>CARBON_FRAME_STATS=n</c>, 0 = off).
    /// When set, <see cref="AutoExit"/> also waits for these frames.
    /// </summary>
    public int FrameStatsFrames { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the scene opens the nearest trader's window once in the world
    /// (<c>CARBON_OPEN_TRADER=1</c>; screenshot tool). The capture waits for the trader's stock to arrive.
    /// </summary>
    public bool OpenTrader { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the trader window opened by <see cref="OpenTrader"/> starts in the
    /// blacksmith's repair mode (<c>CARBON_OPEN_TRADER=repair</c>; screenshot tool).
    /// </summary>
    public bool OpenTraderRepair { get; init; }

    /// <summary>
    /// Gets or sets the forge animation to show for screenshot review (<c>CARBON_FORGE_PREVIEW</c>, e.g.
    /// <c>success:0.35</c>): <c>charge</c>, <c>success</c>, <c>nochange</c>, <c>downgrade</c> or <c>destroyed</c>, with an
    /// optional frozen time in seconds. It opens the forge window and plays the effect locally; nothing is sent to the
    /// server, which alone rolls real outcomes (ADR-024).
    /// </summary>
    public string? ForgePreview { get; init; }

    /// <summary>
    /// Gets or sets an optional constant walk direction applied instead of keyboard movement
    /// (<c>CARBON_AUTO_WALK=x,y</c>; used by the screenshot tool to capture actors in motion).
    /// </summary>
    public (float X, float Y)? AutoWalk { get; init; }

    /// <summary>
    /// Gets or sets a HUD window to open before the automatic capture (<c>CARBON_CAPTURE_MODAL=settings|controls|inventory|character|skills|crafting</c>;
    /// used by the screenshot tool to review localized UI, ADR-023 D8). Empty when unset.
    /// </summary>
    public string CaptureModal { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets a menu scene to capture instead of the world (<c>CARBON_CAPTURE_SCENE=login|characters</c>, ADR-023 D8).
    /// <c>login</c> needs <c>CARBON_AUTO_LOGIN=0</c>; <c>characters</c> registers the configured account and captures the
    /// character screen. Empty when unset.
    /// </summary>
    public string CaptureScene { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional UI state the screenshot tool opens on start (<c>CARBON_UI_PREVIEW</c>):
    /// <c>inventory</c>, or <c>inventory-tooltip:N</c>, <c>inventory-split:N</c>, <c>inventory-destroy:N</c> for slot
    /// <c>N</c>. Purely visual; nothing is sent to the server.
    /// </summary>
    public string? UiPreview { get; init; }

    /// <summary>Gets or sets the remote dedicated server host or IP address.</summary>
    public string ServerHost { get; init; } = "127.0.0.1";

    /// <summary>Gets or sets the remote dedicated server TCP port.</summary>
    public ushort ServerTcpPort { get; init; } = 57207;

    /// <summary>Gets or sets a value indicating whether to skip interactive menus and sign in automatically using configured credentials.</summary>
    public bool AutoLogin { get; init; }

    /// <summary>Gets or sets the optional pinned X25519 server public key hex string.</summary>
    public string? ServerPublicKeyHex { get; init; }

    /// <summary>Gets or sets the account username used in online multiplayer mode.</summary>
    public string Username { get; init; } = "adventurer";

    /// <summary>Gets or sets the account password used in online multiplayer mode.</summary>
    public string Password { get; init; } = "carbon123";

    /// <summary>Gets or sets the preferred character display name when creating a starter character.</summary>
    public string CharacterName { get; init; } = "Hero";

    /// <summary>
    /// Gets or sets the directory where server-sent gameplay config documents are cached (ADR-014).
    /// Cached files are re-hashed against the server manifest before use, so a stale or corrupt file is only a cache miss.
    /// </summary>
    public string ConfigCacheDirectory { get; init; } = DefaultConfigCacheDirectory();

    /// <summary>
    /// Parses desktop client options from standard <c>CARBON_*</c> environment variables.
    /// </summary>
    /// <param name="readEnv">Optional environment reader (defaults to <see cref="Environment.GetEnvironmentVariable(string)"/>).</param>
    /// <returns>Configured <see cref="DesktopOptions"/>.</returns>
    public static DesktopOptions FromEnvironment(Func<string, string?>? readEnv = null)
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

        BiomeType? focusBiome = Enum.TryParse(get("CARBON_FOCUS_BIOME"), ignoreCase: true, out BiomeType parsedBiome) &&
            Enum.IsDefined(parsedBiome)
            ? parsedBiome
            : null;

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


        int frameStatsFrames = int.TryParse(get("CARBON_FRAME_STATS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedStats)
            ? Math.Clamp(parsedStats, 0, 100_000)
            : 0;


        string serverHost = get("CARBON_SERVER_HOST") is { Length: > 0 } host ? host.Trim() : "127.0.0.1";
        ushort serverTcpPort = ushort.TryParse(get("CARBON_TCP_PORT") ?? get("CARBON_SERVER_PORT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort parsedPort) && parsedPort > 0
            ? parsedPort
            : (ushort)57207;

        if (get("CARBON_SERVER_URL") is { Length: > 0 } rawUrl &&
            Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out Uri? parsedUri))
        {
            if (!string.IsNullOrWhiteSpace(parsedUri.Host))
            {
                serverHost = parsedUri.Host;
            }

            if (parsedUri.Port > 0)
            {
                serverTcpPort = (ushort)parsedUri.Port;
            }
        }

        string? serverKey = get("CARBON_SERVER_PUBLIC_KEY") is { Length: > 0 } key ? key.Trim() : null;
        string username = get("CARBON_USERNAME") is { Length: > 0 } user ? user.Trim() : "adventurer";
        string password = get("CARBON_PASSWORD") is { Length: > 0 } pass ? pass : "carbon123";
        string charName = get("CARBON_CHARACTER") is { Length: > 0 } ch ? ch.Trim() : "Hero";
        string configCache = get("CARBON_CONFIG_CACHE") is { Length: > 0 } cache ? cache.Trim() : DefaultConfigCacheDirectory();

        return new DesktopOptions
        {
            Seed = seed,
            FocusTileX = focusX,
            FocusTileY = focusY,
            FocusBiome = focusBiome,
            MacroView = string.Equals(get("CARBON_MACRO_VIEW"), "1", StringComparison.Ordinal),
            StartHour = startHour,
            DayLengthSeconds = dayLength,
            WeatherRainOverride = rain,
            TimeScale = timeScale,
            ScreenshotPath = screenshotPath,
            CaptureFrames = captureFrames,
            FrameStatsFrames = frameStatsFrames,
            AutoExit = string.Equals(get("CARBON_AUTO_EXIT"), "1", StringComparison.Ordinal),
            OpenTrader = get("CARBON_OPEN_TRADER") is "1" or "repair",
            OpenTraderRepair = string.Equals(get("CARBON_OPEN_TRADER"), "repair", StringComparison.Ordinal),
            AutoWalk = ParsePoint(get("CARBON_AUTO_WALK")),
            OpenPanel = get("CARBON_OPEN_PANEL") is { Length: > 0 } panel ? panel.Trim() : null,
            CaptureMouse = ParsePoint(get("CARBON_CAPTURE_MOUSE")),
            CaptureModal = get("CARBON_CAPTURE_MODAL") is { Length: > 0 } modal ? modal.Trim().ToLowerInvariant() : string.Empty,
            CaptureScene = get("CARBON_CAPTURE_SCENE") is { Length: > 0 } captureScene ? captureScene.Trim().ToLowerInvariant() : string.Empty,
            ForgePreview = get("CARBON_FORGE_PREVIEW") is { Length: > 0 } forgePreview ? forgePreview.Trim() : null,
            UiPreview = get("CARBON_UI_PREVIEW") is { Length: > 0 } preview ? preview.Trim() : null,
            ServerHost = serverHost,
            ServerTcpPort = serverTcpPort,
            AutoLogin = string.Equals(get("CARBON_AUTO_LOGIN"), "1", StringComparison.Ordinal),
            ServerPublicKeyHex = serverKey,
            Username = username,
            Password = password,
            CharacterName = charName,
            ConfigCacheDirectory = configCache,
        };
    }

    private static string DefaultConfigCacheDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Carbon", "config-cache");

    /// <summary>Parses an <c>x,y</c> pair (invariant culture), or returns <see langword="null"/> when unset or malformed.</summary>
    private static (float X, float Y)? ParsePoint(string? value) =>
        value is { Length: > 0 } &&
        value.Split(',') is [string x, string y] &&
        float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float px) &&
        float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float py)
            ? (px, py)
            : null;
}
