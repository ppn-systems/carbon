// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Desktop.Abstractions.Identity;

/// <summary>
/// Role-segregated character roster management and world-entry port (ISP).
/// Consumed exclusively by character selection and creation scenes.
/// </summary>
public interface ICharacterRosterService : ICharacterProfile
{
    /// <summary>Queries all characters owned by the currently authenticated account.</summary>
    Task<IReadOnlyList<CharacterRosterEntry>> ListCharactersAsync(CancellationToken ct = default);

    /// <summary>Creates a new character on the currently authenticated account with an optional starting class.</summary>
    Task<(bool Succeeded, string Message)> CreateCharacterAsync(string characterName, byte appearancePreset, byte startingClass = 0, CancellationToken ct = default);

    /// <summary>Selects a character, enters the authoritative world, syncs inventory/chunks, and opens Authenticated UDP.</summary>
    Task<(bool Succeeded, string Message)> EnterWorldAsync(Guid characterId, CancellationToken ct = default);
}
