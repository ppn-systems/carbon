// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Class-gated Skills tab hosted inside the fixed <see cref="PlayerModalTabs"/> workspace.
/// Shows only skills unlocked or belonging to the character's advanced class (SRP).
/// </summary>
internal sealed class ClassSkillsModalWidget
{
    private readonly List<SkillDefinition> _skills = new(8);

    public bool ContainsPoint(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        _ = world;
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        return PlayerModalTabs.ContainsWindowOrTabs(in layout, mousePos);
    }

    public bool TryHitTestCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        return PlayerModalTabs.GetCloseButtonRect(in layout).Contains(mousePos);
    }

    public bool TryHitTestTab(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out PlayerModalTab tab)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skills);
        return PlayerModalTabs.TryHitTestTab(mousePos, in layout, hasSkills, out tab);
    }

    public bool TryHitTestUpgradeButton(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort skillId)
    {
        _ = PlayerModalTabs.HasClassSkills(world, _skills);
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        for (int i = 0; i < _skills.Count && i < 4; i++)
        {
            if (GetUpgradeButtonRect(in layout, i).Contains(mousePos))
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
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skills);
        PlayerModalTabs.DrawWindowChromeSkin(spriteBatch, skin, in layout, PlayerModalTab.Skills, hasSkills, mouseScreenPos);

        RectangleF contentCard = new(layout.ContentX, layout.ContentY, layout.ContentW, layout.ContentH);
        skin.DrawCard(spriteBatch, in contentCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        ushort unspent = world.LocalUnspentSkillPoints;
        float iconSize = MathF.Round(68f * s);
        for (int i = 0; i < _skills.Count && i < 4; i++)
        {
            SkillDefinition skill = _skills[i];
            byte rank = world.GetSkillRank(skill.Id);
            RectangleF rowRect = GetRowRect(in layout, i);
            skin.DrawInset(spriteBatch, in rowRect, CarbonUiPalette.SlotFill, rank > 0 ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);

            RectangleF iconSocket = new(rowRect.X + MathF.Round(16f * s), rowRect.Y + MathF.Round(19f * s), iconSize, iconSize);
            skin.DrawSlot(spriteBatch, in iconSocket, CarbonUiPalette.WindowFill, CarbonUiPalette.BorderOuter);

            RectangleF btnRect = GetUpgradeButtonRect(in layout, i);
            bool canAct = unspent > 0 && rank < skill.MaxRank;
            bool hover = mouseScreenPos.HasValue && btnRect.Contains(mouseScreenPos.Value);
            skin.DrawButton(
                spriteBatch,
                in btnRect,
                canAct ? (hover ? new ColorRgba(44, 114, 82, 245) : CarbonUiPalette.ConfirmFill) : CarbonUiPalette.SlotFill,
                canAct ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
        }
    }

    public void DrawGeometry(GeometryBatch batch, IWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        _ = PlayerModalTabs.HasClassSkills(world, _skills);
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;

        for (int i = 0; i < _skills.Count && i < 4; i++)
        {
            SkillDefinition skill = _skills[i];
            byte rank = world.GetSkillRank(skill.Id);
            RectangleF cardRect = GetRowRect(in layout, i);
            Vector2 center = new(cardRect.X + MathF.Round(50f * s), cardRect.Y + (cardRect.Height * 0.5f));
            SkillBarWidget.DrawSkillGlyph(batch, skill.AnimArchetype, center, SkillBarWidget.ResolveAccentColor(skill), rank > 0);
        }
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skills);
        ushort sp = world.LocalUnspentSkillPoints;
        PlayerModalTabs.DrawWindowChromeText(
            spriteBatch,
            font,
            in layout,
            PlayerModalTab.Skills,
            hasSkills,
            $"{world.LocalPlayerClass} Skills",
            $"Skill Points: {sp}",
            sp > 0);

        ColorRgba black = CarbonUiPalette.TextOutline;
        for (int i = 0; i < _skills.Count && i < 4; i++)
        {
            SkillDefinition skill = _skills[i];
            byte rank = world.GetSkillRank(skill.Id);
            byte previewRank = rank > 0 ? rank : (byte)1;
            SkillSpec curSpec = skill.EvaluateRank(previewRank);
            RectangleF cardRect = GetRowRect(in layout, i);
            float textX = cardRect.X + MathF.Round(100f * s);

            spriteBatch.DrawOutlinedString(
                font,
                $"{skill.DisplayName}  (Rank {rank}/{skill.MaxRank})",
                new Vector2(textX, cardRect.Y + MathF.Round(20f * s)),
                CarbonUiPalette.TextPrimary,
                black,
                1,
                layout.TitleScale);
            string detail = $"{skill.Summary}  |  MP {(int)curSpec.ManaCost}  CD {curSpec.CooldownSeconds:F1}s";
            spriteBatch.DrawOutlinedString(
                font,
                CarbonUiSkin.TruncateToWidth(font, detail, cardRect.Width - MathF.Round(264f * s), layout.TextScale),
                new Vector2(textX, cardRect.Y + MathF.Round(56f * s)),
                CarbonUiPalette.TextMuted,
                black,
                1,
                layout.TextScale);

            RectangleF btnRect = GetUpgradeButtonRect(in layout, i);
            string btnTxt = rank >= skill.MaxRank ? "MAX" : (rank == 0 ? "UNLOCK" : "RANK UP");
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, btnTxt, in btnRect, sp > 0 && rank < skill.MaxRank ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted, black, layout.TextScale);
        }
    }

    private static RectangleF GetRowRect(in PlayerModalLayout layout, int index)
    {
        float s = layout.Scale;
        float pad = MathF.Round(16f * s);
        return new RectangleF(
            layout.ContentX + pad,
            layout.ContentY + pad + (index * MathF.Round(118f * s)),
            layout.ContentW - (pad * 2f),
            MathF.Round(106f * s));
    }

    private static RectangleF GetUpgradeButtonRect(in PlayerModalLayout layout, int index)
    {
        float s = layout.Scale;
        RectangleF card = GetRowRect(in layout, index);
        float btnW = MathF.Round(132f * s);
        float btnH = MathF.Round(44f * s);
        return new RectangleF(card.Right - btnW - MathF.Round(16f * s), card.Y + MathF.Round(31f * s), btnW, btnH);
    }
}
