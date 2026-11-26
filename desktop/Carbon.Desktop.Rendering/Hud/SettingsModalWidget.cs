// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Resulting system action from clicking inside the Settings &amp; Controls modal (<c>[ESC]</c>).</summary>
public enum SettingsModalAction : byte
{
    /// <summary>No system exit or resume triggered.</summary>
    None = 0,

    /// <summary>Handled toggle/tab state update internally and persisted settings.</summary>
    SettingChanged = 1,

    /// <summary>Zoom preset changed so caller should update camera zoom.</summary>
    ZoomChanged = 2,

    /// <summary>Closes the Settings modal and resumes gameplay.</summary>
    Resume = 3,

    /// <summary>Logs out to character selection / login screen.</summary>
    Logout = 4,

    /// <summary>Exits the desktop client application.</summary>
    Quit = 5,
}

/// <summary>
/// Two-tab System Settings &amp; Controls Guide modal (<c>[ESC]</c>) skinned cleanly with <see cref="CarbonUiSkin"/>,
/// replacing the bare pause menu with Minimap/Telemetry/CombatNumber/Hint toggles, Camera Zoom presets, and a Keybind Reference (SRP).
/// </summary>
internal sealed class SettingsModalWidget
{
    private const float PanelW = 580f;
    private const float PanelH = 432f;

    private static readonly (string Key, string Desc)[] s_controlsLeft =
    [
        ("WASD / Arrows", "Move hero"),
        ("Hold Shift", "Sprint (uses Stamina)"),
        ("LMB / Space", "Strike, Cast or Harvest"),
        ("Scroll / 1..9", "Select Hotbar Slot"),
        ("TAB", "Lock / Cycle Enemy"),
        ("Q / R / V", "Cast Learned Skills"),
    ];

    private static readonly (string Key, string Desc)[] s_controlsRight =
    [
        ("E", "Pickup Loot / Smith / Door"),
        ("I / B", "Bag, Gear & Stat Points"),
        ("K", "Skill Tome (Unlock/Rank)"),
        ("C", "Crafting Workshop"),
        ("U", "Blacksmith Forge Anvil"),
        ("M / F1 / ESC", "Radar / Hide HUD / Menu"),
    ];

    private int _activeTab;

    public bool ContainsPoint(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return mousePos.X >= ox && mousePos.X <= ox + PanelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH;
    }

    public SettingsModalAction HandleClick(Vector2 mousePos, ClientUiSettings settings, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        if (GetCloseButtonRect(ox, oy).Contains(mousePos) || GetFooterButtonRect(ox, oy, 0).Contains(mousePos))
        {
            return SettingsModalAction.Resume;
        }

        if (GetFooterButtonRect(ox, oy, 1).Contains(mousePos))
        {
            return SettingsModalAction.Logout;
        }

        if (GetFooterButtonRect(ox, oy, 2).Contains(mousePos))
        {
            return SettingsModalAction.Quit;
        }

        if (GetTabRect(ox, oy, 0).Contains(mousePos))
        {
            _activeTab = 0;
            return SettingsModalAction.SettingChanged;
        }

        if (GetTabRect(ox, oy, 1).Contains(mousePos))
        {
            _activeTab = 1;
            return SettingsModalAction.SettingChanged;
        }

        if (_activeTab != 0)
        {
            return SettingsModalAction.None;
        }

        for (int row = 0; row < 4; row++)
        {
            if (GetToggleButtonRect(ox, oy, row).Contains(mousePos))
            {
                switch (row)
                {
                    case 0:
                        settings.ShowMinimap = !settings.ShowMinimap;
                        break;
                    case 1:
                        settings.ShowCombatNumbers = !settings.ShowCombatNumbers;
                        break;
                    case 2:
                        settings.ShowTelemetry = !settings.ShowTelemetry;
                        break;
                    default:
                        settings.ShowKeybindHints = !settings.ShowKeybindHints;
                        break;
                }

                settings.Save();
                return SettingsModalAction.SettingChanged;
            }
        }

        for (int z = 0; z < 3; z++)
        {
            if (GetZoomButtonRect(ox, oy, z).Contains(mousePos))
            {
                settings.CameraZoomPreset = z;
                settings.Save();
                return SettingsModalAction.ZoomChanged;
            }
        }

        return SettingsModalAction.None;
    }

    public void DrawSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        ClientUiSettings settings,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), new ColorRgba(250, 204, 21, 240), bodyAlpha: 248);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(spriteBatch, in closeBtn, hoverClose ? new ColorRgba(220, 38, 38, 245) : new ColorRgba(153, 27, 27, 235), new ColorRgba(248, 113, 113, 245));

        for (int t = 0; t < 2; t++)
        {
            RectangleF tabRect = GetTabRect(ox, oy, t);
            bool active = t == _activeTab;
            skin.DrawButton(
                spriteBatch,
                in tabRect,
                active ? new ColorRgba(180, 83, 9, 240) : new ColorRgba(18, 28, 48, 232),
                active ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(71, 85, 105, 220));
        }

        skin.DrawCard(spriteBatch, new RectangleF(ox + 20f, oy + 86f, PanelW - 40f, 274f), new ColorRgba(71, 85, 105, 225), bodyAlpha: 238);

        if (_activeTab == 0)
        {
            bool[] values = [settings.ShowMinimap, settings.ShowCombatNumbers, settings.ShowTelemetry, settings.ShowKeybindHints];
            for (int r = 0; r < values.Length; r++)
            {
                RectangleF btn = GetToggleButtonRect(ox, oy, r);
                bool on = values[r];
                bool hovered = mouseScreenPos.HasValue && btn.Contains(mouseScreenPos.Value);
                skin.DrawButton(
                    spriteBatch,
                    in btn,
                    on ? (hovered ? new ColorRgba(16, 185, 129, 245) : new ColorRgba(5, 150, 105, 235)) : new ColorRgba(26, 36, 54, 230),
                    on ? new ColorRgba(110, 231, 183, 245) : new ColorRgba(100, 116, 139, 220));
            }

            for (int z = 0; z < 3; z++)
            {
                RectangleF zRect = GetZoomButtonRect(ox, oy, z);
                bool active = settings.CameraZoomPreset == z;
                skin.DrawButton(
                    spriteBatch,
                    in zRect,
                    active ? new ColorRgba(37, 99, 235, 240) : new ColorRgba(20, 30, 50, 228),
                    active ? new ColorRgba(125, 211, 252, 255) : new ColorRgba(71, 85, 105, 215));
            }
        }

        skin.DrawButton(spriteBatch, GetFooterButtonRect(ox, oy, 0), new ColorRgba(5, 150, 105, 238), new ColorRgba(110, 231, 183, 245));
        skin.DrawButton(spriteBatch, GetFooterButtonRect(ox, oy, 1), new ColorRgba(180, 83, 9, 238), new ColorRgba(253, 224, 71, 245));
        skin.DrawButton(spriteBatch, GetFooterButtonRect(ox, oy, 2), new ColorRgba(153, 27, 27, 238), new ColorRgba(248, 113, 113, 245));
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, ClientUiSettings settings, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        ColorRgba black = new(8, 10, 16, 255);

        spriteBatch.DrawOutlinedString(font, "SYSTEM SETTINGS & CONTROLS [ESC]", new Vector2(ox + 20f, oy + 16f), new ColorRgba(250, 204, 21, 255), black, 1, 1.5f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "X", GetCloseButtonRect(ox, oy), ColorRgba.White, black, 1.5f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "HUD & DISPLAY", GetTabRect(ox, oy, 0), ColorRgba.White, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "CONTROLS & GUIDE", GetTabRect(ox, oy, 1), ColorRgba.White, black, 1f);

        if (_activeTab == 0)
        {
            (string Label, bool Val)[] rows =
            [
                ("Show Top-Right Radar Minimap ([M])", settings.ShowMinimap),
                ("Show Floating Combat & XP Numbers", settings.ShowCombatNumbers),
                ("Show Coordinates & FPS Telemetry", settings.ShowTelemetry),
                ("Show Bottom Keybind Hint Ribbon", settings.ShowKeybindHints),
            ];
            for (int r = 0; r < rows.Length; r++)
            {
                RectangleF toggleRect = GetToggleButtonRect(ox, oy, r);
                spriteBatch.DrawOutlinedString(font, rows[r].Label, new Vector2(ox + 38f, toggleRect.Y + 6f), new ColorRgba(226, 232, 240, 255), black, 1, 1f);
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, rows[r].Val ? "ON" : "OFF", in toggleRect, ColorRgba.White, black, 1f);
            }

            spriteBatch.DrawOutlinedString(font, "Camera Zoom Preset", new Vector2(ox + 38f, oy + 294f), new ColorRgba(226, 232, 240, 255), black, 1, 1f);
            string[] zoomLabels = ["WIDE", "STD", "CLOSE"];
            for (int z = 0; z < 3; z++)
            {
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, zoomLabels[z], GetZoomButtonRect(ox, oy, z), ColorRgba.White, black, 1f);
            }
        }
        else
        {
            spriteBatch.DrawOutlinedString(font, "COMBAT & HARVESTING", new Vector2(ox + 36f, oy + 100f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, "MENUS & STATIONS", new Vector2(ox + 302f, oy + 100f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);

            for (int i = 0; i < s_controlsLeft.Length; i++)
            {
                float y = oy + 124f + (i * 28f);
                spriteBatch.DrawOutlinedString(font, s_controlsLeft[i].Key, new Vector2(ox + 36f, y), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
                spriteBatch.DrawOutlinedString(font, s_controlsLeft[i].Desc, new Vector2(ox + 132f, y), ColorRgba.White, black, 1, 1f);
            }

            for (int i = 0; i < s_controlsRight.Length; i++)
            {
                float y = oy + 124f + (i * 28f);
                spriteBatch.DrawOutlinedString(font, s_controlsRight[i].Key, new Vector2(ox + 302f, y), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
                spriteBatch.DrawOutlinedString(font, s_controlsRight[i].Desc, new Vector2(ox + 388f, y), ColorRgba.White, black, 1, 1f);
            }

            spriteBatch.DrawOutlinedString(
                font,
                "Tip: Chopping/mining needs close range; Bows/Staffs shoot at range.",
                new Vector2(ox + 36f, oy + 318f),
                new ColorRgba(148, 163, 184, 255),
                black,
                1,
                1f);
            spriteBatch.DrawOutlinedString(
                font,
                "Tip: Unlock skills with SP in [K], forge +1..+10 near Blacksmith [U].",
                new Vector2(ox + 36f, oy + 334f),
                new ColorRgba(148, 163, 184, 255),
                black,
                1,
                1f);
        }

        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "RESUME", GetFooterButtonRect(ox, oy, 0), ColorRgba.White, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "LOGOUT HERO", GetFooterButtonRect(ox, oy, 1), ColorRgba.White, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "QUIT GAME", GetFooterButtonRect(ox, oy, 2), ColorRgba.White, black, 1f);
    }

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    private static RectangleF GetCloseButtonRect(float ox, float oy) => new(ox + PanelW - 38f, oy + 10f, 26f, 24f);

    private static RectangleF GetTabRect(float ox, float oy, int index) =>
        new(ox + 22f + (index * 158f), oy + 48f, 150f, 28f);

    private static RectangleF GetToggleButtonRect(float ox, float oy, int row) =>
        new(ox + PanelW - 116f, oy + 104f + (row * 44f), 78f, 26f);

    private static RectangleF GetZoomButtonRect(float ox, float oy, int index) =>
        new(ox + PanelW - 224f + (index * 64f), oy + 288f, 58f, 26f);

    private static RectangleF GetFooterButtonRect(float ox, float oy, int index) =>
        new(ox + 20f + (index * 184f), oy + 372f, 172f, 38f);
}
