// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Shared read-only active character identity and preset contract inherited by both
/// character management (<see cref="IClientCharacterService"/>) and world query (<see cref="IClientWorldReadModel"/>) ports.
/// </summary>
public interface IClientCharacterProfile
{
    /// <summary>Gets the active signed-in character display name.</summary>
    string ActiveCharacterName { get; }

    /// <summary>Gets the local player's appearance preset identifier (<c>0..3</c>).</summary>
    byte LocalPlayerAppearancePreset { get; }
}
