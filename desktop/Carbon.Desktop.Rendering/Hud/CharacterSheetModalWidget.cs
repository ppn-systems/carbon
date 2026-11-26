// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions;
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
/// unified <see cref="PlayerModalTabs"/> frame with restrained dark-slate styling (SRP).
/// </summary>
internal sealed class CharacterSheetModalWidget
{
    private const float PanelW = InventoryModalWidget.PanelWidth;
    private const float PanelH = InventoryModalWidget.PanelHeight;

    private static readonly PrimaryAttributeId[] s_statTargets =
    [
        PrimaryAttributeId.Vitality,
        PrimaryAttributeId.Wisdom,
        PrimaryAttributeId.Power,
        PrimaryAttributeId.Agility,
    ];

    private readonly List<SkillDefinition> _skillScratch = new(8);

    public bool ContainsPoint(IClientWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        if (mousePos.X >= ox && mousePos.X <= ox + PanelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH)
        {
            return true;
        }

        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
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
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        return PlayerModalTabs.TryHitTestTab(mousePos, ox, oy, hasSkills, out tab);
    }

    public bool TryHitTestStatPlusButton(Vector2 mousePos, int viewportWidth, int viewportHeight, out PrimaryAttributeId attribute)
    {
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        for (int i = 0; i < s_statTargets.Length; i++)
        {
            if (GetStatPlusRect(ox, oy, i).Contains(mousePos))
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
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawTabsSkin(spriteBatch, skin, ox, PanelW, oy, PlayerModalTab.Character, hasSkills, mouseScreenPos);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), CarbonUiPalette.BorderOuter, bodyAlpha: 246);

        skin.DrawCard(spriteBatch, new RectangleF(ox + 20f, oy + 48f, 224f, 392f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 256f, oy + 48f, 280f, 392f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        bool hasAp = world.LocalUnspentStatPoints > 0;
        for (int i = 0; i < s_statTargets.Length; i++)
        {
            RectangleF rowBox = new(ox + 270f, oy + 106f + (i * 68f), 252f, 56f);
            skin.DrawInset(spriteBatch, in rowBox, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);

            RectangleF plusRect = GetStatPlusRect(ox, oy, i);
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
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        float animTime)
    {
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
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

            Vector2 pedPos = new(ox + 132f, oy + 168f);
            batch.FillCircle(pedPos + new Vector2(0f, -6f), 28f, new ColorRgba(6, 9, 14, 145), 16);
            batch.DrawCircle(pedPos + new Vector2(0f, -6f), 29f, CarbonUiPalette.BorderSubtle, 16, 1.2f);
            VectorRigRenderer.DrawEntityRig(
                batch,
                rig,
                pedPos,
                Direction8.South,
                "idle",
                animTime,
                isNormalized: false,
                targetHeightOverride: 82f,
                hitFlash: 0f,
                equippedItem: weaponArt,
                action: default,
                swingTimer: 0f,
                appearancePreset: world.LocalPlayerAppearancePreset,
                equipment: loadout,
                art: art);
        }

        // XP meter in Character card
        float xpRatio = Math.Clamp(world.LocalPlayerExperience / MathF.Max(1f, world.LocalPlayerXpToNext), 0f, 1f);
        RectangleF xpTrack = new(ox + 36f, oy + 238f, 192f, 8f);
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
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        int ptx,
        int pty,
        float fps,
        bool showTelemetry)
    {
        (float ox, float oy) = InventoryModalWidget.ComputeOrigin(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawTabsText(spriteBatch, font, ox, PanelW, oy, PlayerModalTab.Character, hasSkills);

        ColorRgba black = CarbonUiPalette.TextOutline;
        spriteBatch.DrawOutlinedString(font, "Character Info", new Vector2(ox + 28f, oy + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);

        // Left Character Summary
        string heroName = string.IsNullOrWhiteSpace(world.ActiveCharacterName) ? "Hero" : world.ActiveCharacterName;
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, heroName, 196f, 1.25f), new Vector2(ox + 36f, oy + 184f), CarbonUiPalette.TextPrimary, black, 1, 1.25f);
        spriteBatch.DrawOutlinedString(font, $"{world.LocalPlayerClass}  Lv.{world.LocalPlayerLevel}", new Vector2(ox + 36f, oy + 212f), CarbonUiPalette.TextWarm, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"XP {world.LocalPlayerExperience} / {world.LocalPlayerXpToNext}", new Vector2(ox + 36f, oy + 252f), CarbonUiPalette.TextMuted, black, 1, 1f);

        spriteBatch.DrawOutlinedString(font, $"HP   {(int)MathF.Ceiling(world.LocalPlayerHealth)} / {(int)MathF.Ceiling(world.LocalPlayerMaxHealth)}", new Vector2(ox + 36f, oy + 284f), CarbonUiPalette.TextPrimary, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"MP   {(int)MathF.Floor(world.LocalPlayerMana)} / {(int)MathF.Ceiling(world.LocalPlayerMaxMana)}", new Vector2(ox + 36f, oy + 306f), CarbonUiPalette.TextPrimary, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"STA  {(int)MathF.Floor(world.LocalPlayerStamina)} / {(int)MathF.Ceiling(world.LocalPlayerMaxStamina)}", new Vector2(ox + 36f, oy + 328f), CarbonUiPalette.TextPrimary, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"DEF  {(int)MathF.Round(world.LocalPlayerDefense)}", new Vector2(ox + 36f, oy + 350f), CarbonUiPalette.TextWarm, black, 1, 1f);

        if (showTelemetry)
        {
            spriteBatch.DrawOutlinedString(font, $"Pos ({ptx}, {pty})  {(int)fps} FPS", new Vector2(ox + 36f, oy + 382f), CarbonUiPalette.TextMuted, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, $"Net: {world.TransportLabel}", new Vector2(ox + 36f, oy + 404f), CarbonUiPalette.TextMuted, black, 1, 1f);
        }

        // Right Attributes Card
        ushort unspentAp = world.LocalUnspentStatPoints;
        spriteBatch.DrawOutlinedString(font, "Attributes", new Vector2(ox + 272f, oy + 64f), CarbonUiPalette.TextWarm, black, 1, 1f);
        string ptsLabel = $"Points: {unspentAp}";
        spriteBatch.DrawOutlinedString(
            font,
            ptsLabel,
            new Vector2(ox + 422f, oy + 64f),
            unspentAp > 0 ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted,
            black,
            1,
            1f);

        ushort vitPts = world.GetAttributePoints(PrimaryAttributeId.Vitality);
        ushort wisPts = world.GetAttributePoints(PrimaryAttributeId.Wisdom);
        ushort pwrPts = world.GetAttributePoints(PrimaryAttributeId.Power);
        ushort agiPts = world.GetAttributePoints(PrimaryAttributeId.Agility);

        (string Title, string Bonus, ushort Pts)[] rows =
        [
            ("Vitality", $"Max HP {(int)MathF.Ceiling(world.LocalPlayerMaxHealth)}", vitPts),
            ("Wisdom", $"Max MP {(int)MathF.Ceiling(world.LocalPlayerMaxMana)}", wisPts),
            ("Power", $"Attack +{pwrPts * 3.5f:F0}%", pwrPts),
            ("Agility", $"Stamina {(int)MathF.Ceiling(world.LocalPlayerMaxStamina)}", agiPts),
        ];

        for (int i = 0; i < rows.Length; i++)
        {
            float ry = oy + 106f + (i * 68f);
            spriteBatch.DrawOutlinedString(font, $"{rows[i].Title} ({rows[i].Pts})", new Vector2(ox + 282f, ry + 8f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, rows[i].Bonus, new Vector2(ox + 282f, ry + 30f), CarbonUiPalette.TextMuted, black, 1, 1f);
            RectangleF plusRect = GetStatPlusRect(ox, oy, i);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "+", in plusRect, unspentAp > 0 ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted, black, 1f);
        }
    }

    private static RectangleF GetStatPlusRect(float ox, float oy, int rowIndex) =>
        new(ox + 478f, oy + 120f + (rowIndex * 68f), 32f, 28f);
}
