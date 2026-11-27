// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities.S2C;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Items.Catalog;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

public sealed class MonsterAttackAnimationTests
{
    private static readonly ushort[] s_monsters =
    [
        WorldActorCatalog.WildSlime,
        WorldActorCatalog.RockGolem,
        WorldActorCatalog.FrostWolf,
        WorldActorCatalog.AshWraith,
        WorldActorCatalog.SkeletonArcher,
        WorldActorCatalog.OrcShaman,
        WorldActorCatalog.CrimsonDemon,
        WorldActorCatalog.OrcWarlord,
        WorldActorCatalog.SlimeKing,
    ];

    [Theory]
    [InlineData(WorldActorCatalog.WildSlime, EntityAttackPhase.KindLeap, "attack_leap")]
    [InlineData(WorldActorCatalog.WildSlime, EntityAttackPhase.KindMelee, "attack")]
    [InlineData(WorldActorCatalog.FrostWolf, EntityAttackPhase.KindLeap, "attack_leap")]
    [InlineData(WorldActorCatalog.SkeletonArcher, EntityAttackPhase.KindProjectile, "attack_bow")]
    [InlineData(WorldActorCatalog.RockGolem, EntityAttackPhase.KindMelee, "attack_slam")]
    [InlineData(WorldActorCatalog.AshWraith, EntityAttackPhase.KindSpell, "attack_spell")]
    [InlineData(WorldActorCatalog.AshWraith, EntityAttackPhase.KindProjectile, "attack_cast")]
    [InlineData(WorldActorCatalog.OrcShaman, EntityAttackPhase.KindSpell, "attack_spell")]
    [InlineData(WorldActorCatalog.OrcShaman, EntityAttackPhase.KindProjectile, "attack_cast")]
    [InlineData(WorldActorCatalog.CrimsonDemon, EntityAttackPhase.KindLeap, "attack_leap")]
    [InlineData(WorldActorCatalog.CrimsonDemon, EntityAttackPhase.KindMelee, "attack_slam")]
    [InlineData(WorldActorCatalog.OrcWarlord, EntityAttackPhase.KindMelee, "attack_slam")]
    [InlineData(WorldActorCatalog.OrcWarlord, EntityAttackPhase.KindLeap, "attack_leap")]
    [InlineData(WorldActorCatalog.SlimeKing, EntityAttackPhase.KindLeap, "attack_leap")]
    public void ResolveClip_ShippedRigs_UseDistinctClipPerAttackKind(ushort archetype, byte kind, string expected)
    {
        CompiledVectorRig rig = LoadRig(archetype);

        MonsterAttackAnimator.ResolveClip(rig, archetype, kind).Should().Be(expected);
    }

    [Fact]
    public void ShippedRigs_AttackClips_DeclarePhaseMarksAndDifferBetweenKinds()
    {
        foreach (ushort archetype in s_monsters)
        {
            CompiledVectorRig rig = LoadRig(archetype);
            HashSet<string> clips = [];
            for (byte kind = EntityAttackPhase.KindMelee; kind <= EntityAttackPhase.KindLeap; kind++)
            {
                string clip = MonsterAttackAnimator.ResolveClip(rig, archetype, kind);
                rig.TryGetAnimation(clip, out CompiledVectorAnimation? anim).Should().BeTrue();
                anim!.PhaseMarks.Should().NotBe(AttackPhaseMarks.Default, $"{archetype}/{clip} must declare phaseMarks aligned to its key poses");
                anim.Keyframes.Length.Should().BeGreaterThanOrEqualTo(4, $"{archetype}/{clip} needs windup, release/impact and recovery poses");
                clips.Add(clip);
            }

            clips.Count.Should().BeGreaterThanOrEqualTo(2, $"archetype {archetype} must not reuse one clip for every attack kind");
        }
    }

    [Fact]
    public void ShippedRigs_EliteFlagsAndOverlayAnchors_AreDeclared()
    {
        foreach (ushort archetype in s_monsters)
        {
            CompiledVectorRig rig = LoadRig(archetype);
            bool expectElite = archetype is WorldActorCatalog.OrcWarlord or WorldActorCatalog.SlimeKing;
            rig.IsElite.Should().Be(expectElite, $"catalog elite flag for {rig.Id}");
            rig.Markers.Eyes.Should().HaveCountGreaterThanOrEqualTo(2, $"{rig.Id} needs south-facing eye anchors for the night glow");
            rig.Markers.EyesSide.Should().NotBeEmpty($"{rig.Id} needs a profile eye anchor");
            rig.Markers.EyesFor(isSide: false, isNorth: true).IsEmpty.Should().BeTrue("eyes are hidden when facing away");
            rig.Markers.Crown.Y.Should().BeLessThan(0f).And.BeGreaterThanOrEqualTo(-rig.DesignHeight * 1.3f, $"{rig.Id} crown sits near the head top");
        }

        LoadRig(WorldActorCatalog.SlimeKing).WorldHeight.Should().BeGreaterThan(LoadRig(WorldActorCatalog.WildSlime).WorldHeight, "elites read bigger");
        LoadRig(WorldActorCatalog.OrcWarlord).WorldHeight.Should().BeGreaterThan(LoadRig(WorldActorCatalog.OrcShaman).WorldHeight);
    }

    [Fact]
    public void ToClipTime_PhasesInOrder_IsMonotonicAndHitsMarkers()
    {
        AttackPhaseMarks marks = AttackPhaseMarks.Create(0.4f, 0.6f, 0.8f);
        float previous = -1f;
        for (byte phase = EntityAttackPhase.PhaseWindup; phase <= EntityAttackPhase.PhaseRecovery; phase++)
        {
            for (float p = 0f; p <= 1f; p += 0.125f)
            {
                float t = MonsterAttackAnimator.ToClipTime(phase, p, in marks);
                t.Should().BeGreaterThanOrEqualTo(previous);
                previous = t;
            }
        }

        MonsterAttackAnimator.ToClipTime(EntityAttackPhase.PhaseWindup, 1f, in marks).Should().BeApproximately(0.4f, 1e-6f);
        MonsterAttackAnimator.ToClipTime(EntityAttackPhase.PhaseActive, 0f, in marks).Should().BeApproximately(0.6f, 1e-6f);
        MonsterAttackAnimator.ToClipTime(EntityAttackPhase.PhaseRecovery, 1f, in marks).Should().Be(1f);
        MonsterAttackAnimator.ToClipTime(EntityAttackPhase.PhaseActive, float.NaN, in marks).Should().BeApproximately(0.6f, 1e-6f);
    }

    private static CompiledVectorRig LoadRig(ushort archetype)
    {
        string artRoot = VectorArtValidator.TryFindArtRootDirectory() ?? throw new InvalidOperationException("art root not found");
        CarbonVectorArtLibrary lib = CarbonVectorArtLibrary.LoadFromDirectory(artRoot);
        lib.TryGetMonster(archetype, out CompiledVectorRig? rig).Should().BeTrue();
        return rig!;
    }

    [Fact]
    public void ResolveFacing_ActiveAttackAfterBackingOff_FacesAimNotMovement()
    {
        Vector2 backingOff = new(-1f, 0f);
        AttackPhaseView aiming = new(EntityAttackPhase.KindProjectile, EntityAttackPhase.PhaseWindup, 0.3f, new Vector2(200f, 0f), new Vector2(3f, 0f), 0f, 0f, 0);

        MonsterAttackAnimator.ResolveFacing(aiming, backingOff).Should().Be(Vector2.UnitX, "an archer turns to its target while it draws");
        MonsterAttackAnimator.ResolveFacing(default, backingOff).Should().Be(backingOff, "without an attack the rig keeps its movement facing");
    }
}
