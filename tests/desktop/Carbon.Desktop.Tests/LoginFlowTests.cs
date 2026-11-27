// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Application.Identity;
using Carbon.Application.World;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Networking.Session;
using Carbon.Desktop.Scenes;
using Carbon.Economy;
using Carbon.Graphics.Batching.Camera;
using Carbon.Host;
using Carbon.Infrastructure.Configuration;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Nalix.Abstractions.Primitives;
using Nalix.Hosting;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Start, login, register and server screens (ADR-037): what they offer, and what "Remember me" stores.</summary>
[Collection(LiveHostGroup.Name)]
public sealed class LoginFlowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"carbon-login-{Guid.NewGuid():N}");

    private string AccountFile => Path.Combine(_dir, "account.ini");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void StartMenu_WithNoSavedAccount_ShowsLoginRegisterAndServer()
    {
        StartMenuModel model = new(new FileRememberedAccountStore(this.AccountFile));

        model.Actions.Should().Equal([StartMenuAction.Login, StartMenuAction.Register, StartMenuAction.Server], "a signed-out player can log in, register or pick a server");
        model.SignedInName.Should().BeNull();
    }

    [Fact]
    public void StartMenu_WithSavedAccount_ShowsNameContinueLogOutAndServer()
    {
        FileRememberedAccountStore store = new(this.AccountFile);
        store.Save(SampleAccount("ranger_7"));

        StartMenuModel model = new(new FileRememberedAccountStore(this.AccountFile));

        model.SignedInName.Should().Be("ranger_7", "the start screen greets the remembered account by name");
        model.Actions.Should().Equal([StartMenuAction.Continue, StartMenuAction.LogOut, StartMenuAction.Server]);
    }

    [Fact]
    public void StartMenuLogOut_DeletesTheStoredAccount()
    {
        FileRememberedAccountStore store = new(this.AccountFile);
        store.Save(SampleAccount("ranger_7"));
        StartMenuModel model = new(store);

        RememberedAccount? forgotten = model.LogOut();

        forgotten!.Username.Should().Be("ranger_7", "the forgotten account is handed back so its token can be revoked");
        File.Exists(this.AccountFile).Should().BeFalse("Log out deletes what Remember me stored");
        model.Actions.Should().Equal([StartMenuAction.Login, StartMenuAction.Register, StartMenuAction.Server]);
        new StartMenuModel(new FileRememberedAccountStore(this.AccountFile)).SignedInName.Should().BeNull("a restart stays signed out");
    }

    [Fact]
    public void FileRememberedAccountStore_SaveThenLoad_RoundTripsAndIsOwnerOnly()
    {
        FileRememberedAccountStore store = new(this.AccountFile);
        RememberedAccount saved = SampleAccount("ranger_7");

        store.Save(saved);
        RememberedAccount? loaded = store.Load();

        loaded.Should().NotBeNull();
        loaded!.Username.Should().Be(saved.Username);
        loaded.ServerHost.Should().Be(saved.ServerHost);
        loaded.ServerPort.Should().Be(saved.ServerPort);
        (loaded.Token == saved.Token).Should().BeTrue("the token survives the file through Bytes32.ToString and Parse");
        File.ReadAllText(this.AccountFile).Should().Contain(saved.Token.ToString(), "account.ini keeps the token as Bytes32 text");
        loaded.ExpiresAtUtc.Should().Be(saved.ExpiresAtUtc);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(this.AccountFile).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite, "only the player may read the token");
        }
    }

    [Fact]
    public void Bytes32_ToStringThenParse_RoundTripsEveryByte()
    {
        Bytes32 token = new(System.Security.Cryptography.RandomNumberGenerator.GetBytes(Bytes32.Size));

        Bytes32 parsed = Bytes32.Parse(token.ToString());

        (parsed == token).Should().BeTrue("account.ini relies on this text form");
        parsed.AsSpan().SequenceEqual(token.AsSpan()).Should().BeTrue();
    }

    [Fact]
    public void FileRememberedAccountStore_WithCorruptFile_LoadsAsSignedOut()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(this.AccountFile, "[account]\nusername = x\ntoken = !!!\n");

        new FileRememberedAccountStore(this.AccountFile).Load().Should().BeNull("a damaged file must not crash the start screen");
    }

    [Fact]
    public void LoginFormTab_CyclesUsernamePasswordRememberMeLogin()
    {
        LoginForm form = new();
        LoginFocus[] seen = new LoginFocus[5];

        for (int i = 0; i < seen.Length; i++)
        {
            seen[i] = form.Focus;
            form.Tab(reverse: false);
        }

        seen.Should().Equal([LoginFocus.Username, LoginFocus.Password, LoginFocus.RememberMe, LoginFocus.Submit, LoginFocus.Username], "Tab moves username → password → checkbox → Login and wraps");
        form.Focus = LoginFocus.Username;
        form.Tab(reverse: true);
        form.Focus.Should().Be(LoginFocus.Submit, "Shift+Tab walks backwards");
    }

    [Fact]
    public void RegisterFormTab_CyclesUsernamePasswordConfirmRegister()
    {
        RegisterForm form = new();
        RegisterFocus[] seen = new RegisterFocus[4];

        for (int i = 0; i < seen.Length; i++)
        {
            seen[i] = form.Focus;
            form.Tab(reverse: false);
        }

        seen.Should().Equal([RegisterFocus.Username, RegisterFocus.Password, RegisterFocus.Confirm, RegisterFocus.Submit]);
    }

    [Fact]
    public void RegisterFormValidate_WhenConfirmDiffers_BlocksSubmission()
    {
        RegisterForm form = new() { Username = "ranger_7", Password = "secret-pass", Confirm = "secret-pasS" };

        form.Validate().Should().Be("login.error.password_mismatch", "a mistyped confirmation must not reach the server");
        form.Confirm = "secret-pass";
        form.Validate().Should().BeNull("matching passwords may be sent");
    }

    [Theory]
    [InlineData("ab", "secret-pass", "login.error.username_length")]
    [InlineData("ranger.7", "secret-pass", "login.error.username_chars")]
    [InlineData("ranger_7", "short", "login.error.password_length")]
    public void RegisterFormValidate_MirrorsServerRules(string username, string password, string expectedKey)
    {
        RegisterForm form = new() { Username = username, Password = password, Confirm = password };

        form.Validate().Should().Be(expectedKey);
    }

    [Fact]
    public void LoginFormValidate_WithEmptyPassword_AsksForCredentials()
    {
        new LoginForm { Username = "ranger_7" }.Validate().Should().Be("auth.credentials_required");
        new LoginForm { Username = "ranger_7", Password = "x" }.Validate().Should().BeNull("the server, not the client, judges a login password");
    }

    [Fact]
    public void ServerDirectory_ListsFriendlyNamesAndAddsAnUnlistedStartEndpointAsCustom()
    {
        IniDocument doc = IniDocument.Parse("[server.home]\nname = Home Realm\nhost = 127.0.0.1\nport = 57207\n");

        ServerDirectory listed = ServerDirectory.FromIni(doc, "127.0.0.1", 57207);
        ServerDirectory withDev = ServerDirectory.FromIni(doc, "10.0.0.5", 6000);

        listed.Servers.Should().ContainSingle().Which.Name.Should().Be("Home Realm");
        withDev.Servers.Should().HaveCount(2);
        withDev.Servers[1].IsCustom.Should().BeTrue("a developer endpoint is offered as a custom server, not by its address");
        withDev.Find("10.0.0.5", 6000).Should().Be(withDev.Servers[1]);
    }

    [Theory]
    [InlineData("play.example.com", "play.example.com", 57207)]
    [InlineData("10.0.0.5:6000", "10.0.0.5", 6000)]
    public void ServerSelectTryParseAddress_AcceptsNameOrNameAndPort(string raw, string host, int port)
    {
        ServerSelectScene.TryParseAddress(raw, out string parsedHost, out ushort parsedPort).Should().BeTrue();
        parsedHost.Should().Be(host);
        parsedPort.Should().Be((ushort)port);
    }

    [Fact]
    public void ShippedServerList_LoadsAndNamesEveryServer()
    {
        string path = Path.Combine(FindRepoRoot(), "resources", "client", ServerDirectory.FileName);

        ServerDirectory servers = ServerDirectory.FromIni(IniDocument.Load(path), "127.0.0.1", 57207);

        servers.Servers.Should().NotBeEmpty();
        servers.Servers.Should().OnlyContain(s => !s.IsCustom && s.Name.Length > 0, "the shipped list names its servers, so the default endpoint needs no custom entry");
    }

    [Fact]
    public async Task RememberMe_OverLiveServer_StoresNoPasswordSignsInWithTokenAndLogOutRevokesIt()
    {
        const string username = "remember_e2e";
        const string password = "Pa55word-Never-On-Disk";
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-e2e-remember-{Guid.NewGuid():N}.db");
        ushort tcpPort = AllocateFreeTcpPort();
        ushort udpPort = AllocateFreeUdpPort();
        try
        {
            using SqliteConnectionFactory dbFactory = new($"Data Source={dbPath}");
            await new SqliteSchemaMigrator(dbFactory).MigrateAsync().ConfigureAwait(true);
            SqliteCharacterStore characters = new(dbFactory);
            using AuthService authService = new(
                new SqliteAccountStore(dbFactory),
                characters,
                new Pbkdf2PasswordHasher(),
                rememberTokens: new SqliteRememberTokenStore(dbFactory));
            SessionTransportRegistry transportRegistry = new();
            WorldOutboundSink outboundSink = new(transportRegistry, NullLogger<WorldOutboundSink>.Instance);
            HostRuntimeOptions hostOptions = new() { WorldSeed = 1337UL, TileSize = 32f, Frequency = 0.0008f, TickRate = 30, WebSocketPort = 0, TcpPort = tcpPort, UdpPort = udpPort };
            AuthoritativeWorld world = new(
                hostOptions.WorldSeed,
                hostOptions.TileSize,
                hostOptions.Frequency,
                hostOptions.TickRate,
                new SqliteChunkDeltaRepository(dbFactory),
                characters,
                outboundSink,
                economy: GameEconomy.LoadFromDirectory(GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)!));
            await world.InitializeFromStoreAsync().ConfigureAwait(true);
            NetworkApplication app = Startup.Build(hostOptions, authService, world, outboundSink, transportRegistry, NullLogger.Instance);
            await app.ActivateAsync(CancellationToken.None).ConfigureAwait(true);

            DesktopOptions options = new() { ServerHost = "127.0.0.1", ServerTcpPort = tcpPort, AccountFile = this.AccountFile };
            MultiplayerSession first = new(options);
            MultiplayerSession second = new(options);
            try
            {
                MenuFlow flow = new(first, options, new Camera2D(1280f, 720f), new FileRememberedAccountStore(this.AccountFile), ServerDirectory.FromIni(null, "127.0.0.1", tcpPort));
                (await first.RegisterAsync("127.0.0.1", tcpPort, username, password).ConfigureAwait(true)).Succeeded.Should().BeTrue();

                // Login screen with "Remember me" ticked.
                (await flow.RememberAsync(username, flow.SelectedServer).ConfigureAwait(true)).Should().BeTrue();

                byte[] fileBytes = await File.ReadAllBytesAsync(this.AccountFile).ConfigureAwait(true);
                string fileText = Encoding.UTF8.GetString(fileBytes);
                fileBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(password)).Should().Be(-1, "Remember me must never write the password");
                fileText.Should().NotContainEquivalentOf(Convert.ToBase64String(Encoding.UTF8.GetBytes(password)).TrimEnd('='), "not even encoded");
                fileText.Should().NotContainEquivalentOf(Base64Url.EncodeToString(Encoding.UTF8.GetBytes(password)), "not even encoded");
                fileText.Should().NotContainEquivalentOf(Convert.ToHexString(Encoding.UTF8.GetBytes(password)), "not even as hex, the token's text form");
                fileText.Should().Contain(username);

                // Restart: the start screen's Continue signs in on another session with the stored token alone.
                RememberedAccount remembered = new FileRememberedAccountStore(this.AccountFile).Load()!;
                AuthenticationResult continued = await second.LoginWithTokenAsync(remembered.ServerHost, remembered.ServerPort, remembered.Token).ConfigureAwait(true);
                continued.Succeeded.Should().BeTrue("the server validates the remembered token");
                second.IsAuthenticated.Should().BeTrue();
                (await second.ListCharactersAsync().ConfigureAwait(true)).Should().BeEmpty("the token signs in the right, freshly registered account");

                // Log out: local copy deleted and the token revoked on the server.
                await new MenuFlow(second, options, new Camera2D(1280f, 720f), new FileRememberedAccountStore(this.AccountFile), ServerDirectory.FromIni(null, "127.0.0.1", tcpPort))
                    .SignOutAsync().ConfigureAwait(true);
                File.Exists(this.AccountFile).Should().BeFalse();
                AuthenticationResult replay = await first.LoginWithTokenAsync("127.0.0.1", tcpPort, remembered.Token).ConfigureAwait(true);
                replay.Succeeded.Should().BeFalse("a logged-out token is revoked server-side and cannot be replayed");
                replay.CredentialsRejected.Should().BeTrue();
            }
            finally
            {
                await first.DisposeAsync().ConfigureAwait(true);
                await second.DisposeAsync().ConfigureAwait(true);
                await app.DeactivateAsync(CancellationToken.None).ConfigureAwait(true);
                await app.DisposeAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    private static RememberedAccount SampleAccount(string username) =>
        new(username, "127.0.0.1", 57207, new Bytes32(System.Security.Cryptography.RandomNumberGenerator.GetBytes(Bytes32.Size)), new DateTimeOffset(2026, 12, 27, 0, 0, 0, TimeSpan.Zero));

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Carbon.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static ushort AllocateFreeTcpPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static ushort AllocateFreeUdpPort()
    {
        using UdpClient udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        return (ushort)((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
}
