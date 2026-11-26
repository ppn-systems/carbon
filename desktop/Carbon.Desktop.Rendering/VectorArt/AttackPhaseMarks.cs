// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Normalized clip times (<c>0..1</c>) at which the server attack phases end inside an attack clip
/// (<c>phaseMarks</c> in <c>.cvg.json</c>): windup <c>[0, WindupEnd)</c>, travel <c>[WindupEnd, TravelEnd)</c>,
/// active <c>[TravelEnd, ActiveEnd)</c>, recovery <c>[ActiveEnd, 1]</c>.
/// </summary>
/// <param name="WindupEnd">End of the windup segment.</param>
/// <param name="TravelEnd">End of the travel segment (equal to <paramref name="WindupEnd"/> when unused).</param>
/// <param name="ActiveEnd">End of the active segment.</param>
public readonly record struct AttackPhaseMarks(float WindupEnd, float TravelEnd, float ActiveEnd)
{
    /// <summary>Gets markers used by clips that do not declare <c>phaseMarks</c>.</summary>
    public static AttackPhaseMarks Default { get; } = new(0.40f, 0.55f, 0.70f);

    /// <summary>
    /// Creates clamped, monotonic markers.
    /// </summary>
    /// <param name="windupEnd">End of windup.</param>
    /// <param name="travelEnd">End of travel.</param>
    /// <param name="activeEnd">End of active.</param>
    /// <returns>Markers satisfying <c>0 &lt;= WindupEnd &lt;= TravelEnd &lt;= ActiveEnd &lt;= 1</c>.</returns>
    public static AttackPhaseMarks Create(float windupEnd, float travelEnd, float activeEnd)
    {
        float w = Math.Clamp(windupEnd, 0f, 1f);
        float t = Math.Clamp(travelEnd, w, 1f);
        float a = Math.Clamp(activeEnd, t, 1f);
        return new AttackPhaseMarks(w, t, a);
    }
}
