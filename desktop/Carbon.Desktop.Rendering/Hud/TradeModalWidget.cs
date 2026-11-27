// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
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
/// Trader window (ADR-025): the trader's stock on the left (icon, name, price, units left; click buys), the player's bag on
/// the right (icon, quantity, what the trader pays; click sells one), the wallet and the restock countdown. Everything
/// shown comes from the server's <c>TraderStock</c>; the window only sends requests. Nine-slice skinned (ADR-008),
/// three passes like the other HUD windows.
/// <para>
/// A blacksmith (<see cref="TraderView.CanRepair"/>) adds two footer buttons. <b>Repair</b> switches the bag to repair
/// mode: worn gear shows its durability and the server's repair quote, and a click repairs it. <b>Enhance</b> hands off
/// to the forge enhancement window (ADR-024); this window never enhances itself.
/// </para>
/// </summary>
internal sealed class TradeModalWidget
{
    /// <summary>Most stock rows shown.</summary>
    public const int MaxStockRows = 9;

    private const float PanelW = 760f;
    private const float PanelH = 486f;
    private const float RowH = 40f;
    private const float RowGap = 3f;
    private const float Cell = 50f;
    private const int BagColumns = 6;
    private const float ButtonW = 112f;
    private const float ButtonH = 28f;

    private static readonly ColorRgba s_gold = new(251, 191, 36, 255);
    private static readonly ColorRgba s_soldOut = new(148, 64, 64, 255);
    private static readonly ColorRgba s_repair = new(251, 146, 60, 255);

    /// <summary>Gets or sets whether bag clicks repair instead of sell (blacksmith only).</summary>
    public bool RepairMode { get; set; }

    /// <summary>Gets whether a point is inside the window.</summary>
    public bool ContainsPoint(Vector2 mouse, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        return new RectangleF(ox, oy, PanelW, PanelH).Contains(mouse);
    }

    /// <summary>Gets whether the close button was hit.</summary>
    public bool TryHitTestCloseButton(Vector2 mouse, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        return CloseRect(ox, oy).Contains(mouse);
    }

    /// <summary>Gets whether the blacksmith's Repair toggle was hit.</summary>
    public static bool HitRepairButton(Vector2 mouse, TraderView trader, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(trader);
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        return trader.CanRepair && RepairButtonRect(ox, oy).Contains(mouse);
    }

    /// <summary>Gets whether the blacksmith's Enhance button (hand-off to the forge window) was hit.</summary>
    public static bool HitEnhanceButton(Vector2 mouse, TraderView trader, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(trader);
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        return trader.CanRepair && EnhanceButtonRect(ox, oy).Contains(mouse);
    }

    /// <summary>Gets the stock slot under the mouse, or -1.</summary>
    public int HitStockRow(Vector2 mouse, TraderView trader, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(trader);
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        for (int i = 0; i < Math.Min(MaxStockRows, trader.ItemIds.Length); i++)
        {
            if (StockRow(ox, oy, i).Contains(mouse))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Gets the bag slot under the mouse (an occupied bag slot), or -1.</summary>
    public int HitBagSlot(Vector2 mouse, IWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.BagLimit; s++)
        {
            if (!slots[s].IsEmpty && BagCell(ox, oy, s).Contains(mouse))
            {
                return s;
            }
        }

        return -1;
    }

    /// <summary>Pass 1: frames, rows and cells.</summary>
    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, IWorldReadModel world, TraderView trader, int viewportWidth, int viewportHeight, Vector2? mouse)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(trader);
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), CarbonUiPalette.BorderOuter, bodyAlpha: 246);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 16f, oy + 44f, 396f, 392f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 422f, oy + 44f, 322f, 392f), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        RectangleF close = CloseRect(ox, oy);
        bool hoverClose = mouse.HasValue && close.Contains(mouse.Value);
        skin.DrawButton(spriteBatch, in close, hoverClose ? new ColorRgba(158, 46, 52, 245) : CarbonUiPalette.DangerFill, CarbonUiPalette.DangerBorder);

        for (int i = 0; i < Math.Min(MaxStockRows, trader.ItemIds.Length); i++)
        {
            RectangleF row = StockRow(ox, oy, i);
            bool soldOut = trader.Remaining[i] == 0;
            bool hover = !soldOut && mouse.HasValue && row.Contains(mouse.Value);
            bool affordable = world.LocalPlayerGold >= trader.UnitPrices[i];
            skin.DrawSlot(
                spriteBatch,
                in row,
                hover ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill,
                soldOut ? CarbonUiPalette.BorderSubtle : hover && affordable ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.BagLimit; s++)
        {
            RectangleF cell = BagCell(ox, oy, s);
            bool clickable = this.IsBagSlotActive(trader, slots[s], s);
            bool hover = clickable && mouse.HasValue && cell.Contains(mouse.Value);
            skin.DrawSlot(
                spriteBatch,
                in cell,
                hover ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill,
                hover ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }

        if (trader.CanRepair)
        {
            RectangleF repair = RepairButtonRect(ox, oy);
            bool hoverRepair = mouse.HasValue && repair.Contains(mouse.Value);
            skin.DrawButton(
                spriteBatch,
                in repair,
                this.RepairMode ? CarbonUiPalette.SlotSelectedFill : hoverRepair ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill,
                this.RepairMode || hoverRepair ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
            RectangleF enhance = EnhanceButtonRect(ox, oy);
            bool hoverEnhance = mouse.HasValue && enhance.Contains(mouse.Value);
            skin.DrawButton(spriteBatch, in enhance, hoverEnhance ? new ColorRgba(44, 118, 84, 245) : CarbonUiPalette.ConfirmFill, CarbonUiPalette.ConfirmBorder);
        }
    }

    /// <summary>Pass 2: item icons, coins and the restock bar.</summary>
    public void DrawGeometry(GeometryBatch batch, IWorldReadModel world, TraderView trader, IVectorArtRepository? art, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(trader);
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        CompiledItemVectorArt? coin = null;
        _ = art?.TryGetUiIcon("coin", out coin);
        for (int i = 0; i < Math.Min(MaxStockRows, trader.ItemIds.Length); i++)
        {
            RectangleF row = StockRow(ox, oy, i);
            if (art is not null && art.TryGetItem(trader.ItemIds[i], out CompiledItemVectorArt? icon))
            {
                VectorRigRenderer.DrawItemIcon(batch, icon, new Vector2(row.X + 20f, row.Y + (row.Height * 0.5f)), 26f);
            }

            if (coin is not null)
            {
                VectorRigRenderer.DrawItemIcon(batch, coin, new Vector2(row.X + 262f, row.Y + (row.Height * 0.5f)), 14f);
            }

            // Stock gauge: how much of this restock is left.
            float frac = trader.Stocked[i] == 0 ? 0f : trader.Remaining[i] / (float)trader.Stocked[i];
            RectangleF gauge = new(row.X + 44f, row.Y + row.Height - 6f, 150f, 3f);
            batch.FillRectangle(gauge, new ColorRgba(10, 14, 22, 230));
            batch.FillRectangle(new RectangleF(gauge.X, gauge.Y, gauge.Width * frac, gauge.Height), trader.Remaining[i] == 0 ? s_soldOut : new ColorRgba(16, 185, 129, 230));
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.BagLimit; s++)
        {
            if (!slots[s].IsEmpty && art is not null && art.TryGetItem(slots[s].ItemId, out CompiledItemVectorArt? icon))
            {
                RectangleF cell = BagCell(ox, oy, s);
                VectorRigRenderer.DrawItemIcon(batch, icon, new Vector2(cell.X + (cell.Width * 0.5f), cell.Y + 20f), 24f);
                if (trader.CanRepair)
                {
                    DrawDurability(batch, world, slots[s], cell);
                }

                if (!this.IsBagSlotActive(trader, slots[s], s))
                {
                    batch.FillRectangle(cell, new ColorRgba(8, 10, 14, 140));
                }
            }
        }

        if (coin is not null)
        {
            VectorRigRenderer.DrawItemIcon(batch, coin, new Vector2(ox + 32f, oy + PanelH - 30f), 18f);
        }
    }

    /// <summary>Pass 3: labels.</summary>
    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, IWorldReadModel world, TraderView trader, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(trader);
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        ColorRgba black = CarbonUiPalette.TextOutline;
        spriteBatch.DrawOutlinedString(font, Title(trader.Kind), new Vector2(ox + 18f, oy + 13f), CarbonUiPalette.TextWarm, black, 1, 1f);
        int seconds = trader.SecondsUntilRestock(DateTimeOffset.UtcNow);
        string restock = GameText.Format("trade.restock_in", new TextArg("time", FormatTime(seconds)));
        spriteBatch.DrawOutlinedString(font, restock, new Vector2(ox + PanelW - 60f - Width(font, restock, 1f), oy + 14f), CarbonUiPalette.TextMuted, black, 1, 1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "X", CloseRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);

        spriteBatch.DrawOutlinedString(font, GameText.Get("trade.stock"), new Vector2(ox + 26f, oy + 52f), CarbonUiPalette.TextMuted, black, 1, 1f);
        for (int i = 0; i < Math.Min(MaxStockRows, trader.ItemIds.Length); i++)
        {
            RectangleF row = StockRow(ox, oy, i);
            bool soldOut = trader.Remaining[i] == 0;
            string name = CarbonUiSkin.TruncateToWidth(font, world.Items.GetDisplayName(trader.ItemIds[i]), 150f, 1f);
            spriteBatch.DrawOutlinedString(font, name, new Vector2(row.X + 44f, row.Y + 7f), soldOut ? CarbonUiPalette.TextMuted : CarbonUiPalette.TextPrimary, black, 1, 1f);
            string left = soldOut
                ? GameText.Get("trade.sold_out")
                : GameText.Format("trade.left", new TextArg("count", trader.Remaining[i]));
            spriteBatch.DrawOutlinedString(font, left, new Vector2(row.X + 200f, row.Y + 12f), soldOut ? s_soldOut : CarbonUiPalette.TextMuted, black, 1, 1f);
            bool affordable = world.LocalPlayerGold >= trader.UnitPrices[i];
            spriteBatch.DrawOutlinedString(
                font,
                trader.UnitPrices[i].ToString("N0", CultureInfo.InvariantCulture),
                new Vector2(row.X + 274f, row.Y + 12f),
                affordable ? s_gold : CarbonUiPalette.DangerBorder,
                black,
                1,
                1f);
        }

        spriteBatch.DrawOutlinedString(font, GameText.Get("trade.your_bag"), new Vector2(ox + 432f, oy + 52f), CarbonUiPalette.TextMuted, black, 1, 1f);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.BagLimit; s++)
        {
            ItemSlot slot = slots[s];
            if (slot.IsEmpty)
            {
                continue;
            }

            RectangleF cell = BagCell(ox, oy, s);
            if (slot.Quantity > 1)
            {
                spriteBatch.DrawOutlinedString(font, slot.Quantity.ToString(CultureInfo.InvariantCulture), new Vector2(cell.X + 3f, cell.Y + 2f), CarbonUiPalette.TextPrimary, black, 1, 0.8f);
            }

            int amount = this.RepairMode ? trader.RepairCostOf(s) : trader.SellPriceOf(slot.ItemId);
            if (amount > 0)
            {
                string price = amount.ToString("N0", CultureInfo.InvariantCulture);
                spriteBatch.DrawOutlinedString(
                    font,
                    price,
                    new Vector2(cell.X + cell.Width - 4f - Width(font, price, 0.8f), cell.Y + cell.Height - 18f),
                    this.RepairMode ? s_repair : s_gold,
                    black,
                    1,
                    0.8f);
            }
        }

        spriteBatch.DrawOutlinedString(font, world.LocalPlayerGold.ToString("N0", CultureInfo.InvariantCulture), new Vector2(ox + 46f, oy + PanelH - 38f), s_gold, black, 1, 1f);
        string hint = !trader.CanRepair
            ? GameText.Get("trade.hint")
            : this.RepairMode
                ? trader.RepairSlots.Length > 0 ? GameText.Get("trade.repair_hint") : GameText.Get("trade.repair_none")
                : GameText.Get("trade.hint_smith");
        float hintMax = (trader.CanRepair ? RepairButtonRect(ox, oy).X : ox + PanelW) - (ox + 150f) - 12f;
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, hint, hintMax, 1f), new Vector2(ox + 150f, oy + PanelH - 38f), CarbonUiPalette.TextMuted, black, 1, 1f);
        if (trader.CanRepair)
        {
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("trade.repair"), RepairButtonRect(ox, oy), this.RepairMode ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary, black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("trade.enhance"), EnhanceButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        }
    }

    // A bag item is clickable when the trader buys it (sell mode) or holds a repair quote for it (repair mode).
    private bool IsBagSlotActive(TraderView trader, in ItemSlot slot, int bagSlot) =>
        !slot.IsEmpty && (this.RepairMode ? trader.RepairCostOf(bagSlot) > 0 : trader.SellPriceOf(slot.ItemId) > 0);

    private static void DrawDurability(GeometryBatch batch, IWorldReadModel world, in ItemSlot slot, RectangleF cell)
    {
        if (!world.Items.Rules.Items.TryGet(slot.ItemId, out ItemArchetype? archetype) || archetype.MaxDurability <= 0f)
        {
            return;
        }

        float max = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(archetype, ItemEnhancementPolicy.GetUpgradeLevel(in slot)));
        float frac = Math.Clamp(slot.Durability / max, 0f, 1f);
        ColorRgba color = frac <= 0f ? CarbonUiPalette.HpFill : frac > 0.35f ? CarbonUiPalette.StaminaFill : CarbonUiPalette.XpFill;
        RectangleF bar = new(cell.X + 5f, cell.Y + cell.Height - 5f, cell.Width - 10f, 3f);
        batch.FillRectangle(bar, new ColorRgba(8, 10, 14, 240));
        batch.FillRectangle(new RectangleF(bar.X, bar.Y, MathF.Max(frac <= 0f ? bar.Width : 1f, bar.Width * frac), bar.Height), color);
    }

    /// <summary>Gets the window title of a trader kind.</summary>
    public static string Title(byte kind) => kind switch
    {
        TraderStock.KindMerchant => GameText.Get("trade.title_merchant"),
        TraderStock.KindBlacksmith => GameText.Get("trade.title_blacksmith"),
        _ => GameText.Get("trade.title_general"),
    };

    private static string FormatTime(int seconds) =>
        seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");

    private static float Width(BitmapFont font, string text, float scale) =>
        font.MeasureString(text, TextLayoutOptions.Default, scale).Width;

    private static (float X, float Y) Origin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    private static RectangleF CloseRect(float ox, float oy) => new(ox + PanelW - 34f, oy + 10f, 24f, 24f);

    private static RectangleF EnhanceButtonRect(float ox, float oy) => new(ox + PanelW - 16f - ButtonW, oy + PanelH - 44f, ButtonW, ButtonH);

    private static RectangleF RepairButtonRect(float ox, float oy) => new(ox + PanelW - 24f - (ButtonW * 2f), oy + PanelH - 44f, ButtonW, ButtonH);

    private static RectangleF StockRow(float ox, float oy, int index) => new(ox + 24f, oy + 74f + (index * (RowH + RowGap)), 380f, RowH);

    private static RectangleF BagCell(float ox, float oy, int bagSlot) =>
        new(ox + 432f + (bagSlot % BagColumns * (Cell + 1f)), oy + 74f + (bagSlot / BagColumns * (Cell + 1f)), Cell, Cell);
}
