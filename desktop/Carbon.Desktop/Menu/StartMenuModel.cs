// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Desktop.Abstractions.Identity;

namespace Carbon.Desktop.Menu;

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

    /// <summary>Close the game (the small button under the others).</summary>
    Quit,
}

/// <summary>
/// What the start screen offers (ADR-037): with a remembered account, "Signed in as {name}" and Continue / Log out /
/// Server; without one, Login / Register / Server.
/// </summary>
internal sealed class StartMenuModel
{
    private static readonly StartMenuAction[] s_signedIn = [StartMenuAction.Continue, StartMenuAction.LogOut, StartMenuAction.Server];
    private static readonly StartMenuAction[] s_signedOut = [StartMenuAction.Login, StartMenuAction.Register, StartMenuAction.Server];
    private static readonly StartMenuAction[] s_signedInFocus = [.. s_signedIn, StartMenuAction.Quit];
    private static readonly StartMenuAction[] s_signedOutFocus = [.. s_signedOut, StartMenuAction.Quit];

    private readonly IRememberedAccountStore _accounts;

    public StartMenuModel(IRememberedAccountStore accounts)
    {
        _accounts = accounts;
        this.Remembered = accounts.Load();
        this.EnsureFocusOffered();
    }

    /// <summary>Gets the remembered account, or <see langword="null"/> when signed out.</summary>
    public RememberedAccount? Remembered { get; private set; }

    /// <summary>Gets the name shown as "Signed in as …", or <see langword="null"/> when signed out.</summary>
    public string? SignedInName => this.Remembered?.Username;

    /// <summary>Gets the buttons in display order.</summary>
    public IReadOnlyList<StartMenuAction> Actions => this.Remembered is null ? s_signedOut : s_signedIn;

    /// <summary>Gets the keyboard focus order: the buttons top to bottom, then Quit.</summary>
    public IReadOnlyList<StartMenuAction> FocusOrder => this.Remembered is null ? s_signedOutFocus : s_signedInFocus;

    /// <summary>Gets or sets the button with keyboard focus (the first button at start).</summary>
    public StartMenuAction Focus { get; set; } = StartMenuAction.Continue;

    /// <summary>Moves focus down (or up with <paramref name="reverse"/>), wrapping; a focus no longer offered restarts at the top.</summary>
    public void MoveFocus(bool reverse) => this.Focus = FocusRing.Next(this.FocusOrder, this.Focus, reverse);

    /// <summary>Puts focus on the first button when the current one is not offered (start, or after logging out).</summary>
    public void EnsureFocusOffered()
    {
        IReadOnlyList<StartMenuAction> order = this.FocusOrder;
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == this.Focus)
            {
                return;
            }
        }

        this.Focus = order[0];
    }

    /// <summary>Forgets the remembered account (deleting what was stored) and returns the forgotten one.</summary>
    /// <returns>The account that was remembered, for revoking its token, or <see langword="null"/>.</returns>
    public RememberedAccount? LogOut()
    {
        RememberedAccount? forgotten = this.Remembered;
        _accounts.Clear();
        this.Remembered = null;
        this.EnsureFocusOffered();
        return forgotten;
    }
}
