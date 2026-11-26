// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Entities.S2C;
using Carbon.Desktop.Abstractions.World;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.Effects;

/// <summary>
/// Draws honest, server-driven ground telegraphs for monster attacks from <see cref="AttackPhaseView"/>:
/// melee arcs grow during the wind-up, shots show an aim line, spells mark their locked circle / cone through the cast
/// and the delayed detonation, leaps mark the landing zone. Zero-allocation (<c>stackalloc</c> arc buffers).
/// </summary>
public static class AttackTelegraphRenderer
{
    private const int ArcSegments = 14;

    private static readonly ColorRgba s_meleeColor = new(250, 204, 21, 255);
    private static readonly ColorRgba s_shotColor = new(248, 113, 113, 255);
    private static readonly ColorRgba s_spellColor = new(192, 132, 252, 255);
    private static readonly ColorRgba s_leapColor = new(249, 115, 22, 255);

    /// <summary>
    /// Draws the telegraph for <paramref name="attack"/> under the attacker.
    /// </summary>
    /// <param name="batch">Geometry batch (world space).</param>
    /// <param name="attackerPos">Attacker foot position.</param>
    /// <param name="attack">Attack phase view.</param>
    /// <param name="animTime">Global animation time in seconds (for pulses).</param>
    public static void Draw(GeometryBatch batch, Vector2 attackerPos, in AttackPhaseView attack, float animTime)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!attack.IsActive || attack.Phase == EntityAttackPhase.PhaseRecovery)
        {
            return;
        }

        float p = Math.Clamp(attack.PhaseProgress, 0f, 1f);
        bool windup = attack.Phase == EntityAttackPhase.PhaseWindup;
        bool active = attack.Phase == EntityAttackPhase.PhaseActive;
        float pulse = 0.5f + (0.5f * MathF.Sin(animTime * 14f));
        Vector2 dir = attack.Direction.LengthSquared() > 1e-4f ? Vector2.Normalize(attack.Direction) : Vector2.UnitX;

        switch (attack.AttackKind)
        {
            case EntityAttackPhase.KindMelee:
                if (windup || attack.Phase == EntityAttackPhase.PhaseTravel)
                {
                    float grow = windup ? p : 1f;
                    DrawWedge(batch, attackerPos, dir, attack.AreaRadius, attack.HalfAngleRadians, s_meleeColor, fillAlpha: (byte)(40 + (60 * grow)), fillScale: grow);
                }
                else if (active)
                {
                    DrawWedge(batch, attackerPos, dir, attack.AreaRadius, attack.HalfAngleRadians, s_meleeColor, fillAlpha: (byte)(150 * (1f - p)), fillScale: 1f);
                }

                break;

            case EntityAttackPhase.KindProjectile:
                if (windup)
                {
                    DrawDashedLine(batch, attackerPos, attack.Target, WithAlpha(s_shotColor, (byte)(70 + (150 * p))), 1.2f + (1.4f * p));
                    float reticle = 9f - (4f * p);
                    batch.DrawCircle(attack.Target, reticle, WithAlpha(s_shotColor, (byte)(120 + (120 * p))), 12, 1.4f);
                    batch.DrawLine(attack.Target - new Vector2(reticle + 3f, 0f), attack.Target + new Vector2(reticle + 3f, 0f), WithAlpha(s_shotColor, 150), 1f);
                    batch.DrawLine(attack.Target - new Vector2(0f, reticle + 3f), attack.Target + new Vector2(0f, reticle + 3f), WithAlpha(s_shotColor, 150), 1f);
                }

                break;

            case EntityAttackPhase.KindSpell:
                {
                    bool cone = attack.Shape == 1;
                    float fill = windup ? p : 1f;
                    byte alpha = active ? (byte)(190 * (1f - p)) : (byte)(windup ? 35 + (45 * p) : 70 + (60 * pulse));
                    if (cone)
                    {
                        DrawWedge(batch, attackerPos, dir, attack.AreaRadius, attack.HalfAngleRadians, s_spellColor, alpha, fill);
                    }
                    else
                    {
                        DrawZone(batch, attack.Target, attack.AreaRadius, s_spellColor, alpha, fill, runes: !active, animTime);
                    }
                }

                break;

            case EntityAttackPhase.KindLeap:
                {
                    float fill = windup ? p : 1f;
                    byte alpha = active ? (byte)(200 * (1f - p)) : (byte)(windup ? 40 + (50 * p) : 90 + (60 * pulse));
                    DrawZone(batch, attack.Target, attack.AreaRadius, s_leapColor, alpha, fill, runes: false, animTime);
                    if (!active)
                    {
                        DrawDashedLine(batch, attackerPos, attack.Target, WithAlpha(s_leapColor, 110), 1.2f);
                    }
                }

                break;

            default:
                break;
        }
    }

    private static void DrawZone(GeometryBatch batch, Vector2 center, float radius, ColorRgba color, byte fillAlpha, float fillScale, bool runes, float animTime)
    {
        batch.FillCircle(center, MathF.Max(1f, radius * fillScale), WithAlpha(color, fillAlpha), 28);
        batch.DrawCircle(center, radius, WithAlpha(color, 230), 28, 1.8f);
        batch.DrawCircle(center, radius * 0.62f, WithAlpha(color, 120), 22, 1.1f);
        if (runes)
        {
            // Rotating rune ticks read as a magic circle rather than a plain AoE ring.
            for (int i = 0; i < 6; i++)
            {
                float a = (animTime * 1.6f) + (i * (MathF.PI / 3f));
                Vector2 r = new(MathF.Cos(a), MathF.Sin(a));
                batch.DrawLine(center + (r * radius * 0.70f), center + (r * radius * 0.92f), WithAlpha(color, 200), 1.6f);
            }
        }
    }

    private static void DrawWedge(GeometryBatch batch, Vector2 origin, Vector2 dir, float radius, float halfAngle, ColorRgba color, byte fillAlpha, float fillScale)
    {
        float half = Math.Clamp(halfAngle, 0.05f, MathF.PI);
        float baseAngle = MathF.Atan2(dir.Y, dir.X);
        Span<Vector2> fillPts = stackalloc Vector2[ArcSegments + 2];
        Span<Vector2> edgePts = stackalloc Vector2[ArcSegments + 1];
        fillPts[0] = origin;
        float fillRadius = MathF.Max(1f, radius * fillScale);
        for (int i = 0; i <= ArcSegments; i++)
        {
            float a = baseAngle - half + (2f * half * i / ArcSegments);
            Vector2 r = new(MathF.Cos(a), MathF.Sin(a));
            fillPts[i + 1] = origin + (r * fillRadius);
            edgePts[i] = origin + (r * radius);
        }

        // A wedge wider than 180 degrees is not convex: fan it as triangles.
        ColorRgba fill = WithAlpha(color, fillAlpha);
        for (int i = 1; i < fillPts.Length - 1; i++)
        {
            batch.FillTriangle(origin, fillPts[i], fillPts[i + 1], fill);
        }

        ColorRgba edge = WithAlpha(color, 220);
        for (int i = 0; i < edgePts.Length - 1; i++)
        {
            batch.DrawLine(edgePts[i], edgePts[i + 1], edge, 1.6f);
        }

        batch.DrawLine(origin, edgePts[0], WithAlpha(color, 140), 1.1f);
        batch.DrawLine(origin, edgePts[^1], WithAlpha(color, 140), 1.1f);
    }

    private static void DrawDashedLine(GeometryBatch batch, Vector2 from, Vector2 to, ColorRgba color, float thickness)
    {
        Vector2 delta = to - from;
        float length = delta.Length();
        if (length < 1f)
        {
            return;
        }

        Vector2 step = delta / length;
        const float Dash = 7f;
        const float Gap = 5f;
        for (float d = 0f; d < length; d += Dash + Gap)
        {
            batch.DrawLine(from + (step * d), from + (step * MathF.Min(length, d + Dash)), color, thickness);
        }
    }

    private static ColorRgba WithAlpha(ColorRgba color, byte alpha) => new(color.R, color.G, color.B, alpha);
}
