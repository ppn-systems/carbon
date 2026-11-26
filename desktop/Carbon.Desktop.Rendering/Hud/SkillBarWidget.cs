// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Compact 3-slot RPG active skill bar (<c>[Q] Whirlwind</c>, <c>[R] Arcane Nova</c>, <c>[V] Vital Surge</c>)
/// centered cleanly above the hotbar with mana/stamina readiness, cooldown sweeps, and hover tooltip cards (SRP).
/// </summary>
internal sealed class SkillBarWidget
{
    private const int SkillCount = 3;
    private const float SlotSize = 44f;
    private const float Gap = 8f;

    public bool TryHitTestSkill(Vector2 mousePos, int viewportWidth, int viewportHeight, out byte skillSlot)
    {
        skillSlot = 0;
        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight);
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

    public int DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight);
        float totalW = (SkillCount * SlotSize) + ((SkillCount - 1) * Gap);

        batch.FillRectangle(new RectangleF(startX - 6f, startY - 5f, totalW + 12f, SlotSize + 10f), new ColorRgba(8, 12, 22, 228));
        batch.DrawRectangle(new RectangleF(startX - 6f, startY - 5f, totalW + 12f, SlotSize + 10f), new ColorRgba(56, 189, 248, 175), 1.4f);

        int hoveredSkill = -1;
        for (byte s = 1; s <= SkillCount; s++)
        {
            float sx = startX + ((s - 1) * (SlotSize + Gap));
            RectangleF rect = new(sx, startY, SlotSize, SlotSize);
            bool hovered = mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value);
            if (hovered)
            {
                hoveredSkill = s;
            }

            _ = CharacterProgressionPolicy.TryGetSkillSpec(s, out SkillSpec spec);
            float cd = world.GetSkillCooldown(s);
            bool hasResources = world.LocalPlayerMana >= spec.ManaCost && world.LocalPlayerStamina >= spec.StaminaCost;
            bool ready = cd <= 0.02f && hasResources;

            ColorRgba accent = ResolveAccentColor(s);
            batch.FillRectangle(rect, hovered ? new ColorRgba(24, 36, 58, 242) : new ColorRgba(13, 20, 34, 232));
            DrawSkillGlyph(batch, s, new Vector2(sx + (SlotSize * 0.5f), startY + (SlotSize * 0.48f)), accent, ready);

            if (cd > 0.02f && spec.CooldownSeconds > 0.1f)
            {
                float frac = Math.Clamp(cd / spec.CooldownSeconds, 0f, 1f);
                float maskH = SlotSize * frac;
                batch.FillRectangle(new RectangleF(sx, startY + SlotSize - maskH, SlotSize, maskH), new ColorRgba(6, 10, 20, 195));
            }
            else if (!hasResources)
            {
                batch.FillRectangle(rect, new ColorRgba(12, 16, 28, 130));
            }

            ColorRgba border = ready ? accent : new ColorRgba(71, 85, 105, 220);
            batch.DrawRectangle(rect, hovered ? ColorRgba.White : border, ready ? 1.8f : 1.2f);
        }

        if (hoveredSkill > 0)
        {
            float tipX = Math.Clamp(startX - 50f, 12f, viewportWidth - 270f);
            RectangleF tipBox = new(tipX, startY - 68f, 258f, 60f);
            ColorRgba accent = ResolveAccentColor((byte)hoveredSkill);
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
        int hoveredSkill)
    {
        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight);
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

            spriteBatch.DrawOutlinedString(font, key, new Vector2(sx + 4f, startY + 3f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
            _ = CharacterProgressionPolicy.TryGetSkillSpec(s, out SkillSpec spec);
            spriteBatch.DrawOutlinedString(font, $"{(int)spec.ManaCost}", new Vector2(sx + SlotSize - 18f, startY + SlotSize - 13f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);

            float cd = world.GetSkillCooldown(s);
            if (cd > 0.05f)
            {
                string cdTxt = $"{cd:F1}s";
                TextMeasureResult m = font.MeasureString(cdTxt, TextLayoutOptions.Default, 1.5f);
                spriteBatch.DrawOutlinedString(
                    font,
                    cdTxt,
                    new Vector2(MathF.Floor(sx + ((SlotSize - m.Width) * 0.5f)), startY + 16f),
                    ColorRgba.White,
                    black,
                    1,
                    1.5f);
            }
        }

        if (hoveredSkill > 0 && CharacterProgressionPolicy.TryGetSkillSpec((byte)hoveredSkill, out SkillSpec hoveredSpec))
        {
            float tipX = Math.Clamp(startX - 50f, 12f, viewportWidth - 270f);
            float tipY = startY - 68f;
            spriteBatch.DrawOutlinedString(font, hoveredSpec.DisplayName, new Vector2(tipX + 8f, tipY + 6f), ResolveAccentColor((byte)hoveredSkill), black, 1, 1.5f);
            spriteBatch.DrawOutlinedString(font, $"MP {(int)hoveredSpec.ManaCost} | STA {(int)hoveredSpec.StaminaCost} | CD {hoveredSpec.CooldownSeconds:F0}s", new Vector2(tipX + 8f, tipY + 26f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
            string desc = hoveredSpec.SkillSlot switch
            {
                CharacterProgressionPolicy.SkillWhirlwind => "360-deg spin cleave (1.45x Weapon DMG)",
                CharacterProgressionPolicy.SkillArcaneNova => "Directional nova blast (1.85x Weapon DMG)",
                _ => "Restore 30% MaxHP & +45 Stamina",
            };
            spriteBatch.DrawOutlinedString(font, desc, new Vector2(tipX + 8f, tipY + 41f), new ColorRgba(226, 232, 240, 255), black, 1, 1f);
        }
    }

    private static (float StartX, float StartY) ComputeOrigin(int viewportWidth, int viewportHeight)
    {
        float skillW = (SkillCount * SlotSize) + ((SkillCount - 1) * Gap);
        return (MathF.Floor((viewportWidth - skillW) * 0.5f), MathF.Floor(viewportHeight - 144f));
    }

    private static ColorRgba ResolveAccentColor(byte skillSlot) => skillSlot switch
    {
        CharacterProgressionPolicy.SkillWhirlwind => new ColorRgba(249, 115, 22, 255),
        CharacterProgressionPolicy.SkillArcaneNova => new ColorRgba(56, 189, 248, 255),
        _ => new ColorRgba(16, 185, 129, 255),
    };

    private static void DrawSkillGlyph(GeometryBatch batch, byte skillSlot, Vector2 center, ColorRgba accent, bool ready)
    {
        ColorRgba col = ready
            ? accent
            : new ColorRgba((byte)(accent.R / 2), (byte)(accent.G / 2), (byte)(accent.B / 2), 200);

        if (skillSlot == CharacterProgressionPolicy.SkillWhirlwind)
        {
            batch.DrawCircle(center, 11f, col, 16, 2.4f);
            batch.DrawLine(center + new Vector2(-9f, -5f), center + new Vector2(9f, 5f), ColorRgba.White, 2f);
            batch.DrawLine(center + new Vector2(-9f, 5f), center + new Vector2(9f, -5f), col, 2f);
        }
        else if (skillSlot == CharacterProgressionPolicy.SkillArcaneNova)
        {
            Span<Vector2> diamond =
            [
                center + new Vector2(0f, -12f),
                center + new Vector2(10f, 0f),
                center + new Vector2(0f, 12f),
                center + new Vector2(-10f, 0f),
            ];
            batch.FillConvexPolygon(diamond, col);
            batch.FillCircle(center, 4.5f, new ColorRgba(224, 242, 254, 255), 10);
        }
        else
        {
            batch.FillRectangle(new RectangleF(center.X - 3f, center.Y - 10f, 6f, 20f), col);
            batch.FillRectangle(new RectangleF(center.X - 10f, center.Y - 3f, 20f, 6f), col);
        }
    }
}
