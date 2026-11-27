// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
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

    /// <summary>Keybind reference rows as (key label, action label) localization keys (ADR-023).</summary>
    private static readonly (string KeyLabel, string ActionLabel)[] s_controlsLeft =
    [
        ("settings.key.move", "settings.action.move"),
        ("settings.key.sprint", "settings.action.sprint"),
        ("settings.key.attack", "settings.action.attack"),
        ("settings.key.select_slot", "settings.action.select_slot"),
        ("settings.key.lock_target", "settings.action.lock_target"),
        ("settings.key.class_skills", "settings.action.class_skills"),
        ("settings.key.rest", "settings.action.rest"),
    ];

    private static readonly (string KeyLabel, string ActionLabel)[] s_controlsRight =
    [
        ("settings.key.pickup", "settings.action.pickup"),
        ("settings.key.inventory", "settings.action.inventory"),
        ("settings.key.character", "settings.action.character"),
        ("settings.key.craft", "settings.action.craft"),
        ("settings.key.map", "settings.action.map"),
        ("settings.key.menu", "settings.action.menu"),
    ];

    private static readonly string[] s_toggleLabels = ["settings.show_minimap", "settings.show_combat_numbers", "settings.show_telemetry"];
    private static readonly string[] s_zoomLabels = ["settings.zoom.wide", "settings.zoom.standard", "settings.zoom.close"];

    private int _activeTab;

    /// <summary>Selects the Display (<c>0</c>) or Controls (<c>1</c>) tab.</summary>
    /// <param name="tab">Tab index.</param>
    public void SelectTab(int tab) => _activeTab = Math.Clamp(tab, 0, 1);

    public bool ContainsPoint(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return mousePos.X >= ox && mousePos.X <= ox + PanelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH;
    }

    public SettingsModalAction HandleClick(Vector2 mousePos, UiSettings settings, int viewportWidth, int viewportHeight)
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

        if (GetLanguageButtonRect(ox, oy).Contains(mousePos))
        {
            ILocalizer localizer = GameText.Current;
            string next = NextLocaleCode(localizer);
            if (localizer.SetLanguage(next))
            {
                settings.Language = localizer.Language;
                settings.Save();
            }

            return SettingsModalAction.SettingChanged;
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
        UiSettings settings,
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

            RectangleF langRect = GetLanguageButtonRect(ox, oy);
            bool hoverLang = mouseScreenPos.HasValue && langRect.Contains(mouseScreenPos.Value);
            skin.DrawButton(spriteBatch, in langRect, hoverLang ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill, CarbonUiPalette.BorderActive);

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

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, UiSettings settings, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        ColorRgba black = CarbonUiPalette.TextOutline;

        spriteBatch.DrawOutlinedString(font, GameText.Get("settings.title"), new Vector2(ox + 20f, oy + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "X", GetCloseButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("settings.tab.display"), GetTabRect(ox, oy, 0), CarbonUiPalette.TextPrimary, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("settings.tab.controls"), GetTabRect(ox, oy, 1), CarbonUiPalette.TextPrimary, black, 1f);

        if (_activeTab == 0)
        {
            for (int r = 0; r < s_toggleLabels.Length; r++)
            {
                bool on = r switch
                {
                    0 => settings.ShowMinimap,
                    1 => settings.ShowCombatNumbers,
                    _ => settings.ShowTelemetry,
                };
                RectangleF toggleRect = GetToggleButtonRect(ox, oy, r);
                spriteBatch.DrawOutlinedString(font, GameText.Get(s_toggleLabels[r]), new Vector2(ox + 38f, toggleRect.Y + 4f), CarbonUiPalette.TextPrimary, black, 1, 1f);
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get(on ? "settings.on" : "settings.off"), in toggleRect, CarbonUiPalette.TextPrimary, black, 1f);
            }

            RectangleF langButton = GetLanguageButtonRect(ox, oy);
            spriteBatch.DrawOutlinedString(font, GameText.Get("settings.language"), new Vector2(ox + 38f, langButton.Y + 4f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Current.Locale.NativeName, in langButton, CarbonUiPalette.TextPrimary, black, 1f);

            spriteBatch.DrawOutlinedString(font, GameText.Get("settings.camera_zoom"), new Vector2(ox + 38f, oy + 292f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            for (int z = 0; z < s_zoomLabels.Length; z++)
            {
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get(s_zoomLabels[z]), GetZoomButtonRect(ox, oy, z), CarbonUiPalette.TextPrimary, black, 1f);
            }
        }
        else
        {
            spriteBatch.DrawOutlinedString(font, GameText.Get("settings.section.combat"), new Vector2(ox + 36f, oy + 100f), CarbonUiPalette.TextWarm, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, GameText.Get("settings.section.menus"), new Vector2(ox + 302f, oy + 100f), CarbonUiPalette.TextWarm, black, 1, 1f);

            for (int i = 0; i < s_controlsLeft.Length; i++)
            {
                float y = oy + 128f + (i * 30f);
                spriteBatch.DrawOutlinedString(font, GameText.Get(s_controlsLeft[i].KeyLabel), new Vector2(ox + 36f, y), CarbonUiPalette.TextWarm, black, 1, 1f);
                spriteBatch.DrawOutlinedString(font, GameText.Get(s_controlsLeft[i].ActionLabel), new Vector2(ox + 148f, y), CarbonUiPalette.TextPrimary, black, 1, 1f);
            }

            for (int i = 0; i < s_controlsRight.Length; i++)
            {
                float y = oy + 128f + (i * 30f);
                spriteBatch.DrawOutlinedString(font, GameText.Get(s_controlsRight[i].KeyLabel), new Vector2(ox + 302f, y), CarbonUiPalette.TextWarm, black, 1, 1f);
                spriteBatch.DrawOutlinedString(font, GameText.Get(s_controlsRight[i].ActionLabel), new Vector2(ox + 402f, y), CarbonUiPalette.TextPrimary, black, 1, 1f);
            }

            spriteBatch.DrawOutlinedString(
                font,
                GameText.Get("settings.drop_hint"),
                new Vector2(ox + 36f, oy + 334f),
                CarbonUiPalette.TextMuted,
                black,
                1,
                1f);
        }

        CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("settings.resume"), GetFooterButtonRect(ox, oy, 0), CarbonUiPalette.TextPrimary, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("settings.logout"), GetFooterButtonRect(ox, oy, 1), CarbonUiPalette.TextPrimary, black, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("settings.quit"), GetFooterButtonRect(ox, oy, 2), CarbonUiPalette.TextPrimary, black, 1f);
    }

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    private static RectangleF GetCloseButtonRect(float ox, float oy) => new(ox + PanelW - 38f, oy + 10f, 26f, 24f);

    private static RectangleF GetTabRect(float ox, float oy, int index) =>
        new(ox + 22f + (index * 158f), oy + 48f, 150f, 28f);

    private static RectangleF GetToggleButtonRect(float ox, float oy, int row) =>
        new(ox + PanelW - 116f, oy + 104f + (row * 44f), 78f, 26f);

    private static RectangleF GetLanguageButtonRect(float ox, float oy) =>
        new(ox + PanelW - 196f, oy + 104f + (3 * 44f), 158f, 26f);

    private static string NextLocaleCode(ILocalizer localizer)
    {
        IReadOnlyList<LocaleInfo> locales = localizer.Locales;
        for (int i = 0; i < locales.Count; i++)
        {
            if (string.Equals(locales[i].Code, localizer.Language, StringComparison.Ordinal))
            {
                return locales[(i + 1) % locales.Count].Code;
            }
        }

        return locales.Count > 0 ? locales[0].Code : localizer.Language;
    }

    private static RectangleF GetZoomButtonRect(float ox, float oy, int index) =>
        new(ox + PanelW - 224f + (index * 64f), oy + 288f, 58f, 26f);

    private static RectangleF GetFooterButtonRect(float ox, float oy, int index) =>
        new(ox + 20f + (index * 184f), oy + 372f, 172f, 38f);
}
