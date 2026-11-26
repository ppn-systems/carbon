// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Centered RPG Skill Mastery Tome modal (<c>[K]</c>) allowing players to unlock locked skills (<c>Rank 0 -> 1</c>)
/// and invest earned Skill Points up to <c>Rank 5</c> across Whirlwind, Arcane Nova, and Vital Surge (SRP).
/// </summary>
internal sealed class SkillTomeModalWidget
{
    private const float PanelW = 640f;
    private const float PanelH = 404f;
    private const float CardW = 190f;
    private const float CardH = 304f;
    private const float CardGap = 15f;

    public bool ContainsPoint(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return mousePos.X >= ox && mousePos.X <= ox + PanelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH;
    }

    public bool TryHitTestCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetCloseButtonRect(ox, oy).Contains(mousePos);
    }

    public bool TryHitTestUpgradeButton(Vector2 mousePos, int viewportWidth, int viewportHeight, out CharacterPointTarget target)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        for (int i = 0; i < 3; i++)
        {
            if (GetUpgradeButtonRect(ox, oy, i).Contains(mousePos))
            {
                target = i switch
                {
                    0 => CharacterPointTarget.SkillWhirlwind,
                    1 => CharacterPointTarget.SkillArcaneNova,
                    _ => CharacterPointTarget.SkillVitalSurge,
                };
                return true;
            }
        }

        target = CharacterPointTarget.SkillWhirlwind;
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
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), new ColorRgba(56, 189, 248, 240), bodyAlpha: 246);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(spriteBatch, in closeBtn, hoverClose ? new ColorRgba(220, 38, 38, 245) : new ColorRgba(153, 27, 27, 235), new ColorRgba(248, 113, 113, 245));

        ushort unspent = world.LocalUnspentSkillPoints;
        for (int i = 0; i < 3; i++)
        {
            byte slot = (byte)(i + 1);
            byte rank = world.GetSkillRank(slot);
            RectangleF cardRect = GetCardRect(ox, oy, i);
            ColorRgba accent = ResolveAccent(slot);
            skin.DrawCard(spriteBatch, in cardRect, rank > 0 ? accent : new ColorRgba(71, 85, 105, 220), bodyAlpha: 236);

            RectangleF iconSocket = new(cardRect.X + 71f, cardRect.Y + 14f, 48f, 48f);
            skin.DrawSlot(spriteBatch, in iconSocket, new ColorRgba(10, 15, 28, 244), rank > 0 ? accent : new ColorRgba(71, 85, 105, 210));

            RectangleF btnRect = GetUpgradeButtonRect(ox, oy, i);
            bool canAct = unspent > 0 && rank < CharacterProgressionPolicy.MaxSkillRank;
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
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        for (int i = 0; i < 3; i++)
        {
            byte slot = (byte)(i + 1);
            byte rank = world.GetSkillRank(slot);
            RectangleF cardRect = GetCardRect(ox, oy, i);
            Vector2 center = new(cardRect.X + 95f, cardRect.Y + 38f);
            SkillBarWidget.DrawSkillGlyph(batch, slot, center, ResolveAccent(slot), rank > 0);

            // Rank progress pips
            for (int p = 0; p < CharacterProgressionPolicy.MaxSkillRank; p++)
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
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
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

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "X", in closeBtn, ColorRgba.White, black, 1.5f);

        for (int i = 0; i < 3; i++)
        {
            byte slot = (byte)(i + 1);
            byte rank = world.GetSkillRank(slot);
            RectangleF cardRect = GetCardRect(ox, oy, i);
            byte previewRank = rank > 0 ? rank : (byte)1;
            _ = CharacterProgressionPolicy.TryGetSkillSpec(slot, previewRank, out SkillSpec curSpec);

            CarbonUiSkin.DrawCenteredText(spriteBatch, font, curSpec.DisplayName, new RectangleF(cardRect.X + 8f, cardRect.Y + 68f, CardW - 16f, 20f), ResolveAccent(slot), black, 1.5f);
            string rankTag = rank == 0 ? "LOCKED (0/5)" : $"RANK {rank} / {CharacterProgressionPolicy.MaxSkillRank}";
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, rankTag, new RectangleF(cardRect.X + 8f, cardRect.Y + 88f, CardW - 16f, 16f), rank > 0 ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(248, 113, 113, 255), black, 1f);

            string effectLine = slot switch
            {
                CharacterProgressionPolicy.SkillWhirlwind => $"AOE Cleave: {(int)MathF.Round(curSpec.DamageMultiplier * 100f)}% DMG",
                CharacterProgressionPolicy.SkillArcaneNova => $"Arcane Cone: {(int)MathF.Round(curSpec.DamageMultiplier * 100f)}% DMG",
                _ => $"Heal {(int)MathF.Round(curSpec.SelfHealRatio * 100f)}% HP +{(int)curSpec.StaminaRestore} STA",
            };
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, effectLine, new RectangleF(cardRect.X + 8f, cardRect.Y + 128f, CardW - 16f, 18f), ColorRgba.White, black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"MP {(int)curSpec.ManaCost} | STA {(int)curSpec.StaminaCost}", new RectangleF(cardRect.X + 8f, cardRect.Y + 150f, CardW - 16f, 16f), new ColorRgba(125, 211, 252, 255), black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"Cooldown: {curSpec.CooldownSeconds:F1}s", new RectangleF(cardRect.X + 8f, cardRect.Y + 168f, CardW - 16f, 16f), new ColorRgba(203, 213, 225, 255), black, 1f);

            if (rank > 0 && rank < CharacterProgressionPolicy.MaxSkillRank &&
                CharacterProgressionPolicy.TryGetSkillSpec(slot, (byte)(rank + 1), out SkillSpec nextSpec))
            {
                string nextBonus = slot == CharacterProgressionPolicy.SkillVitalSurge
                    ? $"Next: {(int)MathF.Round(nextSpec.SelfHealRatio * 100f)}% HP"
                    : $"Next: {(int)MathF.Round(nextSpec.DamageMultiplier * 100f)}% DMG";
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, nextBonus, new RectangleF(cardRect.X + 8f, cardRect.Y + 196f, CardW - 16f, 16f), new ColorRgba(74, 222, 128, 255), black, 1f);
            }
            else if (rank == 0)
            {
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, "Cannot cast until unlocked", new RectangleF(cardRect.X + 8f, cardRect.Y + 196f, CardW - 16f, 16f), new ColorRgba(148, 163, 184, 255), black, 1f);
            }

            RectangleF btnRect = GetUpgradeButtonRect(ox, oy, i);
            string btnTxt = rank >= CharacterProgressionPolicy.MaxSkillRank
                ? "MAX RANK"
                : (rank == 0 ? "UNLOCK (1 SP)" : "RANK UP (1 SP)");
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, btnTxt, in btnRect, sp > 0 && rank < 5 ? ColorRgba.White : new ColorRgba(148, 163, 184, 225), black, 1f);
        }
    }

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    private static RectangleF GetCloseButtonRect(float ox, float oy) => new(ox + PanelW - 38f, oy + 10f, 26f, 24f);

    private static RectangleF GetCardRect(float ox, float oy, int index) =>
        new(ox + 20f + (index * (CardW + CardGap)), oy + 68f, CardW, CardH);

    private static RectangleF GetUpgradeButtonRect(float ox, float oy, int index)
    {
        RectangleF card = GetCardRect(ox, oy, index);
        return new RectangleF(card.X + 16f, card.Bottom - 44f, CardW - 32f, 30f);
    }

    private static ColorRgba ResolveAccent(byte skillSlot) => skillSlot switch
    {
        CharacterProgressionPolicy.SkillWhirlwind => new ColorRgba(249, 115, 22, 255),
        CharacterProgressionPolicy.SkillArcaneNova => new ColorRgba(56, 189, 248, 255),
        _ => new ColorRgba(16, 185, 129, 255),
    };
}
