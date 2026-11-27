// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
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
/// The split / confirm-drop / confirm-destroy dialog drawn over the inventory bag (ADR-020): hit testing, geometry and
/// text, with the localized strings cached until the dialog, its quantity or the language changes. Extracted from
/// <see cref="InventoryModalWidget"/> (audit round 2, SRP); behaviour is unchanged.
/// </summary>
internal sealed class InventoryDialogRenderer
{
    private (InventoryDialogKind Kind, ItemSlot Item, int Quantity, int Version) _dialogTextKey = (InventoryDialogKind.None, ItemSlot.Empty, -1, -1);
    private (string Title, string Name, string Body, string Confirm) _dialogText = (string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>Gets the dialog state (kind, item, quantity).</summary>
    public InventoryDialogState State { get; } = new();

    public InventoryDialogButton HitTest(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        if (!this.State.IsOpen)
        {
            return InventoryDialogButton.None;
        }

        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        bool split = this.State.Kind == InventoryDialogKind.Split;
        if (GetButtonRect(in layout, InventoryDialogButton.Confirm).Contains(mousePos))
        {
            return InventoryDialogButton.Confirm;
        }

        if (GetButtonRect(in layout, InventoryDialogButton.Cancel).Contains(mousePos))
        {
            return InventoryDialogButton.Cancel;
        }

        if (split && GetButtonRect(in layout, InventoryDialogButton.Minus).Contains(mousePos))
        {
            return InventoryDialogButton.Minus;
        }

        if (split && GetButtonRect(in layout, InventoryDialogButton.Plus).Contains(mousePos))
        {
            return InventoryDialogButton.Plus;
        }

        return GetDialogRect(in layout).Contains(mousePos) ? InventoryDialogButton.Body : InventoryDialogButton.None;
    }

    public void DrawGeometry(GeometryBatch batch, IWorldReadModel world, IVectorArtRepository? art, in PlayerModalLayout layout, Vector2? mouse)
    {
        float s = layout.Scale;
        batch.FillRectangle(new RectangleF(layout.Ox, layout.Oy, layout.PanelW, layout.PanelH), new ColorRgba(4, 6, 12, 160));
        RectangleF box = GetDialogRect(in layout);
        bool destroy = this.State.Kind == InventoryDialogKind.ConfirmDestroy;
        ColorRgba frame = destroy ? CarbonUiPalette.DangerBorder : CarbonUiPalette.BorderActive;
        batch.FillRectangle(box, CarbonUiPalette.WindowFill);
        batch.FillRectangle(new RectangleF(box.X, box.Y, box.Width, MathF.Max(2f, MathF.Round(3f * s))), frame);
        batch.DrawRectangle(box, frame, 2f);

        RectangleF iconRect = GetDialogIconRect(in layout);
        ItemRarity rarity = ItemRarityColors.GetRarity(world.Items.Rules, this.State.Item.ItemId);
        batch.FillRectangle(iconRect, CarbonUiPalette.SlotFill);
        batch.DrawRectangle(iconRect, rarity == ItemRarity.Common ? CarbonUiPalette.BorderSubtle : ItemRarityColors.GetBorder(rarity), 1.5f);
        if (art is not null && art.TryGetItem(this.State.Item.ItemId, out CompiledItemVectorArt? itemArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, itemArt, new Vector2(iconRect.X + (iconRect.Width * 0.5f), iconRect.Y + (iconRect.Height * 0.5f)), MathF.Round(42f * s));
        }

        if (this.State.Kind == InventoryDialogKind.Split)
        {
            RectangleF bar = GetSplitBarRect(in layout);
            float frac = this.State.Item.Quantity > 0 ? this.State.Quantity / (float)this.State.Item.Quantity : 0f;
            batch.FillRectangle(bar, new ColorRgba(8, 10, 14, 240));
            batch.FillRectangle(new RectangleF(bar.X, bar.Y, MathF.Max(2f, bar.Width * frac), bar.Height), CarbonUiPalette.XpFill);
            DrawDialogButton(batch, GetButtonRect(in layout, InventoryDialogButton.Minus), mouse, CarbonUiPalette.CardFill, CarbonUiPalette.BorderOuter);
            DrawDialogButton(batch, GetButtonRect(in layout, InventoryDialogButton.Plus), mouse, CarbonUiPalette.CardFill, CarbonUiPalette.BorderOuter);
        }

        DrawDialogButton(
            batch,
            GetButtonRect(in layout, InventoryDialogButton.Confirm),
            mouse,
            destroy ? CarbonUiPalette.DangerFill : CarbonUiPalette.ConfirmFill,
            destroy ? CarbonUiPalette.DangerBorder : CarbonUiPalette.ConfirmBorder);
        DrawDialogButton(batch, GetButtonRect(in layout, InventoryDialogButton.Cancel), mouse, CarbonUiPalette.CardFill, CarbonUiPalette.BorderOuter);
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, IWorldReadModel world, in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF box = GetDialogRect(in layout);
        RectangleF iconRect = GetDialogIconRect(in layout);
        ItemSlot item = this.State.Item;
        (string title, string name, string body, string confirm) = this.GetDialogText(world);

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

        if (this.State.Kind == InventoryDialogKind.Split)
        {
            RectangleF bar = GetSplitBarRect(in layout);
            spriteBatch.DrawOutlinedString(
                font,
                GameText.Get("inventory.split.hint"),
                new Vector2(bar.X, bar.Bottom + MathF.Round(12f * s)),
                CarbonUiPalette.BorderOuter,
                black,
                1,
                layout.TextScale);
            DrawCentered(spriteBatch, font, GetButtonRect(in layout, InventoryDialogButton.Minus), "-", CarbonUiPalette.TextPrimary, layout.TitleScale);
            DrawCentered(spriteBatch, font, GetButtonRect(in layout, InventoryDialogButton.Plus), "+", CarbonUiPalette.TextPrimary, layout.TitleScale);
        }

        DrawCentered(spriteBatch, font, GetButtonRect(in layout, InventoryDialogButton.Confirm), confirm, CarbonUiPalette.TextPrimary, layout.TextScale);
        DrawCentered(spriteBatch, font, GetButtonRect(in layout, InventoryDialogButton.Cancel), GameText.Get("inventory.cancel"), CarbonUiPalette.TextMuted, layout.TextScale);
    }

    /// <summary>
    /// Gets the dialog's title, item name, body and confirm label, rebuilt only when the dialog, its quantity or the
    /// language changes (no per-frame formatting).
    /// </summary>
    private (string Title, string Name, string Body, string Confirm) GetDialogText(IWorldReadModel world)
    {
        InventoryDialogState d = this.State;
        int version = GameText.Current.Version;
        if (_dialogTextKey == (d.Kind, d.Item, d.Quantity, version))
        {
            return _dialogText;
        }

        ItemSlot item = d.Item;
        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
        string name = world.Items.Rules.ItemCatalog.TryGet(item.ItemId, out ItemCatalogEntry? entry)
            ? GameText.ItemName(entry.Code, world.Items.GetDisplayName(item.ItemId))
            : world.Items.GetDisplayName(item.ItemId);
        if (upg > 0)
        {
            name = GameText.Format("inventory.enhanced_name", new TextArg("name", name), new TextArg("level", upg));
        }

        int qty = d.EffectiveQuantity;
        _dialogText = d.Kind switch
        {
            InventoryDialogKind.Split => (
                GameText.Get("inventory.split.title"),
                name,
                GameText.Format("inventory.split.body", new TextArg("count", d.Quantity), new TextArg("total", item.Quantity)),
                GameText.Get("inventory.split.confirm")),
            InventoryDialogKind.ConfirmDestroy => (
                GameText.Get("inventory.destroy.title"),
                name,
                GameText.Plural("inventory.destroy.body", qty, new TextArg("count", qty)),
                GameText.Get("inventory.destroy.confirm")),
            InventoryDialogKind.ConfirmDrop or InventoryDialogKind.None or _ => (
                GameText.Get("inventory.drop.title"),
                name,
                GameText.Plural("inventory.drop.body", qty, new TextArg("count", qty)),
                GameText.Get("inventory.drop.confirm")),
        };
        _dialogTextKey = (d.Kind, d.Item, d.Quantity, version);
        return _dialogText;
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
        RectangleF minus = GetButtonRect(in layout, InventoryDialogButton.Minus);
        RectangleF plus = GetButtonRect(in layout, InventoryDialogButton.Plus);
        float h = MathF.Round(10f * s);
        float gap = MathF.Round(10f * s);
        return new RectangleF(minus.Right + gap, minus.Y + MathF.Floor((minus.Height - h) * 0.5f), plus.X - minus.Right - (gap * 2f), h);
    }

    internal static RectangleF GetButtonRect(in PlayerModalLayout layout, InventoryDialogButton button)
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
}
