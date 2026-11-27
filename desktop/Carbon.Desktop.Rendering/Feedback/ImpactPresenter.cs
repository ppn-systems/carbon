// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.VectorArt;

namespace Carbon.Desktop.Rendering.Feedback;

/// <summary>
/// Presents confirmed hits (docs/art/combat-animation.md §3.4). A hit the local hero dealt waits until the swing reaches its
/// impact pose: on LAN the server's <c>CombatHit</c> arrives while the hero is still winding up. Then it plays a hit-stop on
/// the swing, a white flash on the target, trauma for the camera shake and a burst of particles. Hits on the local hero
/// shake at once. Fixed-size state only: nothing allocates per frame.
/// </summary>
public sealed class ImpactPresenter
{
    /// <summary>Most hits waiting for contact at once.</summary>
    public const int MaxPending = 8;

    /// <summary>Most targets flashing at once.</summary>
    public const int MaxFlashes = 16;

    /// <summary>Seconds a presented target flash lasts (held at full through the hit-stop, then fading).</summary>
    public const float FlashSeconds = 0.12f;

    private readonly PendingImpact[] _pending = new PendingImpact[MaxPending];
    private readonly TargetFlash[] _flashes = new TargetFlash[MaxFlashes];
    private readonly List<StrikeImpactEvent> _scratch = new(16);
    private TraumaShake _shake;
    private HitStop _hitStop;
    private float _hitStopRemaining;

    /// <summary>Gets the current camera trauma (tests and diagnostics).</summary>
    public float Trauma => _shake.Trauma;

    /// <summary>Gets whether a hit-stop is holding the local swing right now.</summary>
    public bool IsFrozen => _hitStopRemaining > 0f;

    /// <summary>
    /// Drains new hits from <paramref name="world"/>, presents those that reached contact, and advances flashes, shake and
    /// hit-stop. Reads the world only: it cannot delay input or the simulation.
    /// </summary>
    /// <param name="world">World read model.</param>
    /// <param name="dt">Frame seconds.</param>
    /// <param name="options">Impact settings.</param>
    /// <param name="particles">Particle sink for impact bursts, or <see langword="null"/>.</param>
    public void Update(IWorldReadModel world, float dt, ImpactFeelOptions options, IImpactParticleSink? particles = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        _scratch.Clear();
        world.DrainStrikeImpactEvents(_scratch);
        this.Step(world.CurrentAction, world.LastAttackDirection, CollectionsMarshal.AsSpan(_scratch), dt, options, particles);
    }

    /// <summary>
    /// Advances the presentation by <paramref name="dt"/> with <paramref name="newHits"/> just confirmed. It takes the
    /// local swing by value: nothing here can reach the session, input or the simulation.
    /// </summary>
    /// <param name="action">The session's local action this frame.</param>
    /// <param name="swingDirection">Direction of the local swing.</param>
    /// <param name="newHits">Hits confirmed since the last step.</param>
    /// <param name="dt">Frame seconds.</param>
    /// <param name="options">Impact settings.</param>
    /// <param name="particles">Particle sink for impact bursts, or <see langword="null"/>.</param>
    public void Step(in ActionState action, Vector2 swingDirection, ReadOnlySpan<StrikeImpactEvent> newHits, float dt, ImpactFeelOptions options, IImpactParticleSink? particles = null)
    {
        for (int i = 0; i < newHits.Length; i++)
        {
            this.Enqueue(in newHits[i], SecondsUntilContact(in action, newHits[i].IsLocalAttacker));
        }

        for (int i = 0; i < MaxPending; i++)
        {
            ref PendingImpact pending = ref _pending[i];
            if (!pending.Active)
            {
                continue;
            }

            pending.Delay -= dt;
            if (pending.Delay <= 0f)
            {
                pending.Active = false;
                this.Present(in pending.Hit, in action, swingDirection, particles);
            }
        }

        for (int i = 0; i < MaxFlashes; i++)
        {
            ref TargetFlash flash = ref _flashes[i];
            if (flash.Remaining > 0f)
            {
                flash.Remaining = MathF.Max(0f, flash.Remaining - (_hitStopRemaining > 0f ? 0f : dt));
            }
        }

        _hitStopRemaining = MathF.Max(0f, _hitStopRemaining - dt);
        _shake.Update(dt);
        if (!options.ScreenShake)
        {
            _shake.Reset();
        }
    }

    /// <summary>Gets the clip time to display for the local swing, including any hit-stop.</summary>
    /// <param name="action">The local action.</param>
    /// <returns>Displayed normalized progress.</returns>
    public float DisplayedProgress(in ActionState action) => _hitStop.Displayed(in action);

    /// <summary>Gets the presented flash strength (0..1) of <paramref name="networkId"/>.</summary>
    /// <param name="networkId">Target network id.</param>
    /// <returns>Flash strength.</returns>
    public float FlashFor(ulong networkId)
    {
        for (int i = 0; i < MaxFlashes; i++)
        {
            if (_flashes[i].NetworkId == networkId && _flashes[i].Remaining > 0f)
            {
                return Math.Clamp(_flashes[i].Remaining / FlashSeconds, 0f, 1f);
            }
        }

        return 0f;
    }

    /// <summary>Gets the camera offset for this frame (zero when screen shake is off).</summary>
    /// <param name="options">Impact settings.</param>
    /// <returns>World-unit offset.</returns>
    public Vector2 CameraOffset(ImpactFeelOptions options) => _shake.Offset(options.ScreenShake);

    /// <summary>Gets the seconds until the local swing reaches its impact pose (0 when not the local hero's hit or already past).</summary>
    /// <param name="action">The local action.</param>
    /// <param name="localAttacker">Whether the local hero dealt the hit.</param>
    /// <returns>Seconds to hold the hit's feedback.</returns>
    internal static float SecondsUntilContact(in ActionState action, bool localAttacker)
    {
        HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackTiming.StyleOf(action.Kind, action.ComboStep));
        if (!localAttacker || !action.IsActive || timing.Total <= 0f)
        {
            return 0f;
        }

        float wait = (HeroAttackPose.ImpactFraction(in timing) - action.Progress) * action.DurationSeconds;
        return Math.Clamp(wait, 0f, HeroAttackTiming.MaxAnticipationSeconds);
    }

    private void Enqueue(in StrikeImpactEvent hit, float delay)
    {
        int slot = 0;
        float soonest = float.MaxValue;
        for (int i = 0; i < MaxPending; i++)
        {
            if (!_pending[i].Active)
            {
                slot = i;
                break;
            }

            // Full: replace the hit that is due soonest (it would present within a frame anyway).
            if (_pending[i].Delay < soonest)
            {
                soonest = _pending[i].Delay;
                slot = i;
            }
        }

        _pending[slot] = new PendingImpact { Active = true, Delay = delay, Hit = hit };
    }

    private void Present(in StrikeImpactEvent hit, in ActionState action, Vector2 swingDirection, IImpactParticleSink? particles)
    {
        float share = hit.DefenderMaxHealth > 0f ? Math.Clamp(hit.Damage / hit.DefenderMaxHealth, 0f, 1f) : 0.1f;
        if (hit.IsLocalAttacker)
        {
            float freeze = HitStop.SecondsFor(hit.Damage, hit.DefenderMaxHealth);
            HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackTiming.StyleOf(action.Kind, action.ComboStep));
            if (timing.Total > 0f)
            {
                _hitStop.Arm(in action, HeroAttackPose.ImpactFraction(in timing), freeze);
                _hitStopRemaining = freeze;
            }

            this.Flash(hit.DefenderNetworkId, FlashSeconds);
            _shake.Add(0.22f + (share * 0.9f) + (hit.IsCritical ? 0.15f : 0f) + (hit.IsKilled ? 0.1f : 0f));
            particles?.SpawnStrikeBurst(hit.WorldPosition, swingDirection, ImpactMaterial.Flesh, hit.IsCritical || hit.IsKilled ? 1.4f : 1f);
        }
        else if (hit.IsLocalVictim)
        {
            _shake.Add(0.3f + (share * 1.2f));
        }
    }

    private void Flash(ulong networkId, float seconds)
    {
        int slot = 0;
        float lowest = float.MaxValue;
        for (int i = 0; i < MaxFlashes; i++)
        {
            if (_flashes[i].NetworkId == networkId || _flashes[i].Remaining <= 0f)
            {
                slot = i;
                break;
            }

            if (_flashes[i].Remaining < lowest)
            {
                lowest = _flashes[i].Remaining;
                slot = i;
            }
        }

        _flashes[slot] = new TargetFlash { NetworkId = networkId, Remaining = seconds };
    }

    private struct PendingImpact
    {
        public bool Active;
        public float Delay;
        public StrikeImpactEvent Hit;
    }

    private struct TargetFlash
    {
        public ulong NetworkId;
        public float Remaining;
    }
}
