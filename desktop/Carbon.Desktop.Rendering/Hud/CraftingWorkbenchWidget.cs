// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Left-docked RPG crafting workbench panel with 5 category filter tabs, live bag ingredient counts,
/// hover stat preview, and zero-overlap layout clear of the hotbar and target frames (SRP).
/// </summary>
internal sealed class CraftingWorkbenchWidget
{
    private const float PanelX = 16f;
    private const float PanelY = 142f;
    private const float PanelW = 344f;
    private const float PanelH = 420f;
    private const float TabRowY = PanelY + 30f;
    private const float TabH = 22f;
    private const float ListStartY = PanelY + 58f;
    private const float RowH = 25f;
    private const int MaxVisibleRows = 12;

    private static readonly string[] s_tabLabels = ["ALL", "TOOL", "GEAR", "BLD", "POT"];

    private int _activeTab;
    private int _hoveredRecipeIdx = -1;

    public bool TryHitTestTab(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        _ = viewportWidth;
        _ = viewportHeight;
        if (mousePos.Y < TabRowY || mousePos.Y > TabRowY + TabH || mousePos.X < PanelX + 10f || mousePos.X > PanelX + PanelW - 10f)
        {
            return false;
        }

        float tabW = (PanelW - 24f) / s_tabLabels.Length;
        int idx = (int)((mousePos.X - (PanelX + 10f)) / tabW);
        if (idx >= 0 && idx < s_tabLabels.Length)
        {
            _activeTab = idx;
            return true;
        }

        return false;
    }

    public bool TryHitTestRecipe(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort recipeId)
    {
        _ = viewportWidth;
        _ = viewportHeight;
        recipeId = 0;
        if (!this.ContainsPoint(mousePos, viewportWidth, viewportHeight))
        {
            return false;
        }

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int visible = 0;
        for (int i = 0; i < recipes.Length && visible < MaxVisibleRows; i++)
        {
            if (!MatchesTab(recipes[i], _activeTab))
            {
                continue;
            }

            float ry = ListStartY + (visible * (RowH + 2f));
            if (mousePos.X >= PanelX + 10f && mousePos.X <= PanelX + PanelW - 10f &&
                mousePos.Y >= ry && mousePos.Y < ry + RowH)
            {
                recipeId = (ushort)recipes[i].Id;
                return true;
            }

            visible++;
        }

        return false;
    }

    public bool ContainsPoint(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        _ = viewportWidth;
        _ = viewportHeight;
        return mousePos.X >= PanelX && mousePos.X <= PanelX + PanelW && mousePos.Y >= PanelY && mousePos.Y <= PanelY + PanelH;
    }

    public void DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        _hoveredRecipeIdx = -1;
        if (mouseScreenPos.HasValue && this.TryHitTestRecipe(mouseScreenPos.Value, viewportWidth, viewportHeight, out ushort hovId))
        {
            ReadOnlySpan<CraftingRecipe> all = StarterGameCatalog.Recipes.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Id == hovId)
                {
                    _hoveredRecipeIdx = i;
                    break;
                }
            }
        }

        RectangleF panel = new(PanelX, PanelY, PanelW, PanelH);
        batch.FillRectangle(panel, new ColorRgba(10, 15, 28, 240));
        batch.DrawRectangle(panel, new ColorRgba(250, 204, 21, 225), 1.5f);

        float tabW = (PanelW - 24f) / s_tabLabels.Length;
        for (int t = 0; t < s_tabLabels.Length; t++)
        {
            RectangleF tRect = new(PanelX + 10f + (t * tabW), TabRowY, tabW - 3f, TabH);
            bool active = t == _activeTab;
            batch.FillRectangle(tRect, active ? new ColorRgba(217, 119, 6, 235) : new ColorRgba(18, 28, 48, 235));
            batch.DrawRectangle(tRect, active ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(71, 85, 105, 215), 1f);
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int visible = 0;
        for (int i = 0; i < recipes.Length && visible < MaxVisibleRows; i++)
        {
            if (!MatchesTab(recipes[i], _activeTab))
            {
                continue;
            }

            float ry = ListStartY + (visible * (RowH + 2f));
            bool canCraft = CanCraftFromSpan(slots, recipes[i]);
            bool hovered = i == _hoveredRecipeIdx;
            ColorRgba rowBg = hovered
                ? new ColorRgba(51, 65, 85, 235)
                : (canCraft ? new ColorRgba(20, 83, 45, 145) : new ColorRgba(15, 23, 42, 185));
            batch.FillRectangle(new RectangleF(PanelX + 10f, ry, PanelW - 20f, RowH), rowBg);

            if (art is not null && art.TryGetItem(recipes[i].Output.ItemId, out CompiledItemVectorArt? outArt))
            {
                VectorRigRenderer.DrawItemIcon(batch, outArt, new Vector2(PanelX + 24f, ry + (RowH * 0.5f)), targetSize: 17f);
            }

            visible++;
        }

        // Bottom preview strip for hovered recipe
        batch.FillRectangle(new RectangleF(PanelX + 10f, PanelY + PanelH - 36f, PanelW - 20f, 28f), new ColorRgba(15, 23, 42, 235));
        batch.DrawRectangle(new RectangleF(PanelX + 10f, PanelY + PanelH - 36f, PanelW - 20f, 28f), new ColorRgba(71, 85, 105, 210), 1f);
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, IClientWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        _ = viewportWidth;
        _ = viewportHeight;
        ColorRgba black = new(10, 12, 18, 255);
        spriteBatch.DrawOutlinedString(font, "CRAFTING WORKBENCH (Click or 1-6)", new Vector2(PanelX + 12f, PanelY + 9f), new ColorRgba(250, 204, 21, 255), black, 1, 1.5f);

        float tabW = (PanelW - 24f) / s_tabLabels.Length;
        for (int t = 0; t < s_tabLabels.Length; t++)
        {
            ColorRgba tc = t == _activeTab ? ColorRgba.White : new ColorRgba(148, 163, 184, 255);
            spriteBatch.DrawOutlinedString(font, s_tabLabels[t], new Vector2(PanelX + 18f + (t * tabW), TabRowY + 6f), tc, black, 1, 1f);
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int visible = 0;
        for (int i = 0; i < recipes.Length && visible < MaxVisibleRows; i++)
        {
            CraftingRecipe recipe = recipes[i];
            if (!MatchesTab(recipe, _activeTab))
            {
                continue;
            }

            float ry = ListStartY + (visible * (RowH + 2f));
            bool canCraft = CanCraftFromSpan(slots, recipe);
            string outName = StarterGameCatalog.GetDisplayName(recipe.Output.ItemId);
            string firstIng = recipe.Ingredients.Count > 0
                ? $"{CountBagItem(slots, recipe.Ingredients[0].ItemId)}/{recipe.Ingredients[0].Quantity} {StarterGameCatalog.GetDisplayName(recipe.Ingredients[0].ItemId)}"
                : string.Empty;
            string secondIng = recipe.Ingredients.Count > 1
                ? $" + {CountBagItem(slots, recipe.Ingredients[1].ItemId)}/{recipe.Ingredients[1].Quantity}"
                : string.Empty;

            ColorRgba rowColor = canCraft ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 165, 165, 255);
            string tag = canCraft ? "[CRAFT]" : "[--]";
            string line = $"{outName} x{recipe.Output.Quantity}: {firstIng}{secondIng} {tag}";
            spriteBatch.DrawOutlinedString(font, line, new Vector2(PanelX + 38f, ry + 7f), rowColor, black, 1, 1f);
            visible++;
        }

        string preview = _hoveredRecipeIdx >= 0 && _hoveredRecipeIdx < recipes.Length
            ? StarterGameCatalog.GetActionHint(recipes[_hoveredRecipeIdx].Output.ItemId)
            : "Hover a recipe to inspect stats; click row to craft";
        spriteBatch.DrawOutlinedString(font, preview, new Vector2(PanelX + 16f, PanelY + PanelH - 26f), new ColorRgba(226, 232, 240, 255), black, 1, 1f);
    }

    private static bool MatchesTab(CraftingRecipe recipe, int tab)
    {
        if (tab == 0)
        {
            return true;
        }

        if (!StarterGameCatalog.Items.TryGet(recipe.Output.ItemId, out ItemArchetype? def))
        {
            return false;
        }

        return tab switch
        {
            1 => def.Category is ItemCategory.Tool or ItemCategory.Weapon,
            2 => def.Category == ItemCategory.Armor,
            3 => def.Category == ItemCategory.Placeable,
            4 => def.Category == ItemCategory.Consumable,
            _ => true,
        };
    }

    private static int CountBagItem(ReadOnlySpan<ItemSlot> slots, uint itemId)
    {
        int total = 0;
        int bagCount = Math.Min(PlayerInventoryLayout.BagLimit, slots.Length);
        for (int i = 0; i < bagCount; i++)
        {
            if (slots[i].ItemId == itemId)
            {
                total += slots[i].Quantity;
            }
        }

        return total;
    }

    private static bool CanCraftFromSpan(ReadOnlySpan<ItemSlot> slots, CraftingRecipe recipe)
    {
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            if (CountBagItem(slots, recipe.Ingredients[i].ItemId) < recipe.Ingredients[i].Quantity)
            {
                return false;
            }
        }

        return true;
    }
}
