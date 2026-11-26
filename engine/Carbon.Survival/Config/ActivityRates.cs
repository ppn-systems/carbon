// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Survival.Components;

namespace Carbon.Survival.Config;

/// <summary>
/// Per-activity multipliers applied to a base drain rate. Walking is the reference activity.
/// </summary>
public sealed class ActivityRates
{
    /// <summary>Gets the multiplier while idle.</summary>
    public float Idle { get; init; } = 1f;

    /// <summary>Gets the multiplier while walking.</summary>
    public float Walking { get; init; } = 1f;

    /// <summary>Gets the multiplier while sprinting.</summary>
    public float Sprinting { get; init; } = 1f;

    /// <summary>Gets the multiplier while fighting.</summary>
    public float Fighting { get; init; } = 1f;

    /// <summary>Gets the multiplier while swimming.</summary>
    public float Swimming { get; init; } = 1f;

    /// <summary>Gets the multiplier while resting.</summary>
    public float Resting { get; init; } = 1f;

    /// <summary>
    /// Returns the multiplier for <paramref name="activity"/>.
    /// </summary>
    /// <param name="activity">Current activity.</param>
    /// <returns>The drain multiplier.</returns>
    public float For(NeedsActivity activity) => activity switch
    {
        NeedsActivity.Idle => this.Idle,
        NeedsActivity.Walking => this.Walking,
        NeedsActivity.Sprinting => this.Sprinting,
        NeedsActivity.Fighting => this.Fighting,
        NeedsActivity.Swimming => this.Swimming,
        NeedsActivity.Resting => this.Resting,
        NeedsActivity.Sleeping => this.Resting,
        _ => this.Walking,
    };

    internal void Validate(string name)
    {
        NeedsConfig.RequireRange(this.Idle, 0f, 10f, name + ".idle");
        NeedsConfig.RequireRange(this.Walking, 0f, 10f, name + ".walking");
        NeedsConfig.RequireRange(this.Sprinting, 0f, 10f, name + ".sprinting");
        NeedsConfig.RequireRange(this.Fighting, 0f, 10f, name + ".fighting");
        NeedsConfig.RequireRange(this.Swimming, 0f, 10f, name + ".swimming");
        NeedsConfig.RequireRange(this.Resting, 0f, 10f, name + ".resting");
    }
}
