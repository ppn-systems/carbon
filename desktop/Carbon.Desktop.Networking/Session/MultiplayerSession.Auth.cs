// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Contracts.Identity.C2S;
using Carbon.Contracts.Identity.S2C;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking.Session;

public sealed partial class MultiplayerSession
{
    /// <inheritdoc/>
    public async Task<AuthenticationResult> LoginAsync(
        string host,
        ushort tcpPort,
        string username,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || tcpPort == 0)
        {
            return new AuthenticationResult(false, GameText.Get("auth.invalid_endpoint"));
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new AuthenticationResult(false, GameText.Get("auth.credentials_required"));
        }

        try
        {
            TcpSession tcp = await this.EnsureTcpConnectedAsync(host.Trim(), tcpPort, ct).ConfigureAwait(false);
            RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);

            using AuthResult result = await RequestPasswordSignInAsync(tcp, reqOpts, username.Trim(), password, ct).ConfigureAwait(false);

            if (result.Status == AuthStatus.Ok)
            {
                _isAuthenticated = true;
                _reconnectHost = host.Trim();
                _reconnectPort = tcpPort;
                _lastUsername = username.Trim();
                _lastPassword = password;
                _lastToken = [];
                return new AuthenticationResult(true, GameText.Get("auth.signed_in"), result.AccountId);
            }

            return new AuthenticationResult(false, FormatAuthStatus(result.Status));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await this.TearDownTransportsAsync().ConfigureAwait(false);

            // The exception text names sockets and addresses: log it for developers, show the player plain words.
            await Console.Error.WriteLineAsync($"[Auth] Connection failed: {ex.Message}").ConfigureAwait(false);
            return new AuthenticationResult(false, GameText.Get("auth.server_unreachable"));
        }
    }

    /// <inheritdoc/>
    public async Task<AuthenticationResult> RegisterAsync(
        string host,
        ushort tcpPort,
        string username,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || tcpPort == 0)
        {
            return new AuthenticationResult(false, GameText.Get("auth.invalid_endpoint"));
        }

        string trimmedUser = username?.Trim() ?? string.Empty;
        if (trimmedUser.Length < 3 || trimmedUser.Length > 24)
        {
            return new AuthenticationResult(false, GameText.Get("auth.username_length"));
        }

        if (string.IsNullOrEmpty(password) || password.Length < 6 || password.Length > 128)
        {
            return new AuthenticationResult(false, GameText.Get("auth.password_length"));
        }

        try
        {
            TcpSession tcp = await this.EnsureTcpConnectedAsync(host.Trim(), tcpPort, ct).ConfigureAwait(false);
            RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);

            using PacketScope<AuthRegister> lease = PacketFactory<AuthRegister>.Acquire();
            lease.Value.Username = trimmedUser;
            lease.Value.Password = password;
            lease.Value.ClientProtocolVersion = CarbonProtocol.Version;

            using AuthResult result = await tcp.RequestAsync<AuthResult>(
                lease.Value,
                reqOpts,
                predicate: null,
                ct).ConfigureAwait(false);

            if (result.Status == AuthStatus.Ok)
            {
                _isAuthenticated = true;
                _reconnectHost = host.Trim();
                _reconnectPort = tcpPort;
                _lastUsername = trimmedUser;
                _lastPassword = password;
                _lastToken = [];
                return new AuthenticationResult(true, GameText.Get("auth.registered"), result.AccountId);
            }

            return new AuthenticationResult(false, FormatAuthStatus(result.Status));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await this.TearDownTransportsAsync().ConfigureAwait(false);

            // The exception text names sockets and addresses: log it for developers, show the player plain words.
            await Console.Error.WriteLineAsync($"[Auth] Connection failed: {ex.Message}").ConfigureAwait(false);
            return new AuthenticationResult(false, GameText.Get("auth.server_unreachable"));
        }
    }

    /// <inheritdoc/>
    public async Task<AuthenticationResult> LoginWithTokenAsync(
        string host,
        ushort tcpPort,
        byte[] token,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrWhiteSpace(host) || tcpPort == 0)
        {
            return new AuthenticationResult(false, GameText.Get("auth.invalid_endpoint"));
        }

        if (token.Length == 0)
        {
            return new AuthenticationResult(false, GameText.Get("auth.invalid_credentials"));
        }

        try
        {
            TcpSession tcp = await this.EnsureTcpConnectedAsync(host.Trim(), tcpPort, ct).ConfigureAwait(false);
            RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);
            using AuthResult result = await RequestTokenSignInAsync(tcp, reqOpts, token, ct).ConfigureAwait(false);

            if (result.Status == AuthStatus.Ok)
            {
                _isAuthenticated = true;
                _reconnectHost = host.Trim();
                _reconnectPort = tcpPort;
                _lastUsername = string.Empty;
                _lastPassword = string.Empty;
                _lastToken = (byte[])token.Clone();
                return new AuthenticationResult(true, GameText.Get("auth.signed_in"), result.AccountId);
            }

            return new AuthenticationResult(false, FormatAuthStatus(result.Status), CredentialsRejected: result.Status == AuthStatus.InvalidCredentials);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await this.TearDownTransportsAsync().ConfigureAwait(false);
            await Console.Error.WriteLineAsync($"[Auth] Connection failed: {ex.Message}").ConfigureAwait(false);
            return new AuthenticationResult(false, GameText.Get("auth.server_unreachable"));
        }
    }

    /// <inheritdoc/>
    public async Task<RememberTokenGrant?> RequestRememberTokenAsync(CancellationToken ct = default)
    {
        TcpSession tcp = this.RequireAuthenticatedTcpSession();
        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);
        using PacketScope<RememberTokenRequest> lease = PacketFactory<RememberTokenRequest>.Acquire();
        using RememberTokenResult reply = await tcp.RequestAsync<RememberTokenResult>(
            lease.Value,
            reqOpts,
            predicate: null,
            ct).ConfigureAwait(false);

        return reply.Status == OpStatus.Ok && reply.Token is { Length: > 0 }
            ? new RememberTokenGrant((byte[])reply.Token.Clone(), DateTimeOffset.FromUnixTimeSeconds(reply.ExpiresAtUnixSeconds))
            : null;
    }

    /// <inheritdoc/>
    public async Task RevokeRememberTokenAsync(string host, ushort tcpPort, byte[] token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || tcpPort == 0 || token is not { Length: > 0 })
        {
            return;
        }

        try
        {
            TcpSession tcp = await this.EnsureTcpConnectedAsync(host.Trim(), tcpPort, ct).ConfigureAwait(false);
            RequestOptions reqOpts = RequestOptions.Default.WithTimeout(3000).WithEncrypt(tcp.State.EncryptionEnabled);
            using PacketScope<RememberTokenRevoke> lease = PacketFactory<RememberTokenRevoke>.Acquire();
            lease.Value.Token = token;
            using MutationResult _ = await tcp.RequestAsync<MutationResult>(lease.Value, reqOpts, predicate: null, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Best effort: the local copy is deleted regardless, and the token expires on the server by itself.
            await Console.Error.WriteLineAsync($"[Auth] Could not revoke the remembered sign-in: {ex.Message}").ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CharacterRosterEntry>> ListCharactersAsync(CancellationToken ct = default)
    {
        TcpSession tcp = this.RequireAuthenticatedTcpSession();
        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);

        List<CharacterRosterEntry> roster = [];
        using PacketScope<CharacterListRequest> lease = PacketFactory<CharacterListRequest>.Acquire();
        await foreach (CharacterSummary item in tcp.StreamAsync<CharacterSummary>(
            lease.Value,
            reqOpts,
            ct).ConfigureAwait(false))
        {
            if (item.IsTerminator || item.CharacterId == Guid.Empty)
            {
                continue;
            }

            _characterNames[item.CharacterId] = item.Name;
            _characterPresets[item.CharacterId] = item.AppearancePreset;
            roster.Add(new CharacterRosterEntry(
                item.CharacterId,
                item.Name,
                item.Level,
                item.ZoneTier,
                item.PositionX,
                item.PositionY,
                item.AppearancePreset,
                (HeroArchetypeClass)item.ClassId,
                item.Traits));
        }

        return roster;
    }

    /// <inheritdoc/>
    public async Task<(bool Succeeded, string Message)> CreateCharacterAsync(
        string characterName,
        byte appearancePreset,
        CancellationToken ct = default)
    {
        TcpSession tcp = this.RequireAuthenticatedTcpSession();
        string trimmed = characterName?.Trim() ?? string.Empty;
        if (trimmed.Length < 2 || trimmed.Length > 20)
        {
            return (false, GameText.Get("auth.name_length"));
        }

        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);
        using PacketScope<CharacterCreate> lease = PacketFactory<CharacterCreate>.Acquire();
        lease.Value.CharacterName = trimmed;
        lease.Value.AppearancePreset = appearancePreset;

        using MutationResult reply = await tcp.RequestAsync<MutationResult>(
            lease.Value,
            reqOpts,
            predicate: null,
            ct).ConfigureAwait(false);

        if (reply.Status == OpStatus.Ok)
        {
            _localAppearancePreset = appearancePreset;
            return (true, GameText.Format("auth.character_created", new TextArg("name", trimmed)));
        }

        if (reply.Status == OpStatus.Invalid)
        {
            return (false, GameText.Get("auth.invalid_name"));
        }

        if (reply.Status == OpStatus.Conflict)
        {
            return (false, GameText.Get("auth.name_taken"));
        }

        return (false, GameText.Format("auth.create_failed", new TextArg("status", reply.Status.ToString())));
    }

    /// <inheritdoc/>
    public async Task<(bool Succeeded, string Message)> EnterWorldAsync(Guid characterId, CancellationToken ct = default)
    {
        if (characterId == Guid.Empty)
        {
            return (false, GameText.Get("auth.no_character"));
        }

        TcpSession tcp = this.RequireAuthenticatedTcpSession();

        // Gameplay tables come only from the server (ADR-014): never enter the world on stale or missing config.
        try
        {
            using CancellationTokenSource configTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            configTimeout.CancelAfter(SessionConfigSync.ConfigSyncTimeoutMs);
            _ = await this.WaitForGameConfigAsync(configTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, GameText.Get("auth.config_timeout"));
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }

        RequestOptions reqOpts = RequestOptions.Default.WithTimeout(5000).WithEncrypt(tcp.State.EncryptionEnabled);

        using PacketScope<CharacterSelect> selectLease = PacketFactory<CharacterSelect>.Acquire();
        selectLease.Value.CharacterId = characterId;

        using CharacterEnterWorld enter = await tcp.RequestAsync<CharacterEnterWorld>(
            selectLease.Value,
            reqOpts,
            predicate: null,
            ct).ConfigureAwait(false);

        if (enter.Status != OpStatus.Ok)
        {
            return (false, GameText.Format("auth.enter_failed", new TextArg("status", enter.Status.ToString())));
        }

        lock (_worldGate)
        {
            _remoteEntities.Clear();
            _requestedChunks.Clear();
            _combatEvents.Clear();
            _propImpactEvents.Clear();
            _itemBreakEvents.Clear();
            _dirtyChunks.Clear();
            _hudNotifications.Clear();
            _reconciler.Clear();
            _clientTick = 0U;
            _latestServerTime = enter.TickRate > 0 ? enter.ServerTick / (double)enter.TickRate : 0.0;
            _hasServerReconcile = false;
            _visualOffset = Vector2.Zero;
            _selectedSlot = enter.SelectedHotbarSlot < 9 ? enter.SelectedHotbarSlot : (byte)0;
            _localLevel = Math.Max((ushort)1, enter.Level);
            _localExperience = enter.Experience;
            _localXpToNext = Math.Max(1U, enter.XpToNextLevel);
            _localHealth = enter.CurrentHealth;
            _localMaxHealth = MathF.Max(1f, enter.MaxHealth);
            _localMana = enter.CurrentMana;
            _localMaxMana = MathF.Max(1f, enter.MaxMana);
            this.ResetStaminaPrediction(enter.CurrentStamina);
            _localMaxStamina = MathF.Max(1f, enter.MaxStamina);
            _localDefense = enter.Defense;
            _localClass = (HeroArchetypeClass)enter.ClassId;
            _localTraits = enter.Traits;
            _skillCooldowns.Clear();
            _ = CharacterProgressionBinaryCodec.TryDeserializeInto(enter.ProgressionBlob ?? [], _progression);
            if (enter.UnlockedSkillMask != 0UL)
            {
                _progression.SetUnlockedSkillMask(_progression.UnlockedSkillMask | (Contracts.Entities.CharacterSkillMask)enter.UnlockedSkillMask);
            }

            // The server's PlayerStatsSync corrects this right after entry; until then use the synced budget.
            _unspentStatPoints = SyncedProgressionConfig.Current?.GetUnspentStatPoints(_localLevel, _progression) ?? 0;
            _unspentSkillPoints = CharacterProgressionPolicy.GetUnspentSkillPoints(_localLevel, _progression);
            _remoteViewsDirty = true;
            _cachedRemoteViews = [];
            _lockedTargetNetworkId = 0UL;
            _forgeFeedbackTimer = 0f;
            _lastForgeSucceeded = false;
            _wasMovingLastTick = false;
            _idleKeepAliveTimer = 0f;
            _inventoryLoaded = false;
            _localFacingDir = new Vector2(0f, 1f);
            _localHitFlashTimer = 0f;
            _damageVignetteTimer = 0f;
            _localStatusEffects = [];
            _currentAction = ActionState.None;
            _comboStep = 0;

            _worldSeed = enter.WorldSeed;
            _tileSize = enter.TileSize > 0f ? enter.TileSize : 32f;
            _tickRate = enter.TickRate > 0 ? enter.TickRate : (ushort)30;
            _localNetworkId = enter.PlayerNetworkId;
            _localAppearancePreset = enter.AppearancePreset != 0
                ? enter.AppearancePreset
                : (_characterPresets.TryGetValue(characterId, out byte cachedPreset) ? cachedPreset : _localAppearancePreset);
            _activeCharacterName = _characterNames.TryGetValue(characterId, out string? cachedName)
                ? cachedName
                : _options.CharacterName;

            ProceduralWorldGenerator generator = new(_worldSeed);
            _chunkWorld = new ChunkWorld2D(generator, _tileSize, enter.Frequency) { WaterRules = _waterRules.Current };
            _constructionMarkers.Clear();
            Vector2 initialPos = new(enter.PositionX, enter.PositionY);
            _chunkWorld.UpdateAroundPosition(initialPos, chunkRadius: 4, evictionRadius: 8);

            _simulation = new WorldSimulation(_tickRate, _tileSize)
            {
                WorldPhysics = _chunkWorld,
            };

            _localPlayer = _simulation.SpawnNetworkEntity(_localNetworkId, initialPos, radius: 7f);
            this.MakeLocalPlayerSwimmer();
            _simulation.Entities.SetVelocity(_localPlayer, new Velocity2D(0f, 0f, 5f));
        }

        _activeCharacterId = characterId;
        _autoReconnectEnabled = true;
        _wasTcpConnectedLastFrame = true;
        _tcpReconnectAttempt = 0;
        _tcpReconnectTimer = 0f;

        await this.FetchInventorySnapshotAsync(tcp, reqOpts, ct).ConfigureAwait(false);
        this.RequestMissingChunkDeltasAround(new Vector2(enter.PositionX, enter.PositionY));
        _udpReconnectAttempt = 0;
        _udpReconnectTimer = 1.5f;
        await this.TryOpenAuthenticatedUdpAsync(tcp, ct).ConfigureAwait(false);

        return (true, GameText.Get("auth.entered"));
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        _autoReconnectEnabled = false;
        _activeCharacterId = Guid.Empty;
        _lastUsername = string.Empty;
        _lastPassword = string.Empty;
        _lastToken = [];

        await _connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _wasTcpConnectedLastFrame = false;
            _tcpReconnectAttempt = 0;
            _tcpReconnectTimer = 0f;
            this.CancelSessionOperations();
            await this.TearDownUdpAsync().ConfigureAwait(false);
            _udpReconnectAttempt = 0;
            _udpReconnectTimer = 0f;

            TcpSession? tcp = _tcpSession;
            if (tcp is { IsConnected: true } && _isAuthenticated)
            {
                try
                {
                    RequestOptions reqOpts = RequestOptions.Default.WithTimeout(2000).WithEncrypt(tcp.State.EncryptionEnabled);
                    using PacketScope<AuthLogout> lease = PacketFactory<AuthLogout>.Acquire();
                    using MutationResult _ = await tcp.RequestAsync<MutationResult>(lease.Value, reqOpts, predicate: null, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Best-effort server logout before clearing local world state.
                }
            }

            this.ClearWorldState();
            _isAuthenticated = false;
            _activeCharacterName = string.Empty;
        }
        finally
        {
            _ = _connectGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        AuthenticationResult loginOutcome = await this.LoginAsync(
            _options.ServerHost,
            _options.ServerTcpPort,
            _options.Username,
            _options.Password,
            ct).ConfigureAwait(false);

        if (!loginOutcome.Succeeded)
        {
            AuthenticationResult regOutcome = await this.RegisterAsync(
                _options.ServerHost,
                _options.ServerTcpPort,
                _options.Username,
                _options.Password,
                ct).ConfigureAwait(false);

            if (!regOutcome.Succeeded)
            {
                throw new InvalidOperationException($"Auto authentication failed: {loginOutcome.Message} / {regOutcome.Message}");
            }
        }

        IReadOnlyList<CharacterRosterEntry> roster = await this.ListCharactersAsync(ct).ConfigureAwait(false);
        Guid selectedCharacterId;
        if (roster.Count > 0)
        {
            selectedCharacterId = roster[0].CharacterId;
        }
        else
        {
            (bool created, string createMsg) = await this.CreateCharacterAsync(_options.CharacterName, 0, ct: ct).ConfigureAwait(false);
            if (!created)
            {
                throw new InvalidOperationException($"Failed to create starter character: {createMsg}");
            }

            IReadOnlyList<CharacterRosterEntry> updated = await this.ListCharactersAsync(ct).ConfigureAwait(false);
            if (updated.Count == 0)
            {
                throw new InvalidOperationException("Character list remained empty after creation.");
            }

            selectedCharacterId = updated[0].CharacterId;
        }

        (bool entered, string enterMsg) = await this.EnterWorldAsync(selectedCharacterId, ct).ConfigureAwait(false);
        if (!entered)
        {
            throw new InvalidOperationException(enterMsg);
        }
    }

    private static async Task<AuthResult> RequestPasswordSignInAsync(TcpSession tcp, RequestOptions reqOpts, string username, string password, CancellationToken ct)
    {
        using PacketScope<AuthLogin> lease = PacketFactory<AuthLogin>.Acquire();
        lease.Value.Username = username;
        lease.Value.Password = password;
        lease.Value.ClientProtocolVersion = CarbonProtocol.Version;
        return await tcp.RequestAsync<AuthResult>(lease.Value, reqOpts, predicate: null, ct).ConfigureAwait(false);
    }

    private static async Task<AuthResult> RequestTokenSignInAsync(TcpSession tcp, RequestOptions reqOpts, byte[] token, CancellationToken ct)
    {
        using PacketScope<TokenSignInRequest> lease = PacketFactory<TokenSignInRequest>.Acquire();
        lease.Value.Token = token;
        lease.Value.ClientProtocolVersion = CarbonProtocol.Version;
        return await tcp.RequestAsync<AuthResult>(lease.Value, reqOpts, predicate: null, ct).ConfigureAwait(false);
    }

    private TcpSession RequireAuthenticatedTcpSession()
    {
        if (_tcpSession is not { IsConnected: true } || !_isAuthenticated)
        {
            throw new InvalidOperationException("Session must connect and authenticate before issuing world requests.");
        }

        return _tcpSession;
    }

    private async Task FetchInventorySnapshotAsync(TcpSession tcp, RequestOptions reqOpts, CancellationToken ct)
    {
        lock (_worldGate)
        {
            _inventoryLoaded = false;
            _inventory.Clear();
        }

        try
        {
            using PacketScope<InventoryRequest> lease = PacketFactory<InventoryRequest>.Acquire();
            await foreach (InventorySlotItem item in tcp.StreamAsync<InventorySlotItem>(
                lease.Value,
                reqOpts,
                ct).ConfigureAwait(false))
            {
                if (item.IsTerminator || item.SlotIndex >= _inventory.Capacity)
                {
                    continue;
                }

                ItemSlot rawSlot = new(item.ItemId, item.Count, 0UL, item.Durability, FreshnessSeconds: item.FreshnessSeconds);
                ItemSlot hydrated = ItemEnhancementPolicy.WithUpgradeLevel(in rawSlot, item.UpgradeLevel);
                lock (_worldGate)
                {
                    _inventory.SetSlot(item.SlotIndex, in hydrated);
                }
            }
        }
        finally
        {
            lock (_worldGate)
            {
                _inventoryLoaded = true;
            }
        }
    }

    private static string FormatAuthStatus(AuthStatus status) => status switch
    {
        AuthStatus.InvalidCredentials => GameText.Get("auth.invalid_credentials"),
        AuthStatus.AccountExists => GameText.Get("auth.username_exists"),
        AuthStatus.Locked => GameText.Get("auth.banned"),
        AuthStatus.InvalidInput => GameText.Get("auth.invalid_format"),
        AuthStatus.ProtocolMismatch => GameText.Get("auth.version_mismatch"),
        AuthStatus.Ok => GameText.Format("auth.error_status", new TextArg("status", status.ToString())),
        _ => GameText.Format("auth.error_status", new TextArg("status", status.ToString())),
    };
}
