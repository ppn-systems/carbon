// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Desktop.Abstractions.Identity;

namespace Carbon.Desktop.Scenes;

/// <summary>A button on the start screen.</summary>
internal enum StartMenuAction
{
    /// <summary>Sign in with the remembered account and go to character select.</summary>
    Continue,

    /// <summary>Forget the remembered account.</summary>
    LogOut,

    /// <summary>Open the login screen.</summary>
    Login,

    /// <summary>Open the register screen.</summary>
    Register,

    /// <summary>Open the server screen.</summary>
    Server,
}

/// <summary>
/// What the start screen offers (ADR-037): with a remembered account, "Signed in as {name}" and Continue / Log out /
/// Server; without one, Login / Register / Server.
/// </summary>
internal sealed class StartMenuModel
{
    private static readonly StartMenuAction[] s_signedIn = [StartMenuAction.Continue, StartMenuAction.LogOut, StartMenuAction.Server];
    private static readonly StartMenuAction[] s_signedOut = [StartMenuAction.Login, StartMenuAction.Register, StartMenuAction.Server];

    private readonly IRememberedAccountStore _accounts;

    public StartMenuModel(IRememberedAccountStore accounts)
    {
        _accounts = accounts;
        this.Remembered = accounts.Load();
    }

    /// <summary>Gets the remembered account, or <see langword="null"/> when signed out.</summary>
    public RememberedAccount? Remembered { get; private set; }

    /// <summary>Gets the name shown as "Signed in as …", or <see langword="null"/> when signed out.</summary>
    public string? SignedInName => this.Remembered?.Username;

    /// <summary>Gets the buttons in display order.</summary>
    public IReadOnlyList<StartMenuAction> Actions => this.Remembered is null ? s_signedOut : s_signedIn;

    /// <summary>Forgets the remembered account (deleting what was stored) and returns the forgotten one.</summary>
    /// <returns>The account that was remembered, for revoking its token, or <see langword="null"/>.</returns>
    public RememberedAccount? LogOut()
    {
        RememberedAccount? forgotten = this.Remembered;
        _accounts.Clear();
        this.Remembered = null;
        return forgotten;
    }
}
