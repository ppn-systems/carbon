// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Prediction.Client;

/// <summary>
/// Result returned by <see cref="ClientPredictionReconciler.Reconcile"/> indicating whether client state was corrected
/// and providing the visual offset to smooth out sudden snaps.
/// </summary>
/// <param name="WasCorrected"><see langword="true"/> if server state diverged beyond threshold and triggered replay.</param>
/// <param name="CorrectedPosition">Authoritative re-predicted entity world position.</param>
/// <param name="VisualErrorOffset">Previous predicted position minus <paramref name="CorrectedPosition"/> for visual smoothing.</param>
public readonly record struct ReconciliationResult(
    bool WasCorrected,
    Vector2 CorrectedPosition,
    Vector2 VisualErrorOffset);
