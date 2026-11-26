// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Animation.Directions;
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
/// Centered 40-slot Minecraft-style Inventory, Armor Paper-Doll (<c>36..39</c>), Live Character Preview,
/// and Forge Enhancement Anvil (<c>+0..+10</c>) modal toggled via <c>[I]</c> (SRP).
/// </summary>
internal sealed class InventoryForgeModalWidget
{
    private const float PanelW = 684f;
    private const float PanelH = 468f;
    private const float SlotSize = 44f;
    private const float SlotGap = 6f;

    public int SelectedSlot { get; set; } = -1;

    public bool ContainsPoint(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return mousePos.X >= ox && mousePos.X <= ox + PanelW && mousePos.Y >= oy && mousePos.Y <= oy + PanelH;
    }

    public bool TryHitTestSlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity; s++)
        {
            if (GetSlotRect(ox, oy, s).Contains(mousePos))
            {
                slotIndex = (ushort)s;
                return true;
            }
        }

        slotIndex = 0;
        return false;
    }

    public bool TryHitTestForgeButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetForgeButtonRect(ox, oy).Contains(mousePos);
    }

    public bool TryHitTestDropButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        return GetDropButtonRect(ox, oy).Contains(mousePos);
    }

    public int ResolveActionTargetSlot(IClientWorldReadModel world)
    {
        if (this.SelectedSlot >= 0 &&
            this.SelectedSlot < world.Inventory.Capacity &&
            !world.Inventory.GetSlot(this.SelectedSlot).IsEmpty)
        {
            return this.SelectedSlot;
        }

        if (!world.Inventory.GetSlot(world.SelectedHotbarSlot).IsEmpty)
        {
            return world.SelectedHotbarSlot;
        }

        return -1;
    }

    public int DrawGeometry(
        GeometryBatch batch,
        IClientWorldReadModel world,
        IVectorArtRepository? art,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouseScreenPos,
        float animTime)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        InventoryContainer inv = world.Inventory;

        // Dim scrim + beveled obsidian modal frame
        batch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 8, 16, 130));
        batch.FillRectangle(new RectangleF(ox, oy, PanelW, PanelH), new ColorRgba(10, 15, 28, 245));
        batch.DrawRectangle(new RectangleF(ox, oy, PanelW, PanelH), new ColorRgba(250, 204, 21, 225), 2f);
        batch.FillRectangle(new RectangleF(ox + 2f, oy + 2f, PanelW - 4f, 30f), new ColorRgba(15, 23, 42, 248));

        // Left pedestal card (Armor slots + Animated Character Preview)
        RectangleF paperCard = new(ox + 16f, oy + 38f, 236f, 198f);
        batch.FillRectangle(paperCard, new ColorRgba(14, 22, 40, 240));
        batch.DrawRectangle(paperCard, new ColorRgba(51, 65, 85, 225), 1.3f);

        if (art is not null && art.TryGetCharacter(world.LocalPlayerAppearancePreset, out CompiledVectorRig? heroRig))
        {
            ItemSlot activeItem = inv.GetSlot(world.SelectedHotbarSlot);
            CompiledItemVectorArt? weaponArt = null;
            if (!activeItem.IsEmpty)
            {
                _ = art.TryGetItem(activeItem.ItemId, out weaponArt);
            }

            VisualEquipmentLoadout loadout = new(
                PlayerInventoryLayout.HeadSlot < inv.Capacity ? (ushort)inv.GetSlot(PlayerInventoryLayout.HeadSlot).ItemId : (ushort)0,
                PlayerInventoryLayout.ChestSlot < inv.Capacity ? (ushort)inv.GetSlot(PlayerInventoryLayout.ChestSlot).ItemId : (ushort)0,
                PlayerInventoryLayout.LegsSlot < inv.Capacity ? (ushort)inv.GetSlot(PlayerInventoryLayout.LegsSlot).ItemId : (ushort)0,
                PlayerInventoryLayout.FeetSlot < inv.Capacity ? (ushort)inv.GetSlot(PlayerInventoryLayout.FeetSlot).ItemId : (ushort)0,
                (ushort)activeItem.ItemId,
                ItemEnhancementPolicy.GetUpgradeLevel(in activeItem));

            Vector2 pedPos = new(ox + 158f, oy + 186f);
            batch.FillCircle(pedPos + new Vector2(0f, -6f), 26f, new ColorRgba(4, 8, 16, 125), 16);
            batch.DrawCircle(pedPos + new Vector2(0f, -6f), 27f, new ColorRgba(56, 189, 248, 165), 18, 1.5f);
            VectorRigRenderer.DrawEntityRig(
                batch,
                heroRig,
                pedPos,
                Direction8.South,
                "idle",
                animTime,
                isNormalized: false,
                targetHeightOverride: 76f,
                hitFlash: 0f,
                equippedItem: weaponArt,
                action: default,
                legacySwingTimer: 0f,
                appearancePreset: world.LocalPlayerAppearancePreset,
                equipment: loadout);
        }

        // Right Forge Enhancement Anvil card
        RectangleF forgeCard = new(ox + 262f, oy + 38f, 406f, 198f);
        batch.FillRectangle(forgeCard, new ColorRgba(14, 22, 40, 240));
        batch.DrawRectangle(forgeCard, new ColorRgba(56, 189, 248, 190), 1.3f);

        int forgeTargetSlot = this.ResolveActionTargetSlot(world);
        ItemSlot forgeItem = forgeTargetSlot >= 0 ? inv.GetSlot(forgeTargetSlot) : ItemSlot.Empty;
        RectangleF targetBox = new(ox + 278f, oy + 68f, 54f, 54f);
        batch.FillRectangle(targetBox, new ColorRgba(18, 28, 50, 248));
        batch.DrawRectangle(targetBox, forgeItem.IsEmpty ? new ColorRgba(71, 85, 105, 220) : new ColorRgba(250, 204, 21, 240), 1.8f);
        if (!forgeItem.IsEmpty && art is not null && art.TryGetItem(forgeItem.ItemId, out CompiledItemVectorArt? forgeArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, forgeArt, new Vector2(targetBox.X + 27f, targetBox.Y + 27f), 34f);
        }

        bool canForge = !forgeItem.IsEmpty &&
            ItemEnhancementPolicy.CanUpgrade(in forgeItem, out uint m1, out int c1, out uint m2, out int c2, out _) &&
            inv.CountItem(m1) >= c1 &&
            (m2 == 0U || inv.CountItem(m2) >= c2);

        RectangleF forgeBtn = GetForgeButtonRect(ox, oy);
        bool hoverForge = mouseScreenPos.HasValue && forgeBtn.Contains(mouseScreenPos.Value);
        batch.FillRectangle(forgeBtn, canForge ? (hoverForge ? new ColorRgba(16, 185, 129, 255) : new ColorRgba(5, 150, 105, 245)) : new ColorRgba(30, 41, 59, 220));
        batch.DrawRectangle(forgeBtn, canForge ? new ColorRgba(110, 231, 183, 255) : new ColorRgba(71, 85, 105, 220), 1.6f);

        RectangleF dropBtn = GetDropButtonRect(ox, oy);
        bool canDrop = !forgeItem.IsEmpty;
        bool hoverDrop = mouseScreenPos.HasValue && dropBtn.Contains(mouseScreenPos.Value);
        batch.FillRectangle(dropBtn, canDrop ? (hoverDrop ? new ColorRgba(220, 38, 38, 255) : new ColorRgba(153, 27, 27, 240)) : new ColorRgba(30, 41, 59, 220));
        batch.DrawRectangle(dropBtn, canDrop ? new ColorRgba(248, 113, 113, 255) : new ColorRgba(71, 85, 105, 220), 1.6f);

        // Draw all 40 slots (0..8 hotbar, 9..35 backpack, 36..39 armor)
        int hoveredSlot = -1;
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < inv.Capacity; s++)
        {
            RectangleF rect = GetSlotRect(ox, oy, s);
            bool hovered = mouseScreenPos.HasValue && rect.Contains(mouseScreenPos.Value);
            if (hovered)
            {
                hoveredSlot = s;
            }

            bool selected = s == this.SelectedSlot;
            bool isArmor = s >= PlayerInventoryLayout.BagLimit;
            ColorRgba bg = selected
                ? new ColorRgba(45, 58, 92, 250)
                : (hovered ? new ColorRgba(26, 38, 62, 245) : (isArmor ? new ColorRgba(18, 28, 48, 240) : new ColorRgba(14, 21, 36, 238)));
            batch.FillRectangle(rect, bg);

            ItemSlot item = inv.GetSlot(s);
            CompiledItemVectorArt? itemArt = null;
            ColorRgba border = selected
                ? new ColorRgba(250, 204, 21, 255)
                : (hovered ? ColorRgba.White : (isArmor ? new ColorRgba(148, 163, 184, 210) : new ColorRgba(60, 74, 96, 220)));

            if (!item.IsEmpty && art is not null && art.TryGetItem(item.ItemId, out itemArt) && !selected && !hovered)
            {
                border = new ColorRgba(itemArt.AccentColor.R, itemArt.AccentColor.G, itemArt.AccentColor.B, 185);
            }

            batch.DrawRectangle(rect, border, selected ? 2.2f : 1.3f);
            if (!item.IsEmpty)
            {
                if (itemArt is not null)
                {
                    VectorRigRenderer.DrawItemIcon(batch, itemArt, new Vector2(rect.X + (SlotSize * 0.5f), rect.Y + (SlotSize * 0.46f)), 28f);
                }

                if (StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? def) && def.MaxDurability > 0f)
                {
                    byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
                    float effMax = MathF.Max(1f, ItemEnhancementPolicy.GetEffectiveMaxDurability(def, upg));
                    float frac = Math.Clamp(item.Durability / effMax, 0f, 1f);
                    ColorRgba dCol = frac <= 0f
                        ? new ColorRgba(239, 68, 68, 255)
                        : (frac > 0.5f ? new ColorRgba(56, 189, 248, 240) : new ColorRgba(250, 204, 21, 245));
                    batch.FillRectangle(new RectangleF(rect.X + 4f, rect.Y + SlotSize - 6f, SlotSize - 8f, 3f), new ColorRgba(8, 12, 20, 235));
                    batch.FillRectangle(new RectangleF(rect.X + 4f, rect.Y + SlotSize - 6f, MathF.Max(1f, (SlotSize - 8f) * frac), 3f), dCol);
                }
            }
        }

        return hoveredSlot;
    }

    public void DrawText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        int hoveredSlot)
    {
        (float ox, float oy) = ComputeOrigin(viewportWidth, viewportHeight);
        ColorRgba black = new(8, 10, 16, 255);
        InventoryContainer inv = world.Inventory;

        spriteBatch.DrawOutlinedString(font, "INVENTORY / ARMOR / FORGE", new Vector2(ox + 16f, oy + 9f), new ColorRgba(250, 204, 21, 255), black, 1, 2f);
        spriteBatch.DrawOutlinedString(font, "LMB: Select/Swap  RMB: Quick Equip  [I/ESC]: Close", new Vector2(ox + 324f, oy + 11f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);

        // Pedestal stats summary
        spriteBatch.DrawOutlinedString(font, $"LV.{world.LocalPlayerLevel}  DEF {(int)MathF.Round(world.LocalPlayerDefense)}", new Vector2(ox + 118f, oy + 46f), ColorRgba.White, black, 1, 1.5f);
        spriteBatch.DrawOutlinedString(font, $"HP {(int)MathF.Ceiling(world.LocalPlayerHealth)}/{(int)MathF.Ceiling(world.LocalPlayerMaxHealth)}", new Vector2(ox + 102f, oy + 196f), new ColorRgba(252, 165, 165, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"MP {(int)MathF.Floor(world.LocalPlayerMana)}/{(int)MathF.Ceiling(world.LocalPlayerMaxMana)}", new Vector2(ox + 102f, oy + 212f), new ColorRgba(125, 211, 252, 255), black, 1, 1f);

        // Armor slot placeholder tags
        string[] armorLabels = ["HEAD", "CHEST", "LEGS", "FEET"];
        for (int a = 0; a < 4; a++)
        {
            int slotIdx = PlayerInventoryLayout.HeadSlot + a;
            if (slotIdx < inv.Capacity && inv.GetSlot(slotIdx).IsEmpty)
            {
                RectangleF r = GetSlotRect(ox, oy, slotIdx);
                spriteBatch.DrawOutlinedString(font, armorLabels[a], new Vector2(r.X + 4f, r.Y + 16f), new ColorRgba(148, 163, 184, 200), black, 1, 1f);
            }
        }

        // Forge Panel text
        spriteBatch.DrawOutlinedString(font, "ENHANCEMENT ANVIL (+1..+10)", new Vector2(ox + 278f, oy + 46f), new ColorRgba(125, 211, 252, 255), black, 1, 1.5f);
        int forgeTargetSlot = this.ResolveActionTargetSlot(world);
        ItemSlot forgeItem = forgeTargetSlot >= 0 ? inv.GetSlot(forgeTargetSlot) : ItemSlot.Empty;
        if (forgeItem.IsEmpty)
        {
            spriteBatch.DrawOutlinedString(font, "Click gear below to Forge or Drop", new Vector2(ox + 344f, oy + 86f), new ColorRgba(148, 163, 184, 255), black, 1, 1.5f);
        }
        else
        {
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in forgeItem);
            string itemName = StarterGameCatalog.GetDisplayName(forgeItem.ItemId);
            spriteBatch.DrawOutlinedString(font, $"{itemName} (+{upg})", new Vector2(ox + 344f, oy + 68f), ColorRgba.White, black, 1, 1.5f);

            if (ItemEnhancementPolicy.CanUpgrade(in forgeItem, out uint m1, out int c1, out uint m2, out int c2, out float chance))
            {
                spriteBatch.DrawOutlinedString(font, $"Next: +{upg + 1} (+12% Stats) | Rate {(int)(chance * 100f)}%", new Vector2(ox + 344f, oy + 88f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
                string req1 = $"{StarterGameCatalog.GetDisplayName(m1)}: {inv.CountItem(m1)}/{c1}";
                string req2 = m2 != 0U ? $"  |  {StarterGameCatalog.GetDisplayName(m2)}: {inv.CountItem(m2)}/{c2}" : string.Empty;
                spriteBatch.DrawOutlinedString(font, req1 + req2, new Vector2(ox + 278f, oy + 138f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
            }
            else
            {
                string maxMsg = upg >= ItemEnhancementPolicy.MaxUpgradeLevel
                    ? "MAX ENHANCEMENT REACHED (+10)"
                    : "Material / Consumable (Not Forgable)";
                spriteBatch.DrawOutlinedString(font, maxMsg, new Vector2(ox + 344f, oy + 92f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
            }
        }

        RectangleF forgeBtn = GetForgeButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "FORGE +1", new Vector2(forgeBtn.X + 34f, forgeBtn.Y + 9f), ColorRgba.White, black, 1, 1.5f);
        RectangleF dropBtn = GetDropButtonRect(ox, oy);
        spriteBatch.DrawOutlinedString(font, "DROP x1", new Vector2(dropBtn.X + 20f, dropBtn.Y + 9f), ColorRgba.White, black, 1, 1.5f);

        // Slot quantities & +upgrade badges for all 40 slots
        for (int s = 0; s < PlayerInventoryLayout.TotalCapacity && s < inv.Capacity; s++)
        {
            ItemSlot item = inv.GetSlot(s);
            if (item.IsEmpty)
            {
                continue;
            }

            RectangleF rect = GetSlotRect(ox, oy, s);
            byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in item);
            if (upg > 0)
            {
                ColorRgba pCol = upg >= 6 ? new ColorRgba(34, 211, 238, 255) : new ColorRgba(250, 204, 21, 255);
                spriteBatch.DrawOutlinedString(font, $"+{upg}", new Vector2(rect.X + SlotSize - 17f, rect.Y + 3f), pCol, black, 1, 1f);
            }

            if (item.Quantity > 1)
            {
                string qty = $"x{item.Quantity}";
                TextMeasureResult qSize = font.MeasureString(qty, TextLayoutOptions.Default, 1.5f);
                spriteBatch.DrawOutlinedString(font, qty, new Vector2(MathF.Floor(rect.X + SlotSize - qSize.Width - 3f), rect.Y + SlotSize - 17f), ColorRgba.White, black, 1, 1.5f);
            }
        }

        if (hoveredSlot >= 0 && hoveredSlot < inv.Capacity)
        {
            ItemSlot hItem = inv.GetSlot(hoveredSlot);
            if (!hItem.IsEmpty)
            {
                byte upg = ItemEnhancementPolicy.GetUpgradeLevel(in hItem);
                string hLabel = upg > 0 ? $"{StarterGameCatalog.GetDisplayName(hItem.ItemId)} +{upg}" : StarterGameCatalog.GetDisplayName(hItem.ItemId);
                string hint = StarterGameCatalog.GetActionHint(hItem.ItemId);
                spriteBatch.DrawOutlinedString(font, $"{hLabel} — {hint}", new Vector2(ox + 18f, oy + PanelH - 18f), new ColorRgba(253, 224, 71, 255), black, 1, 1f);
            }
        }
    }

    private static (float Ox, float Oy) ComputeOrigin(int viewportWidth, int viewportHeight)
    {
        float ox = MathF.Floor((viewportWidth - PanelW) * 0.5f);
        float oy = MathF.Max(14f, MathF.Floor(((viewportHeight - PanelH) * 0.5f) - 16f));
        return (ox, oy);
    }

    private static RectangleF GetForgeButtonRect(float ox, float oy) => new(ox + 510f, oy + 190f, 144f, 34f);

    private static RectangleF GetDropButtonRect(float ox, float oy) => new(ox + 392f, oy + 190f, 108f, 34f);

    private static RectangleF GetSlotRect(float ox, float oy, int slotIndex)
    {
        if (slotIndex >= PlayerInventoryLayout.HeadSlot && slotIndex <= PlayerInventoryLayout.FeetSlot)
        {
            int row = slotIndex - PlayerInventoryLayout.HeadSlot;
            return new RectangleF(ox + 26f, oy + 44f + (row * 47f), SlotSize, SlotSize);
        }

        float gridX = ox + 114f;
        if (slotIndex >= PlayerInventoryLayout.BackpackStart && slotIndex < PlayerInventoryLayout.BagLimit)
        {
            int idx = slotIndex - PlayerInventoryLayout.BackpackStart;
            int col = idx % 9;
            int row = idx / 9;
            return new RectangleF(gridX + (col * (SlotSize + SlotGap)), oy + 244f + (row * (SlotSize + SlotGap)), SlotSize, SlotSize);
        }

        int hCol = Math.Clamp(slotIndex, 0, 8);
        return new RectangleF(gridX + (hCol * (SlotSize + SlotGap)), oy + 402f, SlotSize, SlotSize);
    }
}
