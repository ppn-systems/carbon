// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Simulation.ECS.Components;

/// <summary>
/// Associates a local simulation entity with a persistent network entity identifier and optional owning session.
/// Unmanaged struct component.
/// </summary>
/// <param name="NetworkId">Authoritative network identifier shared between server and clients.</param>
/// <param name="OwnerSessionId">Optional owning session identifier (0 for non-player / world-owned entities).</param>
public readonly record struct NetworkIdentity(ulong NetworkId, ulong OwnerSessionId = 0);
