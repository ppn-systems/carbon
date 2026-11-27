// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.Spatial;
using Carbon.Simulation.World;

namespace Carbon.Application.World;

/// <summary>
/// Tracks Area of Interest (AoI) visibility sets per connected session and computes entered/exited/visible
/// entity slices using reusable scratch buffers so tick synchronization stays allocation-free and $O(1)$ per lookup.
/// An entity enters within <see cref="ViewRadius"/> but only leaves beyond <see cref="ExitRadius"/>, so one wandering
/// along the view edge does not despawn and respawn on the client every few ticks.
/// </summary>
public sealed class SpatialAoiTracker
{
    private const int InitialScratchCapacity = 256;

    /// <summary>
    /// Default hysteresis band, in world units, between the enter radius and the exit radius. It equals the two-tile
    /// slack the world adds to the view radius for nearby-connection broadcasts, so an entity in the band still gets
    /// its combat and attack-phase broadcasts.
    /// </summary>
    public const float DefaultExitMargin = 64f;

    private readonly Dictionary<ulong, HashSet<ulong>> _visibleByConnection = new();
    private readonly HashSet<ulong> _currentIds = new(InitialScratchCapacity);
    private readonly List<ulong> _exitedIds = new(64);
    private readonly float _viewRadius;
    private readonly float _exitRadius;
    private int[] _broadphaseBuffer = new int[InitialScratchCapacity];

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialAoiTracker"/> class.
    /// </summary>
    /// <param name="viewRadius">Visibility half-extent in world units: entities enter the view inside it.</param>
    /// <param name="exitMargin">Extra distance beyond <paramref name="viewRadius"/> before a visible entity leaves.</param>
    public SpatialAoiTracker(float viewRadius = 768f, float exitMargin = DefaultExitMargin)
    {
        _viewRadius = viewRadius > 0f ? viewRadius : 768f;
        _exitRadius = _viewRadius + MathF.Max(0f, exitMargin);
    }

    /// <summary>Gets the configured Area of Interest radius in world units (the enter radius).</summary>
    public float ViewRadius => _viewRadius;

    /// <summary>Gets the radius beyond which an already-visible entity leaves the view.</summary>
    public float ExitRadius => _exitRadius;

    /// <summary>
    /// Registers or resets the visibility set for a session connection.
    /// </summary>
    /// <param name="connectionId">Connection identifier.</param>
    public void TrackConnection(ulong connectionId)
    {
        if (!_visibleByConnection.TryGetValue(connectionId, out HashSet<ulong>? set))
        {
            _visibleByConnection[connectionId] = new HashSet<ulong>(64);
        }
        else
        {
            set.Clear();
        }
    }

    /// <summary>
    /// Removes all AoI tracking state for a disconnected session.
    /// </summary>
    /// <param name="connectionId">Connection identifier.</param>
    public void UntrackConnection(ulong connectionId) =>
        _ = _visibleByConnection.Remove(connectionId);

    /// <summary>
    /// Evaluates visible network entities around <paramref name="observerPosition"/>, populating
    /// newly entered network IDs, still-visible network IDs, and departed network IDs. <paramref name="radiusScale"/>
    /// shrinks both the enter and the exit radius for this observer (blindness, ADR-036 D9).
    /// </summary>
    public void ComputeVisibility(
        ulong connectionId,
        ulong selfNetworkId,
        Vector2 observerPosition,
        WorldSimulation simulation,
        List<ulong> enteredIds,
        List<ulong> visibleIds,
        List<ulong> exitedIds,
        float radiusScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(enteredIds);
        ArgumentNullException.ThrowIfNull(visibleIds);
        ArgumentNullException.ThrowIfNull(exitedIds);

        enteredIds.Clear();
        visibleIds.Clear();
        exitedIds.Clear();
        _currentIds.Clear();
        _exitedIds.Clear();

        if (!_visibleByConnection.TryGetValue(connectionId, out HashSet<ulong>? known))
        {
            known = new HashSet<ulong>(64);
            _visibleByConnection[connectionId] = known;
        }

        CellCoord centerCell = simulation.SpatialGrid.WorldToCell(observerPosition);
        int cellRadius = Math.Max(1, (int)MathF.Ceiling(_exitRadius / simulation.SpatialGrid.CellSize));
        simulation.SpatialGrid.QueryCellRadius(in centerCell, cellRadius, _broadphaseBuffer, out int hits);

        // A full buffer may have clipped the query: grow and re-query in this same tick, because any entity left out
        // would read as "exited" and be despawned on the client, then respawned on the next tick.
        while (hits >= _broadphaseBuffer.Length)
        {
            _broadphaseBuffer = new int[_broadphaseBuffer.Length * 2];
            simulation.SpatialGrid.QueryCellRadius(in centerCell, cellRadius, _broadphaseBuffer, out hits);
        }

        float scale = float.IsFinite(radiusScale) ? Math.Clamp(radiusScale, 0f, 1f) : 1f;
        float enterRadiusSq = _viewRadius * scale * _viewRadius * scale;
        float exitRadiusSq = _exitRadius * scale * _exitRadius * scale;
        for (int i = 0; i < hits; i++)
        {
            Entity candidate = simulation.Entities.GetEntity(_broadphaseBuffer[i]);
            if (!simulation.Entities.IsAlive(candidate) ||
                !simulation.TryGetNetworkIdentity(candidate, out NetworkIdentity identity) ||
                identity.NetworkId == selfNetworkId ||
                !simulation.Entities.HasTransform(candidate))
            {
                continue;
            }

            float distSq = Vector2.DistanceSquared(observerPosition, simulation.Entities.GetTransform(candidate).Position);
            bool wasVisible = known.Contains(identity.NetworkId);
            if (distSq > (wasVisible ? exitRadiusSq : enterRadiusSq))
            {
                continue;
            }

            _ = _currentIds.Add(identity.NetworkId);
            if (wasVisible)
            {
                visibleIds.Add(identity.NetworkId);
            }
            else
            {
                _ = known.Add(identity.NetworkId);
                enteredIds.Add(identity.NetworkId);
            }
        }

        foreach (ulong oldId in known)
        {
            if (!_currentIds.Contains(oldId))
            {
                _exitedIds.Add(oldId);
            }
        }

        for (int i = 0; i < _exitedIds.Count; i++)
        {
            ulong leftId = _exitedIds[i];
            _ = known.Remove(leftId);
            exitedIds.Add(leftId);
        }
    }
}
