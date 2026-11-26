// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
/// Compact 3-slot RPG active skill bar (<c>[Q] Whirlwind</c>, <c>[R] Arcane Nova</c>, <c>[V] Vital Surge</c>)
/// aligned cleanly alongside the bottom hotbar, showing locked padlocks when <c>Rank == 0</c>,
/// learned mastery pips (<c>1..5</c>), cooldown sweeps, and hover tooltip cards (SRP).
/// </summary>
internal sealed class SkillBarWidget
{
    private const int SkillCount = 3;
    private const float SlotSize = 42f;
    private const float Gap = 5f;
    private const float HotbarWidth = (9 * 44f) + (8 * 4f);

    public bool TryHitTestSkill(Vector2 mousePos, int viewportWidth, int viewportHeight, bool showHints, out byte skillSlot)
    {
        skillSlot = 0;
        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight, showHints);
        if (mousePos.Y < startY || mousePos.Y > startY + SlotSize)
        {
            return false;
        }

        for (int i = 0; i < SkillCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            if (mousePos.X >= sx && mousePos.X <= sx + SlotSize)
            {
                skillSlot = (byte)(i + 1);
                return true;
            }
        }

        return false;
    }

    public void DrawSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        bool showHints)
    {
        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight, showHints);
        float totalW = (SkillCount * SlotSize) + ((SkillCount - 1) * Gap);
        skin.DrawCard(spriteBatch, new RectangleF(startX - 7f, startY - 7f, totalW + 14f, SlotSize + 14f), new ColorRgba(56, 189, 248, 205), bodyAlpha: 230);

        for (byte s = 1; s <= SkillCount; s++)
        {
            float sx = startX + ((s - 1) * (SlotSize + Gap));
            byte rank = world.GetSkillRank(s);
            if (rank == 0)
            {
                skin.DrawSlot(spriteBatch, new RectangleF(sx, startY, SlotSize, SlotSize), new ColorRgba(10, 14, 24, 232), new ColorRgba(51, 65, 85, 195));
                continue;
            }

            _ = CharacterProgressionPolicy.TryGetSkillSpec(s, rank, out SkillSpec spec);
            float cd = world.GetSkillCooldown(s);
            bool ready = cd <= 0.02f && world.LocalPlayerMana >= spec.ManaCost && world.LocalPlayerStamina >= spec.StaminaCost;
            skin.DrawSlot(spriteBatch, new RectangleF(sx, startY, SlotSize, SlotSize), new ColorRgba(14, 22, 38, 238), ready ? ResolveAccentColor(s) : new ColorRgba(71, 85, 105, 220));
        }
    }

    public int DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        bool showHints,
        Vector2? mouseScreenPos)
    {
        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight, showHints);
        int hoveredSkill = -1;

        for (byte s = 1; s <= SkillCount; s++)
        {
            float sx = startX + ((s - 1) * (SlotSize + Gap));
            RectangleF rect = new(sx, startY, SlotSize, SlotSize);
            if (mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value))
            {
                hoveredSkill = s;
            }

            byte rank = world.GetSkillRank(s);
            ColorRgba accent = ResolveAccentColor(s);
            if (rank == 0)
            {
                DrawLockedPadlock(batch, new Vector2(sx + (SlotSize * 0.5f), startY + (SlotSize * 0.48f)));
                continue;
            }

            _ = CharacterProgressionPolicy.TryGetSkillSpec(s, rank, out SkillSpec spec);
            float cd = world.GetSkillCooldown(s);
            bool hasResources = world.LocalPlayerMana >= spec.ManaCost && world.LocalPlayerStamina >= spec.StaminaCost;
            bool ready = cd <= 0.02f && hasResources;

            DrawSkillGlyph(batch, s, new Vector2(sx + (SlotSize * 0.5f), startY + (SlotSize * 0.44f)), accent, ready);

            // Learned mastery pips along bottom-left
            for (int r = 0; r < rank && r < 5; r++)
            {
                batch.FillRectangle(new RectangleF(sx + 4f + (r * 4.5f), startY + SlotSize - 6f, 3f, 3f), new ColorRgba(250, 204, 21, 240));
            }

            if (cd > 0.02f && spec.CooldownSeconds > 0.1f)
            {
                float frac = Math.Clamp(cd / spec.CooldownSeconds, 0f, 1f);
                float maskH = SlotSize * frac;
                batch.FillRectangle(new RectangleF(sx + 2f, startY + SlotSize - maskH, SlotSize - 4f, maskH - 2f), new ColorRgba(6, 10, 20, 195));
            }
            else if (!hasResources)
            {
                batch.FillRectangle(new RectangleF(sx + 2f, startY + 2f, SlotSize - 4f, SlotSize - 4f), new ColorRgba(10, 14, 24, 125));
            }
        }

        if (hoveredSkill > 0)
        {
            float tipX = Math.Clamp(startX - 10f, 12f, viewportWidth - 282f);
            RectangleF tipBox = new(tipX, startY - 72f, 270f, 62f);
            ColorRgba accent = world.GetSkillRank((byte)hoveredSkill) > 0 ? ResolveAccentColor((byte)hoveredSkill) : new ColorRgba(148, 163, 184, 255);
            batch.FillRectangle(tipBox, new ColorRgba(8, 12, 24, 245));
            batch.DrawRectangle(tipBox, accent, 1.4f);
        }

        return hoveredSkill;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        bool showHints,
        int hoveredSkill)
    {
        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight, showHints);
        ColorRgba black = new(8, 10, 16, 255);

        for (byte s = 1; s <= SkillCount; s++)
        {
            float sx = startX + ((s - 1) * (SlotSize + Gap));
            string key = s switch
            {
                CharacterProgressionPolicy.SkillWhirlwind => "Q",
                CharacterProgressionPolicy.SkillArcaneNova => "R",
                _ => "V",
            };

            byte rank = world.GetSkillRank(s);
            spriteBatch.DrawOutlinedString(
                font,
                key,
                new Vector2(sx + 4f, startY + 3f),
                rank > 0 ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(148, 163, 184, 220),
                black,
                1,
                1f);

            if (rank == 0)
            {
                spriteBatch.DrawOutlinedString(font, "LOCK", new Vector2(sx + SlotSize - 24f, startY + SlotSize - 12f), new ColorRgba(148, 163, 184, 225), black, 1, 1f);
                continue;
            }

            _ = CharacterProgressionPolicy.TryGetSkillSpec(s, rank, out SkillSpec spec);
            spriteBatch.DrawOutlinedString(font, $"{(int)spec.ManaCost}", new Vector2(sx + SlotSize - 17f, startY + SlotSize - 12f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);

            float cd = world.GetSkillCooldown(s);
            if (cd > 0.05f)
            {
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"{cd:F1}s", new RectangleF(sx, startY, SlotSize, SlotSize), ColorRgba.White, black, 1.5f);
            }
        }

        if (hoveredSkill > 0)
        {
            byte hSlot = (byte)hoveredSkill;
            byte rank = world.GetSkillRank(hSlot);
            byte effectiveRank = rank > 0 ? rank : (byte)1;
            if (CharacterProgressionPolicy.TryGetSkillSpec(hSlot, effectiveRank, out SkillSpec hoveredSpec))
            {
                float tipX = Math.Clamp(startX - 10f, 12f, viewportWidth - 282f);
                float tipY = startY - 72f;
                if (rank == 0)
                {
                    spriteBatch.DrawOutlinedString(font, $"{hoveredSpec.DisplayName} [LOCKED]", new Vector2(tipX + 8f, tipY + 6f), new ColorRgba(250, 204, 21, 255), black, 1, 1.5f);
                    spriteBatch.DrawOutlinedString(font, "Requires 1 Skill Point to unlock", new Vector2(tipX + 8f, tipY + 26f), new ColorRgba(248, 113, 113, 255), black, 1, 1f);
                    spriteBatch.DrawOutlinedString(font, "Open Skill Tome [K] to unlock & upgrade", new Vector2(tipX + 8f, tipY + 42f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
                }
                else
                {
                    spriteBatch.DrawOutlinedString(font, $"{hoveredSpec.DisplayName} (Rank {rank}/5)", new Vector2(tipX + 8f, tipY + 6f), ResolveAccentColor(hSlot), black, 1, 1.5f);
                    spriteBatch.DrawOutlinedString(font, $"MP {(int)hoveredSpec.ManaCost} | STA {(int)hoveredSpec.StaminaCost} | CD {hoveredSpec.CooldownSeconds:F1}s", new Vector2(tipX + 8f, tipY + 26f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
                    string desc = hSlot switch
                    {
                        CharacterProgressionPolicy.SkillWhirlwind => $"360-deg spin cleave ({(int)MathF.Round(hoveredSpec.DamageMultiplier * 100f)}% Weapon DMG)",
                        CharacterProgressionPolicy.SkillArcaneNova => $"Directional nova ({(int)MathF.Round(hoveredSpec.DamageMultiplier * 100f)}% Weapon DMG)",
                        _ => $"Restore {(int)MathF.Round(hoveredSpec.SelfHealRatio * 100f)}% HP & +{(int)hoveredSpec.StaminaRestore} STA",
                    };
                    spriteBatch.DrawOutlinedString(font, desc, new Vector2(tipX + 8f, tipY + 42f), new ColorRgba(226, 232, 240, 255), black, 1, 1f);
                }
            }
        }
    }

    internal static void DrawSkillGlyph(GeometryBatch batch, byte skillSlot, Vector2 center, ColorRgba accent, bool ready)
    {
        ColorRgba col = ready
            ? accent
            : new ColorRgba((byte)(accent.R / 2), (byte)(accent.G / 2), (byte)(accent.B / 2), 205);

        if (skillSlot == CharacterProgressionPolicy.SkillWhirlwind)
        {
            batch.DrawCircle(center, 10f, col, 16, 2.2f);
            batch.DrawLine(center + new Vector2(-8f, -4f), center + new Vector2(8f, 4f), ColorRgba.White, 1.8f);
            batch.DrawLine(center + new Vector2(-8f, 4f), center + new Vector2(8f, -4f), col, 1.8f);
        }
        else if (skillSlot == CharacterProgressionPolicy.SkillArcaneNova)
        {
            Span<Vector2> diamond =
            [
                center + new Vector2(0f, -11f),
                center + new Vector2(9f, 0f),
                center + new Vector2(0f, 11f),
                center + new Vector2(-9f, 0f),
            ];
            batch.FillConvexPolygon(diamond, col);
            batch.FillCircle(center, 4f, new ColorRgba(224, 242, 254, 255), 10);
        }
        else
        {
            batch.FillRectangle(new RectangleF(center.X - 2.5f, center.Y - 9f, 5f, 18f), col);
            batch.FillRectangle(new RectangleF(center.X - 9f, center.Y - 2.5f, 18f, 5f), col);
        }
    }

    private static void DrawLockedPadlock(GeometryBatch batch, Vector2 center)
    {
        ColorRgba steel = new(148, 163, 184, 200);
        batch.DrawCircle(center + new Vector2(0f, -4f), 4.5f, steel, 10, 1.5f);
        batch.FillRectangle(new RectangleF(center.X - 5.5f, center.Y - 2f, 11f, 8.5f), new ColorRgba(71, 85, 105, 225));
        batch.DrawRectangle(new RectangleF(center.X - 5.5f, center.Y - 2f, 11f, 8.5f), steel, 1f);
        batch.FillCircle(center + new Vector2(0f, 2f), 1.4f, new ColorRgba(12, 18, 34, 255), 6);
    }

    private static (float StartX, float StartY) ComputeOrigin(int viewportWidth, int viewportHeight, bool showHints)
    {
        float hotbarX = MathF.Floor((viewportWidth - HotbarWidth) * 0.5f);
        float skillW = (SkillCount * SlotSize) + ((SkillCount - 1) * Gap);
        float startX = MathF.Max(12f, hotbarX - skillW - 14f);
        float startY = viewportHeight - (showHints ? 75f : 56f);
        return (startX, startY);
    }

    private static ColorRgba ResolveAccentColor(byte skillSlot) => skillSlot switch
    {
        CharacterProgressionPolicy.SkillWhirlwind => new ColorRgba(249, 115, 22, 255),
        CharacterProgressionPolicy.SkillArcaneNova => new ColorRgba(56, 189, 248, 255),
        _ => new ColorRgba(16, 185, 129, 255),
    };
}
