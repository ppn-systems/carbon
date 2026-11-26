// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Minecraft-inspired large 40-slot Bag &amp; Armor modal (<c>[I]</c>) framed cleanly inside
/// the fixed <see cref="PlayerModalTabs"/> workspace. Supports cursor pickup, Shift/RMB quick-equip,
/// and drag-outside-to-drop (SRP).
/// </summary>
internal sealed class InventoryModalWidget
{
    private const float BaseSlotSize = 70f;
    private const float BaseSlotGap = 8f;

    private readonly List<SkillDefinition> _skillScratch = new(8);

    public int CarriedSlot { get; set; } = -1;

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

    public bool TryHitTestSlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity; s++)
        {
            if (GetSlotRect(in layout, s).Contains(mousePos))
            {
                slotIndex = (ushort)s;
                return true;
            }
        }

        slotIndex = 0;
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
        PlayerModalTabs.DrawWindowChromeSkin(spriteBatch, skin, in layout, PlayerModalTab.Inventory, hasSkills, mouseScreenPos);

        RectangleF stageCard = GetStageCardRect(in layout);
        skin.DrawCard(spriteBatch, in stageCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        RectangleF dollCard = GetPaperDollRect(in layout);
        skin.DrawInset(spriteBatch, in dollCard, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            RectangleF rect = GetSlotRect(in layout, s);
            bool hovered = mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value);
            bool carried = s == this.CarriedSlot;
            bool isArmor = s >= PlayerInventoryLayout.BagLimit;

            ColorRgba fill = carried
                ? CarbonUiPalette.SlotSelectedFill
                : (hovered ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill);
            ColorRgba border = carried
                ? CarbonUiPalette.BorderActive
                : (hovered || isArmor ? CarbonUiPalette.BorderOuter : CarbonUiPalette.BorderSubtle);
            skin.DrawSlot(spriteBatch, in rect, fill, border);
        }
    }

    public int DrawGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        float animTime)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        RectangleF dollRect = GetPaperDollRect(in layout);
        if (art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? heroRig))
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

            float pedR = MathF.Round(34f * s);
            Vector2 pedPos = new(dollRect.X + (dollRect.Width * 0.5f), dollRect.Bottom - MathF.Round(16f * s));
            batch.FillCircle(pedPos + new Vector2(0f, -6f * s), pedR, new ColorRgba(6, 9, 14, 150), 16);
            batch.DrawCircle(pedPos + new Vector2(0f, -6f * s), pedR + 1f, CarbonUiPalette.BorderSubtle, 16, 1.3f);
            VectorRigRenderer.DrawEntityRig(
                batch,
                heroRig,
                pedPos,
                Direction8.South,
                "idle",
                animTime,
                isNormalized: false,
                targetHeightOverride: MathF.Round(116f * s),
                hitFlash: 0f,
                equippedItem: weaponArt,
                action: default,
                swingTimer: 0f,
                appearancePreset: world.LocalPlayerAppearancePreset,
                equipment: loadout,
                art: art);
        }

        // Subtle separator bar between Backpack (3x9) and Hotbar (1x9)
        float slotSize = MathF.Round(BaseSlotSize * s);
        float slotGap = MathF.Round(BaseSlotGap * s);
        float bagStartY = layout.ContentY + MathF.Round(172f * s) + MathF.Round(12f * s);
        float divY = bagStartY + (3f * slotSize) + (2f * slotGap) + MathF.Round(10f * s);
        batch.FillRectangle(new RectangleF(layout.ContentX, divY, layout.ContentW, MathF.Max(2f, MathF.Round(2f * s))), CarbonUiPalette.BorderSubtle);

        float iconSize = MathF.Round(48f * s);
        float barInset = MathF.Round(7f * s);
        float barH = MathF.Max(4f, MathF.Round(4.5f * s));
        int hoveredSlot = -1;
        for (int idx = 0; idx < PlayerInventoryLayout.TotalCapacity && idx < slots.Length; idx++)
        {
            RectangleF rect = GetSlotRect(in layout, idx);
            if (mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value))
            {
                hoveredSlot = idx;
            }

            ItemSlot item = slots[idx];
            if (idx >= PlayerInventoryLayout.BagLimit && (item.IsEmpty || idx == this.CarriedSlot))
            {
                DrawEmptyArmorSilhouette(batch, in rect, idx - PlayerInventoryLayout.BagLimit);
            }

            if (!item.IsEmpty && idx != this.CarriedSlot)
            {
                if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? itemArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, itemArt, new Vector2(rect.X + (rect.Width * 0.5f), rect.Y + (rect.Height * 0.46f)), iconSize);
                }

                if (world.Items.Rules.Items.TryGet(item.ItemId, out ItemArchetype? def) && def.MaxDurability > 0f)
                {
                    byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                    float effMax = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(def, upg));
                    float frac = Math.Clamp(item.Durability / effMax, 0f, 1f);
                    ColorRgba dCol = frac <= 0f
                        ? CarbonUiPalette.HpFill
                        : (frac > 0.35f ? CarbonUiPalette.StaminaFill : CarbonUiPalette.XpFill);
                    float barW = rect.Width - (barInset * 2f);
                    float barY = rect.Bottom - barH - MathF.Round(4f * s);
                    batch.FillRectangle(new RectangleF(rect.X + barInset, barY, barW, barH), new ColorRgba(8, 10, 14, 240));
                    batch.FillRectangle(new RectangleF(rect.X + barInset, barY, MathF.Max(2f, barW * frac), barH), dCol);
                }
            }
        }

        return hoveredSlot;
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
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawWindowChromeText(spriteBatch, font, in layout, PlayerModalTab.Inventory, hasSkills, "Inventory");

        ColorRgba black = CarbonUiPalette.TextOutline;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        for (int idx = 0; idx < PlayerInventoryLayout.TotalCapacity && idx < slots.Length; idx++)
        {
            RectangleF rect = GetSlotRect(in layout, idx);
            ItemSlot item = slots[idx];

            if (idx < 9)
            {
                spriteBatch.DrawOutlinedString(
                    font,
                    $"{idx + 1}",
                    new Vector2(rect.X + MathF.Round(5f * s), rect.Y + MathF.Round(3f * s)),
                    CarbonUiPalette.TextMuted,
                    black,
                    1,
                    layout.TextScale);
            }

            if (!item.IsEmpty && idx != this.CarriedSlot)
            {
                byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                if (upg > 0)
                {
                    string upgTxt = $"+{upg}";
                    TextMeasureResult uSize = font.MeasureString(upgTxt, TextLayoutOptions.Default, layout.TextScale);
                    spriteBatch.DrawOutlinedString(
                        font,
                        upgTxt,
                        new Vector2(rect.Right - uSize.Width - MathF.Round(4f * s), rect.Y + MathF.Round(3f * s)),
                        CarbonUiPalette.TextWarm,
                        black,
                        1,
                        layout.TextScale);
                }

                if (item.Quantity > 1)
                {
                    string qTxt = $"{item.Quantity}";
                    TextMeasureResult qSize = font.MeasureString(qTxt, TextLayoutOptions.Default, layout.TextScale);
                    spriteBatch.DrawOutlinedString(
                        font,
                        qTxt,
                        new Vector2(rect.Right - qSize.Width - MathF.Round(5f * s), rect.Bottom - qSize.Height - MathF.Round(3f * s)),
                        CarbonUiPalette.TextPrimary,
                        black,
                        1,
                        layout.TextScale);
                }
            }
        }
    }

    public void DrawTopOverlayGeometry(
        GeometryBatch batch,
        IWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        int hoveredSlot)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;

        if (this.CarriedSlot >= 0 && mouseScreenPos.HasValue && art is not null)
        {
            Span<ItemSlot> dragSpan = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(dragSpan);
            if (this.CarriedSlot < dragSpan.Length)
            {
                ItemSlot carried = dragSpan[this.CarriedSlot];
                if (!carried.IsEmpty && art.TryGetItem(carried.ItemId, out CompiledItemVectorArt? dragArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, dragArt, mouseScreenPos.Value, MathF.Round(48f * s));
                }
            }

            return;
        }

        if (hoveredSlot >= 0 && mouseScreenPos.HasValue)
        {
            Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(slots);
            if (hoveredSlot < slots.Length && !slots[hoveredSlot].IsEmpty)
            {
                float tipW = MathF.Round(300f * s);
                float tipH = MathF.Round(70f * s);
                float tx = Math.Clamp(mouseScreenPos.Value.X + MathF.Round(16f * s), 12f, viewportWidth - tipW - 12f);
                float ty = Math.Clamp(mouseScreenPos.Value.Y - tipH - MathF.Round(8f * s), 12f, viewportHeight - tipH - 12f);
                batch.FillRectangle(new RectangleF(tx, ty, tipW, tipH), CarbonUiPalette.WindowFill);
                batch.DrawRectangle(new RectangleF(tx, ty, tipW, tipH), CarbonUiPalette.BorderOuter, 1.5f);
            }
        }
    }

    public void DrawTopOverlayText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        int hoveredSlot)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        float s = layout.Scale;
        ColorRgba black = CarbonUiPalette.TextOutline;

        if (this.CarriedSlot >= 0 && mouseScreenPos.HasValue)
        {
            Span<ItemSlot> dragSpan = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(dragSpan);
            if (this.CarriedSlot < dragSpan.Length && dragSpan[this.CarriedSlot].Quantity > 1)
            {
                string qty = $"{dragSpan[this.CarriedSlot].Quantity}";
                spriteBatch.DrawOutlinedString(
                    font,
                    qty,
                    mouseScreenPos.Value + new Vector2(MathF.Round(10f * s), MathF.Round(6f * s)),
                    CarbonUiPalette.TextPrimary,
                    black,
                    1,
                    layout.TextScale);
            }

            return;
        }

        if (hoveredSlot < 0 || !mouseScreenPos.HasValue)
        {
            return;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        if (hoveredSlot >= slots.Length || slots[hoveredSlot].IsEmpty)
        {
            return;
        }

        ItemSlot hItem = slots[hoveredSlot];
        float tipW = MathF.Round(300f * s);
        float tipH = MathF.Round(70f * s);
        float tx = Math.Clamp(mouseScreenPos.Value.X + MathF.Round(16f * s), 12f, viewportWidth - tipW - 12f);
        float ty = Math.Clamp(mouseScreenPos.Value.Y - tipH - MathF.Round(8f * s), 12f, viewportHeight - tipH - 12f);

        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in hItem);
        string title = upg > 0 ? $"{world.Items.GetDisplayName(hItem.ItemId)} +{upg}" : world.Items.GetDisplayName(hItem.ItemId);
        float padX = MathF.Round(10f * s);
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, title, tipW - (padX * 2f), layout.TextScale),
            new Vector2(tx + padX, ty + MathF.Round(10f * s)),
            CarbonUiPalette.TextWarm,
            black,
            1,
            layout.TextScale);

        float upgMult = ItemEnhancementPolicy.GetStatMultiplier(upg);
        string statLine = world.Items.Rules.Items.TryGet(hItem.ItemId, out ItemArchetype? def)
            ? (def.Category == ItemCategory.Armor
                ? $"+{(int)MathF.Round(def.ArmorDefense * upgMult)} DEF  +{(int)MathF.Round(def.BonusHealth * upgMult)} HP"
                : def.Category == ItemCategory.Weapon
                    ? $"+{(int)MathF.Round(def.BaseDamage * upgMult)} DMG  RNG {(int)def.AttackRange}"
                    : def.Category == ItemCategory.Tool
                        ? $"Tier {def.HarvestTier} Tool"
                        : def.Category == ItemCategory.Consumable
                            ? $"+{(int)def.HealHealth} HP  +{(int)def.RestoreMana} MP"
                            : $"Stack {hItem.Quantity}/{def.MaxStackSize}")
            : $"x{hItem.Quantity}";
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, statLine, tipW - (padX * 2f), layout.TextScale),
            new Vector2(tx + padX, ty + MathF.Round(37f * s)),
            CarbonUiPalette.TextMuted,
            black,
            1,
            layout.TextScale);
    }

    internal static RectangleF GetSlotRectForTesting(in PlayerModalLayout layout, int slotIndex) =>
        GetSlotRect(in layout, slotIndex);

    private static RectangleF GetStageCardRect(in PlayerModalLayout layout) =>
        new(layout.ContentX, layout.ContentY, layout.ContentW, MathF.Round(172f * layout.Scale));

    private static RectangleF GetPaperDollRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        float dollW = MathF.Round(264f * s);
        float dollH = MathF.Round(156f * s);
        return new RectangleF(
            layout.Ox + MathF.Floor((layout.PanelW - dollW) * 0.5f),
            layout.ContentY + MathF.Round(8f * s),
            dollW,
            dollH);
    }

    private static RectangleF GetSlotRect(in PlayerModalLayout layout, int slotIndex)
    {
        float s = layout.Scale;
        float slotSize = MathF.Round(BaseSlotSize * s);
        float slotGap = MathF.Round(BaseSlotGap * s);

        if (slotIndex >= PlayerInventoryLayout.BagLimit)
        {
            RectangleF doll = GetPaperDollRect(in layout);
            float armorGapX = MathF.Round(22f * s);
            float leftX = doll.X - armorGapX - slotSize;
            float rightX = doll.Right + armorGapX;
            float topY = layout.ContentY + MathF.Round(10f * s);
            float botY = topY + slotSize + MathF.Round(12f * s);
            int armorIdx = slotIndex - PlayerInventoryLayout.BagLimit;
            return armorIdx switch
            {
                0 => new RectangleF(leftX, topY, slotSize, slotSize),
                1 => new RectangleF(leftX, botY, slotSize, slotSize),
                2 => new RectangleF(rightX, topY, slotSize, slotSize),
                _ => new RectangleF(rightX, botY, slotSize, slotSize),
            };
        }

        float bagStartY = layout.ContentY + MathF.Round(172f * s) + MathF.Round(12f * s);
        if (slotIndex < 9)
        {
            float hotbarY = bagStartY + (3f * slotSize) + (2f * slotGap) + MathF.Round(22f * s);
            return new RectangleF(layout.ContentX + (slotIndex * (slotSize + slotGap)), hotbarY, slotSize, slotSize);
        }

        int bagIdx = slotIndex - 9;
        int col = bagIdx % 9;
        int row = bagIdx / 9;
        return new RectangleF(
            layout.ContentX + (col * (slotSize + slotGap)),
            bagStartY + (row * (slotSize + slotGap)),
            slotSize,
            slotSize);
    }

    private static void DrawEmptyArmorSilhouette(GeometryBatch batch, in RectangleF rect, int armorIndex)
    {
        float cx = rect.X + (rect.Width * 0.5f);
        float cy = rect.Y + (rect.Height * 0.5f);
        float u = rect.Width / 50f;
        ColorRgba ghost = new(76, 86, 102, 115);

        switch (armorIndex)
        {
            case 0: // Helmet
                batch.FillRectangle(new RectangleF(cx - (10f * u), cy - (9f * u), 20f * u, 12f * u), ghost);
                batch.FillRectangle(new RectangleF(cx - (12f * u), cy + (1f * u), 24f * u, 5f * u), ghost);
                break;
            case 1: // Chestplate
                batch.FillRectangle(new RectangleF(cx - (13f * u), cy - (10f * u), 26f * u, 7f * u), ghost);
                batch.FillRectangle(new RectangleF(cx - (9f * u), cy - (3f * u), 18f * u, 14f * u), ghost);
                break;
            case 2: // Greaves / Legs
                batch.FillRectangle(new RectangleF(cx - (9f * u), cy - (11f * u), 18f * u, 6f * u), ghost);
                batch.FillRectangle(new RectangleF(cx - (9f * u), cy - (5f * u), 7f * u, 16f * u), ghost);
                batch.FillRectangle(new RectangleF(cx + (2f * u), cy - (5f * u), 7f * u, 16f * u), ghost);
                break;
            default: // Boots
                batch.FillRectangle(new RectangleF(cx - (10f * u), cy - (8f * u), 7f * u, 12f * u), ghost);
                batch.FillRectangle(new RectangleF(cx - (12f * u), cy + (3f * u), 10f * u, 6f * u), ghost);
                batch.FillRectangle(new RectangleF(cx + (3f * u), cy - (8f * u), 7f * u, 12f * u), ghost);
                batch.FillRectangle(new RectangleF(cx + (2f * u), cy + (3f * u), 10f * u, 6f * u), ghost);
                break;
        }
    }
}
