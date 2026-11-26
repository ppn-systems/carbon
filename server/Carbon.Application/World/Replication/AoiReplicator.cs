// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Carbon.Abstractions.World;
using Carbon.Contracts.Entities;
using Carbon.Simulation.ECS;
using Carbon.Simulation.Networking;
using Carbon.Simulation.World;

namespace Carbon.Application.World.Replication;

/// <summary>The identity half of an <see cref="EntitySpawnSnapshot"/>: what an entity is and wears.</summary>
internal readonly record struct SpawnIdentity(
    EntityKind Kind,
    string Name,
    ushort Archetype,
    ushort HeadId,
    ushort ChestId,
    ushort LegsId,
    ushort FeetId,
    ushort WeaponId,
    ushort Level,
    int LootCount,
    byte WeaponUpgrade)
{
    /// <summary>Gets the identity used for an entity the world knows nothing more about.</summary>
    public static SpawnIdentity Unknown { get; } = new(EntityKind.Player, "Adventurer", 0, 0, 0, 0, 0, 0, 1, 0, 0);
}

/// <summary>Resolves the <see cref="SpawnIdentity"/> of a replicated entity.</summary>
internal delegate SpawnIdentity SpawnIdentityResolver(ulong networkId);

/// <summary>
/// Area-of-interest replication (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>). Per observer and tick
/// it computes what entered, stayed and left the view, and stages the spawns, despawns and dirty-entity delta batch in
/// the outbound buffer; at the end of the tick it clears every entity's dirty flags. Reuses its scratch lists, so the
/// steady state does not allocate. Called only under the world lock.
/// </summary>
internal sealed class AoiReplicator
{
    private readonly WorldSimulation _simulation;
    private readonly OutboundTickBuffer _outbound;
    private readonly SpawnIdentityResolver _identities;
    private readonly float _defaultRadius;
    private readonly SpatialAoiTracker _tracker;
    private readonly List<ulong> _entered = new(64);
    private readonly List<ulong> _visible = new(128);
    private readonly List<ulong> _exited = new(64);
    private readonly List<Entity> _dirtyReset = new(256);

    /// <summary>Initializes a new instance of the <see cref="AoiReplicator"/> class.</summary>
    public AoiReplicator(
        WorldSimulation simulation,
        OutboundTickBuffer outbound,
        float viewRadius,
        float defaultRadius,
        SpawnIdentityResolver identities)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _defaultRadius = defaultRadius;
        _tracker = new SpatialAoiTracker(viewRadius);
    }

    /// <summary>Gets the view radius in world units.</summary>
    public float ViewRadius => _tracker.ViewRadius;

    /// <summary>Starts (or resets) visibility tracking for a connection; everything in view spawns again.</summary>
    public void Track(ulong connectionId) => _tracker.TrackConnection(connectionId);

    /// <summary>Stops visibility tracking for a connection.</summary>
    public void Untrack(ulong connectionId) => _tracker.UntrackConnection(connectionId);

    /// <summary>
    /// Stages this tick's spawns, despawns and delta batch for one observer. The delta batch is sent when any visible
    /// entity or the observer itself changed. Returns the observer's own dirty flags.
    /// </summary>
    public EntityDirtyMask ReplicateTo(
        ulong connectionId,
        ulong selfNetworkId,
        Entity self,
        Vector2 selfPosition,
        uint lastClientTick,
        ulong serverTick)
    {
        _tracker.ComputeVisibility(connectionId, selfNetworkId, selfPosition, _simulation, _entered, _visible, _exited);
        OutboundBatch pending = _outbound.Pending;

        for (int i = 0; i < _entered.Count; i++)
        {
            if (this.TryBuildSpawnSnapshot(_entered[i], out EntitySpawnSnapshot snap))
            {
                pending.Spawns.Add(new OutboundSpawn(connectionId, snap));
            }
        }

        for (int i = 0; i < _exited.Count; i++)
        {
            pending.Despawns.Add(new OutboundDespawn(connectionId, _exited[i], serverTick));
        }

        int deltaOffset = pending.DeltaEntries.Count;
        for (int i = 0; i < _visible.Count; i++)
        {
            ulong visibleNetId = _visible[i];
            if (_simulation.TryGetEntityByNetworkId(visibleNetId, out Entity visibleEntity))
            {
                EntityDirtyMask mask = _simulation.GetDirtyFlags(visibleEntity);
                if (mask != EntityDirtyMask.None)
                {
                    Vector2 pos = _simulation.Entities.GetTransform(visibleEntity).Position;
                    Vector2 vel = _simulation.Entities.HasVelocity(visibleEntity)
                        ? _simulation.Entities.GetVelocity(visibleEntity).Linear
                        : Vector2.Zero;
                    float hp = _simulation.Entities.HasHealth(visibleEntity)
                        ? _simulation.Entities.GetHealth(visibleEntity).Current
                        : 100f;
                    pending.DeltaEntries.Add(new EntityDeltaEntry(visibleNetId, (byte)mask, pos.X, pos.Y, vel.X, vel.Y, hp));
                }
            }
        }

        int deltaCount = pending.DeltaEntries.Count - deltaOffset;
        EntityDirtyMask selfDirty = _simulation.GetDirtyFlags(self);
        if (selfDirty != EntityDirtyMask.None || deltaCount > 0)
        {
            pending.Deltas.Add(new OutboundDelta(
                connectionId,
                serverTick,
                lastClientTick,
                selfPosition.X,
                selfPosition.Y,
                deltaOffset,
                deltaCount));
        }

        return selfDirty;
    }

    /// <summary>Clears the dirty flags of every active entity once all observers were replicated.</summary>
    public void ClearDirtyFlags()
    {
        _dirtyReset.Clear();
        ReadOnlySpan<Entity> activeEntities = CollectionsMarshal.AsSpan((List<Entity>)_simulation.Entities.ActiveEntities);
        for (int i = 0; i < activeEntities.Length; i++)
        {
            _dirtyReset.Add(activeEntities[i]);
        }

        for (int i = 0; i < _dirtyReset.Count; i++)
        {
            _simulation.ClearDirtyFlags(_dirtyReset[i]);
        }
    }

    /// <summary>Builds the spawn snapshot of a replicated entity: its physical state plus its <see cref="SpawnIdentity"/>.</summary>
    public bool TryBuildSpawnSnapshot(ulong networkId, out EntitySpawnSnapshot snapshot)
    {
        if (!_simulation.TryGetEntityByNetworkId(networkId, out Entity entity) || !_simulation.Entities.HasTransform(entity))
        {
            snapshot = default;
            return false;
        }

        Vector2 pos = _simulation.Entities.GetTransform(entity).Position;
        Vector2 vel = _simulation.Entities.HasVelocity(entity)
            ? _simulation.Entities.GetVelocity(entity).Linear
            : Vector2.Zero;
        float radius = _simulation.Entities.HasCollider(entity)
            ? _simulation.Entities.GetCollider(entity).Radius
            : _defaultRadius;
        float hp = _simulation.Entities.HasHealth(entity)
            ? _simulation.Entities.GetHealth(entity).Current
            : 100f;
        float maxHp = _simulation.Entities.HasHealth(entity)
            ? _simulation.Entities.GetHealth(entity).Max
            : 100f;

        SpawnIdentity id = _identities(networkId);
        snapshot = new EntitySpawnSnapshot(
            networkId,
            id.Kind,
            id.Archetype,
            id.Name,
            pos.X,
            pos.Y,
            vel.X,
            vel.Y,
            radius,
            hp,
            maxHp,
            id.HeadId,
            id.ChestId,
            id.LegsId,
            id.FeetId,
            id.WeaponId,
            id.Level,
            id.LootCount,
            id.WeaponUpgrade);
        return true;
    }
}
