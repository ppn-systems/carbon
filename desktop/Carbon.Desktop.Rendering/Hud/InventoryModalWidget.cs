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
/// 42-slot Bag, Armor &amp; Accessory modal (<c>[I]</c>) framed inside the fixed <see cref="PlayerModalTabs"/>
/// workspace. Supports cursor pickup, Shift/RMB quick-equip, drag-outside-to-drop, a read-only "Held" mirror of the
/// selected hotbar item, a Sort button, rarity borders, rich tooltips, and the split/confirm dialogs (ADR-020).
/// </summary>
internal sealed class InventoryModalWidget
{
    private const float BaseSlotSize = 70f;
    private const float BaseSlotGap = 8f;

    private readonly List<SkillDefinition> _skillScratch = new(8);
    private readonly ItemTooltipBuilder _tooltip = new();

    /// <summary>Gets the split/confirm dialog state shown on top of the bag.</summary>
    public InventoryDialogState Dialog { get; } = new();

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

    public bool TryHitTestSortButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        return GetSortButtonRect(in layout).Contains(mousePos);
    }

    public InventoryDialogButton HitTestDialog(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        if (!this.Dialog.IsOpen)
        {
            return InventoryDialogButton.None;
        }

        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        bool split = this.Dialog.Kind == InventoryDialogKind.Split;
        if (GetDialogButtonRect(in layout, InventoryDialogButton.Confirm).Contains(mousePos))
        {
            return InventoryDialogButton.Confirm;
        }

        if (GetDialogButtonRect(in layout, InventoryDialogButton.Cancel).Contains(mousePos))
        {
            return InventoryDialogButton.Cancel;
        }

        if (split && GetDialogButtonRect(in layout, InventoryDialogButton.Minus).Contains(mousePos))
        {
            return InventoryDialogButton.Minus;
        }

        if (split && GetDialogButtonRect(in layout, InventoryDialogButton.Plus).Contains(mousePos))
        {
            return InventoryDialogButton.Plus;
        }

        return GetDialogRect(in layout).Contains(mousePos) ? InventoryDialogButton.Body : InventoryDialogButton.None;
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
            ItemRarity rarity = slots[s].IsEmpty || carried ? ItemRarity.Common : ItemRarityColors.GetRarity(world.Items.Rules, slots[s].ItemId);

            ColorRgba fill = carried
                ? CarbonUiPalette.SlotSelectedFill
                : (hovered ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill);
            ColorRgba border = carried
                ? CarbonUiPalette.BorderActive
                : rarity != ItemRarity.Common
                    ? ItemRarityColors.GetBorder(rarity)
                    : (hovered || isArmor ? CarbonUiPalette.BorderOuter : CarbonUiPalette.BorderSubtle);
            skin.DrawSlot(spriteBatch, in rect, fill, border);
        }

        RectangleF heldRect = GetHeldSlotRect(in layout);
        ItemSlot held = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
        ItemRarity heldRarity = held.IsEmpty ? ItemRarity.Common : ItemRarityColors.GetRarity(world.Items.Rules, held.ItemId);
        skin.DrawInset(
            spriteBatch,
            in heldRect,
            CarbonUiPalette.SlotFill,
            heldRarity != ItemRarity.Common ? ItemRarityColors.GetBorder(heldRarity) : CarbonUiPalette.BorderSubtle);

        RectangleF sortRect = GetSortButtonRect(in layout);
        bool sortHover = mouseScreenPos.HasValue && sortRect.Contains(mouseScreenPos.Value);
        skin.DrawButton(
            spriteBatch,
            in sortRect,
            sortHover ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.CardFill,
            sortHover ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderOuter);
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
                DrawEmptyEquipmentSilhouette(batch, in rect, idx - PlayerInventoryLayout.BagLimit);
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

        RectangleF heldRect = GetHeldSlotRect(in layout);
        ItemSlot heldItem = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
        if (!heldItem.IsEmpty && art is not null && art.TryGetItem(heldItem.ItemId, out CompiledItemVectorArt? heldArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, heldArt, new Vector2(heldRect.X + (heldRect.Width * 0.5f), heldRect.Y + (heldRect.Height * 0.5f)), iconSize);
        }
        else if (heldItem.IsEmpty)
        {
            DrawEmptyEquipmentSilhouette(batch, in heldRect, -1);
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
        this.DrawStageText(spriteBatch, font, world, in layout);

        for (int idx = 0; idx < PlayerInventoryLayout.TotalCapacity && idx < slots.Length; idx++)
        {
            RectangleF rect = GetSlotRect(in layout, idx);
            ItemSlot item = slots[idx];

            if (idx >= PlayerInventoryLayout.Accessory1Slot && item.IsEmpty)
            {
                DrawCaption(spriteBatch, font, in rect, "Ring", layout.TextScale, s);
            }

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

    private void DrawStageText(SpriteBatch spriteBatch, BitmapFont font, IWorldReadModel world, in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF heldRect = GetHeldSlotRect(in layout);
        string heldLabel = $"Held {world.SelectedHotbarSlot + 1}";
        TextMeasureResult heldSize = font.MeasureString(heldLabel, TextLayoutOptions.Default, layout.TextScale);
        spriteBatch.DrawOutlinedString(
            font,
            heldLabel,
            new Vector2(heldRect.X + MathF.Floor((heldRect.Width - heldSize.Width) * 0.5f), heldRect.Bottom + MathF.Round(5f * s)),
            CarbonUiPalette.TextMuted,
            CarbonUiPalette.TextOutline,
            1,
            layout.TextScale);

        RectangleF sortRect = GetSortButtonRect(in layout);
        const string sortLabel = "Sort";
        TextMeasureResult size = font.MeasureString(sortLabel, TextLayoutOptions.Default, layout.TextScale);
        spriteBatch.DrawOutlinedString(
            font,
            sortLabel,
            new Vector2(sortRect.X + MathF.Floor((sortRect.Width - size.Width) * 0.5f), sortRect.Y + MathF.Floor((sortRect.Height - size.Height) * 0.5f)),
            CarbonUiPalette.TextWarm,
            CarbonUiPalette.TextOutline,
            1,
            layout.TextScale);
    }

    private static void DrawCaption(SpriteBatch spriteBatch, BitmapFont font, in RectangleF rect, string text, float textScale, float s)
    {
        TextMeasureResult size = font.MeasureString(text, TextLayoutOptions.Default, textScale);
        spriteBatch.DrawOutlinedString(
            font,
            text,
            new Vector2(rect.X + MathF.Floor((rect.Width - size.Width) * 0.5f), rect.Bottom - size.Height - MathF.Round(3f * s)),
            CarbonUiPalette.TextMuted,
            CarbonUiPalette.TextOutline,
            1,
            textScale);
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
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        if (this.Dialog.IsOpen)
        {
            this.DrawDialogGeometry(batch, world, art, in layout, mouseScreenPos);
            return;
        }

        if (this.CarriedSlot >= 0 && mouseScreenPos.HasValue && art is not null)
        {
            if (this.CarriedSlot < slots.Length)
            {
                ItemSlot carried = slots[this.CarriedSlot];
                if (!carried.IsEmpty && art.TryGetItem(carried.ItemId, out CompiledItemVectorArt? dragArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, dragArt, mouseScreenPos.Value, MathF.Round(48f * s));
                }
            }

            return;
        }

        if (hoveredSlot >= 0 && hoveredSlot < slots.Length && !slots[hoveredSlot].IsEmpty && mouseScreenPos.HasValue)
        {
            _ = _tooltip.Update(world.Items, hoveredSlot, in slots[hoveredSlot]);
            RectangleF tip = this.GetTooltipRect(in layout, mouseScreenPos.Value, viewportWidth, viewportHeight);
            ItemRarity rarity = ItemRarityColors.GetRarity(world.Items.Rules, slots[hoveredSlot].ItemId);
            ColorRgba frame = rarity == ItemRarity.Common ? CarbonUiPalette.BorderOuter : ItemRarityColors.GetBorder(rarity);
            batch.FillRectangle(tip, CarbonUiPalette.WindowFill);
            batch.FillRectangle(new RectangleF(tip.X, tip.Y, tip.Width, MathF.Max(2f, MathF.Round(3f * s))), frame);
            batch.DrawRectangle(tip, frame, 1.5f);
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
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        if (this.Dialog.IsOpen)
        {
            this.DrawDialogText(spriteBatch, font, world, in layout);
            return;
        }

        if (this.CarriedSlot >= 0 && mouseScreenPos.HasValue)
        {
            if (this.CarriedSlot < slots.Length && slots[this.CarriedSlot].Quantity > 1)
            {
                string qty = $"{slots[this.CarriedSlot].Quantity}";
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

        if (hoveredSlot < 0 || !mouseScreenPos.HasValue || hoveredSlot >= slots.Length || slots[hoveredSlot].IsEmpty)
        {
            return;
        }

        _ = _tooltip.Update(world.Items, hoveredSlot, in slots[hoveredSlot]);
        RectangleF tip = this.GetTooltipRect(in layout, mouseScreenPos.Value, viewportWidth, viewportHeight);
        float padX = MathF.Round(10f * s);
        float lineH = GetTooltipLineHeight(in layout);
        float y = tip.Y + MathF.Round(9f * s);
        IReadOnlyList<ItemTooltipLine> lines = _tooltip.Lines;
        for (int i = 0; i < lines.Count; i++)
        {
            spriteBatch.DrawOutlinedString(
                font,
                CarbonUiSkin.TruncateToWidth(font, lines[i].Text, tip.Width - (padX * 2f), layout.TextScale),
                new Vector2(tip.X + padX, y),
                lines[i].Color,
                black,
                1,
                layout.TextScale);
            y += lineH;
        }

        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, ControlsHint, tip.Width - (padX * 2f), layout.TextScale),
            new Vector2(tip.X + padX, y + MathF.Round(2f * s)),
            CarbonUiPalette.BorderOuter,
            black,
            1,
            layout.TextScale);
    }

    internal static RectangleF GetHeldSlotRectForTesting(in PlayerModalLayout layout) => GetHeldSlotRect(in layout);

    internal static RectangleF GetSortButtonRectForTesting(in PlayerModalLayout layout) => GetSortButtonRect(in layout);

    internal static RectangleF GetDialogButtonRectForTesting(in PlayerModalLayout layout, InventoryDialogButton button) =>
        GetDialogButtonRect(in layout, button);

    private const string ControlsHint = "Ctrl+Click split  Del destroy  1-9 hotbar";

    private static float GetTooltipLineHeight(in PlayerModalLayout layout) =>
        MathF.Round(23f * layout.Scale * layout.TextScale);

    private RectangleF GetTooltipRect(in PlayerModalLayout layout, Vector2 mouse, int viewportWidth, int viewportHeight)
    {
        float s = layout.Scale;
        float tipW = MathF.Round(410f * s * layout.TextScale);
        float tipH = MathF.Round(18f * s) + ((_tooltip.Lines.Count + 1) * GetTooltipLineHeight(in layout));
        float tx = Math.Clamp(mouse.X + MathF.Round(18f * s), 12f, MathF.Max(12f, viewportWidth - tipW - 12f));
        float ty = Math.Clamp(mouse.Y - tipH - MathF.Round(8f * s), 12f, MathF.Max(12f, viewportHeight - tipH - 12f));
        return new RectangleF(tx, ty, tipW, tipH);
    }

    private void DrawDialogGeometry(GeometryBatch batch, IWorldReadModel world, IVectorArtRepository? art, in PlayerModalLayout layout, Vector2? mouse)
    {
        float s = layout.Scale;
        batch.FillRectangle(new RectangleF(layout.Ox, layout.Oy, layout.PanelW, layout.PanelH), new ColorRgba(4, 6, 12, 160));
        RectangleF box = GetDialogRect(in layout);
        bool destroy = this.Dialog.Kind == InventoryDialogKind.ConfirmDestroy;
        ColorRgba frame = destroy ? CarbonUiPalette.DangerBorder : CarbonUiPalette.BorderActive;
        batch.FillRectangle(box, CarbonUiPalette.WindowFill);
        batch.FillRectangle(new RectangleF(box.X, box.Y, box.Width, MathF.Max(2f, MathF.Round(3f * s))), frame);
        batch.DrawRectangle(box, frame, 2f);

        RectangleF iconRect = GetDialogIconRect(in layout);
        ItemRarity rarity = ItemRarityColors.GetRarity(world.Items.Rules, this.Dialog.Item.ItemId);
        batch.FillRectangle(iconRect, CarbonUiPalette.SlotFill);
        batch.DrawRectangle(iconRect, rarity == ItemRarity.Common ? CarbonUiPalette.BorderSubtle : ItemRarityColors.GetBorder(rarity), 1.5f);
        if (art is not null && art.TryGetItem(this.Dialog.Item.ItemId, out CompiledItemVectorArt? itemArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, itemArt, new Vector2(iconRect.X + (iconRect.Width * 0.5f), iconRect.Y + (iconRect.Height * 0.5f)), MathF.Round(42f * s));
        }

        if (this.Dialog.Kind == InventoryDialogKind.Split)
        {
            RectangleF bar = GetSplitBarRect(in layout);
            float frac = this.Dialog.Item.Quantity > 0 ? this.Dialog.Quantity / (float)this.Dialog.Item.Quantity : 0f;
            batch.FillRectangle(bar, new ColorRgba(8, 10, 14, 240));
            batch.FillRectangle(new RectangleF(bar.X, bar.Y, MathF.Max(2f, bar.Width * frac), bar.Height), CarbonUiPalette.XpFill);
            DrawDialogButton(batch, GetDialogButtonRect(in layout, InventoryDialogButton.Minus), mouse, CarbonUiPalette.CardFill, CarbonUiPalette.BorderOuter);
            DrawDialogButton(batch, GetDialogButtonRect(in layout, InventoryDialogButton.Plus), mouse, CarbonUiPalette.CardFill, CarbonUiPalette.BorderOuter);
        }

        DrawDialogButton(
            batch,
            GetDialogButtonRect(in layout, InventoryDialogButton.Confirm),
            mouse,
            destroy ? CarbonUiPalette.DangerFill : CarbonUiPalette.ConfirmFill,
            destroy ? CarbonUiPalette.DangerBorder : CarbonUiPalette.ConfirmBorder);
        DrawDialogButton(batch, GetDialogButtonRect(in layout, InventoryDialogButton.Cancel), mouse, CarbonUiPalette.CardFill, CarbonUiPalette.BorderOuter);
    }

    private void DrawDialogText(SpriteBatch spriteBatch, BitmapFont font, IWorldReadModel world, in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF box = GetDialogRect(in layout);
        RectangleF iconRect = GetDialogIconRect(in layout);
        ItemSlot item = this.Dialog.Item;
        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
        string name = world.Items.GetDisplayName(item.ItemId);
        if (upg > 0)
        {
            name = $"{name} +{upg}";
        }

        int qty = this.Dialog.EffectiveQuantity;
        (string title, string body, string confirm) = this.Dialog.Kind switch
        {
            InventoryDialogKind.Split => ("Split stack", $"Take {this.Dialog.Quantity} of {item.Quantity}", "Split"),
            InventoryDialogKind.ConfirmDestroy => ("Destroy item?", qty > 1 ? $"All {qty} are destroyed. No undo." : "It is destroyed. No undo.", "Destroy"),
            InventoryDialogKind.ConfirmDrop or InventoryDialogKind.None or _ =>
                ("Drop item?", qty > 1 ? $"Drop {qty} on the ground here?" : "Drop it on the ground here?", "Drop"),
        };

        ItemRarity rarity = ItemRarityColors.GetRarity(world.Items.Rules, item.ItemId);
        float textX = iconRect.Right + MathF.Round(12f * s);
        float textW = box.Right - textX - MathF.Round(14f * s);
        ColorRgba black = CarbonUiPalette.TextOutline;
        spriteBatch.DrawOutlinedString(font, title, new Vector2(textX, box.Y + MathF.Round(14f * s)), CarbonUiPalette.TextWarm, black, 1, layout.TitleScale);
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, name, textW, layout.TextScale),
            new Vector2(textX, box.Y + MathF.Round(44f * s)),
            ItemRarityColors.GetTitle(rarity),
            black,
            1,
            layout.TextScale);
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, body, textW, layout.TextScale),
            new Vector2(textX, box.Y + MathF.Round(68f * s)),
            CarbonUiPalette.TextPrimary,
            black,
            1,
            layout.TextScale);

        if (this.Dialog.Kind == InventoryDialogKind.Split)
        {
            RectangleF bar = GetSplitBarRect(in layout);
            spriteBatch.DrawOutlinedString(
                font,
                "Scroll or +/-  (Shift: 10)  Enter: split",
                new Vector2(bar.X, bar.Bottom + MathF.Round(12f * s)),
                CarbonUiPalette.BorderOuter,
                black,
                1,
                layout.TextScale);
            DrawCentered(spriteBatch, font, GetDialogButtonRect(in layout, InventoryDialogButton.Minus), "-", CarbonUiPalette.TextPrimary, layout.TitleScale);
            DrawCentered(spriteBatch, font, GetDialogButtonRect(in layout, InventoryDialogButton.Plus), "+", CarbonUiPalette.TextPrimary, layout.TitleScale);
        }

        DrawCentered(spriteBatch, font, GetDialogButtonRect(in layout, InventoryDialogButton.Confirm), confirm, CarbonUiPalette.TextPrimary, layout.TextScale);
        DrawCentered(spriteBatch, font, GetDialogButtonRect(in layout, InventoryDialogButton.Cancel), "Cancel", CarbonUiPalette.TextMuted, layout.TextScale);
    }

    private static void DrawDialogButton(GeometryBatch batch, RectangleF rect, Vector2? mouse, ColorRgba fill, ColorRgba frame)
    {
        bool hover = mouse.HasValue && rect.Contains(mouse.Value);
        batch.FillRectangle(rect, hover ? CarbonUiPalette.SlotHoverFill : fill);
        batch.DrawRectangle(rect, hover ? CarbonUiPalette.BorderActive : frame, 1.5f);
    }

    private static void DrawCentered(SpriteBatch spriteBatch, BitmapFont font, RectangleF rect, string text, ColorRgba color, float scale)
    {
        TextMeasureResult size = font.MeasureString(text, TextLayoutOptions.Default, scale);
        spriteBatch.DrawOutlinedString(
            font,
            text,
            new Vector2(rect.X + MathF.Floor((rect.Width - size.Width) * 0.5f), rect.Y + MathF.Floor((rect.Height - size.Height) * 0.5f)),
            color,
            CarbonUiPalette.TextOutline,
            1,
            scale);
    }

    private static RectangleF GetDialogRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        float w = MathF.Round(420f * s);
        float h = MathF.Round(224f * s);
        return new RectangleF(
            layout.Ox + MathF.Floor((layout.PanelW - w) * 0.5f),
            layout.Oy + MathF.Floor((layout.PanelH - h) * 0.5f),
            w,
            h);
    }

    private static RectangleF GetDialogIconRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF box = GetDialogRect(in layout);
        float size = MathF.Round(64f * s);
        return new RectangleF(box.X + MathF.Round(16f * s), box.Y + MathF.Round(18f * s), size, size);
    }

    private static RectangleF GetSplitBarRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF minus = GetDialogButtonRect(in layout, InventoryDialogButton.Minus);
        RectangleF plus = GetDialogButtonRect(in layout, InventoryDialogButton.Plus);
        float h = MathF.Round(10f * s);
        float gap = MathF.Round(10f * s);
        return new RectangleF(minus.Right + gap, minus.Y + MathF.Floor((minus.Height - h) * 0.5f), plus.X - minus.Right - (gap * 2f), h);
    }

    private static RectangleF GetDialogButtonRect(in PlayerModalLayout layout, InventoryDialogButton button)
    {
        float s = layout.Scale;
        RectangleF box = GetDialogRect(in layout);
        float pad = MathF.Round(16f * s);
        float btnH = MathF.Round(36f * s);
        float btnW = MathF.Round(130f * s);
        float bottomY = box.Bottom - pad - btnH;
        float stepSize = MathF.Round(32f * s);
        float stepY = box.Y + MathF.Round(100f * s);
        return button switch
        {
            InventoryDialogButton.Confirm => new RectangleF(box.Right - pad - btnW - MathF.Round(10f * s) - btnW, bottomY, btnW, btnH),
            InventoryDialogButton.Cancel => new RectangleF(box.Right - pad - btnW, bottomY, btnW, btnH),
            InventoryDialogButton.Minus => new RectangleF(box.X + pad, stepY, stepSize, stepSize),
            InventoryDialogButton.Plus => new RectangleF(box.Right - pad - stepSize, stepY, stepSize, stepSize),
            InventoryDialogButton.None or InventoryDialogButton.Body or _ => RectangleF.Empty,
        };
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
            float outerRightX = rightX + slotSize + MathF.Round(12f * s);
            int armorIdx = slotIndex - PlayerInventoryLayout.BagLimit;
            return armorIdx switch
            {
                0 => new RectangleF(leftX, topY, slotSize, slotSize),
                1 => new RectangleF(leftX, botY, slotSize, slotSize),
                2 => new RectangleF(rightX, topY, slotSize, slotSize),
                3 => new RectangleF(rightX, botY, slotSize, slotSize),
                4 => new RectangleF(outerRightX, topY, slotSize, slotSize),
                _ => new RectangleF(outerRightX, botY, slotSize, slotSize),
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

    /// <summary>Read-only mirror of the selected hotbar item, left of the helmet (the weapon is what the hotbar holds).</summary>
    private static RectangleF GetHeldSlotRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF head = GetSlotRect(in layout, PlayerInventoryLayout.HeadSlot);
        return new RectangleF(head.X - head.Width - MathF.Round(12f * s), head.Y, head.Width, head.Height);
    }

    private static RectangleF GetSortButtonRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF held = GetHeldSlotRect(in layout);
        RectangleF chest = GetSlotRect(in layout, PlayerInventoryLayout.ChestSlot);
        float h = MathF.Round(34f * s);
        return new RectangleF(held.X, chest.Bottom - h, held.Width, h);
    }

    private static void DrawEmptyEquipmentSilhouette(GeometryBatch batch, in RectangleF rect, int armorIndex)
    {
        if (armorIndex is < 0 or >= 4)
        {
            float cx0 = rect.X + (rect.Width * 0.5f);
            float cy0 = rect.Y + (rect.Height * 0.44f);
            float u0 = rect.Width / 50f;
            ColorRgba ghost0 = new(76, 86, 102, 115);
            if (armorIndex < 0)
            {
                // Held: a sword outline.
                batch.FillRectangle(new RectangleF(cx0 - (2f * u0), cy0 - (13f * u0), 4f * u0, 18f * u0), ghost0);
                batch.FillRectangle(new RectangleF(cx0 - (8f * u0), cy0 + (5f * u0), 16f * u0, 3f * u0), ghost0);
                batch.FillRectangle(new RectangleF(cx0 - (2f * u0), cy0 + (8f * u0), 4f * u0, 6f * u0), ghost0);
            }
            else
            {
                // Ring.
                batch.DrawCircle(new Vector2(cx0, cy0), 8f * u0, ghost0, 16, MathF.Max(2f, 3f * u0));
                batch.FillCircle(new Vector2(cx0, cy0 - (10f * u0)), 3f * u0, ghost0, 8);
            }

            return;
        }

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
