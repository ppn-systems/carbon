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
/// carried slot and minimap wheel do around them. Pinned on <see cref="HudOverlayRenderer"/> before the state moved out,
/// then pointed at <see cref="HudUiState"/>.
/// </summary>
public sealed class HudUiStateRulesTests : IDisposable
{
    private const int ViewportWidth = 1280;

    private readonly HudUiState _state = new();
    private readonly HudOverlayRenderer _hud;
    private readonly IWorldReadModel _world = Substitute.For<IWorldReadModel>();

    // The rules live on HudUiState; OpenSettings and the minimap wheel stay on the renderer, which reads the same state.
    public HudUiStateRulesTests() => _hud = new HudOverlayRenderer(Substitute.For<IGraphicsDevice>(), Substitute.For<IVectorArtRepository>(), _state);

    [Fact]
    public void NewHud_StartsVisibleWithNoWindowOpen()
    {
        _state.Visible.Should().BeTrue();
        _state.HasActiveModal.Should().BeFalse();
        _state.ShowPlayerModal.Should().BeFalse();
        _state.ActivePlayerTab.Should().Be(PlayerModalTab.Inventory);
        _state.TradeDragSlot.Should().Be(-1);
        _state.CarriedInventorySlot.Should().Be(-1);
    }

    [Fact]
    public void OpenOrTogglePlayerTab_FromOtherWindows_OpensTheTabAndClosesForgeSettingsAndRepair()
    {
        _state.ShowForgeModal = true;
        _state.ShowSettingsModal = true;
        _state.Repair.Open(RepairVenue.Blacksmith);

        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Crafting).Should().BeTrue();

        _state.ShowPlayerModal.Should().BeTrue();
        _state.ActivePlayerTab.Should().Be(PlayerModalTab.Crafting);
        _state.ShowForgeModal.Should().BeFalse();
        _state.ShowSettingsModal.Should().BeFalse();
        _state.Repair.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void OpenOrTogglePlayerTab_TheShownTab_ClosesTheWindowAndDropsTheCarriedSlot()
    {
        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory);
        _state.CarriedInventorySlot = 7;

        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory).Should().BeTrue();

        _state.ShowPlayerModal.Should().BeFalse();
        _state.CarriedInventorySlot.Should().Be(-1);
        _state.HasActiveModal.Should().BeFalse();
    }

    [Theory]
    [InlineData(PlayerModalTab.Character, -1)]
    [InlineData(PlayerModalTab.Crafting, -1)]
    public void OpenOrTogglePlayerTab_SwitchingAwayFromInventory_DropsTheCarriedSlot(PlayerModalTab tab, int expectedCarried)
    {
        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory);
        _state.CarriedInventorySlot = 7;

        _state.OpenOrTogglePlayerTab(_world, tab);

        _state.ActivePlayerTab.Should().Be(tab);
        _state.CarriedInventorySlot.Should().Be(expectedCarried);
    }

    [Fact]
    public void OpenOrTogglePlayerTab_SwitchingToInventory_KeepsTheCarriedSlot()
    {
        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Character);
        _state.CarriedInventorySlot = 7;

        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory);

        _state.ActivePlayerTab.Should().Be(PlayerModalTab.Inventory);
        _state.CarriedInventorySlot.Should().Be(7);
    }

    [Fact]
    public void OpenOrTogglePlayerTab_SkillsWithoutClassSkills_IsRefusedAndChangesNothing()
    {
        _world.LocalCharacterTraits.Returns(CharacterTraits.None);
        _state.ShowForgeModal = true;

        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Skills).Should().BeFalse();

        _state.ShowPlayerModal.Should().BeFalse();
        _state.ShowForgeModal.Should().BeTrue("a refused tab closes nothing");
    }

    [Fact]
    public void OpenOrTogglePlayerTab_SkillsWithClassSkills_Opens()
    {
        _world.LocalPlayerClass.Returns(HeroArchetypeClass.Vanguard);
        _world.LocalCharacterTraits.Returns(CharacterTraits.ClassAdvanced);

        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Skills).Should().BeTrue();

        _state.ShowPlayerModal.Should().BeTrue();
        _state.ActivePlayerTab.Should().Be(PlayerModalTab.Skills);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseAllModals_ClosesEveryWindowAndKeepsSettingsOnlyWhenAsked(bool keepSettings)
    {
        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory);
        _state.CarriedInventorySlot = 3;
        _state.ShowForgeModal = true;
        _state.Trader = Trader();
        _state.Repair.Open(RepairVenue.Station);
        _state.ShowSettingsModal = true;

        _state.CloseAllModals(keepSettings);

        _state.ShowPlayerModal.Should().BeFalse();
        _state.ShowForgeModal.Should().BeFalse();
        _state.Trader.Should().BeNull();
        _state.Repair.IsOpen.Should().BeFalse();
        _state.CarriedInventorySlot.Should().Be(-1);
        _state.ShowSettingsModal.Should().Be(keepSettings);
    }

    [Fact]
    public void OpenSettings_ClosesTheOtherWindowsAndOpensSettings()
    {
        _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Character);
        _state.Trader = Trader();

        _hud.OpenSettings(1);

        _state.ShowSettingsModal.Should().BeTrue();
        _state.ShowPlayerModal.Should().BeFalse();
        _state.Trader.Should().BeNull();
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
                _state.ShowPlayerModal = true;
                break;
            case "forge":
                _state.ShowForgeModal = true;
                break;
            case "settings":
                _state.ShowSettingsModal = true;
                break;
            case "trader":
                _state.Trader = Trader();
                break;
            default:
                _state.Repair.Open(RepairVenue.Blacksmith);
                break;
        }

        _state.HasActiveModal.Should().BeTrue();
    }

    [Fact]
    public void HasActiveModal_InteractPrompt_IsNotAWindow()
    {
        _state.InteractPrompt = default(InteractionPrompt);

        _state.HasActiveModal.Should().BeFalse();
    }

    [Fact]
    public void TryZoomMinimap_OverTheMap_ZoomsOnlyWhenVisibleShownAndNoWindowIsOpen()
    {
        Vector2 overMap = PointOverMinimap();
        _hud.Settings.ShowMinimap = true;
        _hud.TryZoomMinimap(overMap, 1, ViewportWidth).Should().BeTrue();

        _state.ShowForgeModal = true;
        _hud.TryZoomMinimap(overMap, 1, ViewportWidth).Should().BeFalse("a window takes the wheel");
        _state.ShowForgeModal = false;

        _state.Visible = false;
        _hud.TryZoomMinimap(overMap, 1, ViewportWidth).Should().BeFalse("a hidden HUD takes nothing");
        _state.Visible = true;

        _hud.Settings.ShowMinimap = false;
        _hud.TryZoomMinimap(overMap, 1, ViewportWidth).Should().BeFalse("a hidden minimap takes nothing");
    }

    [Fact]
    public void InventoryWidget_DrawsTheCarriedSlotTheStateHolds()
    {
        InventoryModalWidget bag = new(_state);

        _state.CarriedInventorySlot = 5;

        bag.CarriedSlot.Should().Be(5, "the bag highlights the slot the scene's state carries, not a copy of its own");
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
