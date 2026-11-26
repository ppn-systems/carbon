// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.Identity;

/// <summary>
/// Shared read-only active character identity and preset contract inherited by both
/// character management (<see cref="ICharacterRosterService"/>) and world query (<see cref="World.IWorldReadModel"/>) ports.
/// </summary>
public interface ICharacterProfile
{
    /// <summary>Gets the active signed-in character display name.</summary>
    string ActiveCharacterName { get; }

    /// <summary>Gets the local player's appearance preset identifier (<c>0..3</c>).</summary>
    byte LocalPlayerAppearancePreset { get; }
}
