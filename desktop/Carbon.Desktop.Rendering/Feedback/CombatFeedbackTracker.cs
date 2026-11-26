// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Events;
using Carbon.Desktop.Abstractions.World;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Maps.Props;

namespace Carbon.Desktop.Rendering.Feedback;

/// <summary>Floating world-space damage, crit, heal, or loot popup readout.</summary>
internal readonly record struct FloatingCombatText(
    Vector2 Position,
    string Text,
    ColorRgba Color,
    float Scale,
    float RemainingSeconds);

/// <summary>Prop impact shake and durability bar timer state.</summary>
internal readonly record struct PropHitState(float ShakeTimer, float BarTimer);

/// <summary>Damaged or hovered natural prop marker projected to screen space for durability bars.</summary>
internal readonly record struct DamagedPropHudMarker(Vector2 WorldPos, float Current, float Max, bool IsMineral, float Alpha = 1f);

/// <summary>
/// Manages transient combat &amp; gathering feedback: prop shake timers, debris particles,
/// floating combat numbers, smooth delayed HP ghost-damage trails, and active target lock (SRP).
/// </summary>
public sealed class CombatFeedbackTracker
{
    private const int MaxDebrisParticles = 256;

    private readonly List<CombatFeedbackEvent> _combatScratch = new(16);
    private readonly List<PropImpactEvent> _propImpactScratch = new(16);
    private readonly List<FloatingCombatText> _floatingTexts = new(32);
    private readonly Dictionary<long, PropHitState> _propHits = new(32);
    private readonly List<long> _expiredPropHits = new(16);
    private readonly Dictionary<ulong, float> _entityGhostRatios = new(64);
    private readonly List<ulong> _ghostRemoveScratch = new(16);
    private readonly DebrisParticle[] _particles = new DebrisParticle[MaxDebrisParticles];
    private int _nextParticleCursor;
    private ulong _engagedTargetId;
    private float _engagedTargetTimer;

    /// <summary>Gets active floating combat text items for screen-space rendering.</summary>
    internal IReadOnlyList<FloatingCombatText> FloatingTexts => _floatingTexts;

    /// <summary>Gets the smoothed ghost-damage HP ratio (<c>[0..1]</c>) for the local player.</summary>
    public float LocalGhostHpRatio { get; private set; } = 1f;

    /// <summary>Drains feedback events from <paramref name="world"/> and advances particles, timers, and ghost HP trails.</summary>
    public void Update(IWorldReadModel world, float frameDt, float animTime, Vector2? aimedWorldPos)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (_engagedTargetTimer > 0f)
        {
            _engagedTargetTimer = MathF.Max(0f, _engagedTargetTimer - frameDt);
        }

        float localRatio = world.LocalPlayerMaxHealth > 0f
            ? Math.Clamp(world.LocalPlayerHealth / world.LocalPlayerMaxHealth, 0f, 1f)
            : 1f;
        this.LocalGhostHpRatio = StepGhostRatio(this.LocalGhostHpRatio, localRatio, frameDt);

        _propImpactScratch.Clear();
        world.DrainPropImpactEvents(_propImpactScratch);
        for (int i = 0; i < _propImpactScratch.Count; i++)
        {
            PropImpactEvent impact = _propImpactScratch[i];
            long key = PackTileKey(impact.TileX, impact.TileY);
            _propHits[key] = new PropHitState(ShakeTimer: 0.26f, BarTimer: impact.IsDestroyed ? 0f : 3.5f);
            this.SpawnImpactDebris(in impact, animTime);
        }

        if (_propHits.Count > 0)
        {
            _expiredPropHits.Clear();
            foreach (KeyValuePair<long, PropHitState> kvp in _propHits)
            {
                float nextShake = MathF.Max(0f, kvp.Value.ShakeTimer - frameDt);
                float nextBar = MathF.Max(0f, kvp.Value.BarTimer - frameDt);
                if (nextShake <= 0f && nextBar <= 0f)
                {
                    _expiredPropHits.Add(kvp.Key);
                }
                else
                {
                    _propHits[kvp.Key] = new PropHitState(nextShake, nextBar);
                }
            }

            for (int i = 0; i < _expiredPropHits.Count; i++)
            {
                _ = _propHits.Remove(_expiredPropHits[i]);
            }
        }

        for (int i = 0; i < MaxDebrisParticles; i++)
        {
            ref DebrisParticle p = ref _particles[i];
            if (p.RemainingLife <= 0f)
            {
                continue;
            }

            p.RemainingLife -= frameDt;
            p.Position += p.Velocity * frameDt;
            p.Velocity = new Vector2(p.Velocity.X * (1f - Math.Clamp(frameDt * 3.5f, 0f, 0.5f)), p.Velocity.Y + (190f * frameDt));
        }

        _combatScratch.Clear();
        world.DrainCombatEvents(_combatScratch);
        for (int i = 0; i < _combatScratch.Count; i++)
        {
            CombatFeedbackEvent ev = _combatScratch[i];
            string text = ev.Label ?? (ev.IsCritical
                ? $"-{(int)MathF.Ceiling(ev.Amount)}!"
                : ev.IsHeal
                    ? $"+{(int)MathF.Ceiling(ev.Amount)}"
                    : $"-{(int)MathF.Ceiling(ev.Amount)}");
            bool isXpPopup = ev.Label is not null && ev.Label.StartsWith('+') && ev.Label.EndsWith(" XP", StringComparison.Ordinal);
            ColorRgba color = isXpPopup
                ? new ColorRgba(192, 132, 252, 255)
                : ev.Label is not null && !ev.IsHeal
                    ? new ColorRgba(251, 146, 60, 255)
                    : ev.IsHeal
                        ? new ColorRgba(74, 222, 128, 255)
                        : ev.IsCritical
                            ? new ColorRgba(250, 204, 21, 255)
                            : new ColorRgba(248, 113, 113, 255);
            float scale = ev.IsCritical || ev.IsHeal ? 1.25f : 1.0f;
            _floatingTexts.Add(new FloatingCombatText(ev.WorldPosition + new Vector2(0f, -14f), text, color, scale, 0.95f));
        }

        for (int i = _floatingTexts.Count - 1; i >= 0; i--)
        {
            FloatingCombatText ft = _floatingTexts[i];
            float nextLife = ft.RemainingSeconds - frameDt;
            if (nextLife <= 0f)
            {
                _floatingTexts.RemoveAt(i);
            }
            else
            {
                _floatingTexts[i] = ft with
                {
                    Position = ft.Position + new Vector2(0f, -26f * frameDt),
                    RemainingSeconds = nextLife,
                };
            }
        }

        this.UpdateRemoteGhostRatios(world.RemoteEntities, frameDt, aimedWorldPos);
    }

    /// <summary>Gets whether the tile at (<paramref name="tileX"/>, <paramref name="tileY"/>) has an active shake or bar timer.</summary>
    internal bool TryGetPropHit(int tileX, int tileY, out PropHitState state) =>
        _propHits.TryGetValue(PackTileKey(tileX, tileY), out state);

    /// <summary>Gets the smoothed ghost-damage HP ratio (<c>[0..1]</c>) for a remote entity.</summary>
    public float GetEntityGhostRatio(ulong networkId, float currentHealth, float maxHealth)
    {
        float actual = maxHealth > 0f ? Math.Clamp(currentHealth / maxHealth, 0f, 1f) : 1f;
        return _entityGhostRatios.TryGetValue(networkId, out float ghost) ? MathF.Max(ghost, actual) : actual;
    }

    /// <summary>
    /// Resolves the hovered monster/NPC (within <c>56</c> world units of <paramref name="aimedWorldPos"/>)
    /// or most recently damaged/attacking monster within <c>360</c> world units for the Target Unit Frame.
    /// </summary>
    public bool TryResolveTargetEntity(
        IWorldReadModel world,
        Vector2? aimedWorldPos,
        out RemoteEntityView target,
        out float ghostRatio)
    {
        ArgumentNullException.ThrowIfNull(world);

        IReadOnlyList<RemoteEntityView> remotes = world.RemoteEntities;
        Vector2 playerPos = world.LocalPlayerInterpolatedPosition;

        if (world.LockedTargetNetworkId != 0UL)
        {
            for (int i = 0; i < remotes.Count; i++)
            {
                RemoteEntityView r = remotes[i];
                if (r.NetworkId == world.LockedTargetNetworkId && r.CurrentHealth > 0f)
                {
                    target = r;
                    ghostRatio = this.GetEntityGhostRatio(target.NetworkId, target.CurrentHealth, target.MaxHealth);
                    return true;
                }
            }
        }

        if (aimedWorldPos.HasValue)
        {
            float bestDistSq = 56f * 56f;
            int bestIdx = -1;
            for (int i = 0; i < remotes.Count; i++)
            {
                RemoteEntityView r = remotes[i];
                if (r.Kind is not (EntityKind.Monster or EntityKind.Npc))
                {
                    continue;
                }

                float dSq = Vector2.DistanceSquared(r.Position, aimedWorldPos.Value);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                target = remotes[bestIdx];
                ghostRatio = this.GetEntityGhostRatio(target.NetworkId, target.CurrentHealth, target.MaxHealth);
                return true;
            }
        }

        if (_engagedTargetTimer > 0f && _engagedTargetId != 0UL)
        {
            const float maxLockDistSq = 360f * 360f;
            for (int i = 0; i < remotes.Count; i++)
            {
                RemoteEntityView r = remotes[i];
                if (r.NetworkId == _engagedTargetId && Vector2.DistanceSquared(playerPos, r.Position) <= maxLockDistSq)
                {
                    target = r;
                    ghostRatio = this.GetEntityGhostRatio(target.NetworkId, target.CurrentHealth, target.MaxHealth);
                    return true;
                }
            }
        }

        target = default;
        ghostRatio = 0f;
        return false;
    }

    /// <summary>Draws active debris particles onto <paramref name="batch"/> in world space.</summary>
    public void DrawDebrisParticles(GeometryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        for (int i = 0; i < MaxDebrisParticles; i++)
        {
            ref readonly DebrisParticle p = ref _particles[i];
            if (p.RemainingLife <= 0f)
            {
                continue;
            }

            byte alpha = (byte)(Math.Clamp(p.RemainingLife / p.MaxLife, 0f, 1f) * 255f);
            ColorRgba color = new(p.Color.R, p.Color.G, p.Color.B, alpha);
            batch.FillRectangle(new RectangleF(p.Position.X - (p.Size * 0.5f), p.Position.Y - (p.Size * 0.5f), p.Size, p.Size), color);
        }
    }

    private void UpdateRemoteGhostRatios(IReadOnlyList<RemoteEntityView> remotes, float frameDt, Vector2? aimedWorldPos)
    {
        for (int i = 0; i < remotes.Count; i++)
        {
            RemoteEntityView r = remotes[i];
            float actual = r.MaxHealth > 0f ? Math.Clamp(r.CurrentHealth / r.MaxHealth, 0f, 1f) : 1f;
            if (!_entityGhostRatios.TryGetValue(r.NetworkId, out float ghost))
            {
                _entityGhostRatios[r.NetworkId] = actual;
            }
            else
            {
                if (actual < ghost - 0.005f || r.AttackAnimRemaining > 0.1f)
                {
                    if (r.Kind == EntityKind.Monster)
                    {
                        _engagedTargetId = r.NetworkId;
                        _engagedTargetTimer = 6.0f;
                    }
                }

                _entityGhostRatios[r.NetworkId] = StepGhostRatio(ghost, actual, frameDt);
            }

            if (aimedWorldPos.HasValue && r.Kind == EntityKind.Monster && Vector2.DistanceSquared(r.Position, aimedWorldPos.Value) < 42f * 42f)
            {
                _engagedTargetId = r.NetworkId;
                _engagedTargetTimer = 6.0f;
            }
        }

        if (_entityGhostRatios.Count > remotes.Count + 16)
        {
            _ghostRemoveScratch.Clear();
            foreach (KeyValuePair<ulong, float> kvp in _entityGhostRatios)
            {
                bool found = false;
                for (int i = 0; i < remotes.Count; i++)
                {
                    if (remotes[i].NetworkId == kvp.Key)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    _ghostRemoveScratch.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _ghostRemoveScratch.Count; i++)
            {
                _ = _entityGhostRatios.Remove(_ghostRemoveScratch[i]);
            }
        }
    }

    private void SpawnImpactDebris(in PropImpactEvent impact, float animTime)
    {
        int count = impact.IsDestroyed ? 22 : impact.WasTierBlocked ? 10 : 12;
        uint seed = (uint)(impact.TileX * 73856093) ^ (uint)(impact.TileY * 19349663) ^ (uint)(animTime * 1000f);

        for (int i = 0; i < count; i++)
        {
            seed = (seed * 1664525U) + 1013904223U;
            float angle = (seed & 0xFFFF) / 65535f * MathF.Tau;
            float speed = (impact.IsDestroyed ? 55f : 38f) + (((seed >> 16) & 0xFF) / 255f * 65f);
            Vector2 vel = new(MathF.Cos(angle) * speed, (MathF.Sin(angle) * speed * 0.55f) - 55f);

            ColorRgba color = impact.WasTierBlocked
                ? new ColorRgba(254, 240, 138, 255)
                : impact.Category == PropCategory.Mineral
                    ? impact.PropType is MapPropType.CrystalCluster or MapPropType.GlacialCrystal
                        ? new ColorRgba(56, 189, 248, 255)
                        : (i % 3 == 0 ? new ColorRgba(245, 158, 11, 255) : new ColorRgba(148, 163, 184, 255))
                    : (i % 3 == 0 ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(210, 168, 114, 255));

            _particles[_nextParticleCursor] = new DebrisParticle(
                impact.WorldPosition + new Vector2(0f, -4f),
                vel,
                color,
                size: impact.IsDestroyed ? 3.2f : 2.5f,
                remainingLife: 0.48f + (i % 4 * 0.08f),
                maxLife: 0.72f);
            _nextParticleCursor = (_nextParticleCursor + 1) % MaxDebrisParticles;
        }
    }

    private static float StepGhostRatio(float ghost, float actual, float dt)
    {
        if (actual >= ghost)
        {
            return actual;
        }

        return MathF.Max(actual, ghost - (0.55f * dt));
    }

    private static long PackTileKey(int tileX, int tileY) => ((long)tileX << 32) | (uint)tileY;

    private struct DebrisParticle(
        Vector2 position,
        Vector2 velocity,
        ColorRgba color,
        float size,
        float remainingLife,
        float maxLife)
    {
        public Vector2 Position = position;
        public Vector2 Velocity = velocity;
        public readonly ColorRgba Color = color;
        public readonly float Size = size;
        public float RemainingLife = remainingLife;
        public readonly float MaxLife = maxLife;
    }
}
