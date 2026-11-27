// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Primitives;

namespace Carbon.Desktop.Abstractions.Identity;

/// <summary>A remember-me token the server issued to the signed-in account (ADR-037).</summary>
/// <param name="Token">The token.</param>
/// <param name="ExpiresAtUtc">When the server stops accepting it.</param>
public sealed record RememberTokenGrant(Bytes32 Token, DateTimeOffset ExpiresAtUtc);
