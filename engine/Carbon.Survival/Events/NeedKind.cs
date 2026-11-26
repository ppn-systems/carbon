// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Events;

/// <summary>
/// Which need a <see cref="NeedStateChangedEvent"/> refers to; selects how its state bytes are interpreted.
/// </summary>
public enum NeedKind : byte
{
    /// <summary>States are <see cref="Components.HungerState"/> values.</summary>
    Food = 0,

    /// <summary>States are <see cref="Components.RestState"/> values.</summary>
    Rest = 1,

    /// <summary>States are <see cref="Components.MoodState"/> values.</summary>
    Mood = 2,
}
