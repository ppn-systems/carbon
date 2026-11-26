// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Contracts.Common;

/// <summary>
/// Persistent bitmask traits tracking character lifecycle, progression unlocks, and gameplay toggles.
/// </summary>
[Flags]
public enum CharacterTraits : ulong
{
    /// <summary>No traits set.</summary>
    None = 0UL,

    /// <summary>Indicates starter class gear and hotbar items were granted.</summary>
    StarterLoadoutGranted = 1UL << 0,

    /// <summary>Indicates starter onboarding guidance has been completed.</summary>
    TutorialCompleted = 1UL << 1,

    /// <summary>Indicates PvP combat participation is enabled.</summary>
    PvpEnabled = 1UL << 2,

    /// <summary>Indicates blacksmith forge crafting access is unlocked.</summary>
    ForgeUnlocked = 1UL << 3,
}
