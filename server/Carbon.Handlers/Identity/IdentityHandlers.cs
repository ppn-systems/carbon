// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
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
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        AuthOutcome outcome = await s_authService
            .RegisterAsync(req.Username, req.Password, req.ClientProtocolVersion, context.CancellationToken)
            .ConfigureAwait(false);

        if (outcome.Status == AuthStatus.Ok)
        {
            if (ConnectionAuthRegistry.TryGetAccountId(connectionId, out _))
            {
                _ = s_world.LeaveWorld(connectionId);
            }

            ConnectionAuthRegistry.SetAuthenticated(connectionId, outcome.AccountId);
            context.Connection.Level = PermissionLevel.USER;
        }

        await SendAuthResultAsync(context, outcome, connectionId).ConfigureAwait(false);
        if (outcome.Status == AuthStatus.Ok)
        {
            await ConfigHandlers.SendManifestAsync(context.Sender, context.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Opcode <c>0x0102</c>. Authenticates an existing account and elevates the session upon success.</summary>
    [PacketOpcode(IdentityOpCode.Login)]
    [PacketPermission(PermissionLevel.NONE)]
    public static async ValueTask HandleLoginAsync(IPacketContext<AuthLogin> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        AuthLogin req = context.Packet;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        AuthOutcome outcome = await s_authService
            .LoginAsync(req.Username, req.Password, req.ClientProtocolVersion, context.CancellationToken)
            .ConfigureAwait(false);

        if (outcome.Status == AuthStatus.Ok)
        {
            if (ConnectionAuthRegistry.TryGetAccountId(connectionId, out _))
            {
                _ = s_world.LeaveWorld(connectionId);
            }

            ConnectionAuthRegistry.SetAuthenticated(connectionId, outcome.AccountId);
            context.Connection.Level = PermissionLevel.USER;
        }

        await SendAuthResultAsync(context, outcome, connectionId).ConfigureAwait(false);
        if (outcome.Status == AuthStatus.Ok)
        {
            await ConfigHandlers.SendManifestAsync(context.Sender, context.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Opcode <c>0x0103</c>. Streams all characters belonging to the signed-in account.</summary>
    [PacketOpcode(IdentityOpCode.CharacterListRequest)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleCharacterListAsync(IPacketContext<CharacterListRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        if (ConnectionAuthRegistry.TryGetAccountId(connectionId, out Guid accountId))
        {
            IReadOnlyList<CharacterRecord> characters = await s_authService
                .ListCharactersAsync(accountId, context.CancellationToken)
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
                item.AppearancePreset = c.AppearancePreset;
                item.ClassId = (byte)c.Class;
                item.Traits = c.Flags;
                await context.Sender.ReplyAsync(item).ConfigureAwait(false);
            }
        }

        using PacketScope<CharacterSummary> termLease = PacketFactory<CharacterSummary>.Acquire();
        CharacterSummary terminator = termLease.Value;
        terminator.IsEndOfStream = true;
        terminator.IsTerminator = true;
        terminator.CharacterId = Guid.Empty;
        terminator.Name = string.Empty;
        await context.Sender.ReplyAsync(terminator).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x0104</c>. Creates a new character starting near the world origin spawn.</summary>
    [PacketOpcode(IdentityOpCode.CharacterCreate)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleCharacterCreateAsync(IPacketContext<CharacterCreate> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        if (!ConnectionAuthRegistry.TryGetAccountId(connectionId, out Guid accountId))
        {
            await PacketReplyHelper.SendMutationAsync(context, OpStatus.Forbidden, IdentityOpCode.CharacterCreate).ConfigureAwait(false);
            return;
        }

        Vector2 spawn = s_world.DefaultSpawnWorldPosition;
        CharacterCreateResult result = await s_authService
            .CreateCharacterAsync(
                accountId,
                context.Packet.CharacterName,
                context.Packet.AppearancePreset,
                spawn.X,
                spawn.Y,
                context.Packet.StartingClass,
                context.CancellationToken)
            .ConfigureAwait(false);

        OpStatus status = result.Succeeded
            ? OpStatus.Ok
            : result.FailureReason switch
            {
                "invalid_name" or "invalid_class" => OpStatus.Invalid,
                "character_limit_reached" => OpStatus.Forbidden,
                _ => OpStatus.Conflict,
            };

        await PacketReplyHelper.SendMutationAsync(context, status, IdentityOpCode.CharacterCreate).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x0105</c>. Spawns an owned character into the authoritative world simulation.</summary>
    [PacketOpcode(IdentityOpCode.CharacterSelect)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleCharacterSelectAsync(IPacketContext<CharacterSelect> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        if (!ConnectionAuthRegistry.TryGetAccountId(connectionId, out Guid accountId))
        {
            await SendEnterWorldAsync(context, new EnterWorldResult(OpStatus.Forbidden, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ConfigureAwait(false);
            return;
        }

        CharacterRecord? owned = await s_authService
            .FindOwnedCharacterAsync(accountId, context.Packet.CharacterId, context.CancellationToken)
            .ConfigureAwait(false);

        if (owned is null)
        {
            await SendEnterWorldAsync(context, new EnterWorldResult(OpStatus.NotFound, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ConfigureAwait(false);
            return;
        }

        if (s_world.TryGetPendingCharacterSnapshot(owned.Id, out CharacterRecord? pendingSnapshot) && pendingSnapshot is not null)
        {
            owned = pendingSnapshot;
        }

        EnterWorldResult enter = s_world.TryEnterWorld(connectionId, connectionId, owned);
        await SendEnterWorldAsync(context, enter).ConfigureAwait(false);
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

    private static ValueTask SendAuthResultAsync<TReq>(IPacketContext<TReq> context, AuthOutcome outcome, ulong connectionId)
        where TReq : IPacket
    {
        PacketScope<AuthResult> lease = PacketFactory<AuthResult>.Acquire();
        try
        {
            AuthResult reply = lease.Value;
            reply.Status = outcome.Status;
            reply.AccountId = outcome.AccountId;
            reply.SessionId = outcome.Status == AuthStatus.Ok ? connectionId : 0UL;
            reply.ServerProtocolVersion = CarbonProtocol.Version;
            return context.Sender.ReplyAsync(reply).DisposeOnCompletionAsync(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static ValueTask SendEnterWorldAsync(IPacketContext<CharacterSelect> context, EnterWorldResult result)
    {
        PacketScope<CharacterEnterWorld> lease = PacketFactory<CharacterEnterWorld>.Acquire();
        try
        {
            CharacterEnterWorld reply = lease.Value;
            reply.Status = result.Status;
            reply.PlayerNetworkId = result.PlayerNetworkId;
            reply.WorldSeed = result.WorldSeed;
            reply.GeneratorVersion = result.GeneratorVersion;
            reply.TileSize = result.TileSize;
            reply.Frequency = result.Frequency;
            reply.PositionX = result.PositionX;
            reply.PositionY = result.PositionY;
            reply.ServerTick = result.ServerTick;
            reply.TickRate = result.TickRate;
            reply.AppearancePreset = result.AppearancePreset;
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
            return context.Sender.ReplyAsync(reply).DisposeOnCompletionAsync(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
