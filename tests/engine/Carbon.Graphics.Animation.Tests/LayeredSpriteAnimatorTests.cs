// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Animation.Controllers;
using Carbon.Graphics.Animation.Directions;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.Animation.Tests;

public sealed class LayeredSpriteAnimatorTests
{
    private static AnimationClip MakeDummyClip(string name) =>
        new(name, [new AnimationFrame(default, 0.5f)]);

    [Fact]
    public void AddFindAndRemoveLayer_ShouldManageCollectionCorrectly()
    {
        LayeredSpriteAnimator animator = new();
        AnimationLayer body = new("Body", zOrder: 0);
        AnimationLayer armor = new("Armor", zOrder: 1);

        animator.AddLayer(body);
        animator.AddLayer(armor);

        animator.Layers.Should().HaveCount(2);
        animator.FindLayer("Body").Should().BeSameAs(body);
        animator.FindLayer("Armor").Should().BeSameAs(armor);
        animator.FindLayer("Unknown").Should().BeNull();

        bool removed = animator.RemoveLayer("Body");
        removed.Should().BeTrue();
        animator.Layers.Should().HaveCount(1);
        animator.FindLayer("Body").Should().BeNull();

        bool removeAgain = animator.RemoveLayer("Body");
        removeAgain.Should().BeFalse();
    }

    [Fact]
    public void SetLayerProperties_ShouldMutateLayerFields()
    {
        LayeredSpriteAnimator animator = new();
        AnimationLayer weapon = new("Weapon", zOrder: 2);
        animator.AddLayer(weapon);

        animator.SetLayerVisibility("Weapon", false);
        weapon.IsVisible.Should().BeFalse();

        animator.SetLayerTint("Weapon", ColorRgba.Red);
        weapon.Tint.Should().Be(ColorRgba.Red);

        animator.SetLayerZOrder("Weapon", 5);
        weapon.ZOrder.Should().Be(5);
    }

    [Fact]
    public void SetDirection_AndUpdate_ShouldDriveMasterAnimator()
    {
        LayeredSpriteAnimator animator = new();
        AnimationClip clip = MakeDummyClip("idle");
        animator.MasterAnimator.Play(clip);

        animator.SetDirection(Direction8.NorthWest);
        animator.Direction.Should().Be(Direction8.NorthWest);
        animator.MasterAnimator.Direction.Should().Be(Direction8.NorthWest);

        animator.Update(0.25f);
        animator.MasterAnimator.ElapsedTime.Should().Be(0.25f);
    }
}
