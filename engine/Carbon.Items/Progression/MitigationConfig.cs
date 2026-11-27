// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>Diminishing-returns curves for defense, magic resist and evasion.</summary>
public sealed class MitigationConfig
{
    /// <summary>Gets the defense curve (physical hits).</summary>
    public MitigationCurve Physical { get; init; } = new();

    /// <summary>Gets the magic resist curve (magical hits).</summary>
    public MitigationCurve Magical { get; init; } = new();

    /// <summary>Gets the evasion curve (dodge chance against any hit).</summary>
    public MitigationCurve Evasion { get; init; } = new() { K = 100f, PerAttackerLevel = 2f, Cap = 0.3f };
}
