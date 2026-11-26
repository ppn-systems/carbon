// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions;
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
/// Minecraft-inspired compact 40-slot Bag &amp; Armor modal (<c>[I]</c>) framed cleanly with
/// <see cref="CarbonUiPalette"/> and shared <see cref="PlayerModalTabs"/>.
/// Uses direct slot-to-slot click/drag moves, Shift/RMB quick-equip, and drag-outside-to-drop (SRP).
/// </summary>
internal sealed class InventoryModalWidget
{
    public const float PanelWidth = 556f;
    public const float PanelHeight = 464f;

    private const float SlotSize = 50f;
    private const float SlotGap = 6f;

    private readonly List<SkillDefinition> _skillScratch = new(8);

    public int CarriedSlot { get; set; } = -1;

    public bool ContainsPoint(IClientWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        if (mousePos.X >= ox && mousePos.X <= ox + PanelWidth && mousePos.Y >= oy && mousePos.Y <= oy + PanelHeight)
        {
            return true;
        }

        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        return PlayerModalTabs.GetTabBarBounds(ox, oy, hasSkills).Contains(mousePos);
    }

    public bool TryHitTestCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return PlayerModalTabs.GetCloseButtonRect(ox, PanelWidth, oy).Contains(mousePos);
    }

    public bool TryHitTestTab(IClientWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out PlayerModalTab tab)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        return PlayerModalTabs.TryHitTestTab(mousePos, ox, oy, hasSkills, out tab);
    }

    public bool TryHitTestSlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity; s++)
        {
            if (GetSlotRect(ox, oy, s).Contains(mousePos))
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
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawTabsSkin(spriteBatch, skin, ox, PanelWidth, oy, PlayerModalTab.Inventory, hasSkills, mouseScreenPos);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelWidth, PanelHeight), CarbonUiPalette.BorderOuter, bodyAlpha: 246);

        RectangleF dollCard = GetPaperDollRect(ox, oy);
        skin.DrawCard(spriteBatch, in dollCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            RectangleF rect = GetSlotRect(ox, oy, s);
            bool hovered = mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value);
            bool carried = s == this.CarriedSlot;
            bool isArmor = s >= PlayerInventoryLayout.BagLimit;

            ColorRgba fill = carried
                ? CarbonUiPalette.SlotSelectedFill
                : (hovered ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill);
            ColorRgba border = carried
                ? CarbonUiPalette.BorderActive
                : (hovered ? CarbonUiPalette.BorderOuter : (isArmor ? CarbonUiPalette.BorderOuter : CarbonUiPalette.BorderSubtle));
            skin.DrawSlot(spriteBatch, in rect, fill, border);
        }
    }

    public int DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        float animTime)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        RectangleF dollRect = GetPaperDollRect(ox, oy);
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

            Vector2 pedPos = new(dollRect.X + (dollRect.Width * 0.5f), dollRect.Y + dollRect.Height - 16f);
            batch.FillCircle(pedPos + new Vector2(0f, -6f), 26f, new ColorRgba(6, 9, 14, 150), 16);
            batch.DrawCircle(pedPos + new Vector2(0f, -6f), 27f, CarbonUiPalette.BorderSubtle, 16, 1.2f);
            VectorRigRenderer.DrawEntityRig(
                batch,
                heroRig,
                pedPos,
                Direction8.South,
                "idle",
                animTime,
                isNormalized: false,
                targetHeightOverride: 78f,
                hitFlash: 0f,
                equippedItem: weaponArt,
                action: default,
                swingTimer: 0f,
                appearancePreset: world.LocalPlayerAppearancePreset,
                equipment: loadout,
                art: art);
        }

        int hoveredSlot = -1;
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            RectangleF rect = GetSlotRect(ox, oy, s);
            if (mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value))
            {
                hoveredSlot = s;
            }

            ItemSlot item = slots[s];
            if (s >= PlayerInventoryLayout.BagLimit && (item.IsEmpty || s == this.CarriedSlot))
            {
                DrawEmptyArmorSilhouette(batch, rect, s - PlayerInventoryLayout.BagLimit);
            }

            if (!item.IsEmpty && s != this.CarriedSlot)
            {
                if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? itemArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, itemArt, new Vector2(rect.X + (SlotSize * 0.5f), rect.Y + (SlotSize * 0.46f)), 32f);
                }

                if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? def) && def.MaxDurability > 0f)
                {
                    byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                    float effMax = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(def, upg));
                    float frac = Math.Clamp(item.Durability / effMax, 0f, 1f);
                    ColorRgba dCol = frac <= 0f
                        ? CarbonUiPalette.HpFill
                        : (frac > 0.35f ? CarbonUiPalette.StaminaFill : CarbonUiPalette.XpFill);
                    batch.FillRectangle(new RectangleF(rect.X + 6f, rect.Y + SlotSize - 6f, SlotSize - 12f, 3f), new ColorRgba(8, 10, 14, 240));
                    batch.FillRectangle(new RectangleF(rect.X + 6f, rect.Y + SlotSize - 6f, MathF.Max(1f, (SlotSize - 12f) * frac), 3f), dCol);
                }
            }
        }

        return hoveredSlot;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        bool hasSkills = PlayerModalTabs.HasClassSkills(world, _skillScratch);
        PlayerModalTabs.DrawTabsText(spriteBatch, font, ox, PanelWidth, oy, PlayerModalTab.Inventory, hasSkills);

        ColorRgba black = CarbonUiPalette.TextOutline;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        spriteBatch.DrawOutlinedString(font, "Inventory", new Vector2(ox + 28f, oy + 14f), CarbonUiPalette.TextWarm, black, 1, 1f);

        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            RectangleF rect = GetSlotRect(ox, oy, s);
            ItemSlot item = slots[s];

            if (s < 9)
            {
                spriteBatch.DrawOutlinedString(font, $"{s + 1}", new Vector2(rect.X + 4f, rect.Y + 2f), CarbonUiPalette.TextMuted, black, 1, 1f);
            }

            if (!item.IsEmpty && s != this.CarriedSlot)
            {
                byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                if (upg > 0)
                {
                    spriteBatch.DrawOutlinedString(font, $"+{upg}", new Vector2(rect.Right - 22f, rect.Y + 2f), CarbonUiPalette.TextWarm, black, 1, 1f);
                }

                if (item.Quantity > 1)
                {
                    string qTxt = $"{item.Quantity}";
                    TextMeasureResult qSize = font.MeasureString(qTxt, TextLayoutOptions.Default, 1f);
                    spriteBatch.DrawOutlinedString(font, qTxt, new Vector2(rect.Right - qSize.Width - 4f, rect.Bottom - 18f), CarbonUiPalette.TextPrimary, black, 1, 1f);
                }
            }
        }
    }

    public void DrawTopOverlayGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        int hoveredSlot)
    {
        if (this.CarriedSlot >= 0 && mouseScreenPos.HasValue && art is not null)
        {
            Span<ItemSlot> dragSpan = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(dragSpan);
            if (this.CarriedSlot < dragSpan.Length)
            {
                ItemSlot carried = dragSpan[this.CarriedSlot];
                if (!carried.IsEmpty && art.TryGetItem(carried.ItemId, out CompiledItemVectorArt? dragArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, dragArt, mouseScreenPos.Value, 32f);
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
                const float tipW = 248f;
                const float tipH = 56f;
                float tx = Math.Clamp(mouseScreenPos.Value.X + 16f, 12f, viewportWidth - tipW - 12f);
                float ty = Math.Clamp(mouseScreenPos.Value.Y - tipH - 6f, 12f, viewportHeight - tipH - 12f);
                batch.FillRectangle(new RectangleF(tx, ty, tipW, tipH), CarbonUiPalette.WindowFill);
                batch.DrawRectangle(new RectangleF(tx, ty, tipW, tipH), CarbonUiPalette.BorderOuter, 1.4f);
            }
        }
    }

    public void DrawTopOverlayText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        int hoveredSlot)
    {
        ColorRgba black = CarbonUiPalette.TextOutline;
        if (this.CarriedSlot >= 0 && mouseScreenPos.HasValue)
        {
            Span<ItemSlot> dragSpan = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(dragSpan);
            if (this.CarriedSlot < dragSpan.Length && dragSpan[this.CarriedSlot].Quantity > 1)
            {
                string qty = $"{dragSpan[this.CarriedSlot].Quantity}";
                spriteBatch.DrawOutlinedString(font, qty, mouseScreenPos.Value + new Vector2(8f, 4f), CarbonUiPalette.TextPrimary, black, 1, 1f);
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
        const float tipW = 248f;
        const float tipH = 56f;
        float tx = Math.Clamp(mouseScreenPos.Value.X + 16f, 12f, viewportWidth - tipW - 12f);
        float ty = Math.Clamp(mouseScreenPos.Value.Y - tipH - 6f, 12f, viewportHeight - tipH - 12f);

        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in hItem);
        string title = upg > 0 ? $"{StarterGameCatalog.GetDisplayName(hItem.ItemId)} +{upg}" : StarterGameCatalog.GetDisplayName(hItem.ItemId);
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, title, tipW - 16f, 1f), new Vector2(tx + 8f, ty + 8f), CarbonUiPalette.TextWarm, black, 1, 1f);

        float upgMult = ItemEnhancementPolicy.GetStatMultiplier(upg);
        string statLine = StarterGameCatalog.Items.TryGet(hItem.ItemId, out ItemArchetype? def)
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
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, statLine, tipW - 16f, 1f), new Vector2(tx + 8f, ty + 29f), CarbonUiPalette.TextMuted, black, 1, 1f);
    }

    public static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelWidth) * 0.5f), MathF.Max(42f, MathF.Floor((viewportHeight - PanelHeight) * 0.5f)));

    private static RectangleF GetPaperDollRect(float ox, float oy) => new(ox + 194f, oy + 46f, 168f, 118f);

    private static RectangleF GetSlotRect(float ox, float oy, int slotIndex)
    {
        if (slotIndex >= PlayerInventoryLayout.BagLimit)
        {
            int armorIdx = slotIndex - PlayerInventoryLayout.BagLimit;
            return armorIdx switch
            {
                0 => new RectangleF(ox + 128f, oy + 50f, SlotSize, SlotSize),
                1 => new RectangleF(ox + 128f, oy + 108f, SlotSize, SlotSize),
                2 => new RectangleF(ox + 378f, oy + 50f, SlotSize, SlotSize),
                _ => new RectangleF(ox + 378f, oy + 108f, SlotSize, SlotSize),
            };
        }

        const float gridX = 29f;
        if (slotIndex < 9)
        {
            return new RectangleF(ox + gridX + (slotIndex * (SlotSize + SlotGap)), oy + 388f, SlotSize, SlotSize);
        }

        int bagIdx = slotIndex - 9;
        int col = bagIdx % 9;
        int row = bagIdx / 9;
        return new RectangleF(ox + gridX + (col * (SlotSize + SlotGap)), oy + 192f + (row * (SlotSize + SlotGap)), SlotSize, SlotSize);
    }

    private static void DrawEmptyArmorSilhouette(GeometryBatch batch, in RectangleF rect, int armorIndex)
    {
        float cx = rect.X + (rect.Width * 0.5f);
        float cy = rect.Y + (rect.Height * 0.5f);
        ColorRgba ghost = new(76, 86, 102, 115);

        switch (armorIndex)
        {
            case 0: // Helmet
                batch.FillRectangle(new RectangleF(cx - 10f, cy - 9f, 20f, 12f), ghost);
                batch.FillRectangle(new RectangleF(cx - 12f, cy + 1f, 24f, 5f), ghost);
                break;
            case 1: // Chestplate
                batch.FillRectangle(new RectangleF(cx - 13f, cy - 10f, 26f, 7f), ghost);
                batch.FillRectangle(new RectangleF(cx - 9f, cy - 3f, 18f, 14f), ghost);
                break;
            case 2: // Greaves / Legs
                batch.FillRectangle(new RectangleF(cx - 9f, cy - 11f, 18f, 6f), ghost);
                batch.FillRectangle(new RectangleF(cx - 9f, cy - 5f, 7f, 16f), ghost);
                batch.FillRectangle(new RectangleF(cx + 2f, cy - 5f, 7f, 16f), ghost);
                break;
            default: // Boots
                batch.FillRectangle(new RectangleF(cx - 10f, cy - 8f, 7f, 12f), ghost);
                batch.FillRectangle(new RectangleF(cx - 12f, cy + 3f, 10f, 6f), ghost);
                batch.FillRectangle(new RectangleF(cx + 3f, cy - 8f, 7f, 12f), ghost);
                batch.FillRectangle(new RectangleF(cx + 2f, cy + 3f, 10f, 6f), ghost);
                break;
        }
    }
}
