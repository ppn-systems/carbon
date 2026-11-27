// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
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
internal sealed class InventoryModalWidget(HudUiState state)
{
    private readonly List<SkillDefinition> _skillScratch = new(8);
    private readonly ItemTooltipBuilder _tooltip = new();
    private readonly string[] _heldLabels = new string[PlayerInventoryLayout.HotbarCount];
    private int _heldLabelVersion = -1;
    private readonly InventoryDialogRenderer _dialog = new();

    /// <summary>Gets the split/confirm dialog state shown on top of the bag.</summary>
    public InventoryDialogState Dialog => _dialog.State;

    /// <summary>Gets the carried slot (<see cref="HudUiState.CarriedInventorySlot"/>), drawn highlighted.</summary>
    public int CarriedSlot => state.CarriedInventorySlot;

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
            if (InventoryModalLayout.GetSlotRect(in layout, s).Contains(mousePos))
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
        return InventoryModalLayout.GetSortButtonRect(in layout).Contains(mousePos);
    }

    public InventoryDialogButton HitTestDialog(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        _dialog.HitTest(mousePos, viewportWidth, viewportHeight);

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

        RectangleF stageCard = InventoryModalLayout.GetStageCardRect(in layout);
        skin.DrawCard(spriteBatch, in stageCard, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        RectangleF dollCard = InventoryModalLayout.GetPaperDollRect(in layout);
        skin.DrawInset(spriteBatch, in dollCard, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < slots.Length; s++)
        {
            RectangleF rect = InventoryModalLayout.GetSlotRect(in layout, s);
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

        RectangleF heldRect = InventoryModalLayout.GetHeldSlotRect(in layout);
        ItemSlot held = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
        ItemRarity heldRarity = held.IsEmpty ? ItemRarity.Common : ItemRarityColors.GetRarity(world.Items.Rules, held.ItemId);
        skin.DrawInset(
            spriteBatch,
            in heldRect,
            CarbonUiPalette.SlotFill,
            heldRarity != ItemRarity.Common ? ItemRarityColors.GetBorder(heldRarity) : CarbonUiPalette.BorderSubtle);

        RectangleF sortRect = InventoryModalLayout.GetSortButtonRect(in layout);
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

        RectangleF dollRect = InventoryModalLayout.GetPaperDollRect(in layout);
        if (art is not null && CharacterLookResolver.TryResolve(art, world.Races.Current, world.LocalPlayerLook, out CompiledVectorRig? heroRig, out CharacterPalette palette) && heroRig is not null)
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
                palette: palette,
                equipment: loadout,
                art: art);
        }

        // Subtle separator bar between Backpack (3x9) and Hotbar (1x9)
        float slotSize = MathF.Round(InventoryModalLayout.BaseSlotSize * s);
        float slotGap = MathF.Round(InventoryModalLayout.BaseSlotGap * s);
        float bagStartY = layout.ContentY + MathF.Round(172f * s) + MathF.Round(12f * s);
        float divY = bagStartY + (3f * slotSize) + (2f * slotGap) + MathF.Round(10f * s);
        batch.FillRectangle(new RectangleF(layout.ContentX, divY, layout.ContentW, MathF.Max(2f, MathF.Round(2f * s))), CarbonUiPalette.BorderSubtle);

        float iconSize = MathF.Round(48f * s);
        float barInset = MathF.Round(7f * s);
        float barH = MathF.Max(4f, MathF.Round(4.5f * s));
        int hoveredSlot = -1;
        for (int idx = 0; idx < PlayerInventoryLayout.TotalCapacity && idx < slots.Length; idx++)
        {
            RectangleF rect = InventoryModalLayout.GetSlotRect(in layout, idx);
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
                byte tierLevel = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                EnhancementSlotDecor.DrawFill(batch, in rect, tierLevel, MathF.Round(3f * s));
                if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? itemArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, itemArt, new Vector2(rect.X + (rect.Width * 0.5f), rect.Y + (rect.Height * 0.46f)), iconSize);
                }

                EnhancementSlotDecor.DrawOverlay(batch, in rect, tierLevel, s, animTime);

                if (world.Items.Rules.Items.TryGet(item.ItemId, out ItemArchetype? def) && def.MaxDurability > 0f)
                {
                    byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                    float effMax = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(def, upg));
                    float frac = Math.Clamp(item.Durability / effMax, 0f, 1f);
                    ColorRgba dCol = DurabilityBarStyle.GetFill(world.Items.Rules.Repair, item.Durability, effMax);
                    float barW = rect.Width - (barInset * 2f);
                    float barY = rect.Bottom - barH - MathF.Round(4f * s);
                    batch.FillRectangle(new RectangleF(rect.X + barInset, barY, barW, barH), new ColorRgba(8, 10, 14, 240));
                    batch.FillRectangle(new RectangleF(rect.X + barInset, barY, MathF.Max(2f, barW * frac), barH), dCol);
                }
            }
        }

        RectangleF heldRect = InventoryModalLayout.GetHeldSlotRect(in layout);
        ItemSlot heldItem = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
        byte heldLevel = ItemEnhancementPolicy.GetUpgradeLevel(in heldItem);
        EnhancementSlotDecor.DrawFill(batch, in heldRect, heldLevel, MathF.Round(3f * s));
        if (!heldItem.IsEmpty && art is not null && art.TryGetItem(heldItem.ItemId, out CompiledItemVectorArt? heldArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, heldArt, new Vector2(heldRect.X + (heldRect.Width * 0.5f), heldRect.Y + (heldRect.Height * 0.5f)), iconSize);
        }

        else if (heldItem.IsEmpty)
        {
            DrawEmptyEquipmentSilhouette(batch, in heldRect, -1);
        }

        EnhancementSlotDecor.DrawOverlay(batch, in heldRect, heldLevel, s, animTime);

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
        PlayerModalTabs.DrawWindowChromeText(spriteBatch, font, in layout, PlayerModalTab.Inventory, hasSkills, GameText.Get("hud.window.inventory"));

        ColorRgba black = CarbonUiPalette.TextOutline;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.DrawStageText(spriteBatch, font, world, in layout);

        for (int idx = 0; idx < PlayerInventoryLayout.TotalCapacity && idx < slots.Length; idx++)
        {
            RectangleF rect = InventoryModalLayout.GetSlotRect(in layout, idx);
            ItemSlot item = slots[idx];

            if (idx >= PlayerInventoryLayout.Accessory1Slot && item.IsEmpty)
            {
                DrawCaption(spriteBatch, font, in rect, GameText.Get("inventory.ring"), layout.TextScale, s);
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
                EnhancementSlotDecor.DrawBadgeText(spriteBatch, font, in rect, ItemEnhancementPolicy.GetUpgradeLevel(in item), layout.TextScale, s);

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
        RectangleF heldRect = InventoryModalLayout.GetHeldSlotRect(in layout);
        ItemSlot heldItem = world.SelectedHotbarSlot < PlayerInventoryLayout.TotalCapacity ? GetSlot(world, world.SelectedHotbarSlot) : ItemSlot.Empty;
        EnhancementSlotDecor.DrawBadgeText(spriteBatch, font, in heldRect, ItemEnhancementPolicy.GetUpgradeLevel(in heldItem), layout.TextScale, s);
        string heldLabel = this.GetHeldLabel(world.SelectedHotbarSlot);
        TextMeasureResult heldSize = font.MeasureString(heldLabel, TextLayoutOptions.Default, layout.TextScale);
        spriteBatch.DrawOutlinedString(
            font,
            heldLabel,
            new Vector2(heldRect.X + MathF.Floor((heldRect.Width - heldSize.Width) * 0.5f), heldRect.Bottom + MathF.Round(5f * s)),
            CarbonUiPalette.TextMuted,
            CarbonUiPalette.TextOutline,
            1,
            layout.TextScale);

        RectangleF sortRect = InventoryModalLayout.GetSortButtonRect(in layout);
        string sortLabel = GameText.Get("inventory.sort");
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

    private static ItemSlot GetSlot(IWorldReadModel world, int slotIndex)
    {
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        return slots[slotIndex];
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
            _dialog.DrawGeometry(batch, world, art, in layout, mouseScreenPos);
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
            _dialog.DrawText(spriteBatch, font, world, in layout);
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
            CarbonUiSkin.TruncateToWidth(font, GameText.Get("inventory.controls_hint"), tip.Width - (padX * 2f), layout.TextScale),
            new Vector2(tip.X + padX, y + MathF.Round(2f * s)),
            CarbonUiPalette.BorderOuter,
            black,
            1,
            layout.TextScale);
    }

    internal static RectangleF GetHeldSlotRectForTesting(in PlayerModalLayout layout) => InventoryModalLayout.GetHeldSlotRect(in layout);

    internal static RectangleF GetSortButtonRectForTesting(in PlayerModalLayout layout) => InventoryModalLayout.GetSortButtonRect(in layout);

    internal static RectangleF GetDialogButtonRectForTesting(in PlayerModalLayout layout, InventoryDialogButton button) =>
        InventoryDialogRenderer.GetButtonRect(in layout, button);


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

    private string GetHeldLabel(byte hotbarSlot)
    {
        int version = GameText.Current.Version;
        if (_heldLabelVersion != version)
        {
            for (int i = 0; i < _heldLabels.Length; i++)
            {
                _heldLabels[i] = GameText.Format("inventory.held", new TextArg("slot", i + 1));
            }

            _heldLabelVersion = version;
        }

        return _heldLabels[Math.Min(hotbarSlot, (byte)(_heldLabels.Length - 1))];
    }

    internal static RectangleF GetSlotRectForTesting(in PlayerModalLayout layout, int slotIndex) =>
        InventoryModalLayout.GetSlotRect(in layout, slotIndex);

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
