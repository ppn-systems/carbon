// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Right-edge vertical class skill bar powered by <see cref="SkillCatalog"/>.
/// Stays completely hidden prior to job/class advancement when the hero has zero unlocked class skills.
/// </summary>
internal sealed class SkillBarWidget
{
    private const float SlotSize = 52f;
    private const float Gap = 6f;

    private readonly DerivedTextCache _skillNames = new("skill.", ".name");
    private readonly DerivedTextCache _skillSummaries = new("skill.", ".summary");
    private readonly CachedText _tooltipTitle = new();
    private readonly CachedText _tooltipCost = new();

    private readonly List<SkillDefinition> _visibleSkills = new(8);

    /// <summary>Gets or sets the lowest top the bar may start at (under the HUD column, see <see cref="HudLayout"/>).</summary>
    public float MinTop { get; set; }

    public bool TryHitTestSkill(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort skillId)
    {
        skillId = 0;
        this.CollectVisibleSkills(world);
        if (_visibleSkills.Count == 0)
        {
            return false;
        }

        (float startX, float startY) = this.ComputeOrigin(viewportWidth, viewportHeight, _visibleSkills.Count);
        if (mousePos.X < startX || mousePos.X > startX + SlotSize)
        {
            return false;
        }

        for (int i = 0; i < _visibleSkills.Count; i++)
        {
            float sy = startY + (i * (SlotSize + Gap));
            if (mousePos.Y >= sy && mousePos.Y <= sy + SlotSize)
            {
                skillId = _visibleSkills[i].Id;
                return true;
            }
        }

        return false;
    }

    public void DrawSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        this.CollectVisibleSkills(world);
        if (_visibleSkills.Count == 0)
        {
            return;
        }

        (float startX, float startY) = this.ComputeOrigin(viewportWidth, viewportHeight, _visibleSkills.Count);
        float totalH = (_visibleSkills.Count * SlotSize) + ((_visibleSkills.Count - 1) * Gap);
        skin.DrawCard(
            spriteBatch,
            new RectangleF(startX - 6f, startY - 6f, SlotSize + 12f, totalH + 12f),
            CarbonUiPalette.BorderOuter,
            bodyAlpha: 236);

        for (int i = 0; i < _visibleSkills.Count; i++)
        {
            SkillDefinition skill = _visibleSkills[i];
            float sy = startY + (i * (SlotSize + Gap));
            byte rank = world.GetSkillRank(skill.Id);
            if (rank == 0)
            {
                skin.DrawSlot(spriteBatch, new RectangleF(startX, sy, SlotSize, SlotSize), CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);
                continue;
            }

            SkillSpec spec = skill.EvaluateRank(rank);
            float cd = world.GetSkillCooldown(skill.Id);
            bool ready = cd <= 0.02f && world.LocalPlayerMana >= spec.ManaCost && world.LocalPlayerStamina >= spec.StaminaCost;
            ColorRgba border = ready ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle;
            skin.DrawSlot(spriteBatch, new RectangleF(startX, sy, SlotSize, SlotSize), CarbonUiPalette.CardFill, border);
        }
    }

    public ushort DrawGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        this.CollectVisibleSkills(world);
        if (_visibleSkills.Count == 0)
        {
            return 0;
        }

        (float startX, float startY) = this.ComputeOrigin(viewportWidth, viewportHeight, _visibleSkills.Count);
        SkillDefinition? hoveredDef = null;
        float hoveredY = startY;

        for (int i = 0; i < _visibleSkills.Count; i++)
        {
            SkillDefinition skill = _visibleSkills[i];
            float sy = startY + (i * (SlotSize + Gap));
            RectangleF rect = new(startX, sy, SlotSize, SlotSize);
            if (mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value))
            {
                hoveredDef = skill;
                hoveredY = sy;
            }

            byte rank = world.GetSkillRank(skill.Id);
            ColorRgba accent = ResolveAccentColor(skill);
            if (rank > 0)
            {
                SkillSpec spec = skill.EvaluateRank(rank);
                float cd = world.GetSkillCooldown(skill.Id);
                bool hasResources = world.LocalPlayerMana >= spec.ManaCost && world.LocalPlayerStamina >= spec.StaminaCost;
                bool ready = cd <= 0.02f && hasResources;

                DrawSkillGlyph(batch, skill.AnimArchetype, new Vector2(startX + (SlotSize * 0.5f), sy + (SlotSize * 0.46f)), accent, ready);

                for (int r = 0; r < rank && r < skill.MaxRank; r++)
                {
                    batch.FillRectangle(new RectangleF(startX + 5f + (r * 5f), sy + SlotSize - 7f, 3.5f, 3.5f), CarbonUiPalette.BorderActive);
                }

                if (cd > 0.02f && spec.CooldownSeconds > 0.1f)
                {
                    float frac = Math.Clamp(cd / spec.CooldownSeconds, 0f, 1f);
                    float maskH = SlotSize * frac;
                    batch.FillRectangle(new RectangleF(startX + 2f, sy + SlotSize - maskH, SlotSize - 4f, maskH - 2f), new ColorRgba(8, 10, 16, 195));
                }
                else if (!hasResources)
                {
                    batch.FillRectangle(new RectangleF(startX + 2f, sy + 2f, SlotSize - 4f, SlotSize - 4f), new ColorRgba(10, 12, 18, 120));
                }
            }
            else
            {
                DrawSkillGlyph(batch, skill.AnimArchetype, new Vector2(startX + (SlotSize * 0.5f), sy + (SlotSize * 0.46f)), CarbonUiPalette.TextMuted, ready: false);
            }
        }

        if (hoveredDef is not null)
        {
            const float tipW = 248f;
            const float tipH = 56f;
            float tipX = MathF.Max(12f, startX - tipW - 14f);
            float tipY = Math.Clamp(hoveredY, 12f, viewportHeight - tipH - 12f);
            RectangleF tipBox = new(tipX, tipY, tipW, tipH);
            batch.FillRectangle(tipBox, CarbonUiPalette.WindowFill);
            batch.DrawRectangle(tipBox, CarbonUiPalette.BorderOuter, 1.4f);
            return hoveredDef.Id;
        }

        return 0;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        ushort hoveredSkillId)
    {
        this.CollectVisibleSkills(world);
        if (_visibleSkills.Count == 0)
        {
            return;
        }

        (float startX, float startY) = this.ComputeOrigin(viewportWidth, viewportHeight, _visibleSkills.Count);
        ColorRgba black = CarbonUiPalette.TextOutline;
        SkillDefinition? hoveredDef = null;
        float hoveredY = startY;

        for (int i = 0; i < _visibleSkills.Count; i++)
        {
            SkillDefinition skill = _visibleSkills[i];
            float sy = startY + (i * (SlotSize + Gap));
            if (skill.Id == hoveredSkillId)
            {
                hoveredDef = skill;
                hoveredY = sy;
            }

            byte rank = world.GetSkillRank(skill.Id);
            spriteBatch.DrawOutlinedString(
                font,
                skill.DefaultKeyLabel,
                new Vector2(startX + 5f, sy + 3f),
                rank > 0 ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted,
                black,
                1,
                1f);

            if (rank == 0)
            {
                continue;
            }

            SkillSpec spec = skill.EvaluateRank(rank);
            spriteBatch.DrawOutlinedString(
                font,
                $"{(int)spec.ManaCost}",
                new Vector2(startX + SlotSize - 22f, sy + SlotSize - 18f),
                CarbonUiPalette.TextMuted,
                black,
                1,
                1f);

            float cd = world.GetSkillCooldown(skill.Id);
            if (cd > 0.05f)
            {
                CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"{cd:F1}s", new RectangleF(startX, sy, SlotSize, SlotSize), CarbonUiPalette.TextPrimary, black, 1f);
            }
        }

        if (hoveredDef is not null)
        {
            byte rank = world.GetSkillRank(hoveredDef.Id);
            byte effectiveRank = rank > 0 ? rank : (byte)1;
            SkillSpec hoveredSpec = hoveredDef.EvaluateRank(effectiveRank);
            const float tipW = 248f;
            const float tipH = 56f;
            float tipX = MathF.Max(12f, startX - tipW - 14f);
            float tipY = Math.Clamp(hoveredY, 12f, viewportHeight - tipH - 12f);

            string skillName = _skillNames.Text(hoveredDef.Code, hoveredDef.DisplayName);
            string title = CarbonUiSkin.TruncateToWidth(
                font,
                rank > 0 ? _tooltipTitle.Format("hud.skills.bar_title", new TextArg("name", skillName), new TextArg("rank", rank)) : skillName,
                tipW - 18f,
                1f);
            spriteBatch.DrawOutlinedString(font, title, new Vector2(tipX + 9f, tipY + 8f), CarbonUiPalette.TextWarm, black, 1, 1f);
            string costLine = rank > 0
                ? _tooltipCost.Format(
                    "hud.skills.cost",
                    new TextArg("mp", (int)hoveredSpec.ManaCost),
                    new TextArg("sta", (int)hoveredSpec.StaminaCost),
                    new TextArg("cd", hoveredSpec.CooldownSeconds, decimals: 1))
                : _skillSummaries.Text(hoveredDef.Code, hoveredDef.Summary);
            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, costLine, tipW - 18f, 1f), new Vector2(tipX + 9f, tipY + 30f), CarbonUiPalette.TextMuted, black, 1, 1f);
        }
    }

    internal static void DrawSkillGlyph(GeometryBatch batch, SkillAnimArchetype anim, Vector2 center, ColorRgba accent, bool ready)
    {
        ColorRgba col = ready
            ? accent
            : new ColorRgba((byte)(accent.R / 2), (byte)(accent.G / 2), (byte)(accent.B / 2), 205);

        if (anim == SkillAnimArchetype.SpinSlash)
        {
            batch.DrawCircle(center, 11f, col, 16, 2.2f);
            batch.DrawLine(center + new Vector2(-9f, -4.5f), center + new Vector2(9f, 4.5f), CarbonUiPalette.TextPrimary, 1.8f);
            batch.DrawLine(center + new Vector2(-9f, 4.5f), center + new Vector2(9f, -4.5f), col, 1.8f);
        }
        else if (anim == SkillAnimArchetype.ArcaneWave)
        {
            Span<Vector2> diamond =
            [
                center + new Vector2(0f, -12f),
                center + new Vector2(10f, 0f),
                center + new Vector2(0f, 12f),
                center + new Vector2(-10f, 0f),
            ];
            batch.FillConvexPolygon(diamond, col);
            batch.FillCircle(center, 4f, CarbonUiPalette.TextPrimary, 10);
        }
        else
        {
            batch.FillRectangle(new RectangleF(center.X - 3f, center.Y - 10f, 6f, 20f), col);
            batch.FillRectangle(new RectangleF(center.X - 10f, center.Y - 3f, 20f, 6f), col);
        }
    }

    internal static ColorRgba ResolveAccentColor(SkillDefinition skill) =>
        CarbonUiSkin.ParseHexColor(skill.AccentHex, CarbonUiPalette.BorderActive);

    private void CollectVisibleSkills(IWorldReadModel world) =>
        SkillCatalog.CollectCharacterSkills(
            world.LocalPlayerClass,
            world.LocalCharacterTraits,
            world.LocalUnlockedSkillMask,
            _visibleSkills);

    private (float StartX, float StartY) ComputeOrigin(int viewportWidth, int viewportHeight, int count)
    {
        float totalH = count > 0 ? (count * SlotSize) + ((count - 1) * Gap) : 0f;
        float startX = viewportWidth - SlotSize - 14f;
        float startY = MathF.Max(MathF.Floor((viewportHeight - totalH) * 0.5f), this.MinTop);
        return (startX, startY);
    }
}
