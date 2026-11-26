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
/// showing procedural ability emblems, mana/stamina readiness, and live cooldown timers (SRP).
/// </summary>
internal sealed class SkillBarWidget
{
    private const int SkillCount = 3;
    private const float SlotSize = 46f;
    private const float Gap = 6f;

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

    public void DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight);
        float totalW = (SkillCount * SlotSize) + ((SkillCount - 1) * Gap);

        batch.FillRectangle(new RectangleF(startX - 6f, startY - 6f, totalW + 12f, SlotSize + 12f), new ColorRgba(8, 12, 22, 228));
        batch.DrawRectangle(new RectangleF(startX - 6f, startY - 6f, totalW + 12f, SlotSize + 12f), new ColorRgba(56, 189, 248, 175), 1.4f);

        for (byte s = 1; s <= SkillCount; s++)
        {
            float sx = startX + ((s - 1) * (SlotSize + Gap));
            RectangleF rect = new(sx, startY, SlotSize, SlotSize);
            bool hovered = mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value);

            _ = CharacterProgressionPolicy.TryGetSkillSpec(s, out _, out float mpCost, out float stamCost, out float totalCd);
            float cd = world.GetSkillCooldown(s);
            bool hasResources = world.LocalPlayerMana >= mpCost && world.LocalPlayerStamina >= stamCost;
            bool ready = cd <= 0.02f && hasResources;

            ColorRgba accent = s switch
            {
                CharacterProgressionPolicy.SkillWhirlwind => new ColorRgba(249, 115, 22, 255),
                CharacterProgressionPolicy.SkillArcaneNova => new ColorRgba(56, 189, 248, 255),
                _ => new ColorRgba(16, 185, 129, 255),
            };

            batch.FillRectangle(rect, hovered ? new ColorRgba(24, 36, 58, 242) : new ColorRgba(13, 20, 34, 232));
            DrawSkillGlyph(batch, s, new Vector2(sx + (SlotSize * 0.5f), startY + (SlotSize * 0.48f)), accent, ready);

            if (cd > 0.02f && totalCd > 0.1f)
            {
                float frac = Math.Clamp(cd / totalCd, 0f, 1f);
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
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
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
            _ = CharacterProgressionPolicy.TryGetSkillSpec(s, out _, out float mpCost, out float _, out float _);
            spriteBatch.DrawOutlinedString(font, $"{(int)mpCost}", new Vector2(sx + SlotSize - 18f, startY + SlotSize - 13f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);

            float cd = world.GetSkillCooldown(s);
            if (cd > 0.05f)
            {
                string cdTxt = $"{cd:F1}s";
                TextMeasureResult m = font.MeasureString(cdTxt, TextLayoutOptions.Default, 1.5f);
                spriteBatch.DrawOutlinedString(
                    font,
                    cdTxt,
                    new Vector2(MathF.Floor(sx + ((SlotSize - m.Width) * 0.5f)), startY + 17f),
                    ColorRgba.White,
                    black,
                    1,
                    1.5f);
            }
        }
    }

    private static (float StartX, float StartY) ComputeOrigin(int viewportWidth, int viewportHeight)
    {
        const float hotbarWidth = (9 * 50f) + (8 * 5f);
        float skillW = (SkillCount * SlotSize) + ((SkillCount - 1) * Gap);
        float hotbarRight = ((viewportWidth + hotbarWidth) * 0.5f) + 14f;
        if (hotbarRight + skillW + 16f <= viewportWidth)
        {
            return (MathF.Floor(hotbarRight), MathF.Floor(viewportHeight - SlotSize - 38f));
        }

        return (MathF.Floor(viewportWidth - skillW - 18f), MathF.Floor(viewportHeight - SlotSize - 102f));
    }

    private static void DrawSkillGlyph(GeometryBatch batch, byte skillSlot, Vector2 center, ColorRgba accent, bool ready)
    {
        ColorRgba col = ready
            ? accent
            : new ColorRgba((byte)(accent.R / 2), (byte)(accent.G / 2), (byte)(accent.B / 2), 200);

        if (skillSlot == CharacterProgressionPolicy.SkillWhirlwind)
        {
            batch.DrawCircle(center, 12f, col, 16, 2.5f);
            batch.DrawLine(center + new Vector2(-10f, -6f), center + new Vector2(10f, 6f), ColorRgba.White, 2f);
            batch.DrawLine(center + new Vector2(-10f, 6f), center + new Vector2(10f, -6f), col, 2f);
        }
        else if (skillSlot == CharacterProgressionPolicy.SkillArcaneNova)
        {
            Span<Vector2> diamond =
            [
                center + new Vector2(0f, -13f),
                center + new Vector2(11f, 0f),
                center + new Vector2(0f, 13f),
                center + new Vector2(-11f, 0f),
            ];
            batch.FillConvexPolygon(diamond, col);
            batch.FillCircle(center, 5f, new ColorRgba(224, 242, 254, 255), 10);
        }
        else
        {
            batch.FillRectangle(new RectangleF(center.X - 3.5f, center.Y - 11f, 7f, 22f), col);
            batch.FillRectangle(new RectangleF(center.X - 11f, center.Y - 3.5f, 22f, 7f), col);
        }
    }
}
