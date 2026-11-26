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
            ? (world.LastForgeSucceeded ? new ColorRgba(34, 197, 94, 255) : new ColorRgba(239, 68, 68, 255))
            : new ColorRgba(249, 115, 22, 235);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), frameCol, bodyAlpha: 244);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 16f, oy + 44f, 264f, 380f), new ColorRgba(71, 85, 105, 225), bodyAlpha: 232);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 290f, oy + 44f, 378f, 380f), new ColorRgba(249, 115, 22, 215), bodyAlpha: 232);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(spriteBatch, in closeBtn, hoverClose ? new ColorRgba(220, 38, 38, 245) : new ColorRgba(153, 27, 27, 230), new ColorRgba(248, 113, 113, 240));

        if (_eligibleScratch.Count > MaxVisibleGearRows)
        {
            RectangleF upBtn = GetScrollUpRect(ox, oy);
            RectangleF dnBtn = GetScrollDownRect(ox, oy);
            skin.DrawButton(spriteBatch, in upBtn, new ColorRgba(20, 30, 52, 230), new ColorRgba(100, 116, 139, 220));
            skin.DrawButton(spriteBatch, in dnBtn, new ColorRgba(20, 30, 52, 230), new ColorRgba(100, 116, 139, 220));
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
                sel ? new ColorRgba(44, 34, 22, 245) : new ColorRgba(12, 18, 34, 228),
                sel ? new ColorRgba(250, 204, 21, 255) : new ColorRgba(71, 85, 105, 210));
        }

        RectangleF anvilBox = new(ox + 310f, oy + 82f, 64f, 64f);
        skin.DrawSlot(spriteBatch, in anvilBox, new ColorRgba(14, 20, 36, 248), new ColorRgba(250, 204, 21, 240));

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
            canForge ? (hoverForge ? new ColorRgba(249, 115, 22, 245) : new ColorRgba(194, 65, 12, 240)) : new ColorRgba(24, 34, 52, 215),
            canForge ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(71, 85, 105, 210));
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
        ColorRgba black = new(8, 10, 16, 255);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(slots);

        spriteBatch.DrawOutlinedString(font, "BLACKSMITH FORGE ANVIL [U]", new Vector2(ox + 18f, oy + 14f), new ColorRgba(251, 146, 60, 255), black, 1, 1.5f);
        bool stationNear = world.IsNearForgeStation();
        spriteBatch.DrawOutlinedString(
            font,
            stationNear ? "STATION ACTIVE" : "NEED ANVIL / SMITH",
            new Vector2(ox + 492f, oy + 15f),
            stationNear ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 113, 113, 255),
            black,
            1,
            1f);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "X", new Vector2(closeBtn.X + 8f, closeBtn.Y + 4f), ColorRgba.White, black, 1, 1.5f);

        spriteBatch.DrawOutlinedString(font, $"UPGRADABLE GEAR ({_eligibleScratch.Count})", new Vector2(ox + 26f, oy + 54f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
        if (_eligibleScratch.Count > MaxVisibleGearRows)
        {
            RectangleF upBtn = GetScrollUpRect(ox, oy);
            RectangleF dnBtn = GetScrollDownRect(ox, oy);
            spriteBatch.DrawOutlinedString(font, "^", new Vector2(upBtn.X + 5f, upBtn.Y + 3f), ColorRgba.White, black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, "v", new Vector2(dnBtn.X + 5f, dnBtn.Y + 3f), ColorRgba.White, black, 1, 1f);
        }

        this.ClampScrollOffset();
        for (int i = 0; i < MaxVisibleGearRows && (_scrollOffset + i) < _eligibleScratch.Count; i++)
        {
            ushort sIdx = _eligibleScratch[_scrollOffset + i];
            ItemSlot item = slots[sIdx];
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
            float ry = oy + 84f + (i * (RowH + 4f));
            string tag = sIdx >= PlayerInventoryLayout.BagLimit ? "EQUIPPED" : (sIdx < 9 ? $"HOTBAR {sIdx + 1}" : "BACKPACK");
            string name = StarterGameCatalog.GetDisplayName(item.ItemId);
            string rowLabel = CarbonUiSkin.TruncateToWidth(font, upg > 0 ? $"{name} +{upg}" : name, 190f, 1.5f);
            spriteBatch.DrawOutlinedString(font, rowLabel, new Vector2(ox + 72f, ry), ColorRgba.White, black, 1, 1.5f);
            spriteBatch.DrawOutlinedString(font, tag, new Vector2(ox + 72f, ry + 20f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
        }

        int activeSlot = this.ResolveTargetSlot(world);
        ItemSlot target = activeSlot >= 0 && activeSlot < slots.Length ? slots[activeSlot] : ItemSlot.Empty;
        spriteBatch.DrawOutlinedString(font, "ANVIL ENHANCEMENT", new Vector2(ox + 310f, oy + 54f), new ColorRgba(250, 204, 21, 255), black, 1, 1f);

        if (target.IsEmpty)
        {
            spriteBatch.DrawOutlinedString(font, "No weapons, tools, or armor in bag", new Vector2(ox + 388f, oy + 106f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
            return;
        }

        byte curLv = ItemEnhancementPolicy.GetUpgradeLevel(in target);
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, $"{StarterGameCatalog.GetDisplayName(target.ItemId)} (+{curLv})", 264f, 2f), new Vector2(ox + 386f, oy + 88f), ColorRgba.White, black, 1, 2f);

        if (ItemEnhancementPolicy.CanUpgrade(in target, out uint m1, out int c1, out uint m2, out int c2, out float chance))
        {
            float multNow = ItemEnhancementPolicy.GetStatMultiplier(curLv);
            float multNext = ItemEnhancementPolicy.GetStatMultiplier((byte)(curLv + 1));
            spriteBatch.DrawOutlinedString(
                font,
                $"Enhancement: +{curLv} -> +{curLv + 1}  (Stats {(int)(multNow * 100)}% -> {(int)(multNext * 100)}%)",
                new Vector2(ox + 386f, oy + 118f),
                new ColorRgba(125, 211, 252, 255),
                black,
                1,
                1f);

            spriteBatch.DrawOutlinedString(font, "MATERIALS REQUIRED", new Vector2(ox + 310f, oy + 206f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
            if (m1 != 0U)
            {
                int have1 = CountItemInSpan(slots, m1);
                spriteBatch.DrawOutlinedString(
                    font,
                    $"{StarterGameCatalog.GetDisplayName(m1)}: {have1}/{c1}",
                    new Vector2(ox + 344f, oy + 232f),
                    have1 >= c1 ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 113, 113, 255),
                    black,
                    1,
                    1.5f);
            }

            if (m2 != 0U)
            {
                int have2 = CountItemInSpan(slots, m2);
                spriteBatch.DrawOutlinedString(
                    font,
                    $"{StarterGameCatalog.GetDisplayName(m2)}: {have2}/{c2}",
                    new Vector2(ox + 344f, oy + 258f),
                    have2 >= c2 ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 113, 113, 255),
                    black,
                    1,
                    1.5f);
            }

            spriteBatch.DrawOutlinedString(font, $"SUCCESS PROBABILITY: {(int)MathF.Round(chance * 100f)}%", new Vector2(ox + 310f, oy + 292f), ColorRgba.White, black, 1, 1f);

            if (world.ForgeFeedbackTimer > 0f)
            {
                spriteBatch.DrawOutlinedString(
                    font,
                    world.LastForgeSucceeded ? "FORGE SUCCEEDED! ITEM ENHANCED" : "FORGE FAILED! MATERIALS CONSUMED",
                    new Vector2(ox + 310f, oy + 336f),
                    world.LastForgeSucceeded ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 113, 113, 255),
                    black,
                    1,
                    1.5f);
            }

            RectangleF btn = GetForgeButtonRect(ox, oy);
            spriteBatch.DrawOutlinedString(font, $"FORGE TO +{curLv + 1}", new Vector2(btn.X + 108f, btn.Y + 13f), ColorRgba.White, black, 1, 1.5f);
        }
        else
        {
            spriteBatch.DrawOutlinedString(font, "MAXIMUM ENHANCEMENT LEVEL (+10) REACHED", new Vector2(ox + 386f, oy + 122f), new ColorRgba(34, 211, 238, 255), black, 1, 1f);
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
