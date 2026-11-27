// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Globalization;
using System.Text;
using Carbon.Abstractions.World;
using Carbon.Items.Catalog;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Host.Tests;

/// <summary>
/// Measurement harness for the ADR-033 amendment: plays the owner's activities in a real world on the shipped config at
/// levels 1, 10 and 30 and prints the table. It asserts nothing; the pinned outcomes live in
/// <see cref="StaminaPacingTests"/>. Run it with <c>--filter FullyQualifiedName~StaminaPacingHarness</c> and
/// <c>--logger "console;verbosity=detailed"</c>.
/// </summary>
public sealed class StaminaPacingHarness(ITestOutputHelper output)
{
    private const uint CopperSword = 180;
    private const uint WoodSword = 189;
    private const uint StoneAxe = 190;
    private const uint IronAxe = 181;
    private const ushort AnyMonster = 0; // slimes stop at level 15: level 30 fights whatever lives there

    [Fact]
    [Trait("Category", "Measurement")]
    public void Measure_ShippedStamina_PrintsThePacingTable()
    {
        // Typical builds: level-1 has no points; later levels split their points evenly over the five attributes.
        (HeroBuild Build, ushort MonsterArchetype, int MonsterLevel, float ProbeFrom)[] rows =
        [
            (new HeroBuild(1, 0, 0, WoodSword, StarterGameCatalog.WoodAxe), StaminaPacingWorld.WildSlimeArchetype, 1, 0f),
            (new HeroBuild(10, 8, 8, CopperSword, StoneAxe), StaminaPacingWorld.WildSlimeArchetype, 10, 9000f),
            (new HeroBuild(30, 28, 28, StarterGameCatalog.IronSword, IronAxe), AnyMonster, 30, 42000f),
        ];

        StringBuilder table = new();
        table.AppendLine("| Level | Max | Swings to refusal | Oak: s / swings / stops | Fight: monster / seconds / hits / min stamina / exhausted | Sprint s | Rest: unlock / full s | 60 s of swinging: swings / stops |");
        table.AppendLine("| :--- | ---: | ---: | :--- | :--- | ---: | :--- | :--- |");
        foreach ((HeroBuild build, ushort archetype, int level, float probeFrom) in rows)
        {
            StaminaPacingWorld swinger = StaminaPacingWorld.Create(build);
            float max = swinger.MaxStamina();
            int swings = swinger.SwingUntilRefused();
            (float unlocked, float full) = swinger.RestFromExhaustion();
            (int sustained, int rests) = StaminaPacingWorld.Create(build).SwingFor(60);

            ChopResult chop = StaminaPacingWorld.Create(build).ChopOak();

            StaminaPacingWorld fighter = StaminaPacingWorld.CreateNextToMonster(build, archetype, level, out EntitySpawnSnapshot mob, probeFrom);
            FightResult fight = fighter.Fight(mob);

            float sprint = StaminaPacingWorld.Create(build).SprintUntilExhausted();

            table.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {build.Level} | {max:0} | {swings} | {(chop.Felled ? "" : "NOT FELLED ")}{chop.Seconds:0.0} / {chop.Swings} / {chop.Rests} | {mob.Name} L{mob.Level} {mob.MaxHealth:0} HP / {(fight.Killed ? "" : "NOT KILLED ")}{fight.Seconds:0.0} / {fight.Hits} / {fight.MinStamina:0} / {fight.Exhaustions} | {sprint:0.0} | {unlocked:0.0} / {full:0.0} | {sustained} / {rests} |"));
        }

        output.WriteLine(table.ToString());
    }
}
