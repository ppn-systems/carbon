// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Trader window (ADR-025): the trader's stock on the left (icon, name, price, units left), the player's bag on the right
/// (icon, quantity, what the trader pays), the wallet and the restock countdown. Below them sit the buy cart and the
/// "To sell" basket (D18): a click on stock adds it to the cart, a click or a drag on a bag item moves it to the basket,
/// each line has −/+ and a subtotal, and only the Buy and Sell buttons send anything. Everything priced comes from the
/// server's <c>TraderStock</c>. Nine-slice skinned (ADR-008), three passes like the other HUD windows.
/// <para>
/// A blacksmith (<see cref="TraderView.CanRepair"/>) adds two footer buttons. <b>Repair</b> opens the repair window at
/// the blacksmith (<see cref="RepairModalWidget"/>, ADR-028). <b>Enhance</b> hands off
/// to the forge enhancement window (ADR-024); this window never enhances itself.
/// </para>
/// </summary>
public static class TradeModalWidget
{
    /// <summary>Most stock rows shown.</summary>
    public const int MaxStockRows = 9;

    /// <summary>Basket or cart lines shown at once (the rest are summarized as "+N more").</summary>
    public const int VisibleLines = 3;

    private const float PanelW = 760f;
    private const float PanelH = 652f;
    private const float RowH = 40f;
    private const float RowGap = 3f;
    private const float Cell = 50f;
    private const int BagColumns = 6;
    private const float ButtonW = 112f;
    private const float ButtonH = 28f;
    private const float TopCardH = 404f;
    private const float BasketY = 456f;
    private const float BasketH = 148f;
    private const float LineH = 23f;
    private const float LineStep = 25f;
    private const float ActionW = 90f;
    private const float ClearW = 64f;

    private static readonly ColorRgba s_gold = new(251, 191, 36, 255);
    private static readonly ColorRgba s_soldOut = new(148, 64, 64, 255);
    private static readonly ColorRgba s_reserved = new(251, 146, 60, 255);
    private static readonly ColorRgba s_warning = new(251, 146, 60, 255);

    /// <summary>Gets whether a point is inside the window.</summary>
    public static bool ContainsPoint(Vector2 mouse, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        return new RectangleF(ox, oy, PanelW, PanelH).Contains(mouse);
    }

    /// <summary>Gets whether the close button was hit.</summary>
    public static bool TryHitTestCloseButton(Vector2 mouse, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        return CloseRect(ox, oy).Contains(mouse);
    }

    /// <summary>Gets what the cursor is over: a button, a line control, a stock row, an occupied bag cell or a card.</summary>
    public static TradeWindowHit HitTest(Vector2 mouse, TraderView trader, TradeBasket basket, IWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(trader);
        ArgumentNullException.ThrowIfNull(basket);
        ArgumentNullException.ThrowIfNull(world);
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        if (trader.CanRepair && RepairButtonRect(ox, oy).Contains(mouse))
        {
            return new TradeWindowHit(TradeWindowTarget.Repair);
        }

        if (trader.CanRepair && EnhanceButtonRect(ox, oy).Contains(mouse))
        {
            return new TradeWindowHit(TradeWindowTarget.Enhance);
        }

        RectangleF cart = CartCard(ox, oy);
        RectangleF sell = SellCard(ox, oy);
        if (ActionRect(cart).Contains(mouse))
        {
            return new TradeWindowHit(TradeWindowTarget.BuyButton);
        }

        if (ActionRect(sell).Contains(mouse))
        {
            return new TradeWindowHit(TradeWindowTarget.SellButton);
        }

        if (ClearRect(cart).Contains(mouse))
        {
            return new TradeWindowHit(TradeWindowTarget.ClearBuy);
        }

        if (ClearRect(sell).Contains(mouse))
        {
            return new TradeWindowHit(TradeWindowTarget.ClearSell);
        }

        for (int i = 0; i < Math.Min(VisibleLines, basket.BuyLines.Count); i++)
        {
            RectangleF line = LineRect(cart, i);
            if (MinusRect(line).Contains(mouse))
            {
                return new TradeWindowHit(TradeWindowTarget.BuyMinus, i);
            }

            if (PlusRect(line).Contains(mouse))
            {
                return new TradeWindowHit(TradeWindowTarget.BuyPlus, i);
            }
        }

        for (int i = 0; i < Math.Min(VisibleLines, basket.SellLines.Count); i++)
        {
            RectangleF line = LineRect(sell, i);
            if (MinusRect(line).Contains(mouse))
            {
                return new TradeWindowHit(TradeWindowTarget.SellMinus, i);
            }

            if (PlusRect(line).Contains(mouse))
            {
                return new TradeWindowHit(TradeWindowTarget.SellPlus, i);
            }
        }

        for (int i = 0; i < Math.Min(MaxStockRows, trader.ItemIds.Length); i++)
        {
            if (StockRow(ox, oy, i).Contains(mouse))
            {
                return new TradeWindowHit(TradeWindowTarget.Stock, i);
            }
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.BagLimit; s++)
        {
            if (!slots[s].IsEmpty && BagCell(ox, oy, s).Contains(mouse))
            {
                return new TradeWindowHit(TradeWindowTarget.Bag, s);
            }
        }

        return sell.Contains(mouse) ? new TradeWindowHit(TradeWindowTarget.SellArea)
            : cart.Contains(mouse) ? new TradeWindowHit(TradeWindowTarget.BuyArea)
            : TradeWindowHit.None;
    }

    /// <summary>
    /// Draws the window over a dimmed screen, in three passes (skin, geometry, text), then the item being dragged
    /// (<paramref name="draggedSlot"/>, a bag slot, or -1) under the cursor.
    /// </summary>
    public static void Render(
        SpriteBatch spriteBatch,
        GeometryBatch geometryBatch,
        CarbonUiSkin skin,
        BitmapFont font,
        IVectorArtRepository? art,
        Matrix4x4 screenProj,
        IWorldReadModel world,
        TraderView trader,
        TradeBasket basket,
        int draggedSlot,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouse)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(geometryBatch);
        ArgumentNullException.ThrowIfNull(basket);
        geometryBatch.Begin(screenProj);
        geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 6, 12, 120));
        geometryBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        DrawSkin(spriteBatch, skin, world, trader, basket, viewportWidth, viewportHeight, mouse);
        spriteBatch.End();

        geometryBatch.Begin(screenProj);
        DrawGeometry(geometryBatch, world, trader, basket, art, viewportWidth, viewportHeight);
        geometryBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        DrawText(spriteBatch, font, world, trader, basket, viewportWidth, viewportHeight);
        spriteBatch.End();

        if (draggedSlot >= 0 && mouse.HasValue && art is not null)
        {
            Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            world.CopyInventorySlots(slots);
            if ((uint)draggedSlot < PlayerInventoryLayout.BagLimit && art.TryGetItem(slots[draggedSlot].ItemId, out CompiledItemVectorArt? icon))
            {
                geometryBatch.Begin(screenProj);
                VectorRigRenderer.DrawItemIcon(geometryBatch, icon, mouse.Value, 28f);
                geometryBatch.End();
            }
        }
    }

    /// <summary>Pass 1: frames, rows, cells and buttons.</summary>
    private static void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, IWorldReadModel world, TraderView trader, TradeBasket basket, int viewportWidth, int viewportHeight, Vector2? mouse)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(trader);
        (float ox, float oy) = Origin(viewportWidth, viewportHeight);
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), CarbonUiPalette.BorderOuter, bodyAlpha: 246);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 16f, oy + 44f, 396f, TopCardH), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 422f, oy + 44f, 322f, TopCardH), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        RectangleF close = CloseRect(ox, oy);
        bool hoverClose = mouse.HasValue && close.Contains(mouse.Value);
        skin.DrawButton(spriteBatch, in close, hoverClose ? new ColorRgba(158, 46, 52, 245) : CarbonUiPalette.DangerFill, CarbonUiPalette.DangerBorder);

        for (int i = 0; i < Math.Min(MaxStockRows, trader.ItemIds.Length); i++)
        {
            RectangleF row = StockRow(ox, oy, i);
            bool soldOut = trader.Remaining[i] - basket.InCartAt(i) <= 0;
            bool hover = !soldOut && mouse.HasValue && row.Contains(mouse.Value);
            skin.DrawSlot(
                spriteBatch,
                in row,
                hover ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill,
                hover ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.BagLimit; s++)
        {
            RectangleF cell = BagCell(ox, oy, s);
            bool clickable = IsBagSlotActive(trader, basket, slots[s], s);
            bool hover = clickable && mouse.HasValue && cell.Contains(mouse.Value);
            skin.DrawSlot(
                spriteBatch,
                in cell,
                hover ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill,
                hover ? CarbonUiPalette.BorderActive : basket.ReservedAt(s) > 0 ? s_reserved : CarbonUiPalette.BorderSubtle);
        }

        DrawCardSkin(spriteBatch, skin, CartCard(ox, oy), basket.BuyLines.Count, basket.BuyLines.Count > 0 && !basket.IsBuyPending, mouse);
        DrawCardSkin(spriteBatch, skin, SellCard(ox, oy), basket.SellLines.Count, basket.SellLines.Count > 0 && !basket.IsSellPending, mouse);

        if (trader.CanRepair)
        {
            RectangleF repair = RepairButtonRect(ox, oy);
            bool hoverRepair = mouse.HasValue && repair.Contains(mouse.Value);
            skin.DrawButton(
                spriteBatch,
                in repair,
                hoverRepair ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill,
                hoverRepair ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
            RectangleF enhance = EnhanceButtonRect(ox, oy);
            bool hoverEnhance = mouse.HasValue && enhance.Contains(mouse.Value);
            skin.DrawButton(spriteBatch, in enhance, hoverEnhance ? new ColorRgba(44, 118, 84, 245) : CarbonUiPalette.ConfirmFill, CarbonUiPalette.ConfirmBorder);
        }
    }

    private static void DrawCardSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, RectangleF card, int lines, bool canCommit, Vector2? mouse)
    {
        skin.DrawCard(spriteBatch, in card, CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        for (int i = 0; i < Math.Min(VisibleLines, lines); i++)
        {
            RectangleF line = LineRect(card, i);
            skin.DrawSlot(spriteBatch, in line, CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);
            RectangleF minus = MinusRect(line);
            RectangleF plus = PlusRect(line);
            skin.DrawButton(spriteBatch, in minus, Hover(mouse, minus) ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);
            skin.DrawButton(spriteBatch, in plus, Hover(mouse, plus) ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);
        }

        RectangleF clear = ClearRect(card);
        skin.DrawButton(spriteBatch, in clear, Hover(mouse, clear) && lines > 0 ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill, CarbonUiPalette.BorderSubtle);
        RectangleF action = ActionRect(card);
        skin.DrawButton(
            spriteBatch,
            in action,
            !canCommit ? CarbonUiPalette.SlotFill : Hover(mouse, action) ? new ColorRgba(44, 118, 84, 245) : CarbonUiPalette.ConfirmFill,
            canCommit ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderSubtle);
    }

    /// <summary>Pass 2: item icons, coins, the stock gauges and reserved markers.</summary>
    private static void DrawGeometry(GeometryBatch batch, IWorldReadModel world, TraderView trader, TradeBasket basket, IVectorArtRepository? art, int viewportWidth, int viewportHeight)
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
                VectorRigRenderer.DrawItemIcon(batch, coin, new Vector2(row.X + 268f, row.Y + (row.Height * 0.5f)), 14f);
            }

            // Stock gauge: how much of this restock is left once the cart is bought.
            int left = Math.Max(0, trader.Remaining[i] - basket.InCartAt(i));
            float frac = trader.Stocked[i] == 0 ? 0f : left / (float)trader.Stocked[i];
            RectangleF gauge = new(row.X + 44f, row.Y + row.Height - 6f, 150f, 3f);
            batch.FillRectangle(gauge, new ColorRgba(10, 14, 22, 230));
            batch.FillRectangle(new RectangleF(gauge.X, gauge.Y, gauge.Width * frac, gauge.Height), left == 0 ? s_soldOut : new ColorRgba(16, 185, 129, 230));
            if (basket.InCartAt(i) > 0)
            {
                batch.FillRectangle(new RectangleF(row.X, row.Y + 4f, 3f, row.Height - 8f), s_reserved);
            }
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int s = 0; s < PlayerInventoryLayout.BagLimit; s++)
        {
            if (!slots[s].IsEmpty && art is not null && art.TryGetItem(slots[s].ItemId, out CompiledItemVectorArt? icon))
            {
                RectangleF cell = BagCell(ox, oy, s);
                VectorRigRenderer.DrawItemIcon(batch, icon, new Vector2(cell.X + (cell.Width * 0.5f), cell.Y + 20f), 24f);
                if (!IsBagSlotActive(trader, basket, slots[s], s))
                {
                    batch.FillRectangle(cell, new ColorRgba(8, 10, 14, 140));
                }
            }
        }

        DrawLineIcons(batch, art, CartCard(ox, oy), basket.BuyLines);
        DrawLineIcons(batch, art, SellCard(ox, oy), basket.SellLines);
        if (coin is not null)
        {
            VectorRigRenderer.DrawItemIcon(batch, coin, new Vector2(ox + 32f, oy + PanelH - 30f), 18f);
        }
    }

    private static void DrawLineIcons(GeometryBatch batch, IVectorArtRepository? art, RectangleF card, IReadOnlyList<TradeBasketLine> lines)
    {
        for (int i = 0; i < Math.Min(VisibleLines, lines.Count); i++)
        {
            RectangleF line = LineRect(card, i);
            if (art is not null && art.TryGetItem(lines[i].ItemId, out CompiledItemVectorArt? icon))
            {
                VectorRigRenderer.DrawItemIcon(batch, icon, new Vector2(line.X + 12f, line.Y + (line.Height * 0.5f)), 18f);
            }
        }
    }

    /// <summary>Pass 3: labels.</summary>
    private static void DrawText(SpriteBatch spriteBatch, BitmapFont font, IWorldReadModel world, TraderView trader, TradeBasket basket, int viewportWidth, int viewportHeight)
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
            int left = trader.Remaining[i] - basket.InCartAt(i);
            bool soldOut = left <= 0;
            string name = CarbonUiSkin.TruncateToWidth(font, world.Items.GetDisplayName(trader.ItemIds[i]), 150f, 1f);
            spriteBatch.DrawOutlinedString(font, name, new Vector2(row.X + 44f, row.Y + 7f), soldOut ? CarbonUiPalette.TextMuted : CarbonUiPalette.TextPrimary, black, 1, 1f);
            string leftText = trader.Remaining[i] == 0
                ? GameText.Get("trade.sold_out")
                : GameText.Format("trade.left", new TextArg("count", Math.Max(0, left)));
            spriteBatch.DrawOutlinedString(font, leftText, new Vector2(row.X + 200f, row.Y + 12f), soldOut ? s_soldOut : CarbonUiPalette.TextMuted, black, 1, 1f);
            spriteBatch.DrawOutlinedString(
                font,
                trader.UnitPrices[i].ToString("N0", CultureInfo.InvariantCulture),
                new Vector2(row.X + 280f, row.Y + 12f),
                world.LocalPlayerGold >= trader.UnitPrices[i] ? s_gold : CarbonUiPalette.DangerBorder,
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

            // Units set aside for the basket stay in the bag until the server sells them; the cell shows what is left.
            RectangleF cell = BagCell(ox, oy, s);
            int reserved = basket.ReservedAt(s);
            int shown = slot.Quantity - reserved;
            if (shown > 1 || (reserved > 0 && shown > 0))
            {
                spriteBatch.DrawOutlinedString(font, shown.ToString(CultureInfo.InvariantCulture), new Vector2(cell.X + 3f, cell.Y + 2f), reserved > 0 ? s_reserved : CarbonUiPalette.TextPrimary, black, 1, 0.8f);
            }

            int amount = trader.SellPriceOf(slot.ItemId);
            if (amount > 0)
            {
                string price = amount.ToString("N0", CultureInfo.InvariantCulture);
                spriteBatch.DrawOutlinedString(font, price, new Vector2(cell.X + cell.Width - 4f - Width(font, price, 0.8f), cell.Y + cell.Height - 18f), s_gold, black, 1, 0.8f);
            }
        }

        long gold = world.LocalPlayerGold;
        DrawCardText(spriteBatch, font, world, CartCard(ox, oy), GameText.Get("trade_basket.cart"), basket.BuyLines, basket.BuyTotal, gold, buy: true, basket);
        DrawCardText(spriteBatch, font, world, SellCard(ox, oy), GameText.Get("trade_basket.to_sell"), basket.SellLines, basket.SellTotal, gold, buy: false, basket);

        spriteBatch.DrawOutlinedString(font, gold.ToString("N0", CultureInfo.InvariantCulture), new Vector2(ox + 46f, oy + PanelH - 38f), s_gold, black, 1, 1f);
        string hint = GameText.Get(trader.CanRepair ? "trade.hint_smith" : "trade.hint");
        float hintMax = (trader.CanRepair ? RepairButtonRect(ox, oy).X : ox + PanelW) - (ox + 150f) - 12f;
        spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, hint, hintMax, 1f), new Vector2(ox + 150f, oy + PanelH - 38f), CarbonUiPalette.TextMuted, black, 1, 1f);
        if (trader.CanRepair)
        {
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("trade.repair"), RepairButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("trade.enhance"), EnhanceButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        }
    }

    private static void DrawCardText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        RectangleF card,
        string title,
        IReadOnlyList<TradeBasketLine> lines,
        long total,
        long gold,
        bool buy,
        TradeBasket basket)
    {
        ColorRgba black = CarbonUiPalette.TextOutline;
        spriteBatch.DrawOutlinedString(font, title, new Vector2(card.X + 10f, card.Y + 6f), CarbonUiPalette.TextMuted, black, 1, 1f);
        for (int i = 0; i < Math.Min(VisibleLines, lines.Count); i++)
        {
            TradeBasketLine line = lines[i];
            RectangleF row = LineRect(card, i);
            RectangleF minus = MinusRect(row);
            string name = world.Items.GetDisplayName(line.ItemId);
            if (line.UpgradeLevel > 0)
            {
                name = GameText.Format("trade_basket.enhanced_name", new TextArg("name", name), new TextArg("level", line.UpgradeLevel));
            }

            float nameMax = minus.X - (row.X + 26f) - 6f;
            spriteBatch.DrawOutlinedString(
                font,
                CarbonUiSkin.TruncateToWidth(font, name, nameMax, 1f),
                new Vector2(row.X + 26f, row.Y + 3f),
                line.UpgradeLevel > 0 ? s_warning : CarbonUiPalette.TextPrimary,
                black,
                1,
                1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "-", minus, CarbonUiPalette.TextPrimary, black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "+", PlusRect(row), CarbonUiPalette.TextPrimary, black, 1f);
            RectangleF qty = new(minus.Right, row.Y, PlusRect(row).X - minus.Right, row.Height);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, line.Quantity.ToString(CultureInfo.InvariantCulture), qty, CarbonUiPalette.TextPrimary, black, 1f);
            string subtotal = line.Subtotal.ToString("N0", CultureInfo.InvariantCulture);
            spriteBatch.DrawOutlinedString(font, subtotal, new Vector2(row.Right - 6f - Width(font, subtotal, 1f), row.Y + 3f), s_gold, black, 1, 1f);
        }

        // One status line: why the button asks for a confirm, what is waiting, or what is not shown.
        bool pricesChanged = buy ? basket.BuyPricesChanged : basket.SellPricesChanged;
        bool pending = buy ? basket.IsBuyPending : basket.IsSellPending;
        (string? status, ColorRgba color) = lines.Count == 0
            ? (GameText.Get(buy ? "trade_basket.empty_cart" : "trade_basket.empty_sell"), CarbonUiPalette.TextMuted)
            : pending ? (GameText.Get("trade_basket.waiting"), CarbonUiPalette.TextMuted)
            : pricesChanged ? (GameText.Get("trade_basket.prices_changed"), s_warning)
            : !buy && basket.SellNeedsEnhancedConfirm ? (GameText.Get("trade_basket.enhanced_warning"), s_warning)
            : buy && total > gold ? (GameText.Get("trade_basket.not_enough_gold"), CarbonUiPalette.DangerBorder)
            : lines.Count > VisibleLines ? (GameText.Format("trade_basket.more", new TextArg("count", lines.Count - VisibleLines)), CarbonUiPalette.TextMuted)
            : (null, CarbonUiPalette.TextMuted);
        float statusY = lines.Count == 0 ? card.Y + 32f : card.Y + 26f + (VisibleLines * LineStep) + 1f;
        if (status is not null)
        {
            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, status, card.Width - 20f, 0.85f), new Vector2(card.X + 10f, statusY), color, black, 1, 0.85f);
        }

        RectangleF action = ActionRect(card);
        string totalText = GameText.Format("trade_basket.total", new TextArg("gold", total.ToString("N0", CultureInfo.InvariantCulture)));
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, totalText, ClearRect(card).X - card.X - 16f, 1f),
            new Vector2(card.X + 10f, action.Y + 5f),
            buy && total > gold ? CarbonUiPalette.DangerBorder : s_gold,
            black,
            1,
            1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("trade_basket.clear"), ClearRect(card), lines.Count > 0 ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted, black, 0.9f);
        bool confirm = pricesChanged || (!buy && basket.SellNeedsEnhancedConfirm);
        string label = GameText.Get(confirm ? "trade_basket.confirm" : buy ? "trade_basket.buy" : "trade_basket.sell");
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, label, action, lines.Count > 0 && !pending ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted, black, 1f);
    }

    // A bag item is clickable while the trader buys it and some of it is not yet in the basket.
    private static bool IsBagSlotActive(TraderView trader, TradeBasket basket, in ItemSlot slot, int bagSlot) =>
        !slot.IsEmpty && trader.SellPriceOf(slot.ItemId) > 0 && slot.Quantity > basket.ReservedAt(bagSlot);

    /// <summary>Gets the window title of a trader kind.</summary>
    public static string Title(byte kind) => kind switch
    {
        TraderStock.KindMerchant => GameText.Get("trade.title_merchant"),
        TraderStock.KindBlacksmith => GameText.Get("trade.title_blacksmith"),
        _ => GameText.Get("trade.title_general"),
    };

    private static bool Hover(Vector2? mouse, RectangleF rect) => mouse.HasValue && rect.Contains(mouse.Value);

    private static string FormatTime(int seconds) =>
        seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");

    private static float Width(BitmapFont font, string text, float scale) =>
        font.MeasureString(text, TextLayoutOptions.Default, scale).Width;

    private static (float X, float Y) Origin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Max(0f, MathF.Floor((viewportHeight - PanelH) * 0.5f)));

    private static RectangleF CloseRect(float ox, float oy) => new(ox + PanelW - 34f, oy + 10f, 24f, 24f);

    private static RectangleF EnhanceButtonRect(float ox, float oy) => new(ox + PanelW - 16f - ButtonW, oy + PanelH - 44f, ButtonW, ButtonH);

    private static RectangleF RepairButtonRect(float ox, float oy) => new(ox + PanelW - 24f - (ButtonW * 2f), oy + PanelH - 44f, ButtonW, ButtonH);

    private static RectangleF StockRow(float ox, float oy, int index) => new(ox + 24f, oy + 74f + (index * (RowH + RowGap)), 380f, RowH);

    private static RectangleF BagCell(float ox, float oy, int bagSlot) =>
        new(ox + 432f + (bagSlot % BagColumns * (Cell + 1f)), oy + 74f + (bagSlot / BagColumns * (Cell + 1f)), Cell, Cell);

    private static RectangleF CartCard(float ox, float oy) => new(ox + 16f, oy + BasketY, 396f, BasketH);

    private static RectangleF SellCard(float ox, float oy) => new(ox + 422f, oy + BasketY, 322f, BasketH);

    private static RectangleF LineRect(RectangleF card, int index) => new(card.X + 8f, card.Y + 26f + (index * LineStep), card.Width - 16f, LineH);

    private static RectangleF MinusRect(RectangleF line) => new(line.Right - 130f, line.Y + 2f, 20f, LineH - 4f);

    private static RectangleF PlusRect(RectangleF line) => new(line.Right - 76f, line.Y + 2f, 20f, LineH - 4f);

    private static RectangleF ActionRect(RectangleF card) => new(card.Right - 10f - ActionW, card.Bottom - 32f, ActionW, 24f);

    private static RectangleF ClearRect(RectangleF card) => new(card.Right - 16f - ActionW - ClearW, card.Bottom - 32f, ClearW, 24f);
}
