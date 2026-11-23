// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.Scenes.Tests;

public sealed class SceneManagerModalTests
{
    private sealed class DummyScene : GameScene
    {
        public bool Loaded { get; private set; }
        public bool Entered { get; private set; }
        public bool Left { get; private set; }
        public bool Unloaded { get; private set; }

        public DummyScene(string name) : base(name) { }

        public override void Load(SceneContext context)
        {
            base.Load(context);
            this.Loaded = true;
        }

        public override void Enter(SceneContext context)
        {
            base.Enter(context);
            this.Entered = true;
        }

        public override void Leave(SceneContext context)
        {
            base.Leave(context);
            this.Left = true;
        }

        public override void Unload(SceneContext context)
        {
            base.Unload(context);
            this.Unloaded = true;
        }
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SceneManager")]
    public void PushAndPopScene_ShouldManageModalHierarchyCorrectly()
    {
        IGameWindow fakeWindow = Substitute.For<IGameWindow>();
        Camera2D camera = new(800f, 600f);

        using SceneManager manager = new(fakeWindow, camera);

        DummyScene baseScene = new("BaseWorld");
        DummyScene inventoryModal = new("InventoryModal");

        // Push base scene
        manager.PushScene(baseScene);
        manager.SceneCount.Should().Be(1);
        manager.CurrentScene.Should().BeSameAs(baseScene);
        baseScene.Loaded.Should().BeTrue();
        baseScene.Entered.Should().BeTrue();

        // Push modal inventory on top
        manager.PushScene(inventoryModal);
        manager.SceneCount.Should().Be(2);
        manager.CurrentScene.Should().BeSameAs(inventoryModal);
        inventoryModal.Loaded.Should().BeTrue();
        inventoryModal.Entered.Should().BeTrue();

        // Pop modal inventory
        bool popped = manager.PopScene();
        popped.Should().BeTrue();
        manager.SceneCount.Should().Be(1);
        manager.CurrentScene.Should().BeSameAs(baseScene);
        inventoryModal.Left.Should().BeTrue();
        inventoryModal.Unloaded.Should().BeTrue();

        // Cannot pop the root/last remaining scene
        bool popRoot = manager.PopScene();
        popRoot.Should().BeFalse();
        manager.SceneCount.Should().Be(1);
    }
}
