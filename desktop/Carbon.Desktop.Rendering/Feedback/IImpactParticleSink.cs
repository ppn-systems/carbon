// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Rendering.Feedback;

/// <summary>Receives impact particle bursts from <see cref="ImpactPresenter"/>.</summary>
public interface IImpactParticleSink
{
    /// <summary>Spawns a burst at <paramref name="position"/>, thrown along <paramref name="direction"/>.</summary>
    /// <param name="position">Impact position.</param>
    /// <param name="direction">Swing direction.</param>
    /// <param name="material">What was hit.</param>
    /// <param name="intensity">Burst size multiplier (1 = normal).</param>
    void SpawnStrikeBurst(Vector2 position, Vector2 direction, ImpactMaterial material, float intensity);
}
