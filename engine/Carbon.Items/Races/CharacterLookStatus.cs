// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Races;

/// <summary>Result of checking a <see cref="Carbon.Contracts.Identity.CharacterLook"/> against <c>races.json</c>.</summary>
public enum CharacterLookStatus : byte
{
    /// <summary>Every value names something the race catalog defines.</summary>
    Valid = 0,

    /// <summary>The race id is not in the catalog.</summary>
    UnknownRace = 1,

    /// <summary>The race does not allow the body type.</summary>
    UnknownBodyType = 2,

    /// <summary>The look carries a different number of options than the race defines.</summary>
    OptionCountMismatch = 3,

    /// <summary>An option's choice index is past the option's last choice.</summary>
    OptionOutOfRange = 4,
}
