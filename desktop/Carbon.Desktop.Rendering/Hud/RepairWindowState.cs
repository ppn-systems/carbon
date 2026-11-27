// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Repair;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// The repair window's state and rules (ADR-028; split from <see cref="RepairModalWidget"/>, which only draws it): which
/// venue it is open for, the damaged items listed and the selected one, the scroll, the click handling, and the repair
/// request with its anvil animation. Needs no GL context; the scene owns it through <see cref="HudUiState.Repair"/>.
/// </summary>
public sealed class RepairWindowState
{
    private readonly List<ushort> _damaged = new(PlayerInventoryLayout.TotalCapacity);
    private readonly RepairRequirement[] _heldRows = new RepairRequirement[RepairPlan.MaxRequirements];
    private RepairPlan _heldPlan;
    private int _selectedSlot = -1;
    private int _scrollOffset;

    /// <summary>Gets the venue the window is open for, or <see cref="RepairVenue.None"/> when it is closed.</summary>
    public RepairVenue Venue { get; private set; }

    /// <summary>Gets whether the window is open.</summary>
    public bool IsOpen => this.Venue != RepairVenue.None;

    /// <summary>Gets the hammer-on-anvil animation.</summary>
    public RepairVfx Vfx { get; } = new();

    /// <summary>Opens the window for <paramref name="venue"/>.</summary>
    /// <param name="venue">Station or blacksmith.</param>
    public void Open(RepairVenue venue)
    {
        this.Venue = venue;
        _scrollOffset = 0;
    }

    /// <summary>Closes the window.</summary>
    public void Close() => this.Venue = RepairVenue.None;

    /// <summary>Advances the animation and picks up the server's answer.</summary>
    /// <param name="dt">Frame seconds.</param>
    /// <param name="repair">The repair session.</param>
    public void Update(float dt, IRepairSession repair)
    {
        ArgumentNullException.ThrowIfNull(repair);
        this.Vfx.Update(dt, repair.ResultSerial, repair.LastResult.IsOk);
    }

    /// <summary>Handles a click: closes, selects an item, scrolls, or reports a repair button.</summary>
    public RepairClick HandleClick(Vector2 mouse, IWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        (float ox, float oy) = RepairLayout.Origin(viewportWidth, viewportHeight);
        if (RepairLayout.CloseRect(ox, oy).Contains(mouse))
        {
            return RepairClick.Close;
        }

        if (RepairLayout.FullButtonRect(ox, oy).Contains(mouse))
        {
            return RepairClick.RepairFull;
        }

        if (RepairLayout.PartialButtonRect(ox, oy).Contains(mouse))
        {
            return RepairClick.RepairPartial;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        RepairPlan.CollectDamaged(world.Items.Rules.Repair, slots, _damaged);
        if (RepairLayout.ScrollUpRect(ox, oy).Contains(mouse))
        {
            _scrollOffset = Math.Max(0, _scrollOffset - 1);
            return RepairClick.Consumed;
        }

        if (RepairLayout.ScrollDownRect(ox, oy).Contains(mouse))
        {
            _scrollOffset = Math.Min(Math.Max(0, _damaged.Count - RepairLayout.MaxRows), _scrollOffset + 1);
            return RepairClick.Consumed;
        }

        for (int i = 0; i < RepairLayout.MaxRows && _scrollOffset + i < _damaged.Count; i++)
        {
            if (RepairLayout.ItemRow(ox, oy, i).Contains(mouse))
            {
                if (!this.Vfx.IsActive)
                {
                    _selectedSlot = _damaged[_scrollOffset + i];
                }

                return RepairClick.Consumed;
            }
        }

        return RepairClick.None;
    }

    /// <summary>
    /// Sends the repair of the selected item when the window shows every requirement met, and starts the animation.
    /// </summary>
    /// <param name="world">World read model.</param>
    /// <param name="repair">The repair session.</param>
    /// <param name="partial">Whether to repair only as far as the bag and wallet pay.</param>
    /// <returns><see langword="true"/> when a request was sent.</returns>
    public bool TryStart(IWorldReadModel world, IRepairSession repair, bool partial)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(repair);
        if (this.Vfx.IsActive || repair.IsPending)
        {
            return false;
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        Span<RepairRequirement> rows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];
        RepairPlan plan = this.BuildPlan(world, slots, rows, partial);
        if (!plan.HasItem || !plan.CanRepair || (partial && !plan.IsPartial) ||
            !repair.TryRepair((ushort)plan.SlotIndex, (ushort)plan.ItemId, (byte)plan.Quote.Steps, this.Venue))
        {
            return false;
        }

        this.Hold(in plan, rows);
        RepairQuote quote = plan.Quote;
        this.Vfx.Begin(quote.Durability / quote.MaxDurability, quote.NewDurability / quote.MaxDurability);
        return true;
    }

    /// <summary>
    /// Shows the animation on the first damaged item for screenshot review (<c>CARBON_REPAIR_PREVIEW</c>): the work, or a
    /// success when <paramref name="success"/>, at <paramref name="seconds"/>. Visual only; nothing is sent.
    /// </summary>
    /// <returns><see langword="false"/> when no item is damaged.</returns>
    public bool Preview(IWorldReadModel world, bool success, float seconds, bool freeze)
    {
        ArgumentNullException.ThrowIfNull(world);
        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        Span<RepairRequirement> rows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];
        RepairPlan plan = this.BuildPlan(world, slots, rows, partial: false);
        if (!plan.HasItem)
        {
            return false;
        }

        this.Hold(in plan, rows);
        RepairQuote quote = plan.Quote;
        this.Vfx.Preview(success, quote.Durability / quote.MaxDurability, quote.NewDurability / quote.MaxDurability, seconds, freeze);
        return true;
    }

    // Keeps the plan the animation plays, so the window still shows the item after the server repaired it.
    private void Hold(in RepairPlan plan, ReadOnlySpan<RepairRequirement> rows)
    {
        _heldPlan = plan;
        rows[..plan.RequirementCount].CopyTo(_heldRows);
    }

    /// <summary>Gets the damaged items listed, equipped first, as of the last plan.</summary>
    internal IReadOnlyList<ushort> Damaged => _damaged;

    /// <summary>Gets the first listed row.</summary>
    internal int ScrollOffset => _scrollOffset;

    /// <summary>Gets the plan the animation plays (see <see cref="Hold"/>) while it plays.</summary>
    /// <param name="rows">Receives the held plan's requirements.</param>
    /// <param name="plan">The held plan.</param>
    /// <returns><see langword="true"/> while the animation plays an item it was started on.</returns>
    internal bool TryGetHeldPlan(Span<RepairRequirement> rows, out RepairPlan plan)
    {
        plan = _heldPlan;
        if (!this.Vfx.IsActive || !_heldPlan.HasItem)
        {
            return false;
        }

        _heldRows.AsSpan(0, plan.RequirementCount).CopyTo(rows);
        return true;
    }

    // The plan of the selected item (the first damaged one when nothing valid is selected).
    internal RepairPlan BuildPlan(IWorldReadModel world, Span<ItemSlot> slots, Span<RepairRequirement> rows, bool partial)
    {
        world.CopyInventorySlots(slots);
        RepairTable table = world.Items.Rules.Repair;
        RepairPlan.CollectDamaged(table, slots, _damaged);
        _scrollOffset = Math.Min(_scrollOffset, Math.Max(0, _damaged.Count - RepairLayout.MaxRows));
        if ((_selectedSlot < 0 || !this.Vfx.IsActive) && (_selectedSlot < 0 || !_damaged.Contains((ushort)_selectedSlot)))
        {
            _selectedSlot = _damaged.Count > 0 ? _damaged[0] : -1;
        }

        return RepairPlan.Build(table, slots, _selectedSlot, this.Venue, world.GetRepairStationTier(this.Venue), world.LocalPlayerGold, partial, rows);
    }
}
