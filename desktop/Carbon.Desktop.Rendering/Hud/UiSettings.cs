// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.PostProcessing;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Persisted per-user client preferences (language, HUD visibility, camera zoom) stored AOT-safely as
/// <c>settings.ini</c> in the user's local config directory (ADR-023 D3):
/// <code>
/// [general]
/// language = vi
///
/// [display]
/// show_minimap = true
/// camera_zoom = 1
/// graphics_quality = auto
/// </code>
/// <c>CARBON_LANG</c> overrides <c>[general] language</c> at startup. A legacy <c>ui_settings.cfg</c> is read once when
/// <c>settings.ini</c> does not exist yet.
/// </summary>
public sealed class UiSettings
{
    private static readonly string s_configDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Carbon");

    private static readonly string s_settingsPath = Path.Combine(s_configDirectory, "settings.ini");
    private static readonly string s_legacySettingsPath = Path.Combine(s_configDirectory, "ui_settings.cfg");

    /// <summary>Gets or sets whether the minimap under the HUD clock is visible (on by default; <c>[M]</c> toggles it).</summary>
    public bool ShowMinimap { get; set; } = true;

    /// <summary>Gets or sets whether floating combat damage/heal/XP numbers render over actors.</summary>
    public bool ShowCombatNumbers { get; set; } = true;

    /// <summary>Gets or sets whether FPS and world coordinates render inside the character sheet.</summary>
    public bool ShowTelemetry { get; set; } = true;

    /// <summary>Gets or sets whether the optional bottom keybind hint ribbon is displayed (off by default).</summary>
    public bool ShowKeybindHints { get; set; }

    /// <summary>Gets or sets discrete camera zoom preset (<c>0</c>=Wide, <c>1</c>=Standard, <c>2</c>=Close).</summary>
    public int CameraZoomPreset { get; set; } = 1;

    /// <summary>
    /// Gets or sets the chosen graphics quality preset (<see langword="null"/> = auto: picked from the GPU, ADR-041).
    /// Cosmetic only; nothing gameplay-relevant reads it.
    /// </summary>
    public GraphicsQuality? GraphicsQuality { get; set; }

    /// <summary>
    /// Gets or sets the preset the world renderer is actually using (the auto choice resolved, or an override), so the
    /// Settings window can highlight it. Not saved.
    /// </summary>
    public GraphicsQuality EffectiveGraphicsQuality { get; set; } = PostProcessing.GraphicsQuality.Medium;

    /// <summary>
    /// Gets or sets the chosen UI language code (empty = default). <c>CARBON_LANG</c> overrides it at startup (ADR-023 D3).
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Loads settings from <paramref name="filePath"/> (or the default <c>settings.ini</c>) if present.</summary>
    /// <param name="filePath">Optional settings file path.</param>
    /// <returns>The loaded settings, or defaults when the file is missing, unreadable or malformed.</returns>
    public static UiSettings LoadOrDefault(string? filePath = null)
    {
        bool useDefaultPath = string.IsNullOrWhiteSpace(filePath);
        string path = useDefaultPath ? s_settingsPath : filePath!;
        UiSettings settings = new();
        try
        {
            if (File.Exists(path))
            {
                settings.Apply(IniDocument.Load(path));
            }
            else if (useDefaultPath && File.Exists(s_legacySettingsPath))
            {
                settings.ApplyLegacy(File.ReadAllLines(s_legacySettingsPath));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep default settings if local config directory is read-only.
        }
        catch (IniFormatException ex)
        {
            Console.Error.WriteLine($"[Settings] Ignoring malformed settings file: {ex.Message}");
        }

        return settings;
    }

    /// <summary>Writes current preferences to <paramref name="filePath"/> (or the default <c>settings.ini</c>) without reflection.</summary>
    /// <param name="filePath">Optional settings file path.</param>
    public void Save(string? filePath = null)
    {
        string path = string.IsNullOrWhiteSpace(filePath) ? s_settingsPath : filePath;
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                _ = Directory.CreateDirectory(dir);
            }

            string[] lines =
            [
                "; Carbon client settings. CARBON_LANG overrides [general] language.",
                "[general]",
                $"language = {IniDocument.Escape(this.Language)}",
                string.Empty,
                "[display]",
                $"show_minimap = {Bool(this.ShowMinimap)}",
                $"show_combat_numbers = {Bool(this.ShowCombatNumbers)}",
                $"show_telemetry = {Bool(this.ShowTelemetry)}",
                $"show_keybind_hints = {Bool(this.ShowKeybindHints)}",
                $"camera_zoom = {this.CameraZoomPreset.ToString(CultureInfo.InvariantCulture)}",
                $"graphics_quality = {(this.GraphicsQuality is { } quality ? GraphicsQualityPolicy.ToSettingValue(quality) : "auto")}",
            ];
            File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore disk write failure on restricted environments.
        }
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private void Apply(IniDocument document)
    {
        if (document.TryGetValue("general.language", out string language))
        {
            this.Language = language;
        }

        foreach (IniEntry entry in document.InSection("display"))
        {
            this.Set(entry.Key, entry.Value);
        }
    }

    private void ApplyLegacy(IEnumerable<string> lines)
    {
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            string key = line[..eq].Trim() switch
            {
                nameof(this.ShowMinimap) => "show_minimap",
                nameof(this.ShowCombatNumbers) => "show_combat_numbers",
                nameof(this.ShowTelemetry) => "show_telemetry",
                nameof(this.ShowKeybindHints) => "show_keybind_hints",
                nameof(this.CameraZoomPreset) => "camera_zoom",
                _ => string.Empty,
            };
            this.Set(key, line[(eq + 1)..].Trim());
        }
    }

    private void Set(string key, string value)
    {
        bool isBool = bool.TryParse(value, out bool flag);
        switch (key)
        {
            case "show_minimap" when isBool:
                this.ShowMinimap = flag;
                break;
            case "show_combat_numbers" when isBool:
                this.ShowCombatNumbers = flag;
                break;
            case "show_telemetry" when isBool:
                this.ShowTelemetry = flag;
                break;
            case "show_keybind_hints" when isBool:
                this.ShowKeybindHints = flag;
                break;
            case "camera_zoom" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int zoom):
                this.CameraZoomPreset = Math.Clamp(zoom, 0, 2);
                break;
            case "graphics_quality":
                this.GraphicsQuality = GraphicsQualityPolicy.TryParse(value, out GraphicsQuality quality) ? quality : null;
                break;
            default:
                break;
        }
    }
}
