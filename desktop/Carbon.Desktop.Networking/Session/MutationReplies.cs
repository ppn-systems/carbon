// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Contracts.Items;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Reply filters for the requests the client awaits a <see cref="MutationResult"/> for. Every write handler answers
/// with a <c>MutationResult</c>, and map edits are sent without waiting, so a wait without a filter could take a map
/// edit's reply as its own. <see cref="MutationResult.RequestOpCode"/> names the request each reply answers (ADR-038).
/// </summary>
internal static class MutationReplies
{
    /// <summary>Matches the reply to <c>CharacterCreate</c>.</summary>
    public static readonly Func<MutationResult, bool> CharacterCreate = static r => r.RequestOpCode == IdentityOpCode.CharacterCreate;

    /// <summary>Matches the reply to <c>AuthLogout</c>.</summary>
    public static readonly Func<MutationResult, bool> Logout = static r => r.RequestOpCode == IdentityOpCode.Logout;

    /// <summary>Matches the reply to <c>RespawnRequest</c>.</summary>
    public static readonly Func<MutationResult, bool> Respawn = static r => r.RequestOpCode == ItemOpCode.RespawnRequest;

    /// <summary>Matches the reply to <c>RememberTokenRevoke</c>.</summary>
    public static readonly Func<MutationResult, bool> RememberTokenRevoke = static r => r.RequestOpCode == ItemOpCode.RememberTokenRevoke;
}
