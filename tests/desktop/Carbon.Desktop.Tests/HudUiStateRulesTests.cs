// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The HUD's window rules (audit OOP-3): which windows are open, which tab shows, what opening one closes, and what the
/// carried slot and minimap wheel do around them. Pinned on <see cref="HudOverlayRenderer"/> before the state moved out.
/// </summary>
public sealed class HudUiStateRulesTests : IDisposable
{
    private const int ViewportWidth = 1280;

    private readonly HudOverlayRenderer _hud = new(Substitute.For<IGraphicsDevice>(), Substitute.For<IVectorArtRepository>());
    private readonly IWorldReadModel _world = Substitute.For<IWorldReadModel>();

    [Fact]
    public void NewHud_StartsVisibleWithNoWindowOpen()
    {
        _hud.Visible.Should().BeTrue();
        _hud.HasActiveModal.Should().BeFalse();
        _hud.ShowPlayerModal.Should().BeFalse();
        _hud.ActivePlayerTab.Should().Be(PlayerModalTab.Inventory);
        _hud.TradeDragSlot.Should().Be(-1);
        _hud.CarriedInventorySlot.Should().Be(-1);
    }

    [Fact]
    public void OpenOrTogglePlayerTab_FromOtherWindows_OpensTheTabAndClosesForgeSettingsAndRepair()
    {
        _hud.ShowForgeModal = true;
        _hud.ShowSettingsModal = true;
        _hud.Repair.Open(RepairVenue.Blacksmith);

        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Crafting).Should().BeTrue();

        _hud.ShowPlayerModal.Should().BeTrue();
        _hud.ActivePlayerTab.Should().Be(PlayerModalTab.Crafting);
        _hud.ShowForgeModal.Should().BeFalse();
        _hud.ShowSettingsModal.Should().BeFalse();
        _hud.Repair.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void OpenOrTogglePlayerTab_TheShownTab_ClosesTheWindowAndDropsTheCarriedSlot()
    {
        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory);
        _hud.CarriedInventorySlot = 7;

        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory).Should().BeTrue();

        _hud.ShowPlayerModal.Should().BeFalse();
        _hud.CarriedInventorySlot.Should().Be(-1);
        _hud.HasActiveModal.Should().BeFalse();
    }

    [Theory]
    [InlineData(PlayerModalTab.Character, -1)]
    [InlineData(PlayerModalTab.Crafting, -1)]
    public void OpenOrTogglePlayerTab_SwitchingAwayFromInventory_DropsTheCarriedSlot(PlayerModalTab tab, int expectedCarried)
    {
        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory);
        _hud.CarriedInventorySlot = 7;

        _hud.OpenOrTogglePlayerTab(_world, tab);

        _hud.ActivePlayerTab.Should().Be(tab);
        _hud.CarriedInventorySlot.Should().Be(expectedCarried);
    }

    [Fact]
    public void OpenOrTogglePlayerTab_SwitchingToInventory_KeepsTheCarriedSlot()
    {
        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Character);
        _hud.CarriedInventorySlot = 7;

        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory);

        _hud.ActivePlayerTab.Should().Be(PlayerModalTab.Inventory);
        _hud.CarriedInventorySlot.Should().Be(7);
    }

    [Fact]
    public void OpenOrTogglePlayerTab_SkillsWithoutClassSkills_IsRefusedAndChangesNothing()
    {
        _world.LocalCharacterTraits.Returns(CharacterTraits.None);
        _hud.ShowForgeModal = true;

        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Skills).Should().BeFalse();

        _hud.ShowPlayerModal.Should().BeFalse();
        _hud.ShowForgeModal.Should().BeTrue("a refused tab closes nothing");
    }

    [Fact]
    public void OpenOrTogglePlayerTab_SkillsWithClassSkills_Opens()
    {
        _world.LocalPlayerClass.Returns(HeroArchetypeClass.Vanguard);
        _world.LocalCharacterTraits.Returns(CharacterTraits.ClassAdvanced);

        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Skills).Should().BeTrue();

        _hud.ShowPlayerModal.Should().BeTrue();
        _hud.ActivePlayerTab.Should().Be(PlayerModalTab.Skills);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseAllModals_ClosesEveryWindowAndKeepsSettingsOnlyWhenAsked(bool keepSettings)
    {
        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory);
        _hud.CarriedInventorySlot = 3;
        _hud.ShowForgeModal = true;
        _hud.Trader = Trader();
        _hud.Repair.Open(RepairVenue.Station);
        _hud.ShowSettingsModal = true;

        _hud.CloseAllModals(keepSettings);

        _hud.ShowPlayerModal.Should().BeFalse();
        _hud.ShowForgeModal.Should().BeFalse();
        _hud.Trader.Should().BeNull();
        _hud.Repair.IsOpen.Should().BeFalse();
        _hud.CarriedInventorySlot.Should().Be(-1);
        _hud.ShowSettingsModal.Should().Be(keepSettings);
    }

    [Fact]
    public void OpenSettings_ClosesTheOtherWindowsAndOpensSettings()
    {
        _hud.OpenOrTogglePlayerTab(_world, PlayerModalTab.Character);
        _hud.Trader = Trader();

        _hud.OpenSettings(1);

        _hud.ShowSettingsModal.Should().BeTrue();
        _hud.ShowPlayerModal.Should().BeFalse();
        _hud.Trader.Should().BeNull();
    }

    [Theory]
    [InlineData("player")]
    [InlineData("forge")]
    [InlineData("settings")]
    [InlineData("trader")]
    [InlineData("repair")]
    public void HasActiveModal_AnyWindowOpen_IsTrue(string window)
    {
        switch (window)
        {
            case "player":
                _hud.ShowPlayerModal = true;
                break;
            case "forge":
                _hud.ShowForgeModal = true;
                break;
            case "settings":
                _hud.ShowSettingsModal = true;
                break;
            case "trader":
                _hud.Trader = Trader();
                break;
            default:
                _hud.Repair.Open(RepairVenue.Blacksmith);
                break;
        }

        _hud.HasActiveModal.Should().BeTrue();
    }

    [Fact]
    public void HasActiveModal_InteractPrompt_IsNotAWindow()
    {
        _hud.InteractPrompt = default(InteractionPrompt);

        _hud.HasActiveModal.Should().BeFalse();
    }

    [Fact]
    public void TryZoomMinimap_OverTheMap_ZoomsOnlyWhenVisibleShownAndNoWindowIsOpen()
    {
        Vector2 overMap = PointOverMinimap();
        _hud.Settings.ShowMinimap = true;
        _hud.TryZoomMinimap(overMap, 1, ViewportWidth).Should().BeTrue();

        _hud.ShowForgeModal = true;
        _hud.TryZoomMinimap(overMap, 1, ViewportWidth).Should().BeFalse("a window takes the wheel");
        _hud.ShowForgeModal = false;

        _hud.Visible = false;
        _hud.TryZoomMinimap(overMap, 1, ViewportWidth).Should().BeFalse("a hidden HUD takes nothing");
        _hud.Visible = true;

        _hud.Settings.ShowMinimap = false;
        _hud.TryZoomMinimap(overMap, 1, ViewportWidth).Should().BeFalse("a hidden minimap takes nothing");
    }

    public void Dispose() => _hud.Dispose();

    private static Vector2 PointOverMinimap()
    {
        for (int y = 0; y < 400; y += 4)
        {
            for (int x = ViewportWidth - 1; x > ViewportWidth - 400; x -= 4)
            {
                if (MinimapRadarWidget.ContainsPoint(new Vector2(x, y), ViewportWidth))
                {
                    return new Vector2(x, y);
                }
            }
        }

        throw new InvalidOperationException("No minimap point found.");
    }

    private static TraderView Trader() => new(1UL, 0, 0L, DateTimeOffset.UnixEpoch, [], [], [], [], [], [], CanRepair: false);
}
