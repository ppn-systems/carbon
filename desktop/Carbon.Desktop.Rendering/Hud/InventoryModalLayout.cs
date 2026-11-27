// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Screen rectangles of the inventory modal: stage card, paper doll, bag / hotbar / equipment slots, the "Held" mirror
/// and the Sort button. Pure functions of <see cref="PlayerModalLayout"/>, shared by drawing and hit testing. Extracted
/// from <see cref="InventoryModalWidget"/> (audit round 2, SRP); behaviour is unchanged.
/// </summary>
internal static class InventoryModalLayout
{
    internal const float BaseSlotSize = 70f;
    internal const float BaseSlotGap = 8f;

    internal static RectangleF GetStageCardRect(in PlayerModalLayout layout) =>
        new(layout.ContentX, layout.ContentY, layout.ContentW, MathF.Round(172f * layout.Scale));

    internal static RectangleF GetPaperDollRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        float dollW = MathF.Round(264f * s);
        float dollH = MathF.Round(156f * s);
        return new RectangleF(
            layout.Ox + MathF.Floor((layout.PanelW - dollW) * 0.5f),
            layout.ContentY + MathF.Round(8f * s),
            dollW,
            dollH);
    }

    internal static RectangleF GetSlotRect(in PlayerModalLayout layout, int slotIndex)
    {
        float s = layout.Scale;
        float slotSize = MathF.Round(BaseSlotSize * s);
        float slotGap = MathF.Round(BaseSlotGap * s);

        if (slotIndex >= PlayerInventoryLayout.BagLimit)
        {
            RectangleF doll = GetPaperDollRect(in layout);
            float armorGapX = MathF.Round(22f * s);
            float leftX = doll.X - armorGapX - slotSize;
            float rightX = doll.Right + armorGapX;
            float topY = layout.ContentY + MathF.Round(10f * s);
            float botY = topY + slotSize + MathF.Round(12f * s);
            float outerRightX = rightX + slotSize + MathF.Round(12f * s);
            int armorIdx = slotIndex - PlayerInventoryLayout.BagLimit;
            return armorIdx switch
            {
                0 => new RectangleF(leftX, topY, slotSize, slotSize),
                1 => new RectangleF(leftX, botY, slotSize, slotSize),
                2 => new RectangleF(rightX, topY, slotSize, slotSize),
                3 => new RectangleF(rightX, botY, slotSize, slotSize),
                4 => new RectangleF(outerRightX, topY, slotSize, slotSize),
                _ => new RectangleF(outerRightX, botY, slotSize, slotSize),
            };
        }

        float bagStartY = layout.ContentY + MathF.Round(172f * s) + MathF.Round(12f * s);
        if (slotIndex < 9)
        {
            float hotbarY = bagStartY + (3f * slotSize) + (2f * slotGap) + MathF.Round(22f * s);
            return new RectangleF(layout.ContentX + (slotIndex * (slotSize + slotGap)), hotbarY, slotSize, slotSize);
        }

        int bagIdx = slotIndex - 9;
        int col = bagIdx % 9;
        int row = bagIdx / 9;
        return new RectangleF(
            layout.ContentX + (col * (slotSize + slotGap)),
            bagStartY + (row * (slotSize + slotGap)),
            slotSize,
            slotSize);
    }

    /// <summary>Read-only mirror of the selected hotbar item, left of the helmet (the weapon is what the hotbar holds).</summary>
    internal static RectangleF GetHeldSlotRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF head = GetSlotRect(in layout, PlayerInventoryLayout.HeadSlot);
        return new RectangleF(head.X - head.Width - MathF.Round(12f * s), head.Y, head.Width, head.Height);
    }

    internal static RectangleF GetSortButtonRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF held = GetHeldSlotRect(in layout);
        RectangleF chest = GetSlotRect(in layout, PlayerInventoryLayout.ChestSlot);
        float h = MathF.Round(34f * s);
        return new RectangleF(held.X, chest.Bottom - h, held.Width, h);
    }
}
