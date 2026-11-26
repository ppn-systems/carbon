// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Entities.S2C;
using Carbon.Items.Catalog;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Maps server attack phases onto monster rig clips: picks a distinct clip per attack kind
/// (<c>attack</c> / <c>attack_slam</c> melee, <c>attack_bow</c> / <c>attack_cast</c> shot, <c>attack_spell</c> spell,
/// <c>attack_leap</c> leap) and converts (phase, phase progress) into normalized clip time using the clip's
/// <see cref="AttackPhaseMarks"/>, so animation beats line up with the server timeline for any profile duration.
/// </summary>
public static class MonsterAttackAnimator
{
    /// <summary>Generic fallback attack clip present on every monster rig.</summary>
    public const string FallbackClip = "attack";

    /// <summary>
    /// Resolves the clip a monster plays for <paramref name="attackKind"/>, falling back to <see cref="FallbackClip"/>.
    /// </summary>
    /// <param name="rig">Monster rig.</param>
    /// <param name="archetypeId">Monster archetype id.</param>
    /// <param name="attackKind">Attack kind byte.</param>
    /// <returns>A clip name the rig provides (or <see cref="FallbackClip"/>).</returns>
    public static string ResolveClip(CompiledVectorRig rig, ushort archetypeId, byte attackKind)
    {
        ArgumentNullException.ThrowIfNull(rig);

        string preferred = attackKind switch
        {
            EntityAttackPhase.KindMelee => archetypeId is WorldActorCatalog.RockGolem or WorldActorCatalog.CrimsonDemon ? "attack_slam" : FallbackClip,
            EntityAttackPhase.KindProjectile => rig.TryGetAnimation("attack_bow", out _) ? "attack_bow" : "attack_cast",
            EntityAttackPhase.KindSpell => rig.TryGetAnimation("attack_spell", out _) ? "attack_spell" : "attack_cast",
            EntityAttackPhase.KindLeap => "attack_leap",
            _ => FallbackClip,
        };

        return rig.TryGetAnimation(preferred, out _) ? preferred : FallbackClip;
    }

    /// <summary>
    /// Converts a server phase and local phase progress to normalized clip time.
    /// </summary>
    /// <param name="phase">Phase byte.</param>
    /// <param name="phaseProgress">Progress <c>[0, 1]</c> through the phase.</param>
    /// <param name="marks">Clip phase markers.</param>
    /// <returns>Normalized clip time <c>[0, 1]</c> (0 when no attack is running).</returns>
    public static float ToClipTime(byte phase, float phaseProgress, in AttackPhaseMarks marks)
    {
        float p = Math.Clamp(float.IsFinite(phaseProgress) ? phaseProgress : 0f, 0f, 1f);
        (float start, float end) = phase switch
        {
            EntityAttackPhase.PhaseWindup => (0f, marks.WindupEnd),
            EntityAttackPhase.PhaseTravel => (marks.WindupEnd, marks.TravelEnd),
            EntityAttackPhase.PhaseActive => (marks.TravelEnd, marks.ActiveEnd),
            EntityAttackPhase.PhaseRecovery => (marks.ActiveEnd, 1f),
            _ => (0f, 0f),
        };

        return start + ((end - start) * p);
    }
}
