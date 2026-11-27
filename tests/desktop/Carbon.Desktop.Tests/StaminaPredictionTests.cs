// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Carbon.Configuration;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Networking.Session;
using Carbon.Items.Definitions;
using Carbon.Survival.Components;
using Carbon.Survival.Stamina;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The client's stamina prediction (ADR-033), driven through <see cref="MultiplayerSession"/>'s own stamina members:
/// sprint drain and refusal, regen by movement, rest and hunger, the regen delay, refused spends and swings with their
/// flash and message, and the server's exhaustion and refusal reconciliation. Pinned before the prediction moved out of
/// the session (audit round 3, SRP-12); the session members it drives stay as the session's thin wrappers.
/// </summary>
public sealed class StaminaPredictionTests : IDisposable
{
    // Round numbers so every expectation is exact: regen waits 1 s after a spend, 10/s still, 5/s moving, x2 resting.
    private const string Config = """
        {
          "sprintDrainPerSecond": 20, "swimDrainPerSecond": 5, "regenDelaySeconds": 1,
          "regenPerSecondStill": 10, "regenPerSecondMoving": 5, "restingRegenMultiplier": 2,
          "exhaustedRecoverFraction": 0.25, "minimumRegenScale": 0.25,
          "fatigueRegenScale": { "tired": 0.5, "veryTired": 0.5, "exhausted": 0.5 },
          "hungerRegenScale": { "hungry": 0.5, "urgentlyHungry": 0.5, "starving": 0.5 },
          "swingCosts": { "bareHand": 2, "melee": 5, "tool": 5, "ranged": 4, "spell": 0 },
          "itemSwingCosts": {}, "buildWorkPerSecond": 4
        }
        """;

    private readonly SessionStaminaDriver _session = new();

    public void Dispose() => _session.Dispose();

    [Fact]
    public void Tick_Sprinting_DrainsAndReportsSprint()
    {
        _session.ApplyConfig();
        _session.Reset(100f);

        _session.Tick(0.5f, wantsSprint: true, isMoving: true).Should().BeTrue();

        _session.Current.Should().Be(90f);
    }

    [Fact]
    public void Tick_SprintDrainsToZero_ExhaustsAndRefusesTheNextSprint()
    {
        _session.ApplyConfig();
        _session.Reset(10f);

        _session.Tick(1f, wantsSprint: true, isMoving: true).Should().BeTrue();
        _session.Exhausted.Should().BeTrue();
        _session.Tick(0.1f, wantsSprint: true, isMoving: true).Should().BeFalse("an exhausted player walks");
    }

    [Fact]
    public void Tick_AfterASpend_WaitsTheRegenDelayThenRegeneratesByMovement()
    {
        _session.ApplyConfig();
        _session.Reset(100f);
        _session.Tick(1f, wantsSprint: true, isMoving: true);

        _session.Tick(1f, wantsSprint: false, isMoving: false).Should().BeFalse();
        _session.Current.Should().Be(80f, "regen waits one second after a spend");
        _session.Tick(1f, wantsSprint: false, isMoving: false);
        _session.Current.Should().Be(90f, "10/s standing still");
        _session.Tick(1f, wantsSprint: false, isMoving: true);
        _session.Current.Should().Be(95f, "5/s while walking");
    }

    [Fact]
    public void Tick_RestingAndHungry_ScalesTheRegen()
    {
        _session.ApplyConfig();
        _session.Reset(50f);

        _session.SetRegenState(RestMode.Sitting, RestState.Rested, HungerState.Fed);
        _session.Tick(1f, wantsSprint: false, isMoving: false);
        _session.Current.Should().Be(70f, "resting doubles 10/s");

        _session.SetRegenState(RestMode.None, RestState.Rested, HungerState.Hungry);
        _session.Tick(1f, wantsSprint: false, isMoving: false);
        _session.Current.Should().Be(75f, "hunger halves 10/s");
    }

    [Fact]
    public void Tick_RegenNeverExceedsTheMaximum()
    {
        _session.ApplyConfig();
        _session.Reset(98f);

        _session.Tick(1f, wantsSprint: false, isMoving: false);

        _session.Current.Should().Be(100f);
    }

    [Fact]
    public void Tick_WithoutASyncedConfig_PredictsNothingAndSprintFollowsTheMeter()
    {
        _session.Reset(30f);

        _session.Tick(1f, wantsSprint: true, isMoving: true).Should().BeTrue();
        _session.Current.Should().Be(30f);
        _session.Tick(1f, wantsSprint: false, isMoving: false).Should().BeFalse();
        _session.Current.Should().Be(30f);
    }

    [Fact]
    public void TrySpend_Affordable_PaysWithoutAFlash()
    {
        _session.ApplyConfig();
        _session.Reset(30f);

        _session.TrySpend(12f).Should().BeTrue();

        _session.Current.Should().Be(18f);
        _session.Flash.Should().Be(0f);
        _session.Notifications.Should().BeEmpty();
    }

    [Fact]
    public void TrySpend_TooExpensive_RefusesExhaustsFlashesAndSaysNotEnough()
    {
        _session.ApplyConfig();
        _session.Reset(5f);

        _session.TrySpend(12f).Should().BeFalse();

        _session.Current.Should().Be(5f);
        _session.Exhausted.Should().BeTrue("a refused spend exhausts, as on the server");
        _session.Flash.Should().Be(1f);
        _session.Notifications.Should().Equal(GameText.Get("hud.not_enough_stamina"));
    }

    [Fact]
    public void TrySpend_AlreadyExhausted_SaysTooTired()
    {
        _session.ApplyConfig();
        _session.Reset(5f);
        _session.TrySpend(12f);

        _session.TrySpend(1f).Should().BeFalse();

        _session.Notifications.Should().Equal(GameText.Get("hud.not_enough_stamina"), GameText.Get("stamina.too_tired"));
    }

    [Fact]
    public void TrySpend_WithoutASyncedConfig_ChecksOnlyAffordabilityAndPaysNothing()
    {
        _session.Reset(10f);

        _session.TrySpend(4f).Should().BeTrue();
        _session.Current.Should().Be(10f);
        _session.TrySpend(11f).Should().BeFalse();
        _session.Exhausted.Should().BeFalse();
        _session.Notifications.Should().Equal(GameText.Get("hud.not_enough_stamina"));
    }

    [Fact]
    public void Flash_FadesOverPointSixSeconds()
    {
        _session.ApplyConfig();
        _session.Reset(5f);
        _session.TrySpend(12f);

        _session.Tick(0.3f, wantsSprint: false, isMoving: false);
        _session.Flash.Should().BeApproximately(0.5f, 1e-4f);
        _session.Tick(0.6f, wantsSprint: false, isMoving: false);
        _session.Flash.Should().Be(0f);
    }

    [Fact]
    public void RefuseSwing_Exhausted_RefusesCostlySwingsButNotFreeCasts()
    {
        _session.ApplyConfig();
        _session.Reset(5f);
        _session.RefuseSwing(null).Should().BeFalse("not exhausted yet");
        _session.TrySpend(12f);

        _session.RefuseSwing(null).Should().BeTrue("a bare-hand swing costs 2");
        SyncedItemCatalog items = new();
        items.Rules.Items.TryGet(items.Rules.ItemCatalog.ResolveId("arcane_staff"), out ItemArchetype? staff).Should().BeTrue();
        _session.RefuseSwing(staff).Should().BeFalse("a staff cast is free");
        _session.Notifications.Should().Equal(GameText.Get("hud.not_enough_stamina"), GameText.Get("stamina.too_tired"));
    }

    [Fact]
    public void StatusSync_MirrorsExhaustionAndFlashesOnlyOnANewRefusal()
    {
        _session.ApplyConfig();
        _session.Reset(80f);

        _session.StatusSync(exhausted: true, refusalCount: 0U);
        _session.Exhausted.Should().BeTrue();
        _session.Flash.Should().Be(0f, "no refusal counted yet");

        _session.StatusSync(exhausted: false, refusalCount: 2U);
        _session.Exhausted.Should().BeFalse();
        _session.Flash.Should().Be(1f, "the server refused something the client let through");

        _session.Tick(1f, wantsSprint: false, isMoving: false);
        _session.StatusSync(exhausted: false, refusalCount: 2U);
        _session.Flash.Should().Be(0f, "the same count is not a new refusal");
        _session.Notifications.Should().BeEmpty("the server's own message reports its refusal");
    }

    [Fact]
    public void Reset_ForgetsTheRefusalCountAndTheFlash()
    {
        _session.ApplyConfig();
        _session.Reset(80f);
        _session.StatusSync(exhausted: true, refusalCount: 3U);

        _session.Reset(60f);

        _session.Current.Should().Be(60f);
        _session.Exhausted.Should().BeFalse();
        _session.Flash.Should().Be(0f);
        _session.StatusSync(exhausted: false, refusalCount: 1U);
        _session.Flash.Should().Be(1f, "after a reset any count above zero is new");
    }

    /// <summary>Drives the session's private stamina members (the wrappers that call the prediction).</summary>
    private sealed class SessionStaminaDriver : IDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly MultiplayerSession _session = new(new DesktopOptions());

        public float Current => _session.LocalPlayerStamina;

        public void Dispose() => _session.DisposeAsync().AsTask().GetAwaiter().GetResult();

        public bool Exhausted => _session.LocalPlayerStaminaExhausted;

        public float Flash => _session.LocalPlayerStaminaFlash;

        public List<string> Notifications => (List<string>)Field("_hudNotifications").GetValue(_session)!;

        public void ApplyConfig()
        {
            byte[] json = Encoding.UTF8.GetBytes(Config);
            _session.ApplyConfigCatalog(new ConfigCatalog(1, [new ConfigCatalogEntry(StaminaConfig.DocumentName, ConfigContentHash.Compute(json), 1, json)]));
        }

        public void Reset(float current) => this.Call("ResetStaminaPrediction", current);

        public bool Tick(float deltaSeconds, bool wantsSprint, bool isMoving) => (bool)this.Call("PredictStaminaTick", deltaSeconds, wantsSprint, isMoving)!;

        public bool TrySpend(float cost) => (bool)this.Call("TryPredictStaminaSpend", cost)!;

        public bool RefuseSwing(ItemArchetype? held) => (bool)this.Call("RefuseSwingWhileExhausted", held)!;

        public void StatusSync(bool exhausted, uint refusalCount) =>
            this.Call("OnStaminaStatusSync", new StaminaStatusSync { Exhausted = exhausted, RefusalCount = refusalCount });

        public void SetRegenState(RestMode mode, RestState fatigue, HungerState hunger)
        {
            Field("_localRestMode").SetValue(_session, mode);
            Field("_localRestState").SetValue(_session, (byte)fatigue);
            Field("_localHungerState").SetValue(_session, (byte)hunger);
        }

        private static FieldInfo Field(string name) => typeof(MultiplayerSession).GetField(name, Private)!;

        private object? Call(string name, params object?[] args) => typeof(MultiplayerSession).GetMethod(name, Private)!.Invoke(_session, args);
    }
}
