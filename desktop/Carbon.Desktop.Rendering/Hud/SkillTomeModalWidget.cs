// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
/// Centered RPG Skill Mastery Tome modal (<c>[K]</c>) dynamically iterating <see cref="SkillCatalog.Skills"/>
/// so players can unlock locked skills (<c>Rank 0 -> 1</c>) and invest earned Skill Points (SRP / OCP).
/// </summary>
internal sealed class SkillTomeModalWidget
{
    private const float PanelH = 404f;
    private const float CardW = 190f;
    private const float CardH = 304f;
    private const float CardGap = 15f;

    public bool ContainsPoint(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        float panelW = ComputePanelWidth(SkillBarWidget.GetOrderedSkills().Count);
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight, panelW);
        return mousePos.X >= ox && mousePos.X <= ox + panelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH;
    }

    public bool TryHitTestCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        float panelW = ComputePanelWidth(SkillBarWidget.GetOrderedSkills().Count);
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight, panelW);
        return GetCloseButtonRect(ox, oy, panelW).Contains(mousePos);
    }

    public bool TryHitTestUpgradeButton(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort skillId)
    {
        IReadOnlyList<SkillDefinition> skills = SkillBarWidget.GetOrderedSkills();
        float panelW = ComputePanelWidth(skills.Count);
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight, panelW);
        for (int i = 0; i < skills.Count; i++)
        {
            if (GetUpgradeButtonRect(ox, oy, i).Contains(mousePos))
            {
                skillId = skills[i].Id;
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
        IReadOnlyList<SkillDefinition> skills = SkillBarWidget.GetOrderedSkills();
        float panelW = ComputePanelWidth(skills.Count);
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight, panelW);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, panelW, PanelH), new ColorRgba(56, 189, 248, 240), bodyAlpha: 246);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy, panelW);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(spriteBatch, in closeBtn, hoverClose ? new ColorRgba(220, 38, 38, 245) : new ColorRgba(153, 27, 27, 235), new ColorRgba(248, 113, 113, 245));

        ushort unspent = world.LocalUnspentSkillPoints;
        for (int i = 0; i < skills.Count; i++)
        {
            SkillDefinition skill = skills[i];
            byte rank = world.GetSkillRank(skill.Id);
            RectangleF cardRect = GetCardRect(ox, oy, i);
            ColorRgba accent = SkillBarWidget.ResolveAccentColor(skill);
            skin.DrawCard(spriteBatch, in cardRect, rank > 0 ? accent : new ColorRgba(71, 85, 105, 220), bodyAlpha: 236);

            RectangleF iconSocket = new(cardRect.X + 71f, cardRect.Y + 14f, 48f, 48f);
            skin.DrawSlot(spriteBatch, in iconSocket, new ColorRgba(10, 15, 28, 244), rank > 0 ? accent : new ColorRgba(71, 85, 105, 210));

            RectangleF btnRect = GetUpgradeButtonRect(ox, oy, i);
            bool canAct = unspent > 0 && rank < skill.MaxRank;
            bool hover = mouseScreenPos.HasValue && btnRect.Contains(mouseScreenPos.Value);
            ColorRgba btnFill = canAct
                ? (hover ? new ColorRgba(16, 185, 129, 245) : new ColorRgba(5, 150, 105, 235))
                : new ColorRgba(20, 28, 44, 215);
            ColorRgba btnBorder = canAct ? new ColorRgba(110, 231, 183, 250) : new ColorRgba(71, 85, 105, 200);
            skin.DrawButton(spriteBatch, in btnRect, btnFill, btnBorder);
        }
    }

    public void DrawGeometry(GeometryBatch batch, IClientWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        IReadOnlyList<SkillDefinition> skills = SkillBarWidget.GetOrderedSkills();
        float panelW = ComputePanelWidth(skills.Count);
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight, panelW);
        for (int i = 0; i < skills.Count; i++)
        {
            SkillDefinition skill = skills[i];
            byte rank = world.GetSkillRank(skill.Id);
            RectangleF cardRect = GetCardRect(ox, oy, i);
            Vector2 center = new(cardRect.X + 95f, cardRect.Y + 38f);
            SkillBarWidget.DrawSkillGlyph(batch, skill.AnimArchetype, center, SkillBarWidget.ResolveAccentColor(skill), rank > 0);

            int maxPips = Math.Min(5, (int)skill.MaxRank);
            for (int p = 0; p < maxPips; p++)
            {
                float px = cardRect.X + 44f + (p * 22f);
                float py = cardRect.Y + 108f;
                bool filled = p < rank;
                batch.FillRectangle(new RectangleF(px, py, 16f, 6f), filled ? new ColorRgba(250, 204, 21, 250) : new ColorRgba(20, 30, 50, 230));
                batch.DrawRectangle(new RectangleF(px, py, 16f, 6f), filled ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(71, 85, 105, 210), 1f);
            }
        }
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        IReadOnlyList<SkillDefinition> skills = SkillBarWidget.GetOrderedSkills();
        float panelW = ComputePanelWidth(skills.Count);
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight, panelW);
        ColorRgba black = new(8, 10, 16, 255);

        spriteBatch.DrawOutlinedString(font, "ARCANE & MARTIAL SKILL TOME [K]", new Vector2(ox + 20f, oy + 16f), new ColorRgba(125, 211, 252, 255), black, 1, 1.5f);
        ushort sp = world.LocalUnspentSkillPoints;
        spriteBatch.DrawOutlinedString(
            font,
            $"UNSPENT SKILL POINTS: {sp}",
            new Vector2(ox + 22f, oy + 44f),
            sp > 0 ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(148, 163, 184, 255),
            black,
            1,
            1f);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy, panelW);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "X", in closeBtn, ColorRgba.White, black, 1.5f);

        for (int i = 0; i < skills.Count; i++)
        {
            SkillDefinition skill = skills[i];
            byte rank = world.GetSkillRank(skill.Id);
            RectangleF cardRect = GetCardRect(ox, oy, i);
            byte previewRank = rank > 0 ? rank : (byte)1;
            SkillSpec curSpec = skill.EvaluateRank(previewRank);
            ColorRgba accent = SkillBarWidget.ResolveAccentColor(skill);

            string title = CarbonUiSkin.TruncateToWidth(font, curSpec.DisplayName, CardW - 16f, 1.5f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, title, new RectangleF(cardRect.X + 8f, cardRect.Y + 68f, CardW - 16f, 20f), accent, black, 1.5f);
            string rankTag = rank == 0 ? $"LOCKED (0/{skill.MaxRank})" : $"RANK {rank} / {skill.MaxRank}";
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, rankTag, new RectangleF(cardRect.X + 8f, cardRect.Y + 88f, CardW - 16f, 16f), rank > 0 ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(248, 113, 113, 255), black, 1f);

            string effectLine = skill.EffectKind == SkillEffectKind.SelfRestore
                ? $"Heal {(int)MathF.Round(curSpec.SelfHealRatio * 100f)}% HP +{(int)curSpec.StaminaRestore} STA"
                : $"Potency: {(int)MathF.Round(curSpec.DamageMultiplier * 100f)}% DMG";
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, CarbonUiSkin.TruncateToWidth(font, effectLine, CardW - 16f, 1f), new RectangleF(cardRect.X + 8f, cardRect.Y + 128f, CardW - 16f, 18f), ColorRgba.White, black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"MP {(int)curSpec.ManaCost} | STA {(int)curSpec.StaminaCost}", new RectangleF(cardRect.X + 8f, cardRect.Y + 150f, CardW - 16f, 16f), new ColorRgba(125, 211, 252, 255), black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"Cooldown: {curSpec.CooldownSeconds:F1}s", new RectangleF(cardRect.X + 8f, cardRect.Y + 168f, CardW - 16f, 16f), new ColorRgba(203, 213, 225, 255), black, 1f);

            if (rank > 0 && rank < skill.MaxRank)
            {
                SkillSpec nextSpec = skill.EvaluateRank((byte)(rank + 1));
                string nextBonus = skill.EffectKind == SkillEffectKind.SelfRestore
                    ? $"Next: {(int)MathF.Round(nextSpec.SelfHealRatio * 100f)}% HP"
                    : $"Next: {(int)MathF.Round(nextSpec.DamageMultiplier * 100f)}% DMG";
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, nextBonus, new RectangleF(cardRect.X + 8f, cardRect.Y + 196f, CardW - 16f, 16f), new ColorRgba(74, 222, 128, 255), black, 1f);
            }
            else if (rank == 0)
            {
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, "Cannot cast until unlocked", new RectangleF(cardRect.X + 8f, cardRect.Y + 196f, CardW - 16f, 16f), new ColorRgba(148, 163, 184, 255), black, 1f);
            }

            RectangleF btnRect = GetUpgradeButtonRect(ox, oy, i);
            string btnTxt = rank >= skill.MaxRank
                ? "MAX RANK"
                : (rank == 0 ? "UNLOCK (1 SP)" : "RANK UP (1 SP)");
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, btnTxt, in btnRect, sp > 0 && rank < skill.MaxRank ? ColorRgba.White : new ColorRgba(148, 163, 184, 225), black, 1f);
        }
    }

    private static float ComputePanelWidth(int skillCount) =>
        skillCount <= 0 ? 420f : MathF.Max(420f, 40f + (skillCount * CardW) + ((skillCount - 1) * CardGap));

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight, float panelW) =>
        (MathF.Floor((viewportWidth - panelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    private static RectangleF GetCloseButtonRect(float ox, float oy, float panelW) => new(ox + panelW - 38f, oy + 10f, 26f, 24f);

    private static RectangleF GetCardRect(float ox, float oy, int index) =>
        new(ox + 20f + (index * (CardW + CardGap)), oy + 68f, CardW, CardH);

    private static RectangleF GetUpgradeButtonRect(float ox, float oy, int index)
    {
        RectangleF card = GetCardRect(ox, oy, index);
        return new RectangleF(card.X + 16f, card.Bottom - 44f, CardW - 32f, 30f);
    }
}
