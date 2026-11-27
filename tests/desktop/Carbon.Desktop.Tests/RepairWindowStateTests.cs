// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Tests.Fakes;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The repair window's rules (ADR-028): opening and closing, which control a click hits, which damaged item is selected
/// and repaired, and that nothing changes while the anvil animation plays or a request is pending. Pinned on
/// <see cref="RepairModalWidget"/> before its state moved out of the drawing into <see cref="RepairWindowState"/>.
/// </summary>
public sealed class RepairWindowStateTests : IDisposable
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int BagStart = PlayerInventoryLayout.HotbarCount;

    private readonly SubstituteWorldSession _world = new();
    private readonly IRepairSession _repair = Substitute.For<IRepairSession>();
    private readonly RepairWindowState _window = new();
    private readonly SyncedItemCatalog _items = new();
    private readonly (float X, float Y) _origin = RepairLayout.Origin(Width, Height);

    public RepairWindowStateTests()
    {
        _world.Inner.Items.Returns(_items);
        _world.Inner.LocalPlayerGold.Returns(1_000_000L);
        _world.Inner.GetRepairStationTier(Arg.Any<RepairVenue>()).Returns(9);
        _repair.TryRepair(default, default, default, default).ReturnsForAnyArgs(true);
    }

    private uint Sword => _items.Rules.ItemCatalog.ResolveId("iron_sword");

    [Fact]
    public void OpenAndClose_SetTheVenue()
    {
        _window.Open(RepairVenue.Blacksmith);
        _window.IsOpen.Should().BeTrue();
        _window.Venue.Should().Be(RepairVenue.Blacksmith);

        _window.Close();
        _window.IsOpen.Should().BeFalse();
        _window.Venue.Should().Be(RepairVenue.None);
    }

    [Fact]
    public void HandleClick_Buttons_ReportCloseFullAndPartial()
    {
        this.Damage(BagStart, 1);
        _window.Open(RepairVenue.Station);

        this.Click(RepairLayout.CloseRect(_origin.X, _origin.Y).Center).Should().Be(RepairClick.Close);
        this.Click(RepairLayout.FullButtonRect(_origin.X, _origin.Y).Center).Should().Be(RepairClick.RepairFull);
        this.Click(RepairLayout.PartialButtonRect(_origin.X, _origin.Y).Center).Should().Be(RepairClick.RepairPartial);
        this.Click(new Vector2(2f, 2f)).Should().Be(RepairClick.None);
    }

    [Fact]
    public void TryStart_NothingSelected_RepairsTheFirstDamagedItem()
    {
        this.Damage(BagStart, 2);
        _window.Open(RepairVenue.Station);

        _window.TryStart(_world, _repair, partial: false).Should().BeTrue();

        _repair.Received(1).TryRepair(BagStart, (ushort)this.Sword, Arg.Any<byte>(), RepairVenue.Station);
    }

    [Fact]
    public void TryStart_AfterClickingTheSecondRow_RepairsThatItem()
    {
        this.Damage(BagStart, 2);
        _window.Open(RepairVenue.Station);

        this.Click(RepairLayout.ItemRow(_origin.X, _origin.Y, 1).Center).Should().Be(RepairClick.Consumed);
        _window.TryStart(_world, _repair, partial: false).Should().BeTrue();

        _repair.Received(1).TryRepair(BagStart + 1, (ushort)this.Sword, Arg.Any<byte>(), RepairVenue.Station);
    }

    [Fact]
    public void WhileTheAnvilWorks_SelectionAndNewRepairsAreRefused()
    {
        this.Damage(BagStart, 2);
        _window.Open(RepairVenue.Station);
        _window.TryStart(_world, _repair, partial: false).Should().BeTrue();

        this.Click(RepairLayout.ItemRow(_origin.X, _origin.Y, 1).Center);
        _window.TryStart(_world, _repair, partial: false).Should().BeFalse("the animation is still playing");
        _repair.Received(1).TryRepair(Arg.Any<ushort>(), Arg.Any<ushort>(), Arg.Any<byte>(), Arg.Any<RepairVenue>());

        // Once the anvil stops, the click made during the animation did not change the selection.
        _repair.ClearReceivedCalls();
        _window.Update(0f, _repair);
        _window.Update(RepairVfx.WorkDuration * 10f, _repair);
        _window.TryStart(_world, _repair, partial: false).Should().BeTrue();
        _repair.Received(1).TryRepair(BagStart, (ushort)this.Sword, Arg.Any<byte>(), RepairVenue.Station);
    }

    [Fact]
    public void TryStart_RequestPending_IsRefused()
    {
        this.Damage(BagStart, 1);
        _window.Open(RepairVenue.Station);
        _repair.IsPending.Returns(true);

        _window.TryStart(_world, _repair, partial: false).Should().BeFalse();

        _repair.DidNotReceiveWithAnyArgs().TryRepair(default, default, default, default);
    }

    [Fact]
    public void ScrollDown_ShiftsTheRowsByOne()
    {
        this.Damage(BagStart, RepairLayout.MaxRows + 2);
        _window.Open(RepairVenue.Station);

        this.Click(RepairLayout.ScrollDownRect(_origin.X, _origin.Y).Center).Should().Be(RepairClick.Consumed);
        this.Click(RepairLayout.ItemRow(_origin.X, _origin.Y, 0).Center);
        _window.TryStart(_world, _repair, partial: false).Should().BeTrue();

        _repair.Received(1).TryRepair(BagStart + 1, (ushort)this.Sword, Arg.Any<byte>(), RepairVenue.Station);
    }

    [Fact]
    public void ScrollUp_AtTheTop_StaysAtTheFirstRow()
    {
        this.Damage(BagStart, RepairLayout.MaxRows + 2);
        _window.Open(RepairVenue.Station);

        this.Click(RepairLayout.ScrollUpRect(_origin.X, _origin.Y).Center).Should().Be(RepairClick.Consumed);
        this.Click(RepairLayout.ItemRow(_origin.X, _origin.Y, 0).Center);
        _window.TryStart(_world, _repair, partial: false).Should().BeTrue();

        _repair.Received(1).TryRepair(BagStart, (ushort)this.Sword, Arg.Any<byte>(), RepairVenue.Station);
    }

    [Fact]
    public void Open_ResetsTheScroll()
    {
        this.Damage(BagStart, RepairLayout.MaxRows + 2);
        _window.Open(RepairVenue.Station);
        this.Click(RepairLayout.ScrollDownRect(_origin.X, _origin.Y).Center);

        _window.Open(RepairVenue.Station);
        this.Click(RepairLayout.ItemRow(_origin.X, _origin.Y, 0).Center);
        _window.TryStart(_world, _repair, partial: false).Should().BeTrue();

        _repair.Received(1).TryRepair(BagStart, (ushort)this.Sword, Arg.Any<byte>(), RepairVenue.Station);
    }

    [Fact]
    public void Preview_NothingDamaged_ShowsNothing()
    {
        _window.Open(RepairVenue.Station);

        _window.Preview(_world, success: true, seconds: 0.5f, freeze: true).Should().BeFalse();
        _window.Vfx.IsActive.Should().BeFalse();
    }

    [Fact]
    public void HeldPlan_WhileTheAnvilWorks_IsTheStartedItemEvenAfterTheBagChanges()
    {
        this.Damage(BagStart, 2);
        _window.Open(RepairVenue.Station);
        Span<RepairRequirement> rows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];
        _window.TryGetHeldPlan(rows, out _).Should().BeFalse("nothing was started");

        _window.TryStart(_world, _repair, partial: false).Should().BeTrue();
        _world.Inventory[BagStart] = default;

        _window.TryGetHeldPlan(rows, out RepairPlan held).Should().BeTrue();
        held.SlotIndex.Should().Be(BagStart);
        held.RequirementCount.Should().BePositive();
        rows[0].Need.Should().BePositive();
    }

    [Fact]
    public void HeldPlan_AfterTheAnvilStops_IsReleased()
    {
        this.Damage(BagStart, 1);
        _window.Open(RepairVenue.Station);
        _window.TryStart(_world, _repair, partial: false).Should().BeTrue();

        // No answer arrives: the animation gives up after its longest wait.
        _window.Update(0f, _repair);
        _window.Update(RepairVfx.WorkDuration * 10f, _repair);

        _window.Vfx.IsActive.Should().BeFalse();
        Span<RepairRequirement> rows = stackalloc RepairRequirement[RepairPlan.MaxRequirements];
        _window.TryGetHeldPlan(rows, out _).Should().BeFalse();
    }

    public void Dispose() => ((IAsyncDisposable)_world).DisposeAsync().AsTask().GetAwaiter().GetResult();

    private RepairClick Click(Vector2 at) => _window.HandleClick(at, _world, Width, Height);

    private void Damage(int firstSlot, int count)
    {
        // Enough ingots in the last bag slot to pay for every repair.
        _world.Inventory[PlayerInventoryLayout.BagLimit - 1] = new ItemSlot(_items.Rules.ItemCatalog.ResolveId("iron_ingot"), 99);
        for (int i = 0; i < count; i++)
        {
            _world.Inventory[firstSlot + i] = new ItemSlot(this.Sword, 1, InstanceId: (ulong)(i + 1), Durability: 20f);
        }
    }
}
