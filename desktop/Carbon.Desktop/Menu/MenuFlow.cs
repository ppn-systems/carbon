// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Graphics.Batching.Camera;

namespace Carbon.Desktop.Menu;

/// <summary>
/// What the menu scenes share (ADR-037): the session, the remembered account, the server list and the chosen server.
/// </summary>
internal sealed class MenuFlow
{
    public MenuFlow(IWorldSession session, DesktopOptions options, Camera2D camera, IRememberedAccountStore accounts, ServerDirectory servers)
    {
        this.Session = session ?? throw new ArgumentNullException(nameof(session));
        this.Options = options ?? throw new ArgumentNullException(nameof(options));
        this.Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        this.Accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        this.Servers = servers ?? throw new ArgumentNullException(nameof(servers));
        this.SelectedServer = servers.Find(options.ServerHost, options.ServerTcpPort) ?? servers.Servers[0];
    }

    public IWorldSession Session { get; }

    public DesktopOptions Options { get; }

    public Camera2D Camera { get; }

    public IRememberedAccountStore Accounts { get; }

    public ServerDirectory Servers { get; }

    /// <summary>Gets or sets the server Login, Register and Continue connect to.</summary>
    public GameServer SelectedServer { get; set; }

    /// <summary>Creates the flow for the real client: the account file and shipped server list from <paramref name="options"/>.</summary>
    public static MenuFlow Create(IWorldSession session, DesktopOptions options, Camera2D camera) =>
        new(session, options, camera, new FileRememberedAccountStore(options.AccountFile), ServerDirectory.Load(options));

    /// <summary>
    /// After a password sign-in with "Remember me": asks the server for a token and keeps it with the username and server.
    /// The password is not a parameter: it can never reach the store.
    /// </summary>
    /// <returns>Whether the account is now remembered.</returns>
    public async Task<bool> RememberAsync(string username, GameServer server)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(server);
        RememberTokenGrant? grant = await this.Session.RequestRememberTokenAsync().ConfigureAwait(false);
        if (grant is null)
        {
            return false;
        }

        this.Accounts.Save(new RememberedAccount(username, server.Host, server.Port, grant.Token, grant.ExpiresAtUtc));
        return true;
    }

    /// <summary>Forgets a remembered account: deletes the local copy now, then revokes its token on the server.</summary>
    public Task ForgetAsync(RememberedAccount? account)
    {
        this.Accounts.Clear();
        return account is null
            ? Task.CompletedTask
            : this.Session.RevokeRememberTokenAsync(account.ServerHost, account.ServerPort, account.Token);
    }

    /// <summary>Logs out of the session and forgets any remembered account (in-game and character-screen LOGOUT).</summary>
    public async Task SignOutAsync()
    {
        RememberedAccount? remembered = this.Accounts.Load();
        await this.Session.LogoutAsync().ConfigureAwait(false);
        await this.ForgetAsync(remembered).ConfigureAwait(false);
    }
}
