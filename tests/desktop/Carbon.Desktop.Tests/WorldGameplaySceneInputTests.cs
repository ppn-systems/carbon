// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Scenes;
using Carbon.Desktop.Tests.Fakes;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Scenes.Abstractions;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using FluentAssertions;
using NSubstitute;
using Silk.NET.Input;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Characterization of the world scene's keyboard and mouse routing (hotkeys that open and close windows, Escape, and
/// the world actions taken when no window is open), written against <see cref="WorldGameplayScene"/> before that
/// routing moved out of it. The scene runs headless (its renderers create GPU resources on first draw) with a fake
/// session and fake input in a 1280×720 window.
/// </summary>
public sealed class WorldGameplaySceneInputTests : IDisposable
{
    private readonly SubstituteWorldSession _fake = new();
    private readonly IWorldSession _session;
    private readonly IInputManager _input = Substitute.For<IInputManager>();
    private readonly ISceneManager _scenes = Substitute.For<ISceneManager>();
    private readonly SceneContext _context;
    private readonly WorldGameplayScene _scene;
    private readonly Vector2 _player;
    private readonly Camera2D _camera = new(1280f, 720f);
    private readonly ChunkWorld2D _world;
    private KeyboardSnapshot _keyboard = new(new bool[512], new bool[512]);
    private MouseSnapshot _mouse = new(Vector2.Zero, Vector2.Zero, Vector2.Zero, new bool[8], new bool[8]);

    public WorldGameplaySceneInputTests()
    {
        _session = _fake.Inner;
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL));
        _world = world;
        _player = world.SpawnWorldPosition;
        _session.IsReady.Returns(true);
        _session.ChunkWorld.Returns(world);
        _session.WorldSeed.Returns(1337UL);
        _session.LocalPlayerInterpolatedPosition.Returns(_player);
        _session.RemoteEntities.Returns(new List<RemoteEntityView>());
        _input.Keyboard.Returns(_ => _keyboard);
        _input.Mouse.Returns(_ => _mouse);
        IGameWindow window = Substitute.For<IGameWindow>();
        window.Width.Returns(1280);
        window.Height.Returns(720);
        window.Input.Returns(_input);
        _context = new SceneContext(window, new Camera2D(1280f, 720f), _scenes);

        MenuFlow flow = new(_fake, new DesktopOptions(), _camera, Substitute.For<IRememberedAccountStore>(), ServerDirectory.FromIni(null, "127.0.0.1", 7777));
        _scene = new WorldGameplayScene(flow);
        _scene.Load(_context);
        _scene.Enter(_context);
    }

    private HudUiState Hud => _scene.HudState;

    public void Dispose()
    {
        _scene.Dispose();
        _scenes.Dispose();
        ((IAsyncDisposable)_fake).DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void InventoryKey_OpensTheInventoryAndAgainClosesIt()
    {
        this.Press(Key.I);
        this.Hud.ShowPlayerModal.Should().BeTrue();
        this.Hud.ActivePlayerTab.Should().Be(PlayerModalTab.Inventory);

        this.Press(Key.I);
        this.Hud.ShowPlayerModal.Should().BeFalse();
    }

    [Fact]
    public void SpaceKey_NoWindowOpen_StrikesAheadOfThePlayer()
    {
        this.Press(Key.Space);

        Vector2 expected = _player + (Vector2.UnitX * (32f * 1.25f));
        _session.Received(1).TriggerPrimaryAction(expected, out Arg.Any<(int, int)>());
    }

    [Theory]
    [InlineData(Key.Y, PlayerModalTab.Character)]
    [InlineData(Key.C, PlayerModalTab.Crafting)]
    public void TabKeys_OpenTheirTab(Key key, PlayerModalTab tab)
    {
        this.Press(key);

        this.Hud.ShowPlayerModal.Should().BeTrue();
        this.Hud.ActivePlayerTab.Should().Be(tab);
    }

    [Fact]
    public void SkillsKey_WithoutClassSkills_OpensNothing()
    {
        this.Press(Key.K);

        this.Hud.ShowPlayerModal.Should().BeFalse("the Skills tab needs a class skill");
    }

    [Fact]
    public void ForgeKey_NoForgeInReach_OpensNothing()
    {
        this.Press(Key.U);

        this.Hud.ShowForgeModal.Should().BeFalse();
    }

    [Fact]
    public void ForgeKey_ForgeInReach_OpensAndAgainClosesTheForge()
    {
        _session.IsNearForgeStation(Arg.Any<float>()).Returns(true);

        this.Press(Key.U);
        this.Hud.ShowForgeModal.Should().BeTrue();

        this.Press(Key.U);
        this.Hud.ShowForgeModal.Should().BeFalse();
    }

    [Fact]
    public void ForgeWindow_WalkingOutOfReach_Closes()
    {
        _session.IsNearForgeStation(Arg.Any<float>()).Returns(true);
        this.Press(Key.U);

        _session.IsNearForgeStation(Arg.Any<float>()).Returns(false);
        this.Frame();

        this.Hud.ShowForgeModal.Should().BeFalse("the forge window closes beyond 185 px");
    }

    [Fact]
    public void RepairKey_OpensTheStationRepairAndAgainClosesIt()
    {
        this.Press(Key.N);
        this.Hud.Repair.IsOpen.Should().BeTrue();

        this.Press(Key.N);
        this.Hud.Repair.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void EscapeKey_NothingOpen_OpensSettingsAndAgainClosesThem()
    {
        this.Press(Key.Escape);
        this.Hud.ShowSettingsModal.Should().BeTrue();

        this.Press(Key.Escape);
        this.Hud.ShowSettingsModal.Should().BeFalse();
    }

    [Fact]
    public void EscapeKey_WindowOpen_ClosesItWithoutOpeningSettings()
    {
        this.Press(Key.I);

        this.Press(Key.Escape);

        this.Hud.HasActiveModal.Should().BeFalse();
        this.Hud.ShowSettingsModal.Should().BeFalse();
    }

    [Fact]
    public void EscapeKey_TargetLocked_DropsTheTargetFirst()
    {
        _session.LockedTargetNetworkId.Returns(42UL);

        this.Press(Key.Escape);

        _session.Received(1).SetLockedTarget(0UL);
        this.Hud.ShowSettingsModal.Should().BeFalse();
    }

    [Fact]
    public void SettingsOpen_WorldKeysDoNothing()
    {
        this.Press(Key.Escape);

        this.Press(Key.Space);
        this.Press(Key.F);

        _session.DidNotReceive().TriggerPrimaryAction(Arg.Any<Vector2>(), out Arg.Any<(int, int)>());
        _session.DidNotReceive().TryHarvestNearbyProp(out Arg.Any<(int, int)>());
    }

    [Fact]
    public void F1Key_TogglesTheHud()
    {
        this.Press(Key.F1);
        this.Hud.Visible.Should().BeFalse();

        this.Press(Key.F1);
        this.Hud.Visible.Should().BeTrue();
    }

    [Fact]
    public void RestAndTargetKeys_NoWindowOpen_GoToTheSession()
    {
        this.Press(Key.T);
        this.Press(Key.Tab);

        _session.Received(1).TryToggleRest();
        _session.Received(1).CycleNearestHostileTarget();
    }

    [Fact]
    public void RestAndTargetKeys_WindowOpen_AreIgnored()
    {
        this.Press(Key.I);

        this.Press(Key.T);
        this.Press(Key.Tab);

        _session.DidNotReceive().TryToggleRest();
        _session.DidNotReceive().CycleNearestHostileTarget();
    }

    [Fact]
    public void SpaceKey_AfterWalkingLeft_StrikesToTheLeft()
    {
        this.Hold(Key.A);

        this.Press(Key.Space);

        Vector2 expected = _player - (Vector2.UnitX * (32f * 1.25f));
        _session.Received(1).TriggerPrimaryAction(expected, out Arg.Any<(int, int)>());
    }

    [Fact]
    public void SpaceKey_TargetLocked_StrikesAtTheTarget()
    {
        Vector2 target = _player + new Vector2(10f, 30f);
        _session.LockedTargetNetworkId.Returns(42UL);
        _session.RemoteEntities.Returns(new List<RemoteEntityView> { Monster(42UL, target) });

        this.Press(Key.Space);

        _session.Received(1).TriggerPrimaryAction(target, out Arg.Any<(int, int)>());
    }

    [Fact]
    public void WorldKeys_NoWindowOpen_HarvestPlaceAndDemolish()
    {
        this.Press(Key.F);
        this.Press(Key.H);
        this.Press(Key.Z);

        _session.Received(1).TryHarvestNearbyProp(out Arg.Any<(int, int)>());
        _session.Received(1).TryPlaceStarterWall(out Arg.Any<(int, int)>());
        _session.Received(1).TryDemolishStructure(Arg.Any<Vector2>(), out Arg.Any<(int, int)>());
    }

    [Fact]
    public void WorldKeys_WindowOpen_AreIgnored()
    {
        this.Press(Key.I);

        this.Press(Key.Space);
        this.Press(Key.F);
        this.Press(Key.H);
        this.Press(Key.Z);

        _session.DidNotReceive().TriggerPrimaryAction(Arg.Any<Vector2>(), out Arg.Any<(int, int)>());
        _session.DidNotReceive().TryHarvestNearbyProp(out Arg.Any<(int, int)>());
        _session.DidNotReceive().TryPlaceStarterWall(out Arg.Any<(int, int)>());
        _session.DidNotReceive().TryDemolishStructure(Arg.Any<Vector2>(), out Arg.Any<(int, int)>());
    }

    [Fact]
    public void DoorKey_DoorNextToThePlayer_TogglesIt()
    {
        int tx = (int)MathF.Floor(_player.X / 32f) + 1;
        int ty = (int)MathF.Floor(_player.Y / 32f);
        Maps.Building.StructureCatalog.LoadEmbedded().TryGet(Maps.Building.StructureType.WoodDoor, out Maps.Building.StructureDefinition door).Should().BeTrue();
        Maps.Building.StructureTile tile = door.CreateTile();
        _world.Buildings.PlaceStructure(tx, ty, in tile);

        this.Press(Key.G);

        _session.Received(1).TryToggleDoor(tx, ty);
    }

    [Fact]
    public void NumberKey_SelectsThatHotbarSlot()
    {
        this.Press(Key.Number3);

        _session.Received(1).SelectHotbarSlot(2);
    }

    [Fact]
    public void ScrollDown_NoWindowOpen_SelectsTheNextHotbarSlot()
    {
        _session.SelectedHotbarSlot.Returns((byte)8);

        this.Scroll(-1f);

        _session.Received(1).SelectHotbarSlot(0);
    }

    [Fact]
    public void LeftClick_OpenGround_StrikesUnderTheCursor()
    {
        Vector2 cursor = new(640f, 300f);

        this.Click(MouseButton.Left, cursor);

        Vector2 aim = _camera.ScreenToWorld(in cursor);
        _session.Received(1).TriggerPrimaryAction(aim, out Arg.Any<(int, int)>());
    }

    [Fact]
    public void LeftClick_OnAMonster_LocksItAndStrikes()
    {
        Vector2 cursor = new(640f, 300f);
        this.Frame();
        Vector2 aim = _camera.ScreenToWorld(in cursor);
        _session.RemoteEntities.Returns(new List<RemoteEntityView> { Monster(77UL, aim + new Vector2(5f, 0f)) });

        this.Click(MouseButton.Left, cursor);

        _session.Received(1).SetLockedTarget(77UL);
        _session.Received(1).TriggerPrimaryAction(aim, out Arg.Any<(int, int)>());
    }

    [Fact]
    public void LeftClick_OnTheBlacksmithInForgeReach_OpensTheForgeInsteadOfStriking()
    {
        Vector2 cursor = new(640f, 300f);
        this.Frame();
        Vector2 aim = _camera.ScreenToWorld(in cursor);
        _session.IsNearForgeStation(Arg.Any<float>()).Returns(true);
        _session.GetNpcCapabilities(Items.Catalog.WorldActorCatalog.MasterBlacksmith)
            .Returns(Items.Catalog.WorldActorCatalog.GetNpcSpec(Items.Catalog.WorldActorCatalog.MasterBlacksmith).Capabilities);
        _session.RemoteEntities.Returns(new List<RemoteEntityView> { Npc(9UL, Items.Catalog.WorldActorCatalog.MasterBlacksmith, aim + new Vector2(4f, 0f)) });

        this.Click(MouseButton.Left, cursor);

        this.Hud.ShowForgeModal.Should().BeTrue("clicking a forge artisan in reach opens the forge");
        _session.DidNotReceive().TriggerPrimaryAction(Arg.Any<Vector2>(), out Arg.Any<(int, int)>());
    }

    [Fact]
    public void RightClick_OpenGround_DemolishesUnderTheCursor()
    {
        Vector2 cursor = new(640f, 300f);

        this.Click(MouseButton.Right, cursor);

        Vector2 aim = _camera.ScreenToWorld(in cursor);
        _session.Received(1).TryDemolishStructure(aim, out Arg.Any<(int, int)>());
    }

    [Fact]
    public void SkillKey_CastsAtTheCursor()
    {
        this.Move(new Vector2(640f, 300f));

        this.Press(Key.Q);

        Vector2 aim = _camera.ScreenToWorld(new Vector2(640f, 300f));
        _session.Received(1).TryCastSkill(Items.Catalog.SkillCatalog.WhirlwindId, aim);
    }

    [Fact]
    public void DeathScreen_SwallowsKeysAndClosesWindows()
    {
        this.Press(Key.I);
        _session.LocalDeath.Returns(new DeathView(Contracts.Common.DeathCause.Monster, 0, "monster.wild_slime.name"));

        this.Press(Key.Space);
        this.Press(Key.Escape);

        this.Hud.HasActiveModal.Should().BeFalse("dying closes every window");
        this.Hud.ShowSettingsModal.Should().BeFalse();
        _session.DidNotReceive().TriggerPrimaryAction(Arg.Any<Vector2>(), out Arg.Any<(int, int)>());
    }

    private static RemoteEntityView Npc(ulong id, ushort archetype, Vector2 position) =>
        new(id, Contracts.Entities.EntityKind.Npc, "Blacksmith", position, Vector2.Zero, 8f, 100f, 100f, ArchetypeId: archetype);

    private static RemoteEntityView Monster(ulong id, Vector2 position) =>
        new(id, Contracts.Entities.EntityKind.Monster, "Slime", position, Vector2.Zero, 8f, 50f, 50f);

    private void Frame()
    {
        _scene.Update(_context, 1.0 / 60.0);
        _keyboard = new KeyboardSnapshot(new bool[512], new bool[512]);
        _mouse = new MouseSnapshot(_mouse.Position, _mouse.Position, Vector2.Zero, new bool[8], new bool[8]);
    }

    private void Press(Key key)
    {
        bool[] down = new bool[512];
        down[(int)key] = true;
        _keyboard = new KeyboardSnapshot(down, new bool[512]);
        this.Frame();
    }

    private void Hold(Key key)
    {
        bool[] down = new bool[512];
        down[(int)key] = true;
        _keyboard = new KeyboardSnapshot(down, down);
        _scene.Update(_context, 1.0 / 60.0);
        _keyboard = new KeyboardSnapshot(new bool[512], new bool[512]);
    }

    private void Move(Vector2 at)
    {
        _mouse = new MouseSnapshot(at, at, Vector2.Zero, new bool[8], new bool[8]);
        this.Frame();
    }

    private void Click(MouseButton button, Vector2 at)
    {
        _mouse = new MouseSnapshot(at, at, Vector2.Zero, new bool[8], new bool[8]);
        this.Frame();
        bool[] down = new bool[8];
        down[(int)button] = true;
        _mouse = new MouseSnapshot(at, at, Vector2.Zero, down, new bool[8]);
        this.Frame();
    }

    private void Scroll(float y)
    {
        _mouse = new MouseSnapshot(_mouse.Position, _mouse.Position, new Vector2(0f, y), new bool[8], new bool[8]);
        this.Frame();
    }
}
