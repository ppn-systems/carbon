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
/// Compact data-driven RPG active skill bar rendering every skill registered in <see cref="SkillCatalog.Skills"/>
/// alongside the bottom hotbar, showing locked padlocks when <c>Rank == 0</c>, learned mastery pips,
/// cooldown sweeps, and hover tooltip cards (SRP / OCP).
/// </summary>
internal sealed class SkillBarWidget
{
    private const float SlotSize = 42f;
    private const float Gap = 5f;
    private const float HotbarWidth = (9 * 44f) + (8 * 4f);

    private static SkillDefinition[]? s_orderedSkills;

    internal static IReadOnlyList<SkillDefinition> GetOrderedSkills()
    {
        if (s_orderedSkills is not null)
        {
            return s_orderedSkills;
        }

        List<SkillDefinition> list = [.. SkillCatalog.Skills.All];
        list.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        s_orderedSkills = [.. list];
        return s_orderedSkills;
    }

    public bool TryHitTestSkill(Vector2 mousePos, int viewportWidth, int viewportHeight, bool showHints, out ushort skillId)
    {
        skillId = 0;
        IReadOnlyList<SkillDefinition> skills = GetOrderedSkills();
        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight, showHints, skills.Count);
        if (mousePos.Y < startY || mousePos.Y > startY + SlotSize)
        {
            return false;
        }

        for (int i = 0; i < skills.Count; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            if (mousePos.X >= sx && mousePos.X <= sx + SlotSize)
            {
                skillId = skills[i].Id;
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
        IReadOnlyList<SkillDefinition> skills = GetOrderedSkills();
        if (skills.Count == 0)
        {
            return;
        }

        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight, showHints, skills.Count);
        float totalW = (skills.Count * SlotSize) + ((skills.Count - 1) * Gap);
        skin.DrawCard(spriteBatch, new RectangleF(startX - 7f, startY - 7f, totalW + 14f, SlotSize + 14f), new ColorRgba(56, 189, 248, 205), bodyAlpha: 230);

        for (int i = 0; i < skills.Count; i++)
        {
            SkillDefinition skill = skills[i];
            float sx = startX + (i * (SlotSize + Gap));
            byte rank = world.GetSkillRank(skill.Id);
            if (rank == 0)
            {
                skin.DrawSlot(spriteBatch, new RectangleF(sx, startY, SlotSize, SlotSize), new ColorRgba(10, 14, 24, 232), new ColorRgba(51, 65, 85, 195));
                continue;
            }

            SkillSpec spec = skill.EvaluateRank(rank);
            float cd = world.GetSkillCooldown(skill.Id);
            bool ready = cd <= 0.02f && world.LocalPlayerMana >= spec.ManaCost && world.LocalPlayerStamina >= spec.StaminaCost;
            ColorRgba accent = ResolveAccentColor(skill);
            skin.DrawSlot(spriteBatch, new RectangleF(sx, startY, SlotSize, SlotSize), new ColorRgba(14, 22, 38, 238), ready ? accent : new ColorRgba(71, 85, 105, 220));
        }
    }

    public ushort DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        bool showHints,
        Vector2? mouseScreenPos)
    {
        IReadOnlyList<SkillDefinition> skills = GetOrderedSkills();
        if (skills.Count == 0)
        {
            return 0;
        }

        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight, showHints, skills.Count);
        SkillDefinition? hoveredDef = null;

        for (int i = 0; i < skills.Count; i++)
        {
            SkillDefinition skill = skills[i];
            float sx = startX + (i * (SlotSize + Gap));
            RectangleF rect = new(sx, startY, SlotSize, SlotSize);
            if (mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value))
            {
                hoveredDef = skill;
            }

            byte rank = world.GetSkillRank(skill.Id);
            ColorRgba accent = ResolveAccentColor(skill);
            if (rank == 0)
            {
                DrawLockedPadlock(batch, new Vector2(sx + (SlotSize * 0.5f), startY + (SlotSize * 0.48f)));
                continue;
            }

            SkillSpec spec = skill.EvaluateRank(rank);
            float cd = world.GetSkillCooldown(skill.Id);
            bool hasResources = world.LocalPlayerMana >= spec.ManaCost && world.LocalPlayerStamina >= spec.StaminaCost;
            bool ready = cd <= 0.02f && hasResources;

            DrawSkillGlyph(batch, skill.AnimArchetype, new Vector2(sx + (SlotSize * 0.5f), startY + (SlotSize * 0.44f)), accent, ready);

            for (int r = 0; r < rank && r < skill.MaxRank; r++)
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

        if (hoveredDef is not null)
        {
            float tipX = Math.Clamp(startX - 10f, 12f, viewportWidth - 296f);
            RectangleF tipBox = new(tipX, startY - 72f, 284f, 62f);
            ColorRgba accent = world.GetSkillRank(hoveredDef.Id) > 0
                ? ResolveAccentColor(hoveredDef)
                : new ColorRgba(148, 163, 184, 255);
            batch.FillRectangle(tipBox, new ColorRgba(8, 12, 24, 245));
            batch.DrawRectangle(tipBox, accent, 1.4f);
            return hoveredDef.Id;
        }

        return 0;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        bool showHints,
        ushort hoveredSkillId)
    {
        IReadOnlyList<SkillDefinition> skills = GetOrderedSkills();
        if (skills.Count == 0)
        {
            return;
        }

        (float startX, float startY) = ComputeOrigin(viewportWidth, viewportHeight, showHints, skills.Count);
        ColorRgba black = new(8, 10, 16, 255);
        SkillDefinition? hoveredDef = null;

        for (int i = 0; i < skills.Count; i++)
        {
            SkillDefinition skill = skills[i];
            if (skill.Id == hoveredSkillId)
            {
                hoveredDef = skill;
            }

            float sx = startX + (i * (SlotSize + Gap));
            byte rank = world.GetSkillRank(skill.Id);
            spriteBatch.DrawOutlinedString(
                font,
                skill.DefaultKeyLabel,
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

            SkillSpec spec = skill.EvaluateRank(rank);
            spriteBatch.DrawOutlinedString(font, $"{(int)spec.ManaCost}", new Vector2(sx + SlotSize - 17f, startY + SlotSize - 12f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);

            float cd = world.GetSkillCooldown(skill.Id);
            if (cd > 0.05f)
            {
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"{cd:F1}s", new RectangleF(sx, startY, SlotSize, SlotSize), ColorRgba.White, black, 1.5f);
            }
        }

        if (hoveredDef is not null)
        {
            byte rank = world.GetSkillRank(hoveredDef.Id);
            byte effectiveRank = rank > 0 ? rank : (byte)1;
            SkillSpec hoveredSpec = hoveredDef.EvaluateRank(effectiveRank);
            float tipX = Math.Clamp(startX - 10f, 12f, viewportWidth - 296f);
            float tipY = startY - 72f;
            if (rank == 0)
            {
                string title = CarbonUiSkin.TruncateToWidth(font, $"{hoveredDef.DisplayName} [LOCKED]", 268f, 1.5f);
                spriteBatch.DrawOutlinedString(font, title, new Vector2(tipX + 8f, tipY + 6f), new ColorRgba(250, 204, 21, 255), black, 1, 1.5f);
                spriteBatch.DrawOutlinedString(font, "Requires 1 Skill Point to unlock", new Vector2(tipX + 8f, tipY + 26f), new ColorRgba(248, 113, 113, 255), black, 1, 1f);
                spriteBatch.DrawOutlinedString(font, "Open Skill Tome [K] to unlock & upgrade", new Vector2(tipX + 8f, tipY + 42f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
            }
            else
            {
                string title = CarbonUiSkin.TruncateToWidth(font, $"{hoveredSpec.DisplayName} (R{rank}/{hoveredDef.MaxRank})", 268f, 1.5f);
                spriteBatch.DrawOutlinedString(font, title, new Vector2(tipX + 8f, tipY + 6f), ResolveAccentColor(hoveredDef), black, 1, 1.5f);
                spriteBatch.DrawOutlinedString(font, $"MP {(int)hoveredSpec.ManaCost} | STA {(int)hoveredSpec.StaminaCost} | CD {hoveredSpec.CooldownSeconds:F1}s", new Vector2(tipX + 8f, tipY + 26f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
                string desc = hoveredDef.EffectKind == SkillEffectKind.SelfRestore
                    ? $"Restore {(int)MathF.Round(hoveredSpec.SelfHealRatio * 100f)}% HP & +{(int)hoveredSpec.StaminaRestore} STA"
                    : $"{hoveredDef.Summary} ({(int)MathF.Round(hoveredSpec.DamageMultiplier * 100f)}% DMG)";
                spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, desc, 268f, 1f), new Vector2(tipX + 8f, tipY + 42f), new ColorRgba(226, 232, 240, 255), black, 1, 1f);
            }
        }
    }

    internal static void DrawSkillGlyph(GeometryBatch batch, SkillAnimArchetype anim, Vector2 center, ColorRgba accent, bool ready)
    {
        ColorRgba col = ready
            ? accent
            : new ColorRgba((byte)(accent.R / 2), (byte)(accent.G / 2), (byte)(accent.B / 2), 205);

        if (anim == SkillAnimArchetype.SpinSlash)
        {
            batch.DrawCircle(center, 10f, col, 16, 2.2f);
            batch.DrawLine(center + new Vector2(-8f, -4f), center + new Vector2(8f, 4f), ColorRgba.White, 1.8f);
            batch.DrawLine(center + new Vector2(-8f, 4f), center + new Vector2(8f, -4f), col, 1.8f);
        }
        else if (anim == SkillAnimArchetype.ArcaneWave)
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

    private static (float StartX, float StartY) ComputeOrigin(int viewportWidth, int viewportHeight, bool showHints, int count)
    {
        float hotbarX = MathF.Floor((viewportWidth - HotbarWidth) * 0.5f);
        float skillW = count > 0 ? (count * SlotSize) + ((count - 1) * Gap) : 0f;
        float startX = MathF.Max(12f, hotbarX - skillW - 14f);
        float startY = viewportHeight - (showHints ? 75f : 56f);
        return (startX, startY);
    }

    internal static ColorRgba ResolveAccentColor(SkillDefinition skill) =>
        CarbonUiSkin.ParseHexColor(skill.AccentHex, new ColorRgba(56, 189, 248, 255));
}
