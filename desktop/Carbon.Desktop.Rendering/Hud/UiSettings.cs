// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.PostProcessing;
using Carbon.Desktop.Rendering.UiSkin;

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
/// ui_scale = auto
/// </code>
/// <c>CARBON_LANG</c> overrides <c>[general] language</c> at startup.
/// </summary>
public sealed class UiSettings
{
    private static readonly string s_configDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Carbon");

    private static readonly string s_settingsPath = Path.Combine(s_configDirectory, "settings.ini");

    /// <summary>Gets or sets whether the minimap under the HUD clock is visible (on by default; <c>[M]</c> toggles it).</summary>
    public bool ShowMinimap { get; set; } = true;

    /// <summary>Gets or sets whether floating combat damage/heal/XP numbers render over actors.</summary>
    public bool ShowCombatNumbers { get; set; } = true;

    /// <summary>Gets or sets whether FPS and world coordinates render inside the character sheet.</summary>
    public bool ShowTelemetry { get; set; } = true;

    /// <summary>Gets or sets whether the optional bottom keybind hint ribbon is displayed (off by default).</summary>
    public bool ShowKeybindHints { get; set; }

    /// <summary>Gets or sets whether hits shake the camera (on by default; off means no camera offset at all).</summary>
    public bool ScreenShake { get; set; } = true;

    /// <summary>Gets or sets whether hit flashes are softened to a gentle tint (off by default; accessibility).</summary>
    public bool ReduceFlashes { get; set; }

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
    /// Gets or sets the chosen UI scale preset (<see langword="null"/> = auto: picked from the window size,
    /// <see cref="UiScale.Resolve"/>).
    /// </summary>
    public float? UiScaleChoice { get; set; }

    /// <summary>
    /// Gets or sets the chosen UI language code (empty = default). <c>CARBON_LANG</c> overrides it at startup (ADR-023 D3).
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Loads settings from <paramref name="filePath"/> (or the default <c>settings.ini</c>) if present.</summary>
    /// <param name="filePath">Optional settings file path.</param>
    /// <returns>The loaded settings, or defaults when the file is missing, unreadable or malformed.</returns>
    public static UiSettings LoadOrDefault(string? filePath = null)
    {
        string path = string.IsNullOrWhiteSpace(filePath) ? s_settingsPath : filePath;
        UiSettings settings = new();
        try
        {
            if (File.Exists(path))
            {
                settings.Apply(IniDocument.Load(path));
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
                $"screen_shake = {Bool(this.ScreenShake)}",
                $"reduce_flashes = {Bool(this.ReduceFlashes)}",
                $"camera_zoom = {this.CameraZoomPreset.ToString(CultureInfo.InvariantCulture)}",
                $"graphics_quality = {(this.GraphicsQuality is { } quality ? GraphicsQualityPolicy.ToSettingValue(quality) : "auto")}",
                $"ui_scale = {UiScale.ToSettingValue(this.UiScaleChoice)}",
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
            case "screen_shake" when isBool:
                this.ScreenShake = flag;
                break;
            case "reduce_flashes" when isBool:
                this.ReduceFlashes = flag;
                break;
            case "camera_zoom" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int zoom):
                this.CameraZoomPreset = Math.Clamp(zoom, 0, 2);
                break;
            case "ui_scale":
                this.UiScaleChoice = UiScale.TryParse(value, out float? scale) ? scale : null;
                break;
            case "graphics_quality":
                this.GraphicsQuality = GraphicsQualityPolicy.TryParse(value, out GraphicsQuality quality) ? quality : null;
                break;
            default:
                break;
        }
    }
}
