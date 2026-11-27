// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Primitives.Random;

/// <summary>
/// Source of the secret, rotating roll key for server-authoritative gameplay outcomes (ADR-026). Only the server holds
/// an implementation with a real secret; engine systems that roll on the tick (spawns, food poisoning) depend on this
/// interface so tests inject a fixed key and stay deterministic.
/// </summary>
public interface IGameplayEntropy
{
    /// <summary>Gets the roll key of the rotation epoch that contains <paramref name="tick"/>.</summary>
    /// <param name="tick">Simulation tick of the roll.</param>
    /// <returns>The epoch subkey.</returns>
    GameplayKey KeyAt(ulong tick);
}
