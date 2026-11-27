// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Contracts.Identity.C2S;
using Carbon.Contracts.Identity.S2C;
using Carbon.Handlers.Config;
using Carbon.Handlers.Internal;
using Carbon.Handlers.Session;
using Carbon.Maps.Generators;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;

namespace Carbon.Handlers.Identity;

/// <summary>
/// Stateless packet handlers for registration, login, logout, and character selection.
/// </summary>
[PacketHandler("Carbon.Identity")]
public static partial class IdentityHandlers
{
    [Inject] private static IAuthService s_authService = null!;
    [Inject] private static IWorldSessionGateway s_world = null!;
    [Inject] private static ISessionTransportRegistry s_transports = null!;

    /// <summary>Opcode <c>0x0101</c>. Registers a new account and elevates the session upon success.</summary>
    [PacketOpcode(IdentityOpCode.Register)]
    [PacketPermission(PermissionLevel.NONE)]
    public static async ValueTask HandleRegisterAsync(IPacketContext<AuthRegister> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        AuthRegister req = context.Packet;

        // Captured before the first await: the pooled context may be recycled while the hash runs (audit TS-4).
        IConnection connection = context.Connection;
        IPacketSender sender = context.Sender;
        CancellationToken ct = context.CancellationToken;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(connection);
        AuthOutcome outcome = await s_authService
            .RegisterAsync(req.Username, req.Password, req.ClientProtocolVersion, ct)
            .ConfigureAwait(false);

        if (outcome.Status == AuthStatus.Ok)
        {
            if (ConnectionAuthRegistry.TryGetAccountId(connectionId, out _))
            {
                _ = s_world.LeaveWorld(connectionId);
            }

            ConnectionAuthRegistry.SetAuthenticated(connectionId, outcome.AccountId);
            connection.Level = PermissionLevel.USER;
        }

        await SendAuthResultAsync(sender, outcome).ConfigureAwait(false);
        if (outcome.Status == AuthStatus.Ok)
        {
            await ConfigHandlers.SendManifestAsync(sender, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Opcode <c>0x0102</c>. Authenticates an existing account and elevates the session upon success.</summary>
    [PacketOpcode(IdentityOpCode.Login)]
    [PacketPermission(PermissionLevel.NONE)]
    public static async ValueTask HandleLoginAsync(IPacketContext<AuthLogin> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        AuthLogin req = context.Packet;

        // Captured before the first await: the pooled context may be recycled while the hash runs (audit TS-4).
        IConnection connection = context.Connection;
        IPacketSender sender = context.Sender;
        CancellationToken ct = context.CancellationToken;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(connection);
        AuthOutcome outcome = await s_authService
            .LoginAsync(req.Username, req.Password, req.ClientProtocolVersion, connection.NetworkEndpoint?.Address, ct)
            .ConfigureAwait(false);

        if (outcome.Status == AuthStatus.Ok)
        {
            if (ConnectionAuthRegistry.TryGetAccountId(connectionId, out _))
            {
                _ = s_world.LeaveWorld(connectionId);
            }

            ConnectionAuthRegistry.SetAuthenticated(connectionId, outcome.AccountId);
            connection.Level = PermissionLevel.USER;
        }

        await SendAuthResultAsync(sender, outcome).ConfigureAwait(false);
        if (outcome.Status == AuthStatus.Ok)
        {
            await ConfigHandlers.SendManifestAsync(sender, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Opcode <c>0x0103</c>. Streams all characters belonging to the signed-in account.</summary>
    [PacketOpcode(IdentityOpCode.CharacterListRequest)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleCharacterListAsync(IPacketContext<CharacterListRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Captured before the first await: the pooled context may be recycled while the store runs (audit TS-4).
        IPacketSender sender = context.Sender;
        CancellationToken ct = context.CancellationToken;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        if (ConnectionAuthRegistry.TryGetAccountId(connectionId, out Guid accountId))
        {
            IReadOnlyList<CharacterRecord> characters = await s_authService
                .ListCharactersAsync(accountId, ct)
                .ConfigureAwait(false);

            for (int i = 0; i < characters.Count; i++)
            {
                CharacterRecord c = s_world.TryGetPendingCharacterSnapshot(characters[i].Id, out CharacterRecord? latest) && latest is not null
                    ? latest
                    : characters[i];
                using PacketScope<CharacterSummary> lease = PacketFactory<CharacterSummary>.Acquire();
                CharacterSummary item = lease.Value;
                item.IsEndOfStream = false;
                item.IsTerminator = false;
                item.CharacterId = c.Id;
                item.Name = c.Name;
                item.Level = c.Level;
                item.ZoneTier = WorldProgression.GetZoneTier(
                    s_world.WorldSeed,
                    (int)MathF.Floor(c.PositionX / s_world.TileSize),
                    (int)MathF.Floor(c.PositionY / s_world.TileSize));
                item.PositionX = c.PositionX;
                item.PositionY = c.PositionY;
                item.RaceId = c.Look.RaceId;
                item.BodyType = c.Look.BodyType;
                item.AppearanceOptions = c.Look.ToOptionArray();
                item.ClassId = (byte)c.Class;
                item.Traits = c.Flags;
                await sender.ReplyAsync(item).ConfigureAwait(false);
            }
        }

        using PacketScope<CharacterSummary> termLease = PacketFactory<CharacterSummary>.Acquire();
        CharacterSummary terminator = termLease.Value;
        terminator.IsEndOfStream = true;
        terminator.IsTerminator = true;
        terminator.CharacterId = Guid.Empty;
        terminator.Name = string.Empty;
        await sender.ReplyAsync(terminator).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x0104</c>. Creates a new character starting near the world origin spawn.</summary>
    [PacketOpcode(IdentityOpCode.CharacterCreate)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleCharacterCreateAsync(IPacketContext<CharacterCreate> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Captured before the first await: the pooled context may be recycled while the store runs (audit TS-4).
        IPacketSender sender = context.Sender;
        CancellationToken ct = context.CancellationToken;
        string characterName = context.Packet.CharacterName;
        CharacterCreate request = context.Packet;
        bool lookFits = CharacterLook.TryCreate(request.RaceId, request.BodyType, request.AppearanceOptions, out CharacterLook look);
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        if (!ConnectionAuthRegistry.TryGetAccountId(connectionId, out Guid accountId))
        {
            await PacketReplyHelper.SendMutationAsync(sender, OpStatus.Forbidden, IdentityOpCode.CharacterCreate).ConfigureAwait(false);
            return;
        }

        if (!lookFits)
        {
            await PacketReplyHelper.SendMutationAsync(sender, OpStatus.Invalid, IdentityOpCode.CharacterCreate).ConfigureAwait(false);
            return;
        }

        Vector2 spawn = s_world.DefaultSpawnWorldPosition;
        CharacterCreateResult result = await s_authService
            .CreateCharacterAsync(
                accountId,
                characterName,
                look,
                spawn.X,
                spawn.Y,
                ct)
            .ConfigureAwait(false);

        OpStatus status = result.Succeeded
            ? OpStatus.Ok
            : result.FailureReason switch
            {
                "invalid_name" or "invalid_look" => OpStatus.Invalid,
                "character_limit_reached" => OpStatus.Forbidden,
                _ => OpStatus.Conflict,
            };

        await PacketReplyHelper.SendMutationAsync(sender, status, IdentityOpCode.CharacterCreate).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x0105</c>. Spawns an owned character into the authoritative world simulation.</summary>
    [PacketOpcode(IdentityOpCode.CharacterSelect)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleCharacterSelectAsync(IPacketContext<CharacterSelect> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Captured before the first await: the pooled context may be recycled while the store runs (audit TS-4).
        IPacketSender sender = context.Sender;
        CancellationToken ct = context.CancellationToken;
        Guid characterId = context.Packet.CharacterId;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        if (!ConnectionAuthRegistry.TryGetAccountId(connectionId, out Guid accountId))
        {
            await SendEnterWorldAsync(sender, new EnterWorldResult(OpStatus.Forbidden, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ConfigureAwait(false);
            return;
        }

        CharacterRecord? owned = await s_authService
            .FindOwnedCharacterAsync(accountId, characterId, ct)
            .ConfigureAwait(false);

        if (owned is null)
        {
            await SendEnterWorldAsync(sender, new EnterWorldResult(OpStatus.NotFound, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ConfigureAwait(false);
            return;
        }

        if (s_world.TryGetPendingCharacterSnapshot(owned.Id, out CharacterRecord? pendingSnapshot) && pendingSnapshot is not null)
        {
            owned = pendingSnapshot;
        }

        EnterWorldResult enter = s_world.TryEnterWorld(connectionId, owned);
        await SendEnterWorldAsync(sender, enter).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x0106</c>. Logs out, removes world session, and clears connection identity.</summary>
    [PacketOpcode(IdentityOpCode.Logout)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleLogoutAsync(IPacketContext<AuthLogout> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        s_transports.DisconnectPairedDatagram(connectionId);
        _ = s_world.LeaveWorld(connectionId);
        _ = ConnectionAuthRegistry.Clear(connectionId);
        context.Connection.Level = PermissionLevel.NONE;

        return PacketReplyHelper.SendMutationAsync(context, OpStatus.Ok, IdentityOpCode.Logout);
    }

    private static ValueTask SendAuthResultAsync(IPacketSender sender, AuthOutcome outcome)
    {
        PacketScope<AuthResult> lease = PacketFactory<AuthResult>.Acquire();
        try
        {
            AuthResult reply = lease.Value;
            reply.Status = outcome.Status;
            reply.AccountId = outcome.AccountId;
            return sender.ReplyAsync(reply).DisposeOnCompletionAsync(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static ValueTask SendEnterWorldAsync(IPacketSender sender, EnterWorldResult result)
    {
        PacketScope<CharacterEnterWorld> lease = PacketFactory<CharacterEnterWorld>.Acquire();
        try
        {
            CharacterEnterWorld reply = lease.Value;
            reply.Status = result.Status;
            reply.PlayerNetworkId = result.PlayerNetworkId;
            reply.WorldSeed = result.WorldSeed;
            reply.TileSize = result.TileSize;
            reply.Frequency = result.Frequency;
            reply.PositionX = result.PositionX;
            reply.PositionY = result.PositionY;
            reply.ServerTick = result.ServerTick;
            reply.TickRate = result.TickRate;
            reply.RaceId = result.Look.RaceId;
            reply.BodyType = result.Look.BodyType;
            reply.AppearanceOptions = result.Look.ToOptionArray(reply.AppearanceOptions);
            reply.SelectedHotbarSlot = result.SelectedHotbarSlot;
            reply.Level = result.Level;
            reply.Experience = result.Experience;
            reply.XpToNextLevel = result.XpToNextLevel;
            reply.CurrentHealth = result.CurrentHealth;
            reply.MaxHealth = result.MaxHealth;
            reply.CurrentMana = result.CurrentMana;
            reply.MaxMana = result.MaxMana;
            reply.CurrentStamina = result.CurrentStamina;
            reply.MaxStamina = result.MaxStamina;
            reply.Defense = result.Defense;
            reply.ClassId = result.ClassId;
            reply.Traits = result.Traits;
            reply.UnlockedSkillMask = result.UnlockedSkillMask;
            reply.ProgressionBlob = result.ProgressionBlob ?? [];
            return sender.ReplyAsync(reply).DisposeOnCompletionAsync(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
