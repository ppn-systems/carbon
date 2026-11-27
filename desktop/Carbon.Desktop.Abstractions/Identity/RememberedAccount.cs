// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Primitives;

namespace Carbon.Desktop.Abstractions.Identity;

/// <summary>
/// The account "Remember me" keeps on this device (ADR-037): the name to greet the player with, the server it signs in
/// to, and a server-issued, revocable sign-in token. Never a password.
/// </summary>
/// <param name="Username">Account username, shown as "Signed in as …".</param>
/// <param name="ServerHost">Server the token belongs to.</param>
/// <param name="ServerPort">Server port the token belongs to.</param>
/// <param name="Token">Remember-me token issued by the server.</param>
/// <param name="ExpiresAtUtc">When the server stops accepting the token.</param>
public sealed record RememberedAccount(string Username, string ServerHost, ushort ServerPort, Bytes32 Token, DateTimeOffset ExpiresAtUtc);
