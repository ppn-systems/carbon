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
        ("WASD", "Move"),
        ("Shift", "Sprint"),
        ("LMB / Space", "Attack / Harvest"),
        ("1..9 / Scroll", "Select Slot"),
        ("Tab", "Lock Target"),
        ("Q / R / V", "Class Skills"),
    ];

    private static readonly (string Key, string Desc)[] s_controlsRight =
    [
        ("E", "Pickup / Smith"),
        ("I", "Inventory Bag"),
        ("Y", "Character & Stats"),
        ("C / U", "Craft / Forge"),
        ("M / F1", "Map / Toggle HUD"),
        ("Esc", "Menu"),
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

        for (int row = 0; row < 3; row++)
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
                    default:
                        settings.ShowTelemetry = !settings.ShowTelemetry;
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
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), CarbonUiPalette.BorderOuter, bodyAlpha: 248);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(spriteBatch, in closeBtn, hoverClose ? new ColorRgba(158, 46, 52, 245) : CarbonUiPalette.DangerFill, CarbonUiPalette.DangerBorder);

        for (int t = 0; t < 2; t++)
        {
            RectangleF tabRect = GetTabRect(ox, oy, t);
            bool active = t == _activeTab;
            skin.DrawButton(
                spriteBatch,
                in tabRect,
                active ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.CardFill,
                active ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }

        skin.DrawCard(spriteBatch, new RectangleF(ox + 20f, oy + 86f, PanelW - 40f, 274f), CarbonUiPalette.BorderSubtle, bodyAlpha: 238);

        if (_activeTab == 0)
        {
            bool[] values = [settings.ShowMinimap, settings.ShowCombatNumbers, settings.ShowTelemetry];
            for (int r = 0; r < values.Length; r++)
            {
                RectangleF btn = GetToggleButtonRect(ox, oy, r);
                bool on = values[r];
                bool hovered = mouseScreenPos.HasValue && btn.Contains(mouseScreenPos.Value);
                skin.DrawButton(
                    spriteBatch,
                    in btn,
                    on ? (hovered ? new ColorRgba(44, 114, 82, 245) : CarbonUiPalette.ConfirmFill) : CarbonUiPalette.SlotFill,
                    on ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
            }

            for (int z = 0; z < 3; z++)
            {
                RectangleF zRect = GetZoomButtonRect(ox, oy, z);
                bool active = settings.CameraZoomPreset == z;
                skin.DrawButton(
                    spriteBatch,
                    in zRect,
                    active ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill,
                    active ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
            }
        }

        skin.DrawButton(spriteBatch, GetFooterButtonRect(ox, oy, 0), CarbonUiPalette.ConfirmFill, CarbonUiPalette.ConfirmBorder);
        skin.DrawButton(spriteBatch, GetFooterButtonRect(ox, oy, 1), CarbonUiPalette.CardFill, CarbonUiPalette.BorderOuter);
        skin.DrawButton(spriteBatch, GetFooterButtonRect(ox, oy, 2), CarbonUiPalette.DangerFill, CarbonUiPalette.DangerBorder);
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, ClientUiSettings settings, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        ColorRgba black = CarbonUiPalette.TextOutline;

        spriteBatch.DrawOutlinedString(font, "Settings", new Vector2(ox + 20f, oy + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "X", GetCloseButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "Display", GetTabRect(ox, oy, 0), CarbonUiPalette.TextPrimary, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "Controls", GetTabRect(ox, oy, 1), CarbonUiPalette.TextPrimary, black, 1f);

        if (_activeTab == 0)
        {
            (string Label, bool Val)[] rows =
            [
                ("Show Minimap (M)", settings.ShowMinimap),
                ("Show Combat Numbers", settings.ShowCombatNumbers),
                ("Show FPS & Coords in Character Info", settings.ShowTelemetry),
            ];
            for (int r = 0; r < rows.Length; r++)
            {
                RectangleF toggleRect = GetToggleButtonRect(ox, oy, r);
                spriteBatch.DrawOutlinedString(font, rows[r].Label, new Vector2(ox + 38f, toggleRect.Y + 4f), CarbonUiPalette.TextPrimary, black, 1, 1f);
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, rows[r].Val ? "ON" : "OFF", in toggleRect, CarbonUiPalette.TextPrimary, black, 1f);
            }

            spriteBatch.DrawOutlinedString(font, "Camera Zoom", new Vector2(ox + 38f, oy + 292f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            string[] zoomLabels = ["WIDE", "STD", "CLOSE"];
            for (int z = 0; z < 3; z++)
            {
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, zoomLabels[z], GetZoomButtonRect(ox, oy, z), CarbonUiPalette.TextPrimary, black, 1f);
            }
        }
        else
        {
            spriteBatch.DrawOutlinedString(font, "Combat", new Vector2(ox + 36f, oy + 100f), CarbonUiPalette.TextWarm, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, "Menus", new Vector2(ox + 302f, oy + 100f), CarbonUiPalette.TextWarm, black, 1, 1f);

            for (int i = 0; i < s_controlsLeft.Length; i++)
            {
                float y = oy + 128f + (i * 30f);
                spriteBatch.DrawOutlinedString(font, s_controlsLeft[i].Key, new Vector2(ox + 36f, y), CarbonUiPalette.TextWarm, black, 1, 1f);
                spriteBatch.DrawOutlinedString(font, s_controlsLeft[i].Desc, new Vector2(ox + 148f, y), CarbonUiPalette.TextPrimary, black, 1, 1f);
            }

            for (int i = 0; i < s_controlsRight.Length; i++)
            {
                float y = oy + 128f + (i * 30f);
                spriteBatch.DrawOutlinedString(font, s_controlsRight[i].Key, new Vector2(ox + 302f, y), CarbonUiPalette.TextWarm, black, 1, 1f);
                spriteBatch.DrawOutlinedString(font, s_controlsRight[i].Desc, new Vector2(ox + 402f, y), CarbonUiPalette.TextPrimary, black, 1, 1f);
            }

            spriteBatch.DrawOutlinedString(
                font,
                "Drag an item outside Bag to drop it on the ground.",
                new Vector2(ox + 36f, oy + 324f),
                CarbonUiPalette.TextMuted,
                black,
                1,
                1f);
        }

        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "RESUME", GetFooterButtonRect(ox, oy, 0), CarbonUiPalette.TextPrimary, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "LOGOUT", GetFooterButtonRect(ox, oy, 1), CarbonUiPalette.TextPrimary, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "QUIT", GetFooterButtonRect(ox, oy, 2), CarbonUiPalette.TextPrimary, black, 1f);
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
