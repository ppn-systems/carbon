// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Abstractions.World;

/// <summary>
/// Role-segregated fixed-tick simulation loop and persistence flush port consumed by the hosted tick runner (ISP).
/// </summary>
public interface IWorldSimulationLoop
{
    /// <summary>Gets the fixed simulation tick rate in Hertz.</summary>
    ushort TickRate { get; }

    /// <summary>Gets the current monotonically increasing authoritative server tick.</summary>
    ulong CurrentTick { get; }

    /// <summary>Hydrates persisted chunk deltas from storage into the world coordinator before accepting players.</summary>
    Task InitializeFromStoreAsync(CancellationToken ct = default);

    /// <summary>Advances the authoritative simulation by one fixed tick and dispatches AoI snapshots.</summary>
    void StepFixedTick();

    /// <summary>Flushes dirty chunk deltas and active player character state to the configured stores.</summary>
    Task FlushDirtyPersistenceAsync(CancellationToken ct = default);
}
