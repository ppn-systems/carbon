// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading.Tasks;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Persistence;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Host.Tests;

/// <summary>
/// A damage-over-time pulse on a player (ADR-029) is saved at the next flush, and pulsing does not build a character
/// record on the tick (audit round 3, ALLOC-2: the pulse used to snapshot the whole record, progression blob and status
/// string included, once per pulse per afflicted player).
/// </summary>
public sealed class StatusPulsePersistenceTests(ITestOutputHelper output)
{
    /// <summary>Non-lethal poison for the whole run: one point every 2 s (status_effects.json).</summary>
    private static readonly WorldDebugOverrides s_poisoned = new() { StatusEffects = "poisoned:600000:1" };

    [Fact]
    public async Task FlushDirtyPersistenceAsync_AfterPoisonPulses_SavesTheLostHealthAndTheEffect()
    {
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 1, overrides: s_poisoned);
        for (int i = 0; i < 200; i++)
        {
            // No move input: a player who walks is saved for moving, which would hide a pulse that is never flagged.
            harness.World.StepFixedTick();
        }

        await harness.World.FlushDirtyPersistenceAsync().ConfigureAwait(true);

        CharacterRecord? saved = await harness.Characters.FindByIdAsync(harness.CharacterIds[0]).ConfigureAwait(true);
        saved.Should().NotBeNull();
        saved!.CurrentHealth.Should().BeLessThan(100f, "about three pulses landed in 200 ticks");
        saved.StatusEffects.Should().Contain("poisoned:", "the effect and its remaining time are saved with the pulse");
    }

    [Fact]
    public void StepFixedTick_PoisonedPlayers_StaysWithinTheTickAllocationBudget()
    {
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 8, overrides: s_poisoned);
        for (int i = 0; i < 900; i++)
        {
            harness.Step();
        }

        (double bytesPerTick, _) = harness.Measure(900);
        output.WriteLine($"8 poisoned players: {bytesPerTick:F1} B/tick");

        bytesPerTick.Should().BeLessThanOrEqualTo(
            ServerTickAllocationTests.MaxBytesPerTick,
            "a status pulse only flags the player; the record is built at flush time (AGENTS.md rule 4)");
    }
}
