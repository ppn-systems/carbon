// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Repair;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>What a click in the repair window did.</summary>
public enum RepairClick : byte
{
    /// <summary>The click missed the window's controls.</summary>
    None = 0,

    /// <summary>The close button was hit.</summary>
    Close = 1,

    /// <summary>An item row or a scroll arrow was hit and handled.</summary>
    Consumed = 2,

    /// <summary>The full-repair button was hit.</summary>
    RepairFull = 3,

    /// <summary>The partial-repair button was hit.</summary>
    RepairPartial = 4,
}

/// <summary>
/// Draws the repair window (ADR-028) from its <see cref="RepairWindowState"/>: pick ONE damaged item from the equipment or
/// the bag, see its durability now and after, the restore amount and every requirement (materials, the blacksmith's fee,
/// the station tier) from the synced <c>repair.json</c>, with missing ones in red, then repair it fully or as far as the
/// bag pays. While the server works the anvil stage plays <see cref="RepairVfx"/>. The server checks and spends everything.
/// </summary>
public sealed class RepairModalWidget
{
    private const string AnvilItemCode = "forge_anvil";

    private static readonly ColorRgba s_barBack = new(10, 14, 22, 235);
    private static readonly ColorRgba s_barNow = new(234, 179, 8, 255);
    private static readonly ColorRgba s_barGain = new(74, 222, 128, 150);
    private static readonly ColorRgba s_barFlash = new(240, 253, 244, 255);

    /// <summary>Gets whether <paramref name="mouse"/> is inside the window.</summary>
    public static bool ContainsPoint(Vector2 mouse, int viewportWidth, int viewportHeight)
    {
        (float ox, float oy) = RepairLayout.Origin(viewportWidth, viewportHeight);
        return new RectangleF(ox, oy, RepairLayout.PanelW, RepairLayout.PanelH).Contains(mouse);
    }

    /// <summary>Draws the window in three passes (frames, art, text).</summary>
    public void Render(
        SpriteBatch spriteBatch,
        GeometryBatch geometryBatch,
        CarbonUiSkin skin,
        BitmapFont font,
        IVectorArtRepository? art,
        Matrix4x4 screenProj,
        IWorldReadModel world,
        RepairWindowState state,
        int viewportWidth,
        int viewportHeight,
        Vector2? mouse)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(geometryBatch);
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(world);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        Span<RepairRequirement> rows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];
        RepairPlan full = state.BuildPlan(world, slots, rows, partial: false);
        if (state.TryGetHeldPlan(rows, out RepairPlan held))
        {
            full = held;
        }

        Span<RepairRequirement> partialRows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];
        RepairPlan partial = state.BuildPlan(world, slots, partialRows, partial: true);
        (float ox, float oy) = RepairLayout.Origin(viewportWidth, viewportHeight);

        geometryBatch.Begin(screenProj);
        geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 6, 12, 138));
        geometryBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        this.DrawSkin(state, spriteBatch, skin, in full, in partial, ox, oy, mouse);
        spriteBatch.End();

        geometryBatch.Begin(screenProj);
        this.DrawGeometry(state, geometryBatch, world, art, slots, in full, rows[..full.RequirementCount], ox, oy);
        geometryBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        this.DrawText(state, spriteBatch, font, world, slots, in full, rows[..full.RequirementCount], in partial, ox, oy);
        spriteBatch.End();
    }

    private void DrawSkin(RepairWindowState state, SpriteBatch spriteBatch, CarbonUiSkin skin, in RepairPlan full, in RepairPlan partial, float ox, float oy, Vector2? mouse)
    {
        ColorRgba frame = state.Vfx.IsPlayingSuccess ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.BorderOuter;
        skin.DrawWindow(spriteBatch, new RectangleF(ox, oy, RepairLayout.PanelW, RepairLayout.PanelH), frame, bodyAlpha: 246);
        skin.DrawCard(spriteBatch, RepairLayout.ListCard(ox, oy), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);
        skin.DrawCard(spriteBatch, RepairLayout.DetailCard(ox, oy), CarbonUiPalette.BorderSubtle, bodyAlpha: 236);

        RectangleF close = RepairLayout.CloseRect(ox, oy);
        bool hoverClose = mouse.HasValue && close.Contains(mouse.Value);
        skin.DrawButton(spriteBatch, in close, hoverClose ? new ColorRgba(158, 46, 52, 245) : CarbonUiPalette.DangerFill, CarbonUiPalette.DangerBorder);

        if (state.Damaged.Count > RepairLayout.MaxRows)
        {
            skin.DrawButton(spriteBatch, RepairLayout.ScrollUpRect(ox, oy), CarbonUiPalette.CardFill, CarbonUiPalette.BorderSubtle);
            skin.DrawButton(spriteBatch, RepairLayout.ScrollDownRect(ox, oy), CarbonUiPalette.CardFill, CarbonUiPalette.BorderSubtle);
        }

        for (int i = 0; i < RepairLayout.MaxRows && state.ScrollOffset + i < state.Damaged.Count; i++)
        {
            bool selected = state.Damaged[state.ScrollOffset + i] == full.SlotIndex;
            RectangleF row = RepairLayout.ItemRow(ox, oy, i);
            bool hover = !selected && mouse.HasValue && row.Contains(mouse.Value);
            skin.DrawSlot(
                spriteBatch,
                in row,
                selected ? CarbonUiPalette.SlotSelectedFill : hover ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill,
                selected ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
        }

        skin.DrawSlot(spriteBatch, RepairLayout.StageRect(ox, oy), CarbonUiPalette.SlotFill, CarbonUiPalette.BorderOuter);
        bool idle = !state.Vfx.IsActive;
        DrawActionButton(spriteBatch, skin, RepairLayout.FullButtonRect(ox, oy), idle && full.HasItem && full.CanRepair, mouse);
        DrawActionButton(spriteBatch, skin, RepairLayout.PartialButtonRect(ox, oy), idle && partial.IsPartial && partial.CanRepair, mouse);
    }

    private static void DrawActionButton(SpriteBatch spriteBatch, CarbonUiSkin skin, RectangleF rect, bool enabled, Vector2? mouse)
    {
        bool hover = enabled && mouse.HasValue && rect.Contains(mouse.Value);
        skin.DrawButton(
            spriteBatch,
            in rect,
            enabled ? (hover ? new ColorRgba(92, 80, 52, 245) : new ColorRgba(62, 54, 38, 240)) : CarbonUiPalette.SlotFill,
            enabled ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle);
    }

    private void DrawGeometry(
        RepairWindowState state,
        GeometryBatch batch,
        IWorldReadModel world,
        IVectorArtRepository? art,
        ReadOnlySpan<ItemSlot> slots,
        in RepairPlan plan,
        ReadOnlySpan<RepairRequirement> rows,
        float ox,
        float oy)
    {
        if (art is not null && art.TryGetUiIcon("repair", out CompiledItemVectorArt? titleIcon))
        {
            VectorRigRenderer.DrawItemIcon(batch, titleIcon, new Vector2(ox + 30f, oy + 22f), 20f);
        }

        for (int i = 0; i < RepairLayout.MaxRows && state.ScrollOffset + i < state.Damaged.Count; i++)
        {
            ItemSlot item = slots[state.Damaged[state.ScrollOffset + i]];
            RectangleF row = RepairLayout.ItemRow(ox, oy, i);
            if (art is not null && art.TryGetItem(item.ItemId, out CompiledItemVectorArt? icon))
            {
                VectorRigRenderer.DrawItemIcon(batch, icon, new Vector2(row.X + 22f, row.Y + (row.Height * 0.5f)), 26f);
            }

            float max = MaxDurabilityOf(world.Items.Rules.Repair, in item);
            float share = max > 0f ? Math.Clamp(item.Durability / max, 0f, 1f) : 0f;
            RectangleF mini = new(row.X + 46f, row.Y + row.Height - 4f, 150f, 3f);
            batch.FillRectangle(mini, s_barBack);
            batch.FillRectangle(new RectangleF(mini.X, mini.Y, mini.Width * share, mini.Height), DurabilityColor(share));
        }

        RectangleF stage = RepairLayout.StageRect(ox, oy);
        Vector2 center = new(stage.X + (stage.Width * 0.5f), stage.Y + 40f);
        if (art is not null &&
            world.Items.Rules.ItemCatalog.TryGetByCode(AnvilItemCode, out ItemCatalogEntry? anvilItem) &&
            art.TryGetItem(anvilItem.Id, out CompiledItemVectorArt? anvilArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, anvilArt, new Vector2(center.X, stage.Bottom - 22f), 60f);
        }

        state.Vfx.DrawBehind(batch, center, stage);
        if (plan.HasItem && art is not null && art.TryGetItem(plan.ItemId, out CompiledItemVectorArt? itemArt))
        {
            VectorRigRenderer.DrawItemIcon(batch, itemArt, center, 38f);
        }

        state.Vfx.DrawFront(batch, center);
        if (!plan.HasItem)
        {
            return;
        }

        // The durability bar: now (gold), the planned gain (green ghost), animated while the hammer works.
        RepairQuote quote = plan.Quote;
        RectangleF bar = RepairLayout.DurabilityBar(ox, oy);
        float now = quote.Durability / quote.MaxDurability;
        float shown = state.Vfx.GetBarFraction(now);
        batch.FillRectangle(bar, s_barBack);
        if (!state.Vfx.IsActive)
        {
            batch.FillRectangle(new RectangleF(bar.X, bar.Y, bar.Width * (quote.NewDurability / quote.MaxDurability), bar.Height), s_barGain);
        }

        batch.FillRectangle(new RectangleF(bar.X, bar.Y, bar.Width * shown, bar.Height), DurabilityColor(shown));
        float flash = state.Vfx.FlashStrength;
        if (flash > 0f)
        {
            batch.FillRectangle(bar, s_barFlash with { A = (byte)(200 * flash) });
        }

        CompiledItemVectorArt? coin = null;
        _ = art?.TryGetUiIcon("coin", out coin);
        for (int i = 0; i < rows.Length; i++)
        {
            Vector2 at = new(ox + RepairLayout.RequirementX + 10f, oy + RepairLayout.RequirementY + 8f + (i * RepairLayout.RequirementH));
            CompiledItemVectorArt? icon = null;
            if (rows[i].Kind == RepairRequirementKind.Gold)
            {
                icon = coin;
            }
            else if (art is not null)
            {
                uint iconItem = rows[i].Kind == RepairRequirementKind.Material ? rows[i].ItemId
                    : world.Items.Rules.ItemCatalog.TryGetByCode(AnvilItemCode, out ItemCatalogEntry? kit) ? kit.Id
                    : 0U;
                _ = art.TryGetItem(iconItem, out icon);
            }

            if (icon is not null)
            {
                VectorRigRenderer.DrawItemIcon(batch, icon, at, 16f);
            }
        }
    }

    private void DrawText(
        RepairWindowState state,
        SpriteBatch spriteBatch,
        BitmapFont font,
        IWorldReadModel world,
        ReadOnlySpan<ItemSlot> slots,
        in RepairPlan plan,
        ReadOnlySpan<RepairRequirement> rows,
        in RepairPlan partial,
        float ox,
        float oy)
    {
        ColorRgba black = CarbonUiPalette.TextOutline;
        RepairTable table = world.Items.Rules.Repair;
        spriteBatch.DrawOutlinedString(font, GameText.Get("repair.title"), new Vector2(ox + 46f, oy + 13f), CarbonUiPalette.TextWarm, black, 1, 1f);
        int tier = world.GetRepairStationTier(state.Venue);
        string venue = state.Venue == RepairVenue.Blacksmith
            ? GameText.Get(tier > 0 ? "repair.venue.blacksmith" : "repair.venue.no_blacksmith")
            : GameText.Get(VenueKey(table, tier));
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, venue, 300f, 1f),
            new Vector2(ox + 170f, oy + 14f),
            tier > 0 ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
            black,
            1,
            1f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, "X", RepairLayout.CloseRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);

        spriteBatch.DrawOutlinedString(font, GameText.Format("repair.list_count", new TextArg("count", state.Damaged.Count)), new Vector2(ox + 26f, oy + 52f), CarbonUiPalette.TextMuted, black, 1, 1f);
        if (state.Damaged.Count > RepairLayout.MaxRows)
        {
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "^", RepairLayout.ScrollUpRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
            CarbonUiSkin.DrawCenteredText(spriteBatch, font, "v", RepairLayout.ScrollDownRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        }

        for (int i = 0; i < RepairLayout.MaxRows && state.ScrollOffset + i < state.Damaged.Count; i++)
        {
            ushort s = state.Damaged[state.ScrollOffset + i];
            ItemSlot item = slots[s];
            RectangleF row = RepairLayout.ItemRow(ox, oy, i);
            float max = MaxDurabilityOf(world.Items.Rules.Repair, in item);
            string name = CarbonUiSkin.TruncateToWidth(font, world.Items.GetDisplayName(item.ItemId), 150f, 1f);
            spriteBatch.DrawOutlinedString(font, name, new Vector2(row.X + 46f, row.Y + 5f), CarbonUiPalette.TextPrimary, black, 1, 1f);
            string tag = s >= PlayerInventoryLayout.BagLimit ? GameText.Get("repair.tag.equipped")
                : s < PlayerInventoryLayout.HotbarCount ? GameText.Format("repair.tag.slot", new TextArg("slot", s + 1))
                : GameText.Get("repair.tag.bag");
            spriteBatch.DrawOutlinedString(font, $"{tag}  {Whole(item.Durability)}/{Whole(max)}", new Vector2(row.X + 46f, row.Y + 21f), CarbonUiPalette.TextMuted, black, 1, 1f);
        }

        if (state.Damaged.Count == 0)
        {
            spriteBatch.DrawOutlinedString(font, GameText.Get("repair.none_damaged"), new Vector2(ox + 26f, oy + 82f), CarbonUiPalette.TextMuted, black, 1, 1f);
        }

        if (!plan.HasItem)
        {
            spriteBatch.DrawOutlinedString(font, GameText.Get("repair.select"), new Vector2(ox + RepairLayout.InfoX, oy + 66f), CarbonUiPalette.TextMuted, black, 1, 1f);
            return;
        }

        RepairQuote quote = plan.Quote;
        spriteBatch.DrawOutlinedString(
            font,
            CarbonUiSkin.TruncateToWidth(font, world.Items.GetDisplayName(plan.ItemId), 210f, 1.25f),
            new Vector2(ox + RepairLayout.InfoX, oy + 58f),
            CarbonUiPalette.TextPrimary,
            black,
            1,
            1.25f);
        spriteBatch.DrawOutlinedString(
            font,
            GameText.Format("repair.durability", new TextArg("now", Whole(quote.Durability)), new TextArg("max", Whole(quote.MaxDurability))),
            new Vector2(ox + RepairLayout.InfoX, oy + 86f),
            CarbonUiPalette.TextMuted,
            black,
            1,
            1f);
        spriteBatch.DrawOutlinedString(
            font,
            state.Vfx.IsPlayingSuccess
                ? GameText.Get("repair.done")
                : GameText.Format(
                    "repair.restores",
                    new TextArg("amount", Whole(quote.RestoredDurability)),
                    new TextArg("after", Whole(quote.NewDurability))),
            new Vector2(ox + RepairLayout.InfoX, oy + 132f),
            state.Vfx.IsPlayingSuccess ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.TextWarm,
            black,
            1,
            1f);

        spriteBatch.DrawOutlinedString(font, GameText.Get("repair.requirements"), new Vector2(ox + RepairLayout.RequirementX, oy + RepairLayout.RequirementY - 24f), CarbonUiPalette.TextWarm, black, 1, 1f);
        for (int i = 0; i < rows.Length; i++)
        {
            RepairRequirement row = rows[i];
            string text = row.Kind switch
            {
                RepairRequirementKind.Material => GameText.Format(
                    "repair.material",
                    new TextArg("name", world.Items.GetDisplayName(row.ItemId)),
                    new TextArg("have", row.Have),
                    new TextArg("need", row.Need)),
                RepairRequirementKind.Gold => GameText.Format("repair.fee", new TextArg("gold", row.Need), new TextArg("have", row.Have)),
                RepairRequirementKind.Station => row.IsMet
                    ? GameText.Format("repair.station.ok", new TextArg("station", GameText.Get(StationKey(table, (int)row.Need))))
                    : GameText.Format(
                        "repair.station.low",
                        new TextArg("station", GameText.Get(StationKey(table, (int)row.Need))),
                        new TextArg("here", GameText.Get(StationKey(table, (int)row.Have)))),
                _ => string.Empty,
            };
            spriteBatch.DrawOutlinedString(
                font,
                CarbonUiSkin.TruncateToWidth(font, text, 310f, 1f),
                new Vector2(ox + RepairLayout.RequirementX + 26f, oy + RepairLayout.RequirementY + (i * RepairLayout.RequirementH)),
                row.IsMet ? CarbonUiPalette.ConfirmBorder : CarbonUiPalette.DangerBorder,
                black,
                1,
                1f);
        }

        string fullLabel = state.Vfx.IsWorking ? GameText.Get("repair.button.working") : GameText.Get("repair.button.full");
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, fullLabel, RepairLayout.FullButtonRect(ox, oy), CarbonUiPalette.TextPrimary, black, 1f);
        string partialLabel = partial.IsPartial
            ? GameText.Format("repair.button.partial", new TextArg("steps", partial.Quote.Steps), new TextArg("total", partial.Quote.StepsNeeded))
            : GameText.Get("repair.button.partial_none");
        CarbonUiSkin.DrawCenteredText(
            spriteBatch,
            font,
            CarbonUiSkin.TruncateToWidth(font, partialLabel, RepairLayout.ButtonW - 10f, 1f),
            RepairLayout.PartialButtonRect(ox, oy),
            partial.IsPartial ? CarbonUiPalette.TextPrimary : CarbonUiPalette.TextMuted,
            black,
            1f);
    }

    // The name of a station tier: blacksmith, forge anvil or workbench (repair.json → stations).
    private static string StationKey(RepairTable table, int tier) =>
        tier <= 0 ? "repair.station.none"
            : tier >= table.BlacksmithTier ? "repair.station.blacksmith"
            : tier >= table.ForgeAnvilTier ? "repair.station.anvil"
            : "repair.station.workbench";

    // The window header at a player station: which one is in reach.
    private static string VenueKey(RepairTable table, int tier) =>
        tier <= 0 ? "repair.venue.none"
            : tier >= table.ForgeAnvilTier ? "repair.venue.anvil"
            : "repair.venue.workbench";

    // The effective maximum as the synced repair table quotes it (enhancement included).
    private static float MaxDurabilityOf(RepairTable table, in ItemSlot item) =>
        table.TryQuote(in item, 1, RepairVenue.Station, out RepairQuote quote) == RepairError.None ? quote.MaxDurability : 0f;

    private static long Whole(float value) => (long)MathF.Round(value);

    private static ColorRgba DurabilityColor(float share) =>
        share < 0.25f ? new ColorRgba(239, 68, 68, 255) : share < 0.75f ? s_barNow : new ColorRgba(74, 222, 128, 255);
}
