// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Native AOT persistence store for player accounts.
/// </summary>
public interface IAccountStore
{
    /// <summary>
    /// Finds an account by invariant normalized username.
    /// </summary>
    /// <param name="normalizedUsername">Uppercase trimmed username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account record if found; otherwise <see langword="null"/>.</returns>
    Task<AccountRecord?> FindByUsernameAsync(string normalizedUsername, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new account record if the normalized username is not already taken.
    /// </summary>
    /// <param name="account">Account to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if created; <see langword="false"/> if a duplicate username existed.</returns>
    Task<bool> TryCreateAsync(AccountRecord account, CancellationToken ct = default);
}
