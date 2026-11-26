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
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Full two-pane MMORPG Crafting Workshop (<c>[C]</c>) skinned with <see cref="CarbonUiSkin"/>,
/// featuring 6 category tabs, craftable-only filter, scrollable blueprint cards, live stat inspection,
/// visual ingredient requirement boxes, and <c>[CRAFT x1]</c> / <c>[CRAFT x5]</c> execution (SRP).
/// </summary>
internal sealed class CraftingModalWidget
{
    private const float PanelW = 744f;
    private const float PanelH = 480f;
    private const int MaxVisibleCards = 6;
    private const float CardRowH = 54f;

    private static readonly string[] s_tabLabels = ["ALL", "WEAPONS", "ARMOR", "TOOLS", "BUILD", "POTIONS"];

    private int _activeTab;
    private int _scrollOffset;
    private ushort _selectedRecipeId = 1;
    private bool _craftableOnly;

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

    public void Scroll(int delta) => _scrollOffset = Math.Max(0, _scrollOffset + delta);

    public bool TryHandleClick(
        Vector2 mousePos,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        out ushort craftRecipeId,
        out ushort craftQuantity)
    {
        craftRecipeId = 0;
        craftQuantity = 0;

        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        if (!this.ContainsPoint(mousePos, viewportWidth, viewportHeight))
        {
            return false;
        }

        // Category tabs
        float tabY = oy + 42f;
        float tabW = 90f;
        if (mousePos.Y >= tabY && mousePos.Y <= tabY + 26f)
        {
            for (int t = 0; t < s_tabLabels.Length; t++)
            {
                RectangleF tRect = new(ox + 18f + (t * (tabW + 5f)), tabY, tabW, 26f);
                if (tRect.Contains(mousePos))
                {
                    _activeTab = t;
                    _scrollOffset = 0;
                    return true;
                }
            }

            RectangleF filterRect = GetCraftableFilterRect(ox, oy);
            if (filterRect.Contains(mousePos))
            {
                _craftableOnly = !_craftableOnly;
                _scrollOffset = 0;
                return true;
            }
        }

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

        if (GetCraft1ButtonRect(ox, oy).Contains(mousePos))
        {
            if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? rec) && CanCraftFromSpan(slots, rec))
            {
                craftRecipeId = _selectedRecipeId;
                craftQuantity = 1;
            }

            return true;
        }

        if (GetCraft5ButtonRect(ox, oy).Contains(mousePos))
        {
            if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? rec) && CanCraftFromSpan(slots, rec, quantity: 5))
            {
                craftRecipeId = _selectedRecipeId;
                craftQuantity = 5;
            }

            return true;
        }

        // Recipe rows
        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int matchIdx = 0;
        int shown = 0;
        for (int i = 0; i < recipes.Length && shown < MaxVisibleCards; i++)
        {
            CraftingRecipe rec = recipes[i];
            if (!this.MatchesFilter(slots, rec))
            {
                continue;
            }

            if (matchIdx < _scrollOffset)
            {
                matchIdx++;
                continue;
            }

            RectangleF rowRect = new(ox + 26f, oy + 84f + (shown * (CardRowH + 4f)), 322f, CardRowH);
            if (rowRect.Contains(mousePos))
            {
                _selectedRecipeId = (ushort)rec.Id;
                return true;
            }

            matchIdx++;
            shown++;
        }

        return true;
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

        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, PanelW, PanelH), new ColorRgba(250, 204, 21, 238), bodyAlpha: 242);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 16f, oy + 76f, 356f, 386f), new ColorRgba(71, 85, 105, 225), bodyAlpha: 232);
        skin.DrawCard(spriteBatch, new RectangleF(ox + 382f, oy + 76f, 346f, 386f), new ColorRgba(56, 189, 248, 205), bodyAlpha: 232);

        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        bool hoverClose = mouseScreenPos.HasValue && closeBtn.Contains(mouseScreenPos.Value);
        skin.DrawButton(spriteBatch, in closeBtn, hoverClose ? new ColorRgba(220, 38, 38, 245) : new ColorRgba(153, 27, 27, 230), new ColorRgba(248, 113, 113, 240));

        float tabY = oy + 42f;
        float tabW = 90f;
        for (int t = 0; t < s_tabLabels.Length; t++)
        {
            RectangleF tRect = new(ox + 18f + (t * (tabW + 5f)), tabY, tabW, 26f);
            bool active = t == _activeTab;
            skin.DrawButton(
                spriteBatch,
                in tRect,
                active ? new ColorRgba(180, 83, 9, 240) : new ColorRgba(18, 28, 48, 228),
                active ? new ColorRgba(253, 224, 71, 255) : new ColorRgba(71, 85, 105, 220));
        }

        RectangleF filterRect = GetCraftableFilterRect(ox, oy);
        skin.DrawButton(
            spriteBatch,
            in filterRect,
            _craftableOnly ? new ColorRgba(5, 150, 105, 235) : new ColorRgba(18, 28, 48, 228),
            _craftableOnly ? new ColorRgba(110, 231, 183, 255) : new ColorRgba(71, 85, 105, 220));

        RectangleF upBtn = GetScrollUpRect(ox, oy);
        RectangleF dnBtn = GetScrollDownRect(ox, oy);
        skin.DrawButton(spriteBatch, in upBtn, new ColorRgba(20, 30, 52, 230), new ColorRgba(100, 116, 139, 220));
        skin.DrawButton(spriteBatch, in dnBtn, new ColorRgba(20, 30, 52, 230), new ColorRgba(100, 116, 139, 220));

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int totalMatches = this.CountMatches(slots, recipes);
        int maxScroll = Math.Max(0, totalMatches - MaxVisibleCards);
        if (_scrollOffset > maxScroll)
        {
            _scrollOffset = maxScroll;
        }

        int matchIdx = 0;
        int shown = 0;
        for (int i = 0; i < recipes.Length && shown < MaxVisibleCards; i++)
        {
            CraftingRecipe rec = recipes[i];
            if (!this.MatchesFilter(slots, rec))
            {
                continue;
            }

            if (matchIdx < _scrollOffset)
            {
                matchIdx++;
                continue;
            }

            bool sel = rec.Id == _selectedRecipeId;
            bool canCraft = CanCraftFromSpan(slots, rec);
            RectangleF rowRect = new(ox + 26f, oy + 84f + (shown * (CardRowH + 4f)), 322f, CardRowH);
            ColorRgba rowFill = sel
                ? new ColorRgba(34, 50, 82, 245)
                : (canCraft ? new ColorRgba(14, 48, 36, 225) : new ColorRgba(12, 18, 34, 225));
            ColorRgba rowBorder = sel
                ? new ColorRgba(250, 204, 21, 255)
                : (canCraft ? new ColorRgba(52, 211, 153, 215) : new ColorRgba(71, 85, 105, 205));
            skin.DrawSlot(spriteBatch, in rowRect, rowFill, rowBorder);

            RectangleF iconSocket = new(rowRect.X + 7f, rowRect.Y + 7f, 40f, 40f);
            skin.DrawSlot(spriteBatch, in iconSocket, new ColorRgba(10, 15, 28, 242), rowBorder);

            matchIdx++;
            shown++;
        }

        // Right detail icon socket & ingredient sockets
        RectangleF outIconBox = new(ox + 398f, oy + 92f, 56f, 56f);
        skin.DrawSlot(spriteBatch, in outIconBox, new ColorRgba(12, 18, 34, 245), new ColorRgba(250, 204, 21, 235));

        if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? selectedRec))
        {
            for (int k = 0; k < selectedRec.Ingredients.Count && k < 3; k++)
            {
                RecipeIngredient ing = selectedRec.Ingredients[k];
                bool hasEnough = CountBagItem(slots, ing.ItemId) >= ing.Quantity;
                RectangleF ingCard = new(ox + 398f, oy + 244f + (k * 46f), 314f, 40f);
                skin.DrawSlot(
                    spriteBatch,
                    in ingCard,
                    hasEnough ? new ColorRgba(14, 44, 34, 228) : new ColorRgba(42, 18, 24, 228),
                    hasEnough ? new ColorRgba(52, 211, 153, 220) : new ColorRgba(248, 113, 113, 220));
            }

            bool ready = CanCraftFromSpan(slots, selectedRec);
            bool ready5 = CanCraftFromSpan(slots, selectedRec, quantity: 5);
            RectangleF c1 = GetCraft1ButtonRect(ox, oy);
            RectangleF c5 = GetCraft5ButtonRect(ox, oy);
            skin.DrawButton(
                spriteBatch,
                in c1,
                ready ? new ColorRgba(5, 150, 105, 240) : new ColorRgba(24, 34, 52, 215),
                ready ? new ColorRgba(110, 231, 183, 250) : new ColorRgba(71, 85, 105, 205));
            skin.DrawButton(
                spriteBatch,
                in c5,
                ready5 ? new ColorRgba(180, 83, 9, 240) : new ColorRgba(24, 34, 52, 215),
                ready5 ? new ColorRgba(253, 224, 71, 250) : new ColorRgba(71, 85, 105, 205));
        }
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

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int matchIdx = 0;
        int shown = 0;
        for (int i = 0; i < recipes.Length && shown < MaxVisibleCards; i++)
        {
            CraftingRecipe rec = recipes[i];
            if (!this.MatchesFilter(slots, rec))
            {
                continue;
            }

            if (matchIdx < _scrollOffset)
            {
                matchIdx++;
                continue;
            }

            RectangleF rowRect = new(ox + 26f, oy + 84f + (shown * (CardRowH + 4f)), 322f, CardRowH);
            if (art is not null && art.TryGetItem(rec.Output.ItemId, out CompiledItemVectorArt? rowArt))
            {
                VectorRigRenderer.DrawItemIcon(batch, rowArt, new Vector2(rowRect.X + 27f, rowRect.Y + 27f), 26f);
            }

            matchIdx++;
            shown++;
        }

        if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? selectedRec) && art is not null)
        {
            if (art.TryGetItem(selectedRec.Output.ItemId, out CompiledItemVectorArt? outArt))
            {
                VectorRigRenderer.DrawItemIcon(batch, outArt, new Vector2(ox + 426f, oy + 120f), 36f);
            }

            for (int k = 0; k < selectedRec.Ingredients.Count && k < 3; k++)
            {
                if (art.TryGetItem(selectedRec.Ingredients[k].ItemId, out CompiledItemVectorArt? ingArt))
                {
                    VectorRigRenderer.DrawItemIcon(batch, ingArt, new Vector2(ox + 420f, oy + 264f + (k * 46f)), 24f);
                }
            }
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

        spriteBatch.DrawOutlinedString(font, "CRAFTING WORKSHOP [C]", new Vector2(ox + 18f, oy + 14f), new ColorRgba(250, 204, 21, 255), black, 1, 1.5f);
        RectangleF closeBtn = GetCloseButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "X", new Vector2(closeBtn.X + 8f, closeBtn.Y + 4f), ColorRgba.White, black, 1, 1.5f);

        float tabY = oy + 42f;
        float tabW = 90f;
        for (int t = 0; t < s_tabLabels.Length; t++)
        {
            ColorRgba tc = t == _activeTab ? new ColorRgba(253, 224, 71, 255) : ColorRgba.White;
            TextMeasureResult m = font.MeasureString(s_tabLabels[t], TextLayoutOptions.Default, 1f);
            float tx = ox + 18f + (t * (tabW + 5f)) + ((tabW - m.Width) * 0.5f);
            spriteBatch.DrawOutlinedString(font, s_tabLabels[t], new Vector2(MathF.Floor(tx), tabY + 8f), tc, black, 1, 1f);
        }

        RectangleF filterRect = GetCraftableFilterRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, _craftableOnly ? "READY: ON" : "READY: ALL", new Vector2(filterRect.X + 12f, filterRect.Y + 8f), ColorRgba.White, black, 1, 1f);

        RectangleF upBtn = GetScrollUpRect(ox, oy);
        RectangleF dnBtn = GetScrollDownRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "^", new Vector2(upBtn.X + 6f, upBtn.Y + 5f), ColorRgba.White, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, "v", new Vector2(dnBtn.X + 6f, dnBtn.Y + 5f), ColorRgba.White, black, 1, 1f);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        int matchIdx = 0;
        int shown = 0;
        for (int i = 0; i < recipes.Length && shown < MaxVisibleCards; i++)
        {
            CraftingRecipe rec = recipes[i];
            if (!this.MatchesFilter(slots, rec))
            {
                continue;
            }

            if (matchIdx < _scrollOffset)
            {
                matchIdx++;
                continue;
            }

            RectangleF rowRect = new(ox + 26f, oy + 84f + (shown * (CardRowH + 4f)), 322f, CardRowH);
            bool canCraft = CanCraftFromSpan(slots, rec);
            string outName = StarterGameCatalog.GetDisplayName(rec.Output.ItemId);
            ColorRgba rowTitleCol = canCraft ? ColorRgba.White : new ColorRgba(203, 213, 225, 255);
            spriteBatch.DrawOutlinedString(font, $"{outName} x{rec.Output.Quantity}", new Vector2(rowRect.X + 54f, rowRect.Y + 10f), rowTitleCol, black, 1, 1.5f);

            string summaryTag = FormatRecipeShortTag(rec.Output.ItemId);
            spriteBatch.DrawOutlinedString(font, summaryTag, new Vector2(rowRect.X + 54f, rowRect.Y + 32f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);

            string statusTag = canCraft ? "READY" : "NEED MATS";
            ColorRgba statusCol = canCraft ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 113, 113, 255);
            spriteBatch.DrawOutlinedString(font, statusTag, new Vector2(rowRect.Right - (canCraft ? 46f : 72f), rowRect.Y + 12f), statusCol, black, 1, 1f);

            matchIdx++;
            shown++;
        }

        if (shown == 0)
        {
            spriteBatch.DrawOutlinedString(font, "No matching recipes", new Vector2(ox + 92f, oy + 220f), new ColorRgba(148, 163, 184, 255), black, 1, 1.5f);
        }

        if (StarterGameCatalog.Recipes.TryGet(_selectedRecipeId, out CraftingRecipe? selectedRec))
        {
            uint outId = selectedRec.Output.ItemId;
            int owned = CountBagItem(slots, outId);
            spriteBatch.DrawOutlinedString(font, $"{StarterGameCatalog.GetDisplayName(outId)} x{selectedRec.Output.Quantity}", new Vector2(ox + 464f, oy + 96f), new ColorRgba(250, 204, 21, 255), black, 1, 2f);
            spriteBatch.DrawOutlinedString(font, $"Owned in Bag: {owned}", new Vector2(ox + 464f, oy + 124f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);

            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, FormatDetailedStats(outId), 316f, 1f), new Vector2(ox + 398f, oy + 162f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);
            spriteBatch.DrawOutlinedString(font, CarbonUiSkin.TruncateToWidth(font, StarterGameCatalog.GetActionHint(outId), 316f, 1f), new Vector2(ox + 398f, oy + 182f), new ColorRgba(226, 232, 240, 255), black, 1, 1f);

            spriteBatch.DrawOutlinedString(font, "REQUIRED INGREDIENTS", new Vector2(ox + 398f, oy + 224f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
            for (int k = 0; k < selectedRec.Ingredients.Count && k < 3; k++)
            {
                RecipeIngredient ing = selectedRec.Ingredients[k];
                int have = CountBagItem(slots, ing.ItemId);
                bool ok = have >= ing.Quantity;
                float iy = oy + 256f + (k * 46f);
                spriteBatch.DrawOutlinedString(font, StarterGameCatalog.GetDisplayName(ing.ItemId), new Vector2(ox + 442f, iy), ColorRgba.White, black, 1, 1.5f);
                spriteBatch.DrawOutlinedString(font, $"{have} / {ing.Quantity}", new Vector2(ox + 648f, iy), ok ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(248, 113, 113, 255), black, 1, 1.5f);
            }

            RectangleF c1 = GetCraft1ButtonRect(ox, oy);
            RectangleF c5 = GetCraft5ButtonRect(ox, oy);
            spriteBatch.DrawOutlinedString(font, "CRAFT x1", new Vector2(c1.X + 42f, c1.Y + 12f), ColorRgba.White, black, 1, 1.5f);
            spriteBatch.DrawOutlinedString(font, "CRAFT x5", new Vector2(c5.X + 36f, c5.Y + 12f), ColorRgba.White, black, 1, 1.5f);
        }
    }

    private int CountMatches(ReadOnlySpan<ItemSlot> slots, ReadOnlySpan<CraftingRecipe> recipes)
    {
        int count = 0;
        for (int i = 0; i < recipes.Length; i++)
        {
            if (this.MatchesFilter(slots, recipes[i]))
            {
                count++;
            }
        }

        return count;
    }

    private bool MatchesFilter(ReadOnlySpan<ItemSlot> slots, CraftingRecipe recipe)
    {
        if (_craftableOnly && !CanCraftFromSpan(slots, recipe))
        {
            return false;
        }

        if (_activeTab == 0)
        {
            return true;
        }

        if (!StarterGameCatalog.Items.TryGet(recipe.Output.ItemId, out ItemArchetype? def))
        {
            return false;
        }

        return _activeTab switch
        {
            1 => def.Category == ItemCategory.Weapon,
            2 => def.Category == ItemCategory.Armor,
            3 => def.Category == ItemCategory.Tool,
            4 => def.Category == ItemCategory.Placeable,
            5 => def.Category == ItemCategory.Consumable,
            _ => true,
        };
    }

    private static string FormatRecipeShortTag(uint itemId) =>
        StarterGameCatalog.Items.TryGet(itemId, out ItemArchetype? def)
            ? def.Category switch
            {
                ItemCategory.Weapon => $"Weapon | DMG {(int)def.BaseDamage} | RNG {(int)def.AttackRange}",
                ItemCategory.Armor => $"Armor | DEF +{(int)def.ArmorDefense} | HP +{(int)def.BonusHealth}",
                ItemCategory.Tool => $"Tool | Harvest Tier {def.HarvestTier}",
                ItemCategory.Placeable => "Buildable Structure / Station",
                ItemCategory.Consumable => "Consumable Ration / Tonic",
                ItemCategory.Accessory => "Accessory | Stat Charm",
                ItemCategory.Quest => "Quest Relic",
                ItemCategory.Resource or ItemCategory.None or _ => "Material",
            }
            : string.Empty;

    private static string FormatDetailedStats(uint itemId) =>
        StarterGameCatalog.Items.TryGet(itemId, out ItemArchetype? def)
            ? def.Category switch
            {
                ItemCategory.Weapon => $"DMG {(int)def.BaseDamage} | Crit {(int)(def.CritChance * 100f)}% | RNG {(int)def.AttackRange} | MP {(int)def.AttackManaCost}",
                ItemCategory.Armor => $"DEF +{(int)def.ArmorDefense} | MaxHP +{(int)def.BonusHealth} | MaxMP +{(int)def.BonusMana} | Dur {(int)def.MaxDurability}",
                ItemCategory.Tool => $"Tier {def.HarvestTier} | Flora {(int)def.FloraHarvestDamage} | Mine {(int)def.MineralHarvestDamage}",
                ItemCategory.Consumable or ItemCategory.Placeable or ItemCategory.Accessory or ItemCategory.Resource or ItemCategory.Quest or ItemCategory.None or _ => $"Stack Limit {def.MaxStackSize}",
            }
            : string.Empty;

    private static int CountBagItem(ReadOnlySpan<ItemSlot> slots, uint itemId)
    {
        int total = 0;
        int bagCount = Math.Min(PlayerInventoryLayout.BagLimit, slots.Length);
        for (int i = 0; i < bagCount; i++)
        {
            if (slots[i].ItemId == itemId && slots[i].UpgradeLevel == 0)
            {
                total += slots[i].Quantity;
            }
        }

        return total;
    }

    private static bool CanCraftFromSpan(ReadOnlySpan<ItemSlot> slots, CraftingRecipe recipe, int quantity = 1)
    {
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            RecipeIngredient ing = recipe.Ingredients[i];
            if (CountBagItem(slots, ing.ItemId) < ing.Quantity * quantity)
            {
                return false;
            }
        }

        return true;
    }

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight) =>
        (MathF.Floor((viewportWidth - PanelW) * 0.5f), MathF.Floor((viewportHeight - PanelH) * 0.5f));

    private static RectangleF GetCloseButtonRect(float ox, float oy) => new(ox + PanelW - 34f, oy + 10f, 24f, 24f);

    private static RectangleF GetCraftableFilterRect(float ox, float oy) => new(ox + 602f, oy + 42f, 124f, 26f);

    private static RectangleF GetScrollUpRect(float ox, float oy) => new(ox + 350f, oy + 84f, 18f, 22f);

    private static RectangleF GetScrollDownRect(float ox, float oy) => new(ox + 350f, oy + 402f, 18f, 22f);

    private static RectangleF GetCraft1ButtonRect(float ox, float oy) => new(ox + 398f, oy + 404f, 156f, 38f);

    private static RectangleF GetCraft5ButtonRect(float ox, float oy) => new(ox + 562f, oy + 404f, 150f, 38f);
}
