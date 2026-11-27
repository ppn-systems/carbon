// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;

namespace Carbon.Desktop.Abstractions.Identity;

/// <summary>
/// Shared read-only active character identity and look contract inherited by both
/// character management (<see cref="ICharacterRosterService"/>) and world query (<see cref="World.IWorldReadModel"/>) ports.
/// </summary>
public interface ICharacterProfile
{
    /// <summary>Gets the active signed-in character display name.</summary>
    string ActiveCharacterName { get; }

    /// <summary>Gets the local player's look: race, body type and option choices (ADR-042).</summary>
    CharacterLook LocalPlayerLook { get; }

    /// <summary>Gets the server-synced race catalog (<c>races.json</c>) the creator and the rig picker read (ADR-042).</summary>
    SyncedRaceCatalog Races { get; }
}
