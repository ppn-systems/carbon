// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Primary player menu tabs hosted on the top of the unified fixed-size player window.</summary>
public enum PlayerModalTab : byte
{
    /// <summary>Minecraft-inspired compact backpack, hotbar, and armor equipment tab (<c>[I]</c>).</summary>
    Inventory = 0,

    /// <summary>Detailed hero info, telemetry, and attribute allocator tab (<c>[Y]</c>).</summary>
    Character = 1,

    /// <summary>Crafting workshop blueprint tab (<c>[C]</c>).</summary>
    Crafting = 2,

    /// <summary>Class skill mastery tab (<c>[K]</c>, shown only after class advancement).</summary>
    Skills = 3,
}

/// <summary>
/// Resolution-scaled immutable frame, tab, and inner-workspace metrics shared identically across every
/// <see cref="PlayerModalTab"/> so switching tabs never resizes or shifts the window.
/// </summary>
internal readonly record struct PlayerModalLayout(
    float Scale,
    float TextScale,
    float TitleScale,
    float Ox,
    float Oy,
    float PanelW,
    float PanelH,
    float ContentX,
    float ContentY,
    float ContentW,
    float ContentH);

/// <summary>Shared equal-width tab strip, fixed frame chrome, and header across <see cref="PlayerModalTab"/> views.</summary>
internal static class PlayerModalTabs
{
    private static readonly string[] s_tabLabelKeys = ["hud.tab.bag", "hud.tab.character", "hud.tab.crafting", "hud.tab.skills"];

    private const float BaseSlotSize = 70f;
    private const float BaseSlotGap = 8f;
    private const float BaseSidePad = 37f;
    private const float BaseHeaderBandH = 52f;
    private const float BaseDollStageH = 172f;
    private const float BaseSectionGap = 12f;
    private const float BaseHotbarGap = 22f;
    private const float BaseBottomPad = 26f;
    private const float BaseTabH = 38f;
    private const float BaseTabGap = 6f;

    public static PlayerModalLayout ComputeLayout(int viewportWidth, int viewportHeight)
    {
        float fitW = viewportWidth > 0 ? viewportWidth / 1280f : 1f;
        float fitH = viewportHeight > 0 ? viewportHeight / 720f : 1f;
        float fit = MathF.Min(fitW, fitH);
        float scale = Math.Clamp(0.88f + ((fit - 1f) * 0.40f), 0.85f, 1.24f);
        float textScale = scale >= 1.16f ? 1.25f : 1.0f;
        float titleScale = textScale * 1.25f;

        float slotSize = MathF.Round(BaseSlotSize * scale);
        float slotGap = MathF.Round(BaseSlotGap * scale);
        float contentW = (9f * slotSize) + (8f * slotGap);
        float sidePad = MathF.Round(BaseSidePad * scale);
        float panelW = contentW + (sidePad * 2f);

        float headerBandH = MathF.Round(BaseHeaderBandH * scale);
        float dollH = MathF.Round(BaseDollStageH * scale);
        float sectionGap = MathF.Round(BaseSectionGap * scale);
        float bagH = (3f * slotSize) + (2f * slotGap);
        float hotbarGap = MathF.Round(BaseHotbarGap * scale);
        float contentH = dollH + sectionGap + bagH + hotbarGap + slotSize;
        float bottomPad = MathF.Round(BaseBottomPad * scale);
        float panelH = headerBandH + contentH + bottomPad;

        float tabH = MathF.Round(BaseTabH * scale);
        float ox = MathF.Floor((viewportWidth - panelW) * 0.5f);
        float oy = MathF.Max(tabH + 8f, MathF.Floor(((viewportHeight - (panelH + tabH)) * 0.5f) + tabH));

        return new PlayerModalLayout(
            scale,
            textScale,
            titleScale,
            ox,
            oy,
            panelW,
            panelH,
            ox + sidePad,
            oy + headerBandH,
            contentW,
            contentH);
    }

    public static bool HasClassSkills(IWorldReadModel world, List<SkillDefinition> scratch)
    {
        SkillCatalog.CollectCharacterSkills(
            world.LocalPlayerClass,
            world.LocalCharacterTraits,
            world.LocalUnlockedSkillMask,
            scratch);
        return scratch.Count > 0;
    }

    public static bool ContainsWindowOrTabs(in PlayerModalLayout layout, Vector2 mousePos)
    {
        if (mousePos.X >= layout.Ox &&
            mousePos.X <= layout.Ox + layout.PanelW &&
            mousePos.Y >= layout.Oy &&
            mousePos.Y <= layout.Oy + layout.PanelH)
        {
            return true;
        }

        return GetTabBarBounds(in layout).Contains(mousePos);
    }

    public static RectangleF GetTabBarBounds(in PlayerModalLayout layout)
    {
        float tabH = MathF.Round(BaseTabH * layout.Scale);
        return new RectangleF(layout.Ox, layout.Oy - tabH, layout.PanelW, tabH + 2f);
    }

    public static RectangleF GetTabRect(in PlayerModalLayout layout, int index, int tabCount)
    {
        int count = Math.Max(1, tabCount);
        float tabH = MathF.Round(BaseTabH * layout.Scale);
        float gap = MathF.Round(BaseTabGap * layout.Scale);
        float usableW = layout.PanelW - ((count - 1) * gap);
        float baseTabW = MathF.Floor(usableW / count);
        int remainder = Math.Max(0, (int)MathF.Round(usableW) - ((int)baseTabW * count));
        float x = layout.Ox;
        for (int i = 0; i < index; i++)
        {
            x += baseTabW + (i < remainder ? 1f : 0f) + gap;
        }

        float w = index == count - 1
            ? layout.Ox + layout.PanelW - x
            : baseTabW + (index < remainder ? 1f : 0f);
        return new RectangleF(x, layout.Oy - tabH + 2f, w, tabH);
    }

    public static RectangleF GetCloseButtonRect(in PlayerModalLayout layout)
    {
        float w = MathF.Round(34f * layout.Scale);
        float h = MathF.Round(30f * layout.Scale);
        return new RectangleF(layout.ContentX + layout.ContentW - w, layout.Oy + MathF.Round(11f * layout.Scale), w, h);
    }

    public static bool TryHitTestTab(
        Vector2 mousePos,
        in PlayerModalLayout layout,
        bool showSkills,
        out PlayerModalTab clickedTab)
    {
        int count = showSkills ? 4 : 3;
        for (int i = 0; i < count; i++)
        {
            if (GetTabRect(in layout, i, count).Contains(mousePos))
            {
                clickedTab = (PlayerModalTab)i;
                return true;
            }
        }

        clickedTab = PlayerModalTab.Inventory;
        return false;
    }

    public static void DrawWindowChromeSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        in PlayerModalLayout layout,
        PlayerModalTab activeTab,
        bool showSkills,
        Vector2? mouseScreenPos)
    {
        int count = showSkills ? 4 : 3;
        for (int i = 0; i < count; i++)
        {
            RectangleF tabRect = GetTabRect(in layout, i, count);
            bool active = (int)activeTab == i;
            bool hover = mouseScreenPos.HasValue && tabRect.Contains(mouseScreenPos.Value);
            skin.DrawButton(
                spriteBatch,
                in tabRect,
                active ? CarbonUiPalette.SlotSelectedFill : (hover ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.CardFill),
                active ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderOuter);
        }

        skin.DrawWindow(
            spriteBatch,
            new RectangleF(layout.Ox, layout.Oy, layout.PanelW, layout.PanelH),
            CarbonUiPalette.BorderOuter,
            bodyAlpha: 246);

        RectangleF closeBtn = GetCloseButtonRect(in layout);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(
            spriteBatch,
            in closeBtn,
            hoverClose ? new ColorRgba(158, 46, 52, 245) : CarbonUiPalette.DangerFill,
            CarbonUiPalette.DangerBorder);
    }

    public static void DrawWindowChromeText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        in PlayerModalLayout layout,
        PlayerModalTab activeTab,
        bool showSkills,
        string headerTitle,
        string? rightBadge = null,
        bool highlightBadge = false)
    {
        ReadOnlySpan<string> labels = s_tabLabelKeys;
        int count = showSkills ? 4 : 3;
        ColorRgba black = CarbonUiPalette.TextOutline;

        for (int i = 0; i < count; i++)
        {
            RectangleF tabRect = GetTabRect(in layout, i, count);
            bool active = (int)activeTab == i;
            CarbonUiSkin.DrawCenteredText(
                spriteBatch,
                font,
                GameText.Get(labels[i]),
                in tabRect,
                active ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted,
                black,
                layout.TextScale);
        }

        spriteBatch.DrawOutlinedString(
            font,
            headerTitle,
            new Vector2(layout.ContentX, layout.Oy + MathF.Round(13f * layout.Scale)),
            CarbonUiPalette.TextWarm,
            black,
            1,
            layout.TitleScale);

        RectangleF closeRect = GetCloseButtonRect(in layout);
        if (!string.IsNullOrEmpty(rightBadge))
        {
            TextMeasureResult badgeSize = font.MeasureString(rightBadge, TextLayoutOptions.Default, layout.TextScale);
            float bx = closeRect.X - badgeSize.Width - MathF.Round(16f * layout.Scale);
            float by = layout.Oy + MathF.Round(16f * layout.Scale);
            spriteBatch.DrawOutlinedString(
                font,
                rightBadge,
                new Vector2(bx, by),
                highlightBadge ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted,
                black,
                1,
                layout.TextScale);
        }

        CarbonUiSkin.DrawCenteredText(
            spriteBatch,
            font,
            "X",
            in closeRect,
            CarbonUiPalette.TextPrimary,
            black,
            layout.TextScale);
    }
}
