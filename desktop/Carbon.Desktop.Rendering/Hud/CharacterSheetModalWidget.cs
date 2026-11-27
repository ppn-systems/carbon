// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Detailed Character Info &amp; Attribute Point Allocator tab (<c>[Y]</c>) hosted inside the
/// fixed <see cref="PlayerModalTabs"/> workspace with restrained dark-slate styling (SRP).
/// </summary>
internal sealed class CharacterSheetModalWidget
{
    private const int AttributeRowCount = 4;
    private static readonly PrimaryAttributeId[] s_statTargets =
    [
        PrimaryAttributeId.Vitality,
        PrimaryAttributeId.Wisdom,
        PrimaryAttributeId.Power,
        PrimaryAttributeId.Agility,
    ];

    private readonly List<SkillDefinition> _skillScratch = new(8);
    private readonly CachedText _attributePoints = new();
    private readonly CachedText _classLevel = new();
    private readonly CachedText _xp = new();
    private readonly CachedText _hp = new();
    private readonly CachedText _mp = new();
    private readonly CachedText _sta = new();
    private readonly CachedText _def = new();
    private readonly CachedText _telemetry = new();
    private readonly CachedText _net = new();
    private readonly CachedText[] _rowTitles = [new(), new(), new(), new()];
    private readonly CachedText[] _rowBonuses = [new(), new(), new(), new()];

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
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        return PlayerModalTabs.TryHitTestTab(mousePos, in layout, hasSkills, out tab);
    }

    public bool TryHitTestStatPlusButton(Vector2 mousePos, int viewportWidth, int viewportHeight, out PrimaryAttributeId attribute)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        for (int i = 0; i < s_statTargets.Length; i++)
        {
            if (GetStatPlusRect(in layout, i).Contains(mousePos))
            {
                attribute = s_statTargets[i];
                return true;
            }
        }

        attribute = PrimaryAttributeId.Vitality;
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
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawWindowChromeSkin(spriteBatch, skin, in layout, PlayerModalTab.Character, hasSkills, mouseScreenPos);

        RectangleF leftCard = GetLeftCardRect(in layout);
        RectangleF rightCard = GetRightCardRect(in layout);
        skin.DrawCard(spriteBatch, in leftCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, in rightCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        bool hasAp = world.LocalUnspentStatPoints > 0;
        for (int i = 0; i < s_statTargets.Length; i++)
        {
            RectangleF rowBox = GetStatRowRect(in layout, i);
            skin.DrawInset(spriteBatch, in rowBox, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);

            RectangleF plusRect = GetStatPlusRect(in layout, i);
            bool hoverPlus = mouseScreenPos.HasValue && plusRect.Contains(mouseScreenPos.Value);
            skin.DrawButton(
                spriteBatch,
                in plusRect,
                hasAp ? (hoverPlus ? new ColorRgba(44, 114, 82, 245) : CarbonUiPalette.ConfirmFill) : CarbonUiPalette.SlotFill,
                hasAp ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
        }
    }

    public void DrawGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        float animTime)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        RectangleF leftCard = GetLeftCardRect(in layout);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        if (art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? rig))
        {
            ItemSlot activeItem = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
            CompiledItemVectorArt? weaponArt = null;
            if (!activeItem.IsEmpty)
            {
                _ = art.TryGetItem(activeItem.ItemId, out weaponArt);
            }

            VisualEquipmentLoadout loadout = new(
                PlayerInventoryLayout.HeadSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.HeadSlot].ItemId : (ushort)0,
                PlayerInventoryLayout.ChestSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.ChestSlot].ItemId : (ushort)0,
                PlayerInventoryLayout.LegsSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.LegsSlot].ItemId : (ushort)0,
                PlayerInventoryLayout.FeetSlot < slots.Length ? (ushort)slots[PlayerInventoryLayout.FeetSlot].ItemId : (ushort)0,
                (ushort)activeItem.ItemId,
                ItemEnhancementPolicy.GetUpgradeLevel(in activeItem));

            float pedR = MathF.Round(38f * s);
            Vector2 pedPos = new(leftCard.X + (leftCard.Width * 0.5f), leftCard.Y + MathF.Round(162f * s));
            batch.FillCircle(pedPos + new Vector2(0f, -6f * s), pedR, new ColorRgba(6, 9, 14, 145), 16);
            batch.DrawCircle(pedPos + new Vector2(0f, -6f * s), pedR + 1f, CarbonUiPalette.BorderSubtle, 16, 1.3f);
            VectorRigRenderer.DrawEntityRig(
                batch,
                rig,
                pedPos,
                Direction8.South,
                "idle",
                animTime,
                isNormalized: false,
                targetHeightOverride: MathF.Round(122f * s),
                hitFlash: 0f,
                equippedItem: weaponArt,
                action: default,
                swingTimer: 0f,
                appearancePreset: world.LocalPlayerAppearancePreset,
                equipment: loadout,
                art: art);
        }

        // XP meter in Character card
        float padX = MathF.Round(20f * s);
        float xpRatio = Math.Clamp(world.LocalPlayerExperience / MathF.Max(1f, world.LocalPlayerXpToNext), 0f, 1f);
        RectangleF xpTrack = new(leftCard.X + padX, leftCard.Y + MathF.Round(246f * s), leftCard.Width - (padX * 2f), MathF.Max(8f, MathF.Round(10f * s)));
        batch.FillRectangle(xpTrack, CarbonUiPalette.SlotFill);
        if (xpRatio > 0.004f)
        {
            batch.FillRectangle(new RectangleF(xpTrack.X, xpTrack.Y, xpTrack.Width * xpRatio, xpTrack.Height), CarbonUiPalette.XpFill);
        }

        batch.DrawRectangle(xpTrack, CarbonUiPalette.BorderSubtle, 1f);
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        int ptx,
        int pty,
        float fps,
        bool showTelemetry)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        ushort unspentAp = world.LocalUnspentStatPoints;
        PlayerModalTabs.DrawWindowChromeText(
            spriteBatch,
            font,
            in layout,
            PlayerModalTab.Character,
            hasSkills,
            GameText.Get("hud.character.title"),
            _attributePoints.Format("hud.character.attribute_points", new TextArg("points", unspentAp)),
            unspentAp > 0);

        RectangleF leftCard = GetLeftCardRect(in layout);
        RectangleF rightCard = GetRightCardRect(in layout);
        ColorRgba black = CarbonUiPalette.TextOutline;

        // Left Character Summary
        float lx = leftCard.X + MathF.Round(20f * s);
        float textW = leftCard.Width - MathF.Round(40f * s);
        string heroName = string.IsNullOrWhiteSpace(world.ActiveCharacterName) ? GameText.Get("hud.character.default_name") : world.ActiveCharacterName;
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, heroName, textW, layout.TitleScale),
            new Vector2(lx, leftCard.Y + MathF.Round(184f * s)),
            CarbonUiPalette.TextPrimary,
            black,
            1,
            layout.TitleScale);
        spriteBatch.DrawOutlinedString(font, _classLevel.Format("hud.character.class_level", new TextArg("class", ClassLabel(world.LocalPlayerClass)), new TextArg("level", world.LocalPlayerLevel)), new Vector2(lx, leftCard.Y + MathF.Round(216f * s)), CarbonUiPalette.TextWarm, black, 1, layout.TextScale);
        spriteBatch.DrawOutlinedString(font, _xp.Format("hud.character.xp", new TextArg("current", world.LocalPlayerExperience), new TextArg("next", world.LocalPlayerXpToNext)), new Vector2(lx, leftCard.Y + MathF.Round(264f * s)), CarbonUiPalette.TextMuted, black, 1, layout.TextScale);

        spriteBatch.DrawOutlinedString(font, _hp.Format("hud.character.hp", new TextArg("current", (int)MathF.Ceiling(world.LocalPlayerHealth)), new TextArg("max", (int)MathF.Ceiling(world.LocalPlayerMaxHealth))), new Vector2(lx, leftCard.Y + MathF.Round(304f * s)), CarbonUiPalette.TextPrimary, black, 1, layout.TextScale);
        spriteBatch.DrawOutlinedString(font, _mp.Format("hud.character.mp", new TextArg("current", (int)MathF.Floor(world.LocalPlayerMana)), new TextArg("max", (int)MathF.Ceiling(world.LocalPlayerMaxMana))), new Vector2(lx, leftCard.Y + MathF.Round(334f * s)), CarbonUiPalette.TextPrimary, black, 1, layout.TextScale);
        spriteBatch.DrawOutlinedString(font, _sta.Format("hud.character.sta", new TextArg("current", (int)MathF.Floor(world.LocalPlayerStamina)), new TextArg("max", (int)MathF.Ceiling(world.LocalPlayerMaxStamina))), new Vector2(lx, leftCard.Y + MathF.Round(364f * s)), CarbonUiPalette.TextPrimary, black, 1, layout.TextScale);
        spriteBatch.DrawOutlinedString(font, _def.Format("hud.character.def", new TextArg("value", (int)MathF.Round(world.LocalPlayerDefense))), new Vector2(lx, leftCard.Y + MathF.Round(394f * s)), CarbonUiPalette.TextWarm, black, 1, layout.TextScale);

        if (showTelemetry)
        {
            spriteBatch.DrawOutlinedString(font, _telemetry.Format("hud.character.telemetry", new TextArg("x", ptx), new TextArg("y", pty), new TextArg("fps", (int)fps)), new Vector2(lx, leftCard.Y + MathF.Round(434f * s)), CarbonUiPalette.TextMuted, black, 1, layout.TextScale);
            spriteBatch.DrawOutlinedString(font, _net.Format("hud.character.net", new TextArg("transport", world.TransportLabel)), new Vector2(lx, leftCard.Y + MathF.Round(462f * s)), CarbonUiPalette.TextMuted, black, 1, layout.TextScale);
        }

        // Right Attributes Card
        spriteBatch.DrawOutlinedString(
            font,
            GameText.Get("hud.character.attributes"),
            new Vector2(rightCard.X + MathF.Round(18f * s), rightCard.Y + MathF.Round(18f * s)),
            CarbonUiPalette.TextWarm,
            black,
            1,
            layout.TitleScale);

        ushort vitPts = world.GetAttributePoints(PrimaryAttributeId.Vitality);
        ushort wisPts = world.GetAttributePoints(PrimaryAttributeId.Wisdom);
        ushort pwrPts = world.GetAttributePoints(PrimaryAttributeId.Power);
        ushort agiPts = world.GetAttributePoints(PrimaryAttributeId.Agility);

        for (int i = 0; i < AttributeRowCount; i++)
        {
            (string titleKey, ushort pts, string bonus) = i switch
            {
                0 => ("hud.attribute.vitality", vitPts, _rowBonuses[0].Format("hud.attribute.vitality_bonus", new TextArg("value", (int)MathF.Ceiling(world.LocalPlayerMaxHealth)))),
                1 => ("hud.attribute.wisdom", wisPts, _rowBonuses[1].Format("hud.attribute.wisdom_bonus", new TextArg("value", (int)MathF.Ceiling(world.LocalPlayerMaxMana)))),
                2 => ("hud.attribute.power", pwrPts, _rowBonuses[2].Format("hud.attribute.power_bonus", new TextArg("percent", (int)MathF.Round(pwrPts * 3.5f)))),
                _ => ("hud.attribute.agility", agiPts, _rowBonuses[3].Format("hud.attribute.agility_bonus", new TextArg("value", (int)MathF.Ceiling(world.LocalPlayerMaxStamina)))),
            };
            RectangleF rowRect = GetStatRowRect(in layout, i);
            string title = _rowTitles[i].Format("hud.attribute.row", new TextArg("name", GameText.Get(titleKey)), new TextArg("points", pts));
            spriteBatch.DrawOutlinedString(font, title, new Vector2(rowRect.X + MathF.Round(16f * s), rowRect.Y + MathF.Round(16f * s)), CarbonUiPalette.TextPrimary, black, 1, layout.TextScale);
            spriteBatch.DrawOutlinedString(font, bonus, new Vector2(rowRect.X + MathF.Round(16f * s), rowRect.Y + MathF.Round(48f * s)), CarbonUiPalette.TextMuted, black, 1, layout.TextScale);
            RectangleF plusRect = GetStatPlusRect(in layout, i);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "+", in plusRect, unspentAp > 0 ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted, black, layout.TitleScale);
        }
    }

    /// <summary>Class display name; the <c>character.class.*</c> keys arrive with the character-select migration.</summary>
    private static string ClassLabel<TClass>(TClass playerClass)
        where TClass : struct, Enum => playerClass.ToString();

    private static RectangleF GetLeftCardRect(in PlayerModalLayout layout)
    {
        float leftW = MathF.Round(308f * layout.Scale);
        return new RectangleF(layout.ContentX, layout.ContentY, leftW, layout.ContentH);
    }

    private static RectangleF GetRightCardRect(in PlayerModalLayout layout)
    {
        float leftW = MathF.Round(308f * layout.Scale);
        float gap = MathF.Round(14f * layout.Scale);
        return new RectangleF(layout.ContentX + leftW + gap, layout.ContentY, layout.ContentW - leftW - gap, layout.ContentH);
    }

    private static RectangleF GetStatRowRect(in PlayerModalLayout layout, int rowIndex)
    {
        float s = layout.Scale;
        RectangleF rightCard = GetRightCardRect(in layout);
        float pad = MathF.Round(18f * s);
        return new RectangleF(
            rightCard.X + pad,
            rightCard.Y + MathF.Round(60f * s) + (rowIndex * MathF.Round(106f * s)),
            rightCard.Width - (pad * 2f),
            MathF.Round(90f * s));
    }

    private static RectangleF GetStatPlusRect(in PlayerModalLayout layout, int rowIndex)
    {
        float s = layout.Scale;
        RectangleF row = GetStatRowRect(in layout, rowIndex);
        float btn = MathF.Round(44f * s);
        return new RectangleF(row.Right - btn - MathF.Round(16f * s), row.Y + MathF.Round(23f * s), btn, btn);
    }
}
