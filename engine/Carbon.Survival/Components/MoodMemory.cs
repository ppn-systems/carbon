// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Components;

/// <summary>
/// A timed mood modifier, e.g. "ate a fine meal" for +5 over 10 minutes.
/// </summary>
/// <param name="Thought">Memory kind; <see cref="MoodThought.None"/> marks an empty slot.</param>
/// <param name="Offset">Mood offset in points while active.</param>
/// <param name="RemainingSeconds">Seconds until the memory fades.</param>
public readonly record struct MoodMemory(MoodThought Thought, float Offset, float RemainingSeconds)
{
    /// <summary>Gets a value indicating whether this slot holds an active memory.</summary>
    public bool IsActive => this.Thought != MoodThought.None && this.RemainingSeconds > 0f;
}
