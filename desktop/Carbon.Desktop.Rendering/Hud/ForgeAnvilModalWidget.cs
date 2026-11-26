// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Dedicated Blacksmith Forge Anvil enhancement window (<c>[U]</c>) skinned with <see cref="CarbonUiSkin"/>,
/// decoupled cleanly from personal backpack storage and gated by server proximity to a Blacksmith NPC or Forge Anvil (SRP).
/// </summary>
internal sealed class ForgeAnvilModalWidget
{
    private const float PanelW = 684f;
    private const float PanelH = 440f;
    private const int MaxVisibleGearRows = 7;
    private const float RowH = 48f;

    private readonly List<ushort> _eligibleScratch = new(24);
    private int _selectedSlot = -1;
    private int _scrollOffset;

    public void Scroll(int delta) => _scrollOffset = Math.Max(0, _scrollOffset + delta);

    public bool ContainsPoint(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return mousePos.X >= ox && mousePos.X <= ox + PanelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH;
    }

    public bool TryHitTestCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetCloseButtonRect(ox, oy).Contains(mousePos);
    }

    public bool TryHitTestForgeButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetForgeButtonRect(ox, oy).Contains(mousePos);
    }

    public bool TrySelectGearAt(Vector2 mousePos, IClientWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        if (GetScrollUpRect(ox, oy).Contains(mousePos))
        {
            this.Scroll(-1);
            return true;
        }

        if (GetScrollDownRect(ox, oy).Contains(mousePos))
        {
            this.Scroll(1);
            return true;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(slots);
        this.ClampScrollOffset();

        for (int i = 0; i < MaxVisibleGearRows && (_scrollOffset + i) < _eligibleScratch.Count; i++)
        {
            RectangleF row = new(ox + 26f, oy + 76f + (i * (RowH + 4f)), 244f, RowH);
            if (row.Contains(mousePos))
            {
                _selectedSlot = _eligibleScratch[_scrollOffset + i];
                return true;
            }
        }

        return false;
    }

    public int ResolveTargetSlot(IClientWorldReadModel world)
    {
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(slots);

        if (_selectedSlot >= 0 && _eligibleScratch.Contains((ushort)_selectedSlot))
        {
            return _selectedSlot;
        }

        if (_eligibleScratch.Count > 0)
        {
            _selectedSlot = _eligibleScratch[0];
            return _selectedSlot;
        }

        return -1;
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
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(slots);
        this.ClampScrollOffset();

        ColorRgba frameCol = world.ForgeFeedbackTimer > 0f
            ? (world.LastForgeSucceeded ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder)
            : CarbonUiPalette.BorderOuter;
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), frameCol, bodyAlpha: 246);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 16f, oy + 44f, 264f, 380f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 290f, oy + 44f, 378f, 380f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(spriteBatch, in closeBtn, hoverClose ? new ColorRgba(158, 46, 52, 245) : CarbonUiPalette.DangerFill, CarbonUiPalette.DangerBorder);

        if (_eligibleScratch.Count > MaxVisibleGearRows)
        {
            RectangleF upBtn = GetScrollUpRect(ox, oy);
            RectangleF dnBtn = GetScrollDownRect(ox, oy);
            skin.DrawButton(spriteBatch, in upBtn, CarbonUiPalette.CardFill, CarbonUiPalette.BorderSubtle);
            skin.DrawButton(spriteBatch, in dnBtn, CarbonUiPalette.CardFill, CarbonUiPalette.BorderSubtle);
        }

        int activeSlot = this.ResolveTargetSlot(world);
        for (int i = 0; i < MaxVisibleGearRows && (_scrollOffset + i) < _eligibleScratch.Count; i++)
        {
            ushort sIdx = _eligibleScratch[_scrollOffset + i];
            bool sel = sIdx == activeSlot;
            RectangleF row = new(ox + 26f, oy + 76f + (i * (RowH + 4f)), 244f, RowH);
            skin.DrawSlot(
                spriteBatch,
                in row,
                sel ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill,
                sel ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }

        RectangleF anvilBox = new(ox + 310f, oy + 82f, 64f, 64f);
        skin.DrawSlot(spriteBatch, in anvilBox, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderOuter);

        ItemSlot target = activeSlot >= 0 && activeSlot < slots.Length ? slots[activeSlot] : ItemSlot.Empty;
        bool canForge = !target.IsEmpty &&
            ItemEnhancementPolicy.CanUpgrade(in target, out uint m1, out int c1, out uint m2, out int c2, out _) &&
            CountItemInSpan(slots, m1) >= c1 &&
            (m2 == 0U || CountItemInSpan(slots, m2) >= c2) &&
            world.IsNearForgeStation();

        RectangleF forgeBtn = GetForgeButtonRect(ox, oy);
        bool hoverForge = mouseScreenPos.HasValue && forgeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(
            spriteBatch,
            in forgeBtn,
            canForge ? (hoverForge ? new ColorRgba(92, 80, 52, 245) : new ColorRgba(62, 54, 38, 240)) : CarbonUiPalette.SlotFill,
            canForge ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
    }

    public void DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(slots);
        this.ClampScrollOffset();

        for (int i = 0; i < MaxVisibleGearRows && (_scrollOffset + i) < _eligibleScratch.Count; i++)
        {
            ushort sIdx = _eligibleScratch[_scrollOffset + i];
            if (art is not null && art.TryGetItem(slots[sIdx].ItemId, out CompiledItemVectorArt? gearArt))
            {
                VectorRigRenderer.DrawItemIcon(batch, gearArt, new Vector2(ox + 48f, oy + 100f + (i * (RowH + 4f))), 28f);
            }
        }

        int activeSlot = this.ResolveTargetSlot(world);
        ItemSlot target = activeSlot >= 0 && activeSlot < slots.Length ? slots[activeSlot] : ItemSlot.Empty;
        if (!target.IsEmpty && art is not null && art.TryGetItem(target.ItemId, out CompiledItemVectorArt? targetArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, targetArt, new Vector2(ox + 342f, oy + 114f), 40f);
        }

        if (!target.IsEmpty &&
            ItemEnhancementPolicy.CanUpgrade(in target, out uint m1, out int c1, out uint m2, out int c2, out float chance))
        {
            if (art is not null && m1 != 0U && art.TryGetItem(m1, out CompiledItemVectorArt? m1Art))
            {
                VectorRigRenderer.DrawItemIcon(batch, m1Art, new Vector2(ox + 324f, oy + 240f), 20f);
            }

            if (art is not null && m2 != 0U && art.TryGetItem(m2, out CompiledItemVectorArt? m2Art))
            {
                VectorRigRenderer.DrawItemIcon(batch, m2Art, new Vector2(ox + 324f, oy + 266f), 20f);
            }

            RectangleF barBg = new(ox + 310f, oy + 312f, 336f, 12f);
            batch.FillRectangle(barBg, new ColorRgba(12, 18, 34, 240));
            batch.FillRectangle(new RectangleF(barBg.X, barBg.Y, barBg.Width * Math.Clamp(chance, 0f, 1f), barBg.Height), new ColorRgba(16, 185, 129, 240));
            batch.DrawRectangle(barBg, new ColorRgba(100, 116, 139, 220), 1f);
            _ = c1;
            _ = c2;
        }
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        ColorRgba black = CarbonUiPalette.TextOutline;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(slots);

        spriteBatch.DrawOutlinedString(font, "Blacksmith Forge", new Vector2(ox + 18f, oy + 13f), CarbonUiPalette.TextWarm, black, 1, 1f);
        bool stationNear = world.IsNearForgeStation();
        spriteBatch.DrawOutlinedString(
            font,
            stationNear ? "Active" : "Need Forge",
            new Vector2(ox + 536f, oy + 14f),
            stationNear ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
            black,
            1,
            1f);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "X", in closeBtn, CarbonUiPalette.TextPrimary, black, 1f);

        spriteBatch.DrawOutlinedString(font, $"Gear ({_eligibleScratch.Count})", new Vector2(ox + 26f, oy + 52f), CarbonUiPalette.TextMuted, black, 1, 1f);
        if (_eligibleScratch.Count > MaxVisibleGearRows)
        {
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "^", GetScrollUpRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "v", GetScrollDownRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        }

        this.ClampScrollOffset();
        for (int i = 0; i < MaxVisibleGearRows && (_scrollOffset + i) < _eligibleScratch.Count; i++)
        {
            ushort sIdx = _eligibleScratch[_scrollOffset + i];
            ItemSlot item = slots[sIdx];
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
            float ry = oy + 82f + (i * (RowH + 4f));
            string tag = sIdx >= PlayerInventoryLayout.BagLimit ? "Equipped" : (sIdx < 9 ? $"Slot {sIdx + 1}" : "Bag");
            string name = StarterGameCatalog.GetDisplayName(item.ItemId);
            string rowLabel = CarbonUiSkin.TruncateToWidth(font, upg > 0 ? $"{name} +{upg}" : name, 190f, 1f);
            spriteBatch.DrawOutlinedString(font, rowLabel, new Vector2(ox + 72f, ry), CarbonUiPalette.TextPrimary, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, tag, new Vector2(ox + 72f, ry + 20f), CarbonUiPalette.TextMuted, black, 1, 1f);
        }

        int activeSlot = this.ResolveTargetSlot(world);
        ItemSlot target = activeSlot >= 0 && activeSlot < slots.Length ? slots[activeSlot] : ItemSlot.Empty;
        spriteBatch.DrawOutlinedString(font, "Enhance", new Vector2(ox + 310f, oy + 52f), CarbonUiPalette.TextWarm, black, 1, 1f);

        if (target.IsEmpty)
        {
            spriteBatch.DrawOutlinedString(font, "No upgradable gear", new Vector2(ox + 388f, oy + 106f), CarbonUiPalette.TextMuted, black, 1, 1f);
            return;
        }

        byte curLv = ItemEnhancementPolicy.GetUpgradeLevel(in target);
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, $"{StarterGameCatalog.GetDisplayName(target.ItemId)} (+{curLv})", 264f, 1.25f), new Vector2(ox + 386f, oy + 86f), CarbonUiPalette.TextPrimary, black, 1, 1.25f);

        if (ItemEnhancementPolicy.CanUpgrade(in target, out uint m1, out int c1, out uint m2, out int c2, out float chance))
        {
            float multNow = ItemEnhancementPolicy.GetStatMultiplier(curLv);
            float multNext = ItemEnhancementPolicy.GetStatMultiplier((byte)(curLv + 1));
            spriteBatch.DrawOutlinedString(
                font,
                $"+{curLv} -> +{curLv + 1}  ({(int)(multNow * 100)}% -> {(int)(multNext * 100)}%)",
                new Vector2(ox + 386f, oy + 118f),
                CarbonUiPalette.TextMuted,
                black,
                1,
                1f);

            spriteBatch.DrawOutlinedString(font, "Materials", new Vector2(ox + 310f, oy + 204f), CarbonUiPalette.TextWarm, black, 1, 1f);
            if (m1 != 0U)
            {
                int have1 = CountItemInSpan(slots, m1);
                spriteBatch.DrawOutlinedString(
                    font,
                    $"{StarterGameCatalog.GetDisplayName(m1)}: {have1}/{c1}",
                    new Vector2(ox + 344f, oy + 230f),
                    have1 >= c1 ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
                    black,
                    1,
                    1f);
            }

            if (m2 != 0U)
            {
                int have2 = CountItemInSpan(slots, m2);
                spriteBatch.DrawOutlinedString(
                    font,
                    $"{StarterGameCatalog.GetDisplayName(m2)}: {have2}/{c2}",
                    new Vector2(ox + 344f, oy + 256f),
                    have2 >= c2 ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
                    black,
                    1,
                    1f);
            }

            spriteBatch.DrawOutlinedString(font, $"Success: {(int)MathF.Round(chance * 100f)}%", new Vector2(ox + 310f, oy + 290f), CarbonUiPalette.TextPrimary, black, 1, 1f);

            if (world.ForgeFeedbackTimer > 0f)
            {
                spriteBatch.DrawOutlinedString(
                    font,
                    world.LastForgeSucceeded ? "Enhanced!" : "Failed!",
                    new Vector2(ox + 310f, oy + 336f),
                    world.LastForgeSucceeded ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
                    black,
                    1,
                    1f);
            }

            CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"FORGE +{curLv + 1}", GetForgeButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        }
        else
        {
            spriteBatch.DrawOutlinedString(font, "Max enhancement (+10)", new Vector2(ox + 386f, oy + 122f), CarbonUiPalette.TextWarm, black, 1, 1f);
        }
    }

    private void CollectEligibleSlots(ReadOnlySpan<ItemSlot> slots)
    {
        _eligibleScratch.Clear();
        // Order: Equipped Armor (36..39), Hotbar (0..8), Backpack (9..35)
        for (int s = PlayerInventoryLayout.HeadSlot; s <= PlayerInventoryLayout.FeetSlot && s < slots.Length; s++)
        {
            if (IsEligibleGear(in slots[s]))
            {
                _eligibleScratch.Add((ushort)s);
            }
        }

        for (int s = 0; s < PlayerInventoryLayout.BagLimit && s < slots.Length; s++)
        {
            if (IsEligibleGear(in slots[s]))
            {
                _eligibleScratch.Add((ushort)s);
            }
        }
    }

    private static bool IsEligibleGear(in ItemSlot slot) =>
        !slot.IsEmpty &&
        StarterGameCatalog.Items.TryGet(slot.ItemId, out ItemArchetype? def) &&
        def.MaxDurability > 0f;

    private static int CountItemInSpan(ReadOnlySpan<ItemSlot> slots, uint itemId)
    {
        if (itemId == 0U)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].ItemId == itemId)
            {
                total += slots[i].Quantity;
            }
        }

        return total;
    }

    private void ClampScrollOffset()
    {
        int maxScroll = Math.Max(0, _eligibleScratch.Count - MaxVisibleGearRows);
        if (_scrollOffset > maxScroll)
        {
            _scrollOffset = maxScroll;
        }
    }

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    private static RectangleF GetCloseButtonRect(float ox, float oy) => new(ox + PanelW - 34f, oy + 10f, 24f, 24f);

    private static RectangleF GetScrollUpRect(float ox, float oy) => new(ox + 232f, oy + 50f, 18f, 18f);

    private static RectangleF GetScrollDownRect(float ox, float oy) => new(ox + 254f, oy + 50f, 18f, 18f);

    private static RectangleF GetForgeButtonRect(float ox, float oy) => new(ox + 310f, oy + 368f, 336f, 40f);
}
