// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Carbon.Contracts.Common;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Items.Catalog;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-035 on the client: the server's durability alert becomes a localized toast and, for a break, shatter shards at the player.</summary>
public sealed class ItemBreakFeedbackTests
{
    private static readonly Vector2 s_player = new(320f, 96f);

    [Fact]
    public void Apply_Broke_ShowsTheLocalizedToastFloatsItOverThePlayerAndQueuesTheShatter()
    {
        Buffers b = new();

        DurabilityAlertFeedback.Apply(DurabilityAlertKind.Broke, StarterGameCatalog.IronSword, 4, "Iron Sword", s_player, b.Hud, b.Combat, b.Breaks);

        string expected = GameText.Format("durability.broke", new TextArg("item", "Iron Sword"));
        expected.Should().Contain("Iron Sword").And.NotBe("durability.broke", "the key must exist in the locale files");
        b.Hud.Should().Equal(expected);
        b.Combat.Should().ContainSingle().Which.Label.Should().Be(expected);
        b.Breaks.Should().Equal(new ItemBreakEvent(s_player, StarterGameCatalog.IronSword, 4));
    }

    [Fact]
    public void Apply_AboutToBreak_ShowsOnlyTheRepairWarningToast()
    {
        Buffers b = new();

        DurabilityAlertFeedback.Apply(DurabilityAlertKind.AboutToBreak, StarterGameCatalog.WoodAxe, 0, "Wood Axe", s_player, b.Hud, b.Combat, b.Breaks);

        string expected = GameText.Format("durability.about_to_break", new TextArg("item", "Wood Axe"));
        expected.Should().Contain("Wood Axe").And.NotBe("durability.about_to_break");
        b.Hud.Should().Equal(expected);
        b.Combat.Should().BeEmpty();
        b.Breaks.Should().BeEmpty("only a break shatters the item");
    }

    [Fact]
    public void Update_ItemBreakEventFromTheWorld_ThrowsShatterShardsAtThePlayer()
    {
        CombatFeedbackTracker tracker = new();
        IWorldReadModel world = BreakingWorld.Create(new ItemBreakEvent(s_player, StarterGameCatalog.IronSword, 3));

        tracker.Update(world, frameDt: 0f, animTime: 1f, aimedWorldPos: null);

        tracker.LiveParticleCount.Should().Be(CombatFeedbackTracker.ItemBreakShardCount + 3, "a +3 item throws three extra shards");
        tracker.Update(world, frameDt: 2f, animTime: 3f, aimedWorldPos: null);
        tracker.LiveParticleCount.Should().Be(0, "the shards fade out");
    }

    private sealed class Buffers
    {
        public List<string> Hud { get; } = [];

        public List<CombatFeedbackEvent> Combat { get; } = [];

        public List<ItemBreakEvent> Breaks { get; } = [];
    }

    /// <summary>
    /// A read model that yields one break event once and defaults for everything else the tracker reads
    /// (built with <see cref="DispatchProxy"/> so the test does not hand-write the whole interface).
    /// </summary>
    public class BreakingWorld : DispatchProxy
    {
        private ItemBreakEvent? _pending;

        public static IWorldReadModel Create(ItemBreakEvent broke)
        {
            IWorldReadModel world = Create<IWorldReadModel, BreakingWorld>();
            ((BreakingWorld)(object)world)._pending = broke;
            return world;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            ArgumentNullException.ThrowIfNull(args);
            if (targetMethod.Name == nameof(IWorldReadModel.DrainItemBreakEvents) && _pending is ItemBreakEvent broke)
            {
                ((List<ItemBreakEvent>)args[0]!).Add(broke);
                _pending = null;
                return null;
            }

            if (targetMethod.Name == "get_" + nameof(IWorldReadModel.RemoteEntities))
            {
                return Array.Empty<RemoteEntityView>();
            }

            Type returnType = targetMethod.ReturnType;
            return returnType == typeof(void) || !returnType.IsValueType ? null : Activator.CreateInstance(returnType);
        }
    }
}
