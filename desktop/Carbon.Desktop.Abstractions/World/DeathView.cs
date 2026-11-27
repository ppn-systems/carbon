// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// The local player's death as reported by the server's <c>PlayerDeath</c> push (ADR-031). Ids only: the death screen
/// localizes the cause and the killer's name. The gold the server drops as ground piles is not carried: the death
/// screen does not show it.
/// </summary>
/// <param name="Cause">What killed the player.</param>
/// <param name="KillerArchetypeId">The killing monster's archetype id (0 when not a monster).</param>
/// <param name="KillerNameKey">Localization key of the killing monster's name (<c>monster.&lt;id&gt;.name</c>), or <see langword="null"/>.</param>
public readonly record struct DeathView(DeathCause Cause, ushort KillerArchetypeId, string? KillerNameKey = null);
