// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Dedicated Blacksmith Forge Anvil enhancement window (<c>[U]</c>) skinned with <see cref="CarbonUiSkin"/>,
/// decoupled cleanly from personal backpack storage and gated by server proximity to a Blacksmith NPC or Forge Anvil (SRP).
/// </summary>
internal sealed class ForgeAnvilModalWidget
{
    private const float PanelW = 684f;
    private const float PanelH = 480f;
    private const int MaxVisibleGearRows = 7;
    private const float RowH = 48f;
    private const int MaxMaterialRows = 3;
    private const float MaterialRowY = 216f;
    private const float MaterialRowH = 22f;
    private const float OutcomeBarY = 302f;
    private const string AnvilItemCode = "forge_anvil";

    private static readonly ColorRgba s_successColor = new(16, 185, 129, 240);
    private static readonly ColorRgba s_noChangeColor = new(148, 163, 184, 240);
    private static readonly ColorRgba s_downgradeColor = new(249, 115, 22, 240);
    private static readonly ColorRgba s_destroyColor = new(225, 29, 72, 240);

    private readonly List<ushort> _eligibleScratch = new(24);
    private int _selectedSlot = -1;
    private int _scrollOffset;
    private int _protectionChoice = -1;
    private uint _lastTargetItemId;

    /// <summary>Gets the hammer-on-anvil animation shown in the enhance stage (ADR-024).</summary>
    public ForgeEnhancementVfx Vfx { get; } = new();

    /// <summary>Advances the forge animation and picks up new server results.</summary>
    public void Update(float dt, IWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);
        this.Vfx.Update(dt, world.ForgeResultSerial, world.LastForgeOutcome);
    }

    /// <summary>
    /// Returns the protection scroll the player picked for the selected item, or <c>0</c> when none is picked, none is
    /// owned, or it cannot apply at this level (the server re-checks all of it).
    /// </summary>
    public ushort ResolveProtection(IWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        int target = this.ResolveTargetSlot(world);
        ItemSlot item = target >= 0 && target < slots.Length ? slots[target] : ItemSlot.Empty;
        EnhancementTable table = world.Items.Rules.Enhancement;
        if (_protectionChoice < 0 || _protectionChoice >= table.Protections.Length ||
            !ItemEnhancementPolicy.TryGetStep(world.Items.Rules, in item, out EnhancementStep? step))
        {
            return 0;
        }

        EnhancementProtection scroll = table.Protections[_protectionChoice];
        return scroll.AppliesTo(step) && CountItemInSpan(slots, scroll.ItemId) > 0 ? (ushort)scroll.ItemId : (ushort)0;
    }

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

        int protections = world.Items.Rules.Enhancement.Protections.Length;
        for (int i = -1; i < protections; i++)
        {
            if (GetProtectionRect(ox, oy, i, protections).Contains(mousePos))
            {
                _protectionChoice = i;
                return true;
            }
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
            if (this.Vfx.IsPreview)
            {
                // Screenshot previews frame the most enhanced item, where every outcome is possible.
                for (int i = 1; i < _eligibleScratch.Count; i++)
                {
                    if (slots[_eligibleScratch[i]].UpgradeLevel > slots[_selectedSlot].UpgradeLevel)
                    {
                        _selectedSlot = _eligibleScratch[i];
                    }
                }
            }

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
        skin.DrawCard(spriteBatch, new RectangleF(ox + 16f, oy + 44f, 264f, 420f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 290f, oy + 44f, 378f, 420f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

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

        RectangleF anvilBox = GetStageRect(ox, oy);
        skin.DrawSlot(spriteBatch, in anvilBox, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderOuter);

        ItemSlot target = activeSlot >= 0 && activeSlot < slots.Length ? slots[activeSlot] : ItemSlot.Empty;
        _ = ItemEnhancementPolicy.TryGetStep(world.Items.Rules, in target, out EnhancementStep? step);
        bool canForge = !this.Vfx.IsActive &&
            step is not null &&
            HasMaterials(slots, step) &&
            world.LocalPlayerGold >= ItemEnhancementPolicy.GetGoldFee(world.Items.Rules, in target, step) &&
            world.IsNearForgeStation();

        if (step is not null && step.IsRisky)
        {
            EnhancementTable table = world.Items.Rules.Enhancement;
            for (int i = -1; i < table.Protections.Length; i++)
            {
                RectangleF btn = GetProtectionRect(ox, oy, i, table.Protections.Length);
                bool usable = i < 0 || (table.Protections[i].AppliesTo(step) && CountItemInSpan(slots, table.Protections[i].ItemId) > 0);
                bool picked = i == _protectionChoice || (i < 0 && _protectionChoice < 0);
                skin.DrawButton(
                    spriteBatch,
                    in btn,
                    picked ? new ColorRgba(44, 62, 92, 245) : CarbonUiPalette.SlotFill,
                    picked && usable ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
            }
        }

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
        RectangleF stage = GetStageRect(ox, oy);
        Vector2 itemCenter = new(stage.X + (stage.Width * 0.5f), stage.Y + 40f);
        if (!this.Vfx.IsActive && !target.IsEmpty)
        {
            _lastTargetItemId = target.ItemId;
        }

        uint shownItem = this.Vfx.IsActive && _lastTargetItemId != 0U ? _lastTargetItemId : target.ItemId;
        CompiledItemVectorArt? targetArt = null;
        _ = shownItem != 0U && art is not null && art.TryGetItem(shownItem, out targetArt);
        if (art is not null &&
            world.Items.Rules.ItemCatalog.TryGetByCode(AnvilItemCode, out ItemCatalogEntry? anvilItem) &&
            art.TryGetItem(anvilItem.Id, out CompiledItemVectorArt? anvilArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, anvilArt, new Vector2(itemCenter.X, stage.Bottom - 24f), 64f);
        }

        this.Vfx.DrawBehind(batch, itemCenter, stage);
        if (targetArt is not null && !this.Vfx.HidesItem)
        {
            VectorRigRenderer.DrawItemIcon(batch, targetArt, itemCenter + this.Vfx.GetItemOffset(), 40f);
        }

        this.Vfx.DrawFront(batch, itemCenter, targetArt?.AccentColor ?? new ColorRgba(148, 163, 184, 255));

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

            if (step.IsRisky && art is not null)
            {
                EnhancementTable table = world.Items.Rules.Enhancement;
                for (int i = 0; i < table.Protections.Length; i++)
                {
                    RectangleF btn = GetProtectionRect(ox, oy, i, table.Protections.Length);
                    if (art.TryGetItem(table.Protections[i].ItemId, out CompiledItemVectorArt? scrollArt))
                    {
                        VectorRigRenderer.DrawItemIcon(batch, scrollArt, new Vector2(btn.X + (btn.Width * 0.5f) - 18f, btn.Y + (btn.Height * 0.5f)), 18f);
                    }
                }
            }
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

        spriteBatch.DrawOutlinedString(font, GameText.Get("forge.title"), new Vector2(ox + 18f, oy + 13f), CarbonUiPalette.TextWarm, black, 1, 1f);
        bool stationNear = world.IsNearForgeStation();
        spriteBatch.DrawOutlinedString(
            font,
            GameText.Get(stationNear ? "forge.station_active" : "forge.station_missing"),
            new Vector2(ox + 536f, oy + 14f),
            stationNear ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
            black,
            1,
            1f);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "X", in closeBtn, CarbonUiPalette.TextPrimary, black, 1f);

        spriteBatch.DrawOutlinedString(font, GameText.Format("forge.gear_count", new TextArg("count", _eligibleScratch.Count)), new Vector2(ox + 26f, oy + 52f), CarbonUiPalette.TextMuted, black, 1, 1f);
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
            string tag = sIdx >= PlayerInventoryLayout.BagLimit ? GameText.Get("forge.tag.equipped") : (sIdx < 9 ? GameText.Format("forge.tag.slot", new TextArg("slot", sIdx + 1)) : GameText.Get("forge.tag.bag"));
            string name = world.Items.GetDisplayName(item.ItemId);
            string rowLabel = CarbonUiSkin.TruncateToWidth(font, upg > 0 ? $"{name} +{upg}" : name, 190f, 1f);
            spriteBatch.DrawOutlinedString(font, rowLabel, new Vector2(ox + 72f, ry), CarbonUiPalette.TextPrimary, black, 1, 1f);
            if (upg > 0)
            {
                spriteBatch.DrawOutlinedString(font, EnhancementTierStyles.Badge(upg), new Vector2(ox + 50f, ry + 18f), EnhancementTierStyles.Get(upg).Accent, black, 1, 1f);
            }
            spriteBatch.DrawOutlinedString(font, tag, new Vector2(ox + 72f, ry + 20f), CarbonUiPalette.TextMuted, black, 1, 1f);
        }

        int activeSlot = this.ResolveTargetSlot(world);
        ItemSlot target = activeSlot >= 0 && activeSlot < slots.Length ? slots[activeSlot] : ItemSlot.Empty;
        spriteBatch.DrawOutlinedString(font, GameText.Get("forge.enhance"), new Vector2(ox + 310f, oy + 52f), CarbonUiPalette.TextWarm, black, 1, 1f);
        EnhancementOutcome shown = this.Vfx.PlayingOutcome != EnhancementOutcome.None
            ? this.Vfx.PlayingOutcome
            : (world.ForgeFeedbackTimer > 0f && !this.Vfx.IsActive ? world.LastForgeOutcome : EnhancementOutcome.None);
        if (shown != EnhancementOutcome.None)
        {
            byte shownLevel = world.LastForgeNewLevel;
            if (this.Vfx.IsPreview)
            {
                // A preview has no server result: label it with the level the selected item would reach.
                int targetSlot = this.ResolveTargetSlot(world);
                byte level = targetSlot >= 0 ? ItemEnhancementPolicy.GetUpgradeLevel(in slots[targetSlot]) : (byte)0;
                shownLevel = shown switch
                {
                    EnhancementOutcome.Success => (byte)(level + 1),
                    EnhancementOutcome.Downgrade => (byte)Math.Max(0, level - 1),
                    EnhancementOutcome.NoChange or EnhancementOutcome.Destroyed or EnhancementOutcome.None => level,
                    _ => level,
                };
            }

            spriteBatch.DrawOutlinedString(
                font,
                DescribeOutcome(shown, shownLevel),
                new Vector2(ox + 440f, oy + 52f),
                OutcomeTextColor(shown),
                black,
                1,
                1f);
        }

        if (target.IsEmpty)
        {
            spriteBatch.DrawOutlinedString(font, GameText.Get("forge.no_gear"), new Vector2(ox + 440f, oy + 106f), CarbonUiPalette.TextMuted, black, 1, 1f);
            return;
        }

        byte curLv = ItemEnhancementPolicy.GetUpgradeLevel(in target);
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, $"{world.Items.GetDisplayName(target.ItemId)} (+{curLv})", 210f, 1.25f), new Vector2(ox + 440f, oy + 86f), CarbonUiPalette.TextPrimary, black, 1, 1.25f);

        if (ItemEnhancementPolicy.TryGetStep(world.Items.Rules, in target, out EnhancementStep? step))
        {
            float multNow = ItemEnhancementPolicy.GetStatMultiplier(curLv);
            float multNext = ItemEnhancementPolicy.GetStatMultiplier((byte)(curLv + 1));
            spriteBatch.DrawOutlinedString(
                font,
                $"+{curLv} -> +{curLv + 1}  ({(int)(multNow * 100)}% -> {(int)(multNext * 100)}%)",
                new Vector2(ox + 440f, oy + 116f),
                CarbonUiPalette.TextMuted,
                black,
                1,
                1f);

            string risk = GameText.Get(step.DestroyPermille > 0 ? "forge.risk.break"
                : step.DowngradePermille > 0 ? "forge.risk.drop"
                : "forge.risk.safe");
            spriteBatch.DrawOutlinedString(
                font,
                CarbonUiSkin.TruncateToWidth(font, risk, 216f, 1f),
                new Vector2(ox + 440f, oy + 140f),
                step.DestroyPermille > 0 ? s_destroyColor : (step.DowngradePermille > 0 ? s_downgradeColor : s_successColor),
                black,
                1,
                1f);

            spriteBatch.DrawOutlinedString(font, GameText.Get("forge.materials"), new Vector2(ox + 310f, oy + MaterialRowY - 22f), CarbonUiPalette.TextWarm, black, 1, 1f);
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
                GameText.Format("forge.fee", new TextArg("gold", fee)),
                new Vector2(ox + 310f, oy + OutcomeBarY - 24f),
                world.LocalPlayerGold >= fee ? CarbonUiPalette.TextWarm : CarbonUiPalette.DangerBorder,
                black,
                1,
                1f);

            DrawChance(spriteBatch, font, GameText.Get("forge.chance.success"), step.SuccessPermille, new Vector2(ox + 310f, oy + OutcomeBarY + 18f), s_successColor);
            DrawChance(spriteBatch, font, GameText.Get("forge.chance.no_change"), step.NoChangePermille, new Vector2(ox + 480f, oy + OutcomeBarY + 18f), s_noChangeColor);
            DrawChance(spriteBatch, font, GameText.Get("forge.chance.downgrade"), step.DowngradePermille, new Vector2(ox + 310f, oy + OutcomeBarY + 38f), s_downgradeColor);
            DrawChance(spriteBatch, font, GameText.Get("forge.chance.destroy"), step.DestroyPermille, new Vector2(ox + 480f, oy + OutcomeBarY + 38f), s_destroyColor);

            if (step.IsRisky)
            {
                EnhancementTable table = world.Items.Rules.Enhancement;
                for (int i = -1; i < table.Protections.Length; i++)
                {
                    RectangleF btn = GetProtectionRect(ox, oy, i, table.Protections.Length);
                    string label = i < 0
                        ? GameText.Get("forge.scroll.none")
                        : GameText.Format("forge.scroll.choice", new TextArg("count", CountItemInSpan(slots, table.Protections[i].ItemId)));
                    bool usable = i < 0 || (table.Protections[i].AppliesTo(step) && CountItemInSpan(slots, table.Protections[i].ItemId) > 0);
                    RectangleF textRect = i < 0 ? btn : new RectangleF(btn.X + 16f, btn.Y, btn.Width - 16f, btn.Height);
                    CarbonUiSkin.DrawCenteredText(
                        spriteBatch,
                        font,
                        CarbonUiSkin.TruncateToWidth(font, label, textRect.Width - 8f, 1f),
                        in textRect,
                        usable ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted,
                        black,
                        1f);
                }
            }

            string action = this.Vfx.IsActive && this.Vfx.PlayingOutcome == EnhancementOutcome.None ? GameText.Get("forge.forging") : GameText.Format("forge.button", new TextArg("level", curLv + 1));
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, action, GetForgeButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        }
        else
        {
            spriteBatch.DrawOutlinedString(font, GameText.Format("forge.max_level", new TextArg("level", world.Items.Rules.Enhancement.MaxLevel)), new Vector2(ox + 440f, oy + 116f), CarbonUiPalette.TextWarm, black, 1, 1f);
        }
    }

    private static ColorRgba OutcomeTextColor(EnhancementOutcome outcome) => outcome switch
    {
        EnhancementOutcome.Success => s_successColor,
        EnhancementOutcome.Downgrade => s_downgradeColor,
        EnhancementOutcome.Destroyed => s_destroyColor,
        EnhancementOutcome.NoChange or EnhancementOutcome.None => CarbonUiPalette.TextMuted,
        _ => CarbonUiPalette.TextMuted,
    };

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
        EnhancementOutcome.Success => GameText.Format("forge.outcome.success", new TextArg("level", newLevel)),
        EnhancementOutcome.NoChange => GameText.Get("forge.outcome.no_change"),
        EnhancementOutcome.Downgrade => GameText.Format("forge.outcome.downgrade", new TextArg("level", newLevel)),
        EnhancementOutcome.Destroyed => GameText.Get("forge.outcome.destroyed"),
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

    private static RectangleF GetStageRect(float ox, float oy) => new(ox + 306f, oy + 74f, 120f, 112f);

    /// <summary>Protection picker button <paramref name="index"/> (<c>-1</c> = no scroll) of <paramref name="count"/> scrolls.</summary>
    private static RectangleF GetProtectionRect(float ox, float oy, int index, int count)
    {
        float w = (336f - (count * 6f)) / (count + 1);
        return new RectangleF(ox + 310f + ((index + 1) * (w + 6f)), oy + 362f, w, 26f);
    }

    private static RectangleF GetForgeButtonRect(float ox, float oy) => new(ox + 310f, oy + 404f, 336f, 40f);
}
