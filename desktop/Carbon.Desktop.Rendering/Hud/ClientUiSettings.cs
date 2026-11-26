// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.IO;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Persisted client UI, visibility, and camera zoom preferences stored AOT-safely in a local key-value file.
/// </summary>
public sealed class ClientUiSettings
{
    private static readonly string s_settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Carbon",
        "ui_settings.cfg");

    /// <summary>Gets or sets whether the top-right radar minimap is visible (off by default).</summary>
    public bool ShowMinimap { get; set; }

    /// <summary>Gets or sets whether floating combat damage/heal/XP numbers render over actors.</summary>
    public bool ShowCombatNumbers { get; set; } = true;

    /// <summary>Gets or sets whether FPS and world coordinates render inside the character sheet.</summary>
    public bool ShowTelemetry { get; set; } = true;

    /// <summary>Gets or sets whether the optional bottom keybind hint ribbon is displayed (off by default).</summary>
    public bool ShowKeybindHints { get; set; }

    /// <summary>Gets or sets discrete camera zoom preset (<c>0</c>=Wide, <c>1</c>=Standard, <c>2</c>=Close).</summary>
    public int CameraZoomPreset { get; set; } = 1;

    /// <summary>Loads settings from <paramref name="filePath"/> (or default local config path) if present.</summary>
    public static ClientUiSettings LoadOrDefault(string? filePath = null)
    {
        string path = string.IsNullOrWhiteSpace(filePath) ? s_settingsPath : filePath;
        ClientUiSettings settings = new();
        try
        {
            if (!File.Exists(path))
            {
                return settings;
            }

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                {
                    continue;
                }

                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case nameof(ShowMinimap):
                        if (bool.TryParse(val, out bool bMinimap))
                        {
                            settings.ShowMinimap = bMinimap;
                        }

                        break;
                    case nameof(ShowCombatNumbers):
                        if (bool.TryParse(val, out bool bDmg))
                        {
                            settings.ShowCombatNumbers = bDmg;
                        }

                        break;
                    case nameof(ShowTelemetry):
                        if (bool.TryParse(val, out bool bTele))
                        {
                            settings.ShowTelemetry = bTele;
                        }

                        break;
                    case nameof(ShowKeybindHints):
                        if (bool.TryParse(val, out bool bHints))
                        {
                            settings.ShowKeybindHints = bHints;
                        }

                        break;
                    case nameof(CameraZoomPreset):
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
                        {
                            settings.CameraZoomPreset = Math.Clamp(z, 0, 2);
                        }

                        break;
                    default:
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep default settings if local config directory is read-only.
        }

        return settings;
    }

    /// <summary>Writes current UI preferences cleanly to <paramref name="filePath"/> (or default config path) without reflection.</summary>
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
                $"{nameof(this.ShowMinimap)}={this.ShowMinimap}",
                $"{nameof(this.ShowCombatNumbers)}={this.ShowCombatNumbers}",
                $"{nameof(this.ShowTelemetry)}={this.ShowTelemetry}",
                $"{nameof(this.ShowKeybindHints)}={this.ShowKeybindHints}",
                $"{nameof(this.CameraZoomPreset)}={this.CameraZoomPreset.ToString(CultureInfo.InvariantCulture)}",
            ];
            File.WriteAllLines(path, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore disk write failure on restricted environments.
        }
    }
}
