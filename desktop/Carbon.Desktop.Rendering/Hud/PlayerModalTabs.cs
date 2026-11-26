// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Primary player menu tabs hosted on the top-left of the unified player window.</summary>
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

/// <summary>Shared top-left window tab strip &amp; close-button chrome across <see cref="PlayerModalTab"/> views.</summary>
internal static class PlayerModalTabs
{
    private const float TabW = 104f;
    private const float TabH = 30f;
    private const float TabGap = 4f;

    public static bool HasClassSkills(IClientWorldReadModel world, List<SkillDefinition> scratch)
    {
        SkillCatalog.CollectCharacterSkills(
            world.LocalPlayerClass,
            world.LocalCharacterTraits,
            world.LocalUnlockedSkillMask,
            scratch);
        return scratch.Count > 0;
    }

    public static RectangleF GetTabBarBounds(float ox, float oy, bool showSkills)
    {
        int count = showSkills ? 4 : 3;
        return new RectangleF(ox + 12f, oy - TabH, (count * TabW) + ((count - 1) * TabGap), TabH + 2f);
    }

    public static RectangleF GetTabRect(float ox, float oy, int index) =>
        new(ox + 12f + (index * (TabW + TabGap)), oy - TabH + 2f, TabW, TabH);

    public static RectangleF GetCloseButtonRect(float ox, float panelW, float oy) =>
        new(ox + panelW - 36f, oy + 10f, 26f, 24f);

    public static bool TryHitTestTab(
        Vector2 mousePos,
        float ox,
        float oy,
        bool showSkills,
        out PlayerModalTab clickedTab)
    {
        int count = showSkills ? 4 : 3;
        for (int i = 0; i < count; i++)
        {
            if (GetTabRect(ox, oy, i).Contains(mousePos))
            {
                clickedTab = (PlayerModalTab)i;
                return true;
            }
        }

        clickedTab = PlayerModalTab.Inventory;
        return false;
    }

    public static void DrawTabsSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        float ox,
        float panelW,
        float oy,
        PlayerModalTab activeTab,
        bool showSkills,
        Vector2? mouseScreenPos)
    {
        int count = showSkills ? 4 : 3;
        for (int i = 0; i < count; i++)
        {
            RectangleF tabRect = GetTabRect(ox, oy, i);
            bool active = (int)activeTab == i;
            bool hover = mouseScreenPos.HasValue && tabRect.Contains(mouseScreenPos.Value);
            skin.DrawButton(
                spriteBatch,
                in tabRect,
                active ? CarbonUiPalette.SlotSelectedFill : (hover ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.CardFill),
                active ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderOuter);
        }

        RectangleF closeBtn = GetCloseButtonRect(ox, panelW, oy);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(
            spriteBatch,
            in closeBtn,
            hoverClose ? new ColorRgba(158, 46, 52, 245) : CarbonUiPalette.DangerFill,
            CarbonUiPalette.DangerBorder);
    }

    public static void DrawTabsText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        float ox,
        float panelW,
        float oy,
        PlayerModalTab activeTab,
        bool showSkills)
    {
        string[] labels = ["Bag", "Character", "Crafting", "Skills"];
        int count = showSkills ? 4 : 3;
        ColorRgba black = CarbonUiPalette.TextOutline;

        for (int i = 0; i < count; i++)
        {
            RectangleF tabRect = GetTabRect(ox, oy, i);
            bool active = (int)activeTab == i;
            CarbonUiSkin.DrawCenteredText(
                spriteBatch,
                font,
                labels[i],
                in tabRect,
                active ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted,
                black,
                1f);
        }

        CarbonUiSkin.DrawCenteredText(
            spriteBatch,
            font,
            "X",
            GetCloseButtonRect(ox, panelW, oy),
            CarbonUiPalette.TextPrimary,
            black,
            1f);
    }
}
