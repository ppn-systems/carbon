// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Abstractions.Identity;

/// <summary>
/// Result of an asynchronous login or account registration operation against the dedicated server.
/// </summary>
/// <param name="Succeeded">Whether authentication succeeded and elevated the session.</param>
/// <param name="Message">User-facing status or error explanation.</param>
/// <param name="AccountId">Authenticated account identifier when <paramref name="Succeeded"/> is <see langword="true"/>.</param>
public readonly record struct AuthenticationResult(bool Succeeded, string Message, Guid AccountId = default);
