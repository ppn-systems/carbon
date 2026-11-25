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
/// entity slices using reusable scratch buffers so tick synchronization stays allocation-free.
/// </summary>
public sealed class SpatialAoiTracker
{
    private const int ScratchCapacity = 256;

    private readonly Dictionary<ulong, HashSet<ulong>> _visibleByConnection = new();
    private readonly int[] _broadphaseBuffer = new int[ScratchCapacity];
    private readonly List<ulong> _currentIds = new(ScratchCapacity);
    private readonly List<ulong> _exitedIds = new(64);
    private readonly float _viewRadius;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialAoiTracker"/> class.
    /// </summary>
    /// <param name="viewRadius">Visibility half-extent in world units.</param>
    public SpatialAoiTracker(float viewRadius = 768f) =>
        _viewRadius = viewRadius > 0f ? viewRadius : 768f;

    /// <summary>Gets the configured Area of Interest radius in world units.</summary>
    public float ViewRadius => _viewRadius;

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
    /// newly entered network IDs, still-visible network IDs, and departed network IDs.
    /// </summary>
    public void ComputeVisibility(
        ulong connectionId,
        ulong selfNetworkId,
        Vector2 observerPosition,
        WorldSimulation simulation,
        List<ulong> enteredIds,
        List<ulong> visibleIds,
        List<ulong> exitedIds)
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
        int cellRadius = Math.Max(1, (int)MathF.Ceiling(_viewRadius / simulation.SpatialGrid.CellSize));
        simulation.SpatialGrid.QueryCellRadius(in centerCell, cellRadius, _broadphaseBuffer, out int hits);

        float radiusSq = _viewRadius * _viewRadius;
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

            Vector2 pos = simulation.Entities.GetTransform(candidate).Position;
            if (Vector2.DistanceSquared(observerPosition, pos) > radiusSq)
            {
                continue;
            }

            _currentIds.Add(identity.NetworkId);
            if (known.Add(identity.NetworkId))
            {
                enteredIds.Add(identity.NetworkId);
            }
            else
            {
                visibleIds.Add(identity.NetworkId);
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
