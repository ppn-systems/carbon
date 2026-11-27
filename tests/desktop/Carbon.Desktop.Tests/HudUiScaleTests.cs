// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Gameplay.Repair;
using Carbon.Desktop.Gameplay.Trade;
using Carbon.Desktop.Input;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.Fonts;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Tests.Fakes;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Testing;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The in-game HUD on the shared UI scale (docs/art/menu-ui.md rule 21): every HUD hit-test takes window pixels and lays
/// out on the same 1280×720 canvas the menus use, so at 1920×1080 a point hits exactly what 1.5 times less hits at
/// 1280×720, every window fits, and the menus and the HUD share one scale and one font cache.
/// </summary>
public sealed class HudUiScaleTests : IDisposable
{
    private const float Step = 16f;

    private readonly HudUiState _state = new();
    private readonly IWorldReadModel _world = Substitute.For<IWorldReadModel>();
    private readonly UiScaleState _ui = new(() => null);
    private readonly HudOverlayRenderer _hud;

    public HudUiScaleTests()
    {
        _hud = new HudOverlayRenderer(Substitute.For<IGraphicsDevice>(), Substitute.For<IVectorArtRepository>(), _state, _ui);
        _ui.Choice = null; // Auto, whatever the machine's settings.ini says
    }

    public void Dispose()
    {
        _hud.Dispose();
        _ui.Dispose();
    }

    public static TheoryData<string> Windows() => ["settings", "inventory", "character", "crafting", "forge", "repair"];

    [Theory]
    [MemberData(nameof(Windows))]
    public void ModalHitTests_AtFullHd_HitWhatTheSamePointHitsAtOneX(string window)
    {
        this.Open(window);
        int inside = 0;
        foreach (Vector2 canvas in CanvasGrid())
        {
            bool small = _hud.IsPointInActiveModal(_world, canvas, 1280, 720);
            bool large = _hud.IsPointInActiveModal(_world, canvas * 1.5f, 1920, 1080);
            large.Should().Be(small, $"{window} at {canvas}: window pixels map onto the canvas at 1.5x");
            _hud.TryHitTestModalCloseButton(canvas * 1.5f, 1920, 1080).Should().Be(_hud.TryHitTestModalCloseButton(canvas, 1280, 720), $"{window} close at {canvas}");
            inside += small ? 1 : 0;
        }

        inside.Should().BeGreaterThan(100, $"the {window} window is open");
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void EveryHudWindow_FitsTheWindowAt1080p(string window)
    {
        this.Open(window);
        float left = float.MaxValue;
        float top = float.MaxValue;
        float right = float.MinValue;
        float bottom = float.MinValue;
        for (float y = 0f; y < 1080f; y += 8f)
        {
            for (float x = 0f; x < 1920f; x += 8f)
            {
                if (_hud.IsPointInActiveModal(_world, new Vector2(x, y), 1920, 1080))
                {
                    left = MathF.Min(left, x);
                    top = MathF.Min(top, y);
                    right = MathF.Max(right, x);
                    bottom = MathF.Max(bottom, y);
                }
            }
        }

        right.Should().BeGreaterThan(left, $"the {window} window shows at 1080p");
        left.Should().BeGreaterThan(0f, "nothing is cut off on the left");
        top.Should().BeGreaterThan(0f, "nothing is cut off at the top");
        right.Should().BeLessThan(1912f, "nothing is cut off on the right");
        bottom.Should().BeLessThan(1072f, "nothing is cut off at the bottom");
        (right - left).Should().BeGreaterThan(640f, "the window is drawn 1.5x larger, not left at its 1x size");
    }

    [Fact]
    public void InventorySlots_AtFullHd_CentreAndHitAtOnePointFiveTimesTheirOneXPlace()
    {
        this.Open("inventory");
        for (int slot = 0; slot < 42; slot += 5)
        {
            Vector2 small = _hud.GetInventorySlotCenter(slot, 1280, 720);
            Vector2 large = _hud.GetInventorySlotCenter(slot, 1920, 1080);
            large.X.Should().BeApproximately(small.X * 1.5f, 0.01f);
            large.Y.Should().BeApproximately(small.Y * 1.5f, 0.01f);

            _hud.TryHitTestInventorySlot(large, 1920, 1080, out ushort hit).Should().BeTrue();
            hit.Should().Be((ushort)slot);
        }
    }

    [Fact]
    public void Hotbar_AtFullHd_HitsTheSameSlotAsOnePointFiveTimesLessAtOneX()
    {
        int hits = 0;
        foreach (Vector2 canvas in CanvasGrid(step: 4f))
        {
            bool small = _hud.TryHitTestHotbar(canvas, 1280, 720, out byte smallSlot);
            bool large = _hud.TryHitTestHotbar(canvas * 1.5f, 1920, 1080, out byte largeSlot);
            large.Should().Be(small, $"hotbar at {canvas}");
            largeSlot.Should().Be(smallSlot);
            hits += small ? 1 : 0;
        }

        hits.Should().BeGreaterThan(9, "every hotbar slot is hit somewhere");
    }

    [Fact]
    public void MinimapWheel_AtFullHd_ZoomsWhereTheMinimapIsDrawn()
    {
        _ = _hud.IsPointInActiveModal(_world, Vector2.Zero, 1920, 1080); // the scene's frame has seen the window size
        int zoomed = 0;
        foreach (Vector2 canvas in CanvasGrid(step: 8f))
        {
            bool small = _hud.TryZoomMinimap(canvas, 1, 1280) && _hud.TryZoomMinimap(canvas, -1, 1280);
            _ = _hud.IsPointInActiveModal(_world, Vector2.Zero, 1920, 1080);
            bool large = _hud.TryZoomMinimap(canvas * 1.5f, 1, 1920) && _hud.TryZoomMinimap(canvas * 1.5f, -1, 1920);
            large.Should().Be(small, $"minimap wheel at {canvas}");
            zoomed += small ? 1 : 0;
            _ = _hud.IsPointInActiveModal(_world, Vector2.Zero, 1280, 720);
        }

        zoomed.Should().BeGreaterThan(20, "the wheel zooms over the minimap");
    }

    [Fact]
    public void RespawnButton_AtFullHd_IsOnePointFiveTimesItsOneXRectangle()
    {
        RectangleF small = _hud.GetRespawnButtonRect(1280, 720);
        RectangleF large = _hud.GetRespawnButtonRect(1920, 1080);

        large.X.Should().BeApproximately(small.X * 1.5f, 0.01f);
        large.Y.Should().BeApproximately(small.Y * 1.5f, 0.01f);
        large.Width.Should().BeApproximately(small.Width * 1.5f, 0.01f);
        large.Height.Should().BeApproximately(small.Height * 1.5f, 0.01f);
    }

    [Fact]
    public void HudHitTests_FollowThePlayersChoice()
    {
        this.Open("settings");
        _ui.Choice = 1f;
        Vector2 canvasCorner = new(352f, 132f); // just inside the Settings window's top-left on the 1280x720 canvas

        _hud.IsPointInActiveModal(_world, canvasCorner * 1.5f, 1920, 1080).Should().BeFalse("at 1x the window is centred on the whole 1080p screen");
        _ui.Scale.Should().Be(1f);

        _ui.Choice = null;
        _hud.IsPointInActiveModal(_world, canvasCorner * 1.5f, 1920, 1080).Should().BeTrue("Auto draws it at 1.5x");
        _ui.Scale.Should().Be(1.5f);
    }

    [Fact]
    public async Task RepairWindow_AtFullHd_AClickInsideItsDrawnCornerKeepsItOpen()
    {
        _state.Repair.Open(RepairVenue.Blacksmith);
        RecordingWorldSession session = new();
        await using ConfiguredAsyncDisposable scope = session.ConfigureAwait(true);
        RepairInteraction repair = new(session, _state, new DesktopOptions(), _ui);
        (float ox, float oy) = RepairLayout.Origin(1280, 720);

        repair.HandleWindowInput(Click(new Vector2(ox + RepairLayout.PanelW - 6f, oy + RepairLayout.PanelH - 6f) * 1.5f), 1920, 1080);
        _state.Repair.IsOpen.Should().BeTrue("the click lands inside the window's bottom-right corner as drawn at 1.5x");

        repair.HandleWindowInput(Click(new Vector2(6f, 6f)), 1920, 1080);
        _state.Repair.IsOpen.Should().BeFalse("a click outside closes it");
    }

    [Fact]
    public async Task TradeWindow_AtFullHd_AClickOnTheDrawnRepairButtonOpensRepair()
    {
        RecordingWorldSession session = new();
        await using ConfiguredAsyncDisposable scope = session.ConfigureAwait(true);
        TraderView smith = new(1, 0, 0, DateTimeOffset.MaxValue, [], [], [], [], [], [], CanRepair: true);
        _state.Trader = smith;
        Vector2? repairButton = null;
        foreach (Vector2 canvas in CanvasGrid(step: 4f))
        {
            if (TradeModalWidget.HitTest(canvas, smith, _state.TradeBasket, session, 1280, 720).Target == TradeWindowTarget.Repair)
            {
                repairButton = canvas;
                break;
            }
        }

        repairButton.Should().NotBeNull("a blacksmith's window has a Repair button");
        bool opened = false;
        TradeInteraction trade = new(session, _hud, new DesktopOptions(), static (in _) => false, static () => false, () => opened = true);

        trade.HandleWindowInput(Click(repairButton!.Value * 1.5f), 1920, 1080);

        opened.Should().BeTrue("the click lands on Repair as drawn at 1.5x");
    }

    [Fact]
    public void MenusAndHud_ShareOneScaleAndOneFontCache()
    {
        IWorldSession session = Substitute.For<IWorldSession>();
        MenuFlow flow = new(session, new DesktopOptions(), new Camera2D(1280f, 720f), Substitute.For<IRememberedAccountStore>(), ServerDirectory.FromIni(null, "127.0.0.1", 7777));
        int bakes = 0;
        flow.Ui.EnsureFonts(() => new ScaledFontCache(scale =>
        {
            bakes++;
            return FakeFont(scale);
        }));
        flow.Ui.Resize(1920, 1080);
        _ = flow.Ui.Font;

        using HudOverlayRenderer hud = new(Substitute.For<IGraphicsDevice>(), Substitute.For<IVectorArtRepository>(), new HudUiState(), flow.Ui);
        hud.UiScale.Should().BeSameAs(flow.Ui, "the world's HUD draws with the menus' scale");
        flow.Ui.EnsureFonts(() => throw new InvalidOperationException("a second font cache"));
        _ = hud.UiScale.Font;

        bakes.Should().Be(1, "entering the world never re-bakes the font");
        flow.Ui.Dispose();
    }

    [Fact]
    public void ScaledFontCache_Invalidate_RebakesOnceForTheNewLanguage()
    {
        int bakes = 0;
        using ScaledFontCache fonts = new(scale =>
        {
            bakes++;
            return FakeFont(scale);
        });
        _ = fonts.Get(1.5f);
        _ = fonts.Get(1.5f);
        fonts.Invalidate();
        _ = fonts.Get(1.5f);
        _ = fonts.Get(1.5f);

        bakes.Should().Be(2);
    }

    [Fact]
    public void HudSteadyFrame_ScaleAndHitTests_AllocateNothing()
    {
        _ui.EnsureFonts(() => new ScaledFontCache(FakeFont));
        this.Open("inventory");
        Vector2 slot = _hud.GetInventorySlotCenter(7, 1920, 1080);

        AllocationResult window = AllocationProbe.Measure(1000, warmupIterations: 1000, i =>
        {
            _ui.Resize(1920, 1080);
            _ = _ui.Font;
            _ = _hud.IsPointInActiveModal(_world, slot, 1920, 1080);
            _ = _hud.TryHitTestInventorySlot(slot, 1920, 1080, out _);
            _ = _hud.TryHitTestHotbar(slot, 1920, 1080, out _);
        });

        window.Bytes.Should().Be(0, window.ToString());
        _ui.Fonts!.BakeCount.Should().Be(1, "a steady frame never re-bakes");
    }

    private static DesktopInputFrame Click(Vector2 mouse) => new(
        MoveDirection: Vector2.Zero,
        Sprint: false,
        ZoomMultiplier: null,
        SpaceActionPressed: false,
        LeftClickPressed: true,
        LeftMouseDown: true,
        RightClickPressed: false,
        RightMouseDown: false,
        ShiftHeld: false,
        ScrollSlotDelta: 0,
        MouseScreenPosition: mouse,
        NumberSlotPressed: null,
        SkillPressed: null,
        ToggleInventoryPressed: false,
        ToggleCharacterPressed: false,
        CycleTargetPressed: false,
        InteractOrPickupPressed: false,
        DropItemPressed: false,
        ToggleCraftPressed: false,
        ToggleForgePressed: false,
        ToggleSkillsPressed: false,
        ToggleMinimapPressed: false,
        ToggleRestPressed: false,
        ToggleHudPressed: false,
        HarvestPressed: false,
        ToggleDoorPressed: false,
        PlaceStructurePressed: false,
        DemolishStructurePressed: false,
        ToggleDebugPressed: false,
        ScreenshotPressed: false,
        EscapePressed: false);

    private static BitmapFont FakeFont(float scale) =>
        new("fake", new BitmapFontAtlas(Substitute.For<ITexture>(), scale), new FontMetrics(20f, 20f, 16f, 8f));

    private static IEnumerable<Vector2> CanvasGrid(float step = Step)
    {
        for (float y = step * 0.5f; y < 720f; y += step)
        {
            for (float x = step * 0.5f; x < 1280f; x += step)
            {
                yield return new Vector2(x, y);
            }
        }
    }

    private void Open(string window)
    {
        switch (window)
        {
            case "settings":
                _hud.OpenSettings(0);
                break;
            case "inventory":
                _ = _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Inventory);
                break;
            case "character":
                _ = _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Character);
                break;
            case "crafting":
                _ = _state.OpenOrTogglePlayerTab(_world, PlayerModalTab.Crafting);
                break;
            case "forge":
                _state.ShowForgeModal = true;
                break;
            default:
                _state.Repair.Open(RepairVenue.Blacksmith);
                break;
        }
    }
}
