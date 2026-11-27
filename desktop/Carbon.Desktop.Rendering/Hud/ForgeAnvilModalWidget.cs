// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Enhancement;

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
    private const int MaxMaterialRows = 3;
    private const float MaterialRowY = 172f;
    private const float MaterialRowH = 24f;
    private const float OutcomeBarY = 268f;

    private static readonly ColorRgba s_successColor = new(16, 185, 129, 240);
    private static readonly ColorRgba s_noChangeColor = new(148, 163, 184, 240);
    private static readonly ColorRgba s_downgradeColor = new(249, 115, 22, 240);
    private static readonly ColorRgba s_destroyColor = new(225, 29, 72, 240);

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

    public bool TrySelectGearAt(Vector2 mousePos, IWorldReadModel world, int viewportWidth, int viewportHeight)
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
        this.CollectEligibleSlots(world.Items.Rules, slots);
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

    public int ResolveTargetSlot(IWorldReadModel world)
    {
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(world.Items.Rules, slots);

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
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(world.Items.Rules, slots);
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
        bool canForge = ItemEnhancementPolicy.TryGetStep(world.Items.Rules, in target, out EnhancementStep? step) &&
            HasMaterials(slots, step) &&
            world.LocalPlayerGold >= ItemEnhancementPolicy.GetGoldFee(world.Items.Rules, in target, step) &&
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
        IWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(world.Items.Rules, slots);
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

        if (ItemEnhancementPolicy.TryGetStep(world.Items.Rules, in target, out EnhancementStep? step))
        {
            ReadOnlySpan<RecipeIngredient> materials = step.MaterialSpan;
            for (int i = 0; i < materials.Length && i < MaxMaterialRows; i++)
            {
                if (art is not null && art.TryGetItem(materials[i].ItemId, out CompiledItemVectorArt? matArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, matArt, new Vector2(ox + 324f, oy + MaterialRowY + 8f + (i * MaterialRowH)), 18f);
                }
            }

            DrawOutcomeBar(batch, new RectangleF(ox + 310f, oy + OutcomeBarY, 336f, 12f), step);
        }
    }

    /// <summary>Draws the four outcome chances of <paramref name="step"/> as one stacked bar (success, no change, downgrade, destroy).</summary>
    private static void DrawOutcomeBar(GeometryBatch batch, RectangleF bar, EnhancementStep step)
    {
        batch.FillRectangle(bar, new ColorRgba(12, 18, 34, 240));
        float x = bar.X;
        x = FillSegment(batch, bar, x, step.SuccessPermille, s_successColor);
        x = FillSegment(batch, bar, x, step.NoChangePermille, s_noChangeColor);
        x = FillSegment(batch, bar, x, step.DowngradePermille, s_downgradeColor);
        _ = FillSegment(batch, bar, x, step.DestroyPermille, s_destroyColor);
        batch.DrawRectangle(bar, new ColorRgba(100, 116, 139, 220), 1f);
    }

    private static float FillSegment(GeometryBatch batch, RectangleF bar, float x, int permille, ColorRgba color)
    {
        float w = bar.Width * permille / EnhancementTable.WeightTotal;
        if (w > 0f)
        {
            batch.FillRectangle(new RectangleF(x, bar.Y, w, bar.Height), color);
        }

        return x + w;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        ColorRgba black = CarbonUiPalette.TextOutline;
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        this.CollectEligibleSlots(world.Items.Rules, slots);

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
            string name = world.Items.GetDisplayName(item.ItemId);
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
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, $"{world.Items.GetDisplayName(target.ItemId)} (+{curLv})", 264f, 1.25f), new Vector2(ox + 386f, oy + 86f), CarbonUiPalette.TextPrimary, black, 1, 1.25f);

        if (ItemEnhancementPolicy.TryGetStep(world.Items.Rules, in target, out EnhancementStep? step))
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

            spriteBatch.DrawOutlinedString(font, "Materials", new Vector2(ox + 310f, oy + MaterialRowY - 22f), CarbonUiPalette.TextWarm, black, 1, 1f);
            ReadOnlySpan<RecipeIngredient> materials = step.MaterialSpan;
            for (int i = 0; i < materials.Length && i < MaxMaterialRows; i++)
            {
                int have = CountItemInSpan(slots, materials[i].ItemId);
                spriteBatch.DrawOutlinedString(
                    font,
                    $"{world.Items.GetDisplayName(materials[i].ItemId)}: {have}/{materials[i].Quantity}",
                    new Vector2(ox + 344f, oy + MaterialRowY + (i * MaterialRowH)),
                    have >= materials[i].Quantity ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
                    black,
                    1,
                    1f);
            }

            long fee = ItemEnhancementPolicy.GetGoldFee(world.Items.Rules, in target, step);
            spriteBatch.DrawOutlinedString(
                font,
                $"Fee: {fee} gold",
                new Vector2(ox + 310f, oy + OutcomeBarY - 24f),
                world.LocalPlayerGold >= fee ? CarbonUiPalette.TextWarm : CarbonUiPalette.DangerBorder,
                black,
                1,
                1f);

            DrawChance(spriteBatch, font, "Success", step.SuccessPermille, new Vector2(ox + 310f, oy + OutcomeBarY + 18f), s_successColor);
            DrawChance(spriteBatch, font, "No change", step.NoChangePermille, new Vector2(ox + 480f, oy + OutcomeBarY + 18f), s_noChangeColor);
            DrawChance(spriteBatch, font, "Drop -1", step.DowngradePermille, new Vector2(ox + 310f, oy + OutcomeBarY + 38f), s_downgradeColor);
            DrawChance(spriteBatch, font, "Destroy", step.DestroyPermille, new Vector2(ox + 480f, oy + OutcomeBarY + 38f), s_destroyColor);

            if (world.ForgeFeedbackTimer > 0f)
            {
                spriteBatch.DrawOutlinedString(
                    font,
                    DescribeOutcome(world.LastForgeOutcome, world.LastForgeNewLevel),
                    new Vector2(ox + 310f, oy + 344f),
                    world.LastForgeSucceeded ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
                    black,
                    1,
                    1f);
            }

            CarbonUiSkin.DrawCenteredText(spriteBatch, font, $"FORGE +{curLv + 1}", GetForgeButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        }
        else
        {
            spriteBatch.DrawOutlinedString(font, $"Max enhancement (+{world.Items.Rules.Enhancement.MaxLevel})", new Vector2(ox + 386f, oy + 122f), CarbonUiPalette.TextWarm, black, 1, 1f);
        }
    }

    private static void DrawChance(SpriteBatch spriteBatch, BitmapFont font, string label, int permille, Vector2 pos, ColorRgba color) =>
        spriteBatch.DrawOutlinedString(
            font,
            $"{label} {permille / 10}.{permille % 10}%",
            pos,
            permille > 0 ? color : CarbonUiPalette.TextMuted,
            CarbonUiPalette.TextOutline,
            1,
            1f);

    private static string DescribeOutcome(EnhancementOutcome outcome, byte newLevel) => outcome switch
    {
        EnhancementOutcome.Success => $"Enhanced to +{newLevel}!",
        EnhancementOutcome.NoChange => "Failed - no change.",
        EnhancementOutcome.Downgrade => $"Failed - dropped to +{newLevel}.",
        EnhancementOutcome.Destroyed => "The item shattered!",
        EnhancementOutcome.None => string.Empty,
        _ => string.Empty,
    };

    private static bool HasMaterials(ReadOnlySpan<ItemSlot> slots, EnhancementStep step)
    {
        ReadOnlySpan<RecipeIngredient> materials = step.MaterialSpan;
        for (int i = 0; i < materials.Length; i++)
        {
            if (CountItemInSpan(slots, materials[i].ItemId) < materials[i].Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private void CollectEligibleSlots(ItemRules rules, ReadOnlySpan<ItemSlot> slots)
    {
        _eligibleScratch.Clear();
        // Order: Equipped Armor (36..39), Hotbar (0..8), Backpack (9..35)
        for (int s = PlayerInventoryLayout.HeadSlot; s <= PlayerInventoryLayout.FeetSlot && s < slots.Length; s++)
        {
            if (IsEligibleGear(rules, in slots[s]))
            {
                _eligibleScratch.Add((ushort)s);
            }
        }

        for (int s = 0; s < PlayerInventoryLayout.BagLimit && s < slots.Length; s++)
        {
            if (IsEligibleGear(rules, in slots[s]))
            {
                _eligibleScratch.Add((ushort)s);
            }
        }
    }

    private static bool IsEligibleGear(ItemRules rules, in ItemSlot slot) =>
        !slot.IsEmpty && ItemEnhancementPolicy.IsEnhanceable(rules, slot.ItemId);

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
