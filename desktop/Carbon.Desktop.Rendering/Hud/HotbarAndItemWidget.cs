// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
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

/// <summary>Quick-access dock action next to the bottom hotbar.</summary>
public enum HudDockAction : byte
{
    /// <summary>No dock button hit.</summary>
    None = 0,

    /// <summary>Toggles the hero equipment and bag modal (<c>[I]</c>).</summary>
    Inventory = 1,

    /// <summary>Toggles the crafting workshop modal (<c>[C]</c>).</summary>
    Crafting = 2,

    /// <summary>Toggles the blacksmith forge anvil modal (<c>[U]</c>).</summary>
    Forge = 3,
}

/// <summary>
/// Renders the 9-slot RPG hotbar skinned with <see cref="CarbonUiSkin"/>, crisp <c>.cvg.json</c> vector item art,
/// color-graded durability bars, quick-access BAG/CRAFT/FORGE buttons, and floating item tooltips (SRP).
/// </summary>
internal sealed class HotbarAndItemWidget
{
    private const int SlotCount = 9;
    private const float SlotSize = 50f;
    private const float Gap = 5f;

    public bool TryHitTestDockButton(Vector2 mousePos, int viewportWidth, int viewportHeight, out HudDockAction action)
    {
        (RectangleF bag, RectangleF craft, RectangleF forge) = GetDockRects(viewportWidth, viewportHeight);
        if (bag.Contains(mousePos))
        {
            action = HudDockAction.Inventory;
            return true;
        }

        if (craft.Contains(mousePos))
        {
            action = HudDockAction.Crafting;
            return true;
        }

        if (forge.Contains(mousePos))
        {
            action = HudDockAction.Forge;
            return true;
        }

        action = HudDockAction.None;
        return false;
    }

    public void DrawSkin(
        SpriteBatch spriteBatch,
        CarbonUiSkin skin,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        bool showBag,
        bool showCraft,
        bool showForge)
    {
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - SlotSize - 36f;

        skin.DrawCard(spriteBatch, new RectangleF(startX - 8f, startY - 8f, totalW + 16f, SlotSize + 16f), new ColorRgba(71, 85, 105, 225), bodyAlpha: 224);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            bool active = i == world.SelectedHotbarSlot;
            float sy = active ? startY - 4f : startY;
            ItemSlot item = slots[i];
            ColorRgba border = active
                ? new ColorRgba(250, 204, 21, 255)
                : (!item.IsEmpty && ToolUsagePolicy.IsWornOut(in item) ? new ColorRgba(239, 68, 68, 255) : new ColorRgba(71, 85, 105, 225));
            skin.DrawSlot(spriteBatch, new RectangleF(sx, sy, SlotSize, SlotSize), active ? new ColorRgba(26, 38, 62, 245) : new ColorRgba(12, 18, 34, 232), border);
        }

        (RectangleF bagRect, RectangleF craftRect, RectangleF forgeRect) = GetDockRects(viewportWidth, viewportHeight);
        skin.DrawButton(
            spriteBatch,
            in bagRect,
            showBag ? new ColorRgba(180, 83, 9, 240) : new ColorRgba(16, 24, 42, 228),
            showBag ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(100, 116, 139, 220));
        skin.DrawButton(
            spriteBatch,
            in craftRect,
            showCraft ? new ColorRgba(180, 83, 9, 240) : new ColorRgba(16, 24, 42, 228),
            showCraft ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(100, 116, 139, 220));
        bool nearStation = world.IsNearForgeStation();
        skin.DrawButton(
            spriteBatch,
            in forgeRect,
            showForge ? new ColorRgba(194, 65, 12, 240) : (nearStation ? new ColorRgba(154, 52, 18, 232) : new ColorRgba(16, 24, 42, 210)),
            showForge || nearStation ? new ColorRgba(251, 146, 60, 255) : new ColorRgba(71, 85, 105, 200));
    }

    public bool TryHitTestHotbar(Vector2 mousePos, int viewportWidth, int viewportHeight, out byte slotIndex)
    {
        slotIndex = 0;
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - SlotSize - 36f;

        if (mousePos.Y < startY - 6f || mousePos.Y > startY + SlotSize)
        {
            return false;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            if (mousePos.X >= sx && mousePos.X <= sx + SlotSize)
            {
                slotIndex = (byte)i;
                return true;
            }
        }

        return false;
    }

    public int DrawGeometry(
        GeometryBatch batch,
        BitmapFont font,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        uint activeItemId,
        Vector2? mouseScreenPos)
    {
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - SlotSize - 36f;

        batch.FillRectangle(new RectangleF(startX - 8f, startY - 8f, totalW + 16f, SlotSize + 16f), new ColorRgba(8, 12, 22, 232));
        batch.DrawRectangle(new RectangleF(startX - 8f, startY - 8f, totalW + 16f, SlotSize + 16f), new ColorRgba(51, 65, 85, 225), 1.5f);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        int hoveredSlot = -1;

        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            bool active = i == world.SelectedHotbarSlot;
            float sy = active ? startY - 4f : startY;
            RectangleF rect = new(sx, sy, SlotSize, SlotSize);
            bool hovered = mouseScreenPos.HasValue &&
                mouseScreenPos.Value.X >= sx && mouseScreenPos.Value.X <= sx + SlotSize &&
                mouseScreenPos.Value.Y >= sy && mouseScreenPos.Value.Y <= sy + SlotSize;
            if (hovered)
            {
                hoveredSlot = i;
            }

            ItemSlot item = slots[i];
            ColorRgba accentBorder = new(71, 85, 105, 225);
            CompiledItemVectorArt? itemArt = null;
            if (!item.IsEmpty && art is not null && art.TryGetItem(item.ItemId, out itemArt))
            {
                accentBorder = new ColorRgba(itemArt.AccentColor.R, itemArt.AccentColor.G, itemArt.AccentColor.B, (byte)(active ? 255 : 175));
            }

            ColorRgba slotFill = active
                ? new ColorRgba(26, 38, 60, 248)
                : (hovered ? new ColorRgba(22, 32, 50, 238) : new ColorRgba(13, 20, 34, 228));
            batch.FillRectangle(rect, slotFill);

            ColorRgba border = active
                ? new ColorRgba(250, 204, 21, 255)
                : (hovered ? ColorRgba.White : accentBorder);
            if (!item.IsEmpty && ToolUsagePolicy.IsWornOut(in item))
            {
                border = new ColorRgba(239, 68, 68, 255);
            }

            batch.DrawRectangle(rect, border, active ? 2.5f : 1.3f);

            if (active)
            {
                batch.DrawRectangle(new RectangleF(sx + 2.5f, sy + 2.5f, SlotSize - 5f, SlotSize - 5f), accentBorder, 1f);
            }

            if (!item.IsEmpty)
            {
                Vector2 iconCenter = new(sx + (SlotSize * 0.5f), sy + (SlotSize * 0.48f));
                if (itemArt is not null)
                {
                    VectorRigRenderer.DrawItemIcon(batch, itemArt, iconCenter, targetSize: 30f);
                }

                if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
                {
                    byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                    float effMaxDur = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(archetype, upg));
                    float frac = Math.Clamp(item.Durability / effMaxDur, 0f, 1f);
                    ColorRgba durColor = frac <= 0f
                        ? new ColorRgba(239, 68, 68, 255)
                        : (frac > 0.5f ? new ColorRgba(56, 189, 248, 240) : (frac > 0.2f ? new ColorRgba(250, 204, 21, 240) : new ColorRgba(239, 68, 68, 245)));
                    batch.FillRectangle(new RectangleF(sx + 5f, sy + SlotSize - 7f, SlotSize - 10f, 3.5f), new ColorRgba(15, 23, 42, 235));
                    batch.FillRectangle(new RectangleF(sx + 5f, sy + SlotSize - 7f, MathF.Max(frac <= 0f ? SlotSize - 10f : 1f, (SlotSize - 10f) * frac), 3.5f), durColor);
                }
            }
        }

        // Hovered Item Tooltip Card above skill bar/hotbar, or compact active-item hint ribbon
        if (hoveredSlot >= 0)
        {
            ItemSlot hItem = slots[hoveredSlot];
            if (!hItem.IsEmpty)
            {
                DrawTooltipGeometry(batch, art, startX + (hoveredSlot * (SlotSize + Gap)), startY - 62f, viewportWidth, in hItem);
            }
        }
        else
        {
            string actionHint = StarterGameCatalog.GetActionHint(activeItemId);
            TextMeasureResult tipSize = font.MeasureString(actionHint, TextLayoutOptions.Default, 1f);
            float tipW = tipSize.Width + 18f;
            float tipX = MathF.Floor((viewportWidth - tipW) * 0.5f);
            batch.FillRectangle(new RectangleF(tipX, startY - 26f, tipW, 15f), new ColorRgba(10, 15, 28, 215));
            batch.DrawRectangle(new RectangleF(tipX, startY - 26f, tipW, 15f), new ColorRgba(71, 85, 105, 215), 1f);
        }

        string hint = ResolveHintText(viewportWidth, out float hintScale);
        TextMeasureResult measure = font.MeasureString(hint, TextLayoutOptions.Default, hintScale);
        float hintX = MathF.Floor((viewportWidth - measure.Width) * 0.5f);
        batch.FillRectangle(new RectangleF(hintX - 8f, viewportHeight - 25f, measure.Width + 16f, 20f), new ColorRgba(10, 15, 28, 210));

        return hoveredSlot;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        uint activeItemId,
        int hoveredSlot)
    {
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - SlotSize - 36f;
        ColorRgba black = new(10, 12, 18, 255);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        for (int i = 0; i < SlotCount; i++)
        {
            float sx = startX + (i * (SlotSize + Gap));
            float sy = i == world.SelectedHotbarSlot ? startY - 4f : startY;
            spriteBatch.DrawOutlinedString(font, $"{i + 1}", new Vector2(sx + 4f, sy + 4f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
            ItemSlot item = slots[i];
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
            if (upg > 0)
            {
                string plusTxt = $"+{upg}";
                ColorRgba plusCol = upg >= 6 ? new ColorRgba(34, 211, 238, 255) : new ColorRgba(250, 204, 21, 255);
                spriteBatch.DrawOutlinedString(font, plusTxt, new Vector2(sx + SlotSize - 18f, sy + 4f), plusCol, black, 1, 1f);
            }

            if (!item.IsEmpty && item.Quantity > 1)
            {
                string qty = $"x{item.Quantity}";
                TextMeasureResult qSize = font.MeasureString(qty, TextLayoutOptions.Default, 2f);
                spriteBatch.DrawOutlinedString(font, qty, new Vector2(MathF.Floor(sx + SlotSize - qSize.Width - 3f), sy + SlotSize - 20f), ColorRgba.White, black, 1, 2f);
            }
        }

        if (hoveredSlot >= 0)
        {
            ItemSlot hItem = slots[hoveredSlot];
            if (!hItem.IsEmpty)
            {
                DrawTooltipText(spriteBatch, font, art, startX + (hoveredSlot * (SlotSize + Gap)), startY - 62f, viewportWidth, in hItem);
            }
        }
        else
        {
            string actionHint = StarterGameCatalog.GetActionHint(activeItemId);
            TextMeasureResult tipSize = font.MeasureString(actionHint, TextLayoutOptions.Default, 1f);
            float tipX = MathF.Floor((viewportWidth - tipSize.Width) * 0.5f);
            spriteBatch.DrawOutlinedString(font, actionHint, new Vector2(tipX, startY - 22f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
        }

        string hint = ResolveHintText(viewportWidth, out float hintScale);
        TextMeasureResult hSize = font.MeasureString(hint, TextLayoutOptions.Default, hintScale);
        float hintX = MathF.Floor((viewportWidth - hSize.Width) * 0.5f);
        spriteBatch.DrawOutlinedString(font, hint, new Vector2(hintX, viewportHeight - 22f), new ColorRgba(226, 232, 240, 255), black, 1, hintScale);

        (RectangleF bagRect, RectangleF craftRect, RectangleF forgeRect) = GetDockRects(viewportWidth, viewportHeight);
        spriteBatch.DrawOutlinedString(font, "[I] BAG", new Vector2(bagRect.X + 8f, bagRect.Y + 7f), ColorRgba.White, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, "[C] CRAFT", new Vector2(craftRect.X + 6f, craftRect.Y + 7f), ColorRgba.White, black, 1, 1f);
        ColorRgba forgeTxtCol = world.IsNearForgeStation() ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(148, 163, 184, 255);
        spriteBatch.DrawOutlinedString(font, "[U] FORGE ANVIL", new Vector2(forgeRect.X + 18f, forgeRect.Y + 7f), forgeTxtCol, black, 1, 1f);
    }

    private static void DrawTooltipGeometry(
        GeometryBatch batch,
        IVectorArtRepository? art,
        float slotX,
        float hotbarTopY,
        int viewportWidth,
        in ItemSlot item)
    {
        const float cardW = 318f;
        const float cardH = 108f;
        float x = Math.Clamp(MathF.Floor(slotX + (SlotSize * 0.5f) - (cardW * 0.5f)), 12f, viewportWidth - cardW - 12f);
        float y = hotbarTopY - cardH - 14f;

        ColorRgba accent = new(148, 163, 184, 255);
        CompiledItemVectorArt? bp = null;
        if (art is not null && art.TryGetItem(item.ItemId, out bp))
        {
            accent = bp.AccentColor;
        }

        batch.FillRectangle(new RectangleF(x, y, cardW, cardH), new ColorRgba(8, 12, 24, 245));
        batch.DrawRectangle(new RectangleF(x, y, cardW, cardH), accent, 1.5f);
        batch.FillRectangle(new RectangleF(x + 2f, y + 2f, cardW - 4f, 2f), new ColorRgba(accent.R, accent.G, accent.B, 90));

        RectangleF iconBox = new(x + 8f, y + 8f, 42f, 42f);
        batch.FillRectangle(iconBox, new ColorRgba(15, 23, 42, 245));
        if (bp is not null)
        {
            VectorRigRenderer.DrawItemIcon(batch, bp, new Vector2(x + 29f, y + 29f), 30f);
        }

        batch.DrawRectangle(iconBox, accent, 1f);

        if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
        {
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
            float effMaxDur = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(archetype, upg));
            float frac = Math.Clamp(item.Durability / effMaxDur, 0f, 1f);
            ColorRgba durFill = frac <= 0f
                ? new ColorRgba(239, 68, 68, 255)
                : (frac > 0.5f ? new ColorRgba(56, 189, 248, 245) : (frac > 0.2f ? new ColorRgba(250, 204, 21, 245) : new ColorRgba(239, 68, 68, 245)));
            RectangleF durBg = new(x + 10f, y + 92f, cardW - 20f, 8f);
            batch.FillRectangle(durBg, new ColorRgba(15, 23, 42, 245));
            batch.FillRectangle(new RectangleF(x + 10f, y + 92f, (cardW - 20f) * frac, 8f), durFill);
            batch.DrawRectangle(durBg, new ColorRgba(51, 65, 85, 220), 1f);
        }
    }

    private static void DrawTooltipText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IVectorArtRepository? art,
        float slotX,
        float hotbarTopY,
        int viewportWidth,
        in ItemSlot item)
    {
        const float cardW = 318f;
        const float cardH = 108f;
        float x = Math.Clamp(MathF.Floor(slotX + (SlotSize * 0.5f) - (cardW * 0.5f)), 12f, viewportWidth - cardW - 12f);
        float y = hotbarTopY - cardH - 14f;
        ColorRgba black = new(10, 12, 18, 255);

        string name = StarterGameCatalog.GetDisplayName(item.ItemId);
        byte tier = 1;
        string cat = "Item";
        ColorRgba titleColor = ColorRgba.White;
        if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? bp))
        {
            name = bp.DisplayName;
            tier = bp.Tier;
            cat = bp.CategoryLabel;
            titleColor = bp.AccentColor;
        }

        byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
        string titleName = upg > 0 ? $"{name} +{upg}" : name;
        spriteBatch.DrawOutlinedString(font, titleName, new Vector2(x + 58f, y + 10f), titleColor, black, 1, 1.5f);
        spriteBatch.DrawOutlinedString(font, $"Tier {tier} | {cat}", new Vector2(x + 58f, y + 29f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);

        float upgMult = ItemEnhancementPolicy.GetStatMultiplier(upg);
        float armorDef = StarterGameCatalog.GetArmorDefense(item.ItemId) * upgMult;
        string stats = armorDef > 0f
            ? $"DEF +{(int)MathF.Round(armorDef)} | +{(int)MathF.Round(StarterGameCatalog.GetArmorBonusHealth(item.ItemId) * upgMult)} HP | +{(int)MathF.Round(StarterGameCatalog.GetArmorBonusMana(item.ItemId) * upgMult)} MP"
            : $"DMG {(int)MathF.Round(StarterGameCatalog.GetWeaponBaseDamage(item.ItemId) * upgMult)} | Chop {(int)MathF.Round(StarterGameCatalog.GetFloraHarvestDamage(item.ItemId) * upgMult)} | Mine {(int)MathF.Round(StarterGameCatalog.GetMineralHarvestDamage(item.ItemId) * upgMult)}";
        spriteBatch.DrawOutlinedString(font, stats, new Vector2(x + 10f, y + 56f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, StarterGameCatalog.GetActionHint(item.ItemId), new Vector2(x + 10f, y + 71f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);

        if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
        {
            float effMaxDur = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(archetype, upg));
            string durLabel = item.Durability <= 0f
                ? $"DURABILITY 0/{(int)effMaxDur} [WORN OUT]"
                : $"DURABILITY {(int)MathF.Ceiling(item.Durability)}/{(int)effMaxDur}";
            ColorRgba durTxtColor = item.Durability <= 0f ? new ColorRgba(252, 165, 165, 255) : ColorRgba.White;
            spriteBatch.DrawOutlinedString(font, durLabel, new Vector2(x + 14f, y + 90f), durTxtColor, black, 1, 1f);
        }
    }

    private static (RectangleF Bag, RectangleF Craft, RectangleF Forge) GetDockRects(int viewportWidth, int viewportHeight)
    {
        const float totalW = (SlotCount * SlotSize) + ((SlotCount - 1) * Gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - SlotSize - 36f;
        float dockX = MathF.Min(viewportWidth - 146f, startX + totalW + 10f);
        return (
            new RectangleF(dockX, startY - 1f, 65f, 23f),
            new RectangleF(dockX + 69f, startY - 1f, 69f, 23f),
            new RectangleF(dockX, startY + 26f, 138f, 23f));
    }

    private static string ResolveHintText(int viewportWidth, out float scale)
    {
        if (viewportWidth >= 1080)
        {
            scale = 1.5f;
            return "[WASD] Move [TAB] Lock [Q/R/V] Skill [E] Loot [I] Bag [C] Craft [U] Forge";
        }

        scale = 1f;
        return "[WASD] Move [TAB] Lock [Q/R/V] Skill [E] Loot [I] Bag [C] Craft [U] Forge";
    }
}
