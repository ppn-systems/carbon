// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.Identity;

/// <summary>Keeps the "Remember me" account on this device (ADR-037).</summary>
public interface IRememberedAccountStore
{
    /// <summary>Returns the remembered account, or <see langword="null"/> when none is kept or the file is unreadable.</summary>
    /// <returns>The account, or <see langword="null"/>.</returns>
    RememberedAccount? Load();

    /// <summary>Remembers <paramref name="account"/>, replacing any earlier one.</summary>
    /// <param name="account">The account to keep.</param>
    void Save(RememberedAccount account);

    /// <summary>Forgets the remembered account and deletes what was stored.</summary>
    void Clear();
}
