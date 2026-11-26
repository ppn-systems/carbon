// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Class-gated Skills tab hosted inside the unified <see cref="PlayerModalTabs"/> frame.
/// Shows only skills unlocked or belonging to the character's advanced class (SRP).
/// </summary>
internal sealed class ClassSkillsModalWidget
{
    private const float PanelW = InventoryModalWidget.PanelWidth;
    private const float PanelH = InventoryModalWidget.PanelHeight;

    private readonly List<SkillDefinition> _skills = new(8);

    public bool ContainsPoint(IClientWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        if (mousePos.X >= ox && mousePos.X <= ox + PanelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH)
        {
            return true;
        }

        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skills);
        return PlayerModalTabs.GetTabBarBounds(ox, oy, hasSkills).Contains(mousePos);
    }

    public bool TryHitTestCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        return PlayerModalTabs.GetCloseButtonRect(ox, PanelW, oy).Contains(mousePos);
    }

    public bool TryHitTestTab(IClientWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out PlayerModalTab tab)
    {
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skills);
        return PlayerModalTabs.TryHitTestTab(mousePos, ox, oy, hasSkills, out tab);
    }

    public bool TryHitTestUpgradeButton(IClientWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort skillId)
    {
        _ = PlayerModalTabs.HasClassSkills(world, _skills);
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        for (int i = 0; i < _skills.Count && i < 4; i++)
        {
            if (GetUpgradeButtonRect(ox, oy, i).Contains(mousePos))
            {
                skillId = _skills[i].Id;
                return true;
            }
        }

        skillId = 0;
        return false;
    }

    public void DrawSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skills);
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        PlayerModalTabs.DrawTabsSkin(spriteBatch, skin, ox, PanelW, oy, PlayerModalTab.Skills, hasSkills, mouseScreenPos);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), CarbonUiPalette.BorderOuter, bodyAlpha: 246);

        ushort unspent = world.LocalUnspentSkillPoints;
        for (int i = 0; i < _skills.Count && i < 4; i++)
        {
            SkillDefinition skill = _skills[i];
            byte rank = world.GetSkillRank(skill.Id);
            RectangleF cardRect = GetRowRect(ox, oy, i);
            skin.DrawCard(spriteBatch, in cardRect, rank > 0 ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

            RectangleF iconSocket = new(cardRect.X + 12f, cardRect.Y + 14f, 52f, 52f);
            skin.DrawSlot(spriteBatch, in iconSocket, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderOuter);

            RectangleF btnRect = GetUpgradeButtonRect(ox, oy, i);
            bool canAct = unspent > 0 && rank < skill.MaxRank;
            bool hover = mouseScreenPos.HasValue && btnRect.Contains(mouseScreenPos.Value);
            skin.DrawButton(
                spriteBatch,
                in btnRect,
                canAct ? (hover ? new ColorRgba(44, 114, 82, 245) : CarbonUiPalette.ConfirmFill) : CarbonUiPalette.SlotFill,
                canAct ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
        }
    }

    public void DrawGeometry(GeometryBatch batch, IClientWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        _ = PlayerModalTabs.HasClassSkills(world, _skills);
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);

        for (int i = 0; i < _skills.Count && i < 4; i++)
        {
            SkillDefinition skill = _skills[i];
            byte rank = world.GetSkillRank(skill.Id);
            RectangleF cardRect = GetRowRect(ox, oy, i);
            Vector2 center = new(cardRect.X + 38f, cardRect.Y + 40f);
            SkillBarWidget.DrawSkillGlyph(batch, skill.AnimArchetype, center, SkillBarWidget.ResolveAccentColor(skill), rank > 0);
        }
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skills);
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        PlayerModalTabs.DrawTabsText(spriteBatch, font, ox, PanelW, oy, PlayerModalTab.Skills, hasSkills);

        ColorRgba black = CarbonUiPalette.TextOutline;
        ushort sp = world.LocalUnspentSkillPoints;
        spriteBatch.DrawOutlinedString(font, $"{world.LocalPlayerClass} Skills", new Vector2(ox + 28f, oy + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"Points: {sp}", new Vector2(ox + 412f, oy + 14f), sp > 0 ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted, black, 1, 1f);

        for (int i = 0; i < _skills.Count && i < 4; i++)
        {
            SkillDefinition skill = _skills[i];
            byte rank = world.GetSkillRank(skill.Id);
            byte previewRank = rank > 0 ? rank : (byte)1;
            SkillSpec curSpec = skill.EvaluateRank(previewRank);
            RectangleF cardRect = GetRowRect(ox, oy, i);

            spriteBatch.DrawOutlinedString(font, $"{skill.DisplayName}  (Rank {rank}/{skill.MaxRank})", new Vector2(cardRect.X + 76f, cardRect.Y + 14f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            string detail = $"{skill.Summary}  |  MP {(int)curSpec.ManaCost}  CD {curSpec.CooldownSeconds:F1}s";
            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, detail, cardRect.Width - 208f, 1f), new Vector2(cardRect.X + 76f, cardRect.Y + 40f), CarbonUiPalette.TextMuted, black, 1, 1f);

            RectangleF btnRect = GetUpgradeButtonRect(ox, oy, i);
            string btnTxt = rank >= skill.MaxRank ? "MAX" : (rank == 0 ? "UNLOCK" : "RANK UP");
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, btnTxt, in btnRect, sp > 0 && rank < skill.MaxRank ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted, black, 1f);
        }
    }

    private static RectangleF GetRowRect(float ox, float oy, int index) =>
        new(ox + 24f, oy + 54f + (index * 92f), PanelW - 48f, 80f);

    private static RectangleF GetUpgradeButtonRect(float ox, float oy, int index)
    {
        RectangleF card = GetRowRect(ox, oy, index);
        return new RectangleF(card.Right - 116f, card.Y + 22f, 100f, 36f);
    }
}
