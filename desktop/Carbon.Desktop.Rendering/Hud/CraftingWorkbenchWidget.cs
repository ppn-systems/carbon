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

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Toggleable RPG crafting workbench panel rendering vector output icons, live ingredient progress,
/// hover highlights, and hit-testing for recipes <c>1..6</c> (SRP).
/// </summary>
internal sealed class CraftingWorkbenchWidget
{
    private const float PanelW = 560f;
    private const float PanelH = 210f;

    public bool TryHitTestRecipe(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort recipeId)
    {
        recipeId = 0;
        float x = MathF.Floor((viewportWidth - PanelW) * 0.5f);
        float y = viewportHeight - PanelH - 102f;

        if (mousePos.X < x + 12f || mousePos.X > x + PanelW - 12f)
        {
            return false;
        }

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        for (int i = 0; i < recipes.Length; i++)
        {
            float rowTop = y + 36f + (i * 26f);
            if (mousePos.Y >= rowTop && mousePos.Y < rowTop + 26f)
            {
                recipeId = (ushort)recipes[i].Id;
                return true;
            }
        }

        return false;
    }

    public bool ContainsPoint(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        float x = MathF.Floor((viewportWidth - PanelW) * 0.5f);
        float y = viewportHeight - PanelH - 102f;
        return mousePos.X >= x && mousePos.X <= x + PanelW && mousePos.Y >= y && mousePos.Y <= y + PanelH;
    }

    public void DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos)
    {
        int hoveredIdx = -1;
        if (mouseScreenPos.HasValue && this.TryHitTestRecipe(mouseScreenPos.Value, viewportWidth, viewportHeight, out ushort hovId))
        {
            ReadOnlySpan<CraftingRecipe> all = StarterGameCatalog.Recipes.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Id == hovId)
                {
                    hoveredIdx = i;
                    break;
                }
            }
        }

        float x = MathF.Floor((viewportWidth - PanelW) * 0.5f);
        float y = viewportHeight - PanelH - 102f;
        RectangleF panel = new(x, y, PanelW, PanelH);
        batch.FillRectangle(panel, new ColorRgba(10, 15, 28, 238));
        batch.DrawRectangle(panel, new ColorRgba(250, 204, 21, 230), 1.5f);

        InventoryContainer inventory = world.Inventory;
        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        for (int i = 0; i < recipes.Length; i++)
        {
            float rowY = y + 36f + (i * 26f);
            bool canCraft = RecipeMatcher.CanCraft(inventory, recipes[i]);
            bool hovered = i == hoveredIdx;
            ColorRgba rowBg = hovered
                ? new ColorRgba(51, 65, 85, 215)
                : (canCraft ? new ColorRgba(20, 83, 45, 115) : new ColorRgba(15, 23, 42, 165));
            batch.FillRectangle(new RectangleF(x + 12f, rowY, PanelW - 24f, 24f), rowBg);

            if (art is not null && art.TryGetItem(recipes[i].Output.ItemId, out CompiledItemVectorArt? outArt))
            {
                VectorRigRenderer.DrawItemIcon(batch, outArt, new Vector2(x + 28f, rowY + 12f), targetSize: 18f);
            }
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, InventoryContainer inventory, int viewportWidth, int viewportHeight)
    {
        float x = MathF.Floor((viewportWidth - PanelW) * 0.5f);
        float y = viewportHeight - PanelH - 102f;
        ColorRgba black = new(10, 12, 18, 255);

        spriteBatch.DrawOutlinedString(font, "CRAFTING WORKBENCH (Click or 1-6)", new Vector2(x + 16f, y + 12f), new ColorRgba(250, 204, 21, 255), black, 1, 2f);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        for (int i = 0; i < recipes.Length; i++)
        {
            CraftingRecipe recipe = recipes[i];
            bool canCraft = RecipeMatcher.CanCraft(inventory, recipe);
            string outName = StarterGameCatalog.GetDisplayName(recipe.Output.ItemId);
            string firstIng = recipe.Ingredients.Count > 0
                ? $"{inventory.CountItem(recipe.Ingredients[0].ItemId)}/{recipe.Ingredients[0].Quantity} {StarterGameCatalog.GetDisplayName(recipe.Ingredients[0].ItemId)}"
                : string.Empty;
            string secondIng = recipe.Ingredients.Count > 1
                ? $" + {inventory.CountItem(recipe.Ingredients[1].ItemId)}/{recipe.Ingredients[1].Quantity} {StarterGameCatalog.GetDisplayName(recipe.Ingredients[1].ItemId)}"
                : string.Empty;

            ColorRgba rowColor = canCraft ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 165, 165, 255);
            string tag = canCraft ? "[CRAFT]" : "[NEED]";
            string line = $"[{recipe.Id}] {outName} x{recipe.Output.Quantity}: {firstIng}{secondIng} {tag}";
            spriteBatch.DrawOutlinedString(font, line, new Vector2(x + 48f, y + 44f + (i * 26f)), rowColor, black, 1, 1f);
        }
    }
}
