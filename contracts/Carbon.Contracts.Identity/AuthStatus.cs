// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Identity;

/// <summary>
/// Canonical status returned for account registration and login requests.
/// Append-only values for wire compatibility.
/// </summary>
public enum AuthStatus : byte
{
    /// <summary>Authentication or registration succeeded.</summary>
    Ok = 0,

    /// <summary>Username or password did not match an active account.</summary>
    InvalidCredentials = 1,

    /// <summary>Username is already registered.</summary>
    AccountExists = 2,

    /// <summary>Username or password violated formatting constraints.</summary>
    InvalidInput = 3,

    /// <summary>Account is banned or locked.</summary>
    Locked = 4,

    /// <summary>Client protocol version is incompatible with server protocol.</summary>
    ProtocolMismatch = 5,
}
