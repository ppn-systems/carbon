// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Contracts.Items;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Handlers.Config;
using Carbon.Handlers.Internal;
using Carbon.Handlers.Session;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;

namespace Carbon.Handlers.Identity;

/// <summary>Remember-me sign-in handlers (ADR-037): issue, sign in with, and revoke a token.</summary>
public static partial class IdentityHandlers
{
    /// <summary>Opcode <c>0x40A0</c>. Issues a remember-me token for the signed-in account.</summary>
    [PacketOpcode(ItemOpCode.RememberTokenRequest)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleRememberTokenRequestAsync(IPacketContext<RememberTokenRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Captured before the first await: the pooled context may be recycled while the store runs (audit TS-4).
        IPacketSender sender = context.Sender;
        CancellationToken ct = context.CancellationToken;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        if (!ConnectionAuthRegistry.TryGetAccountId(connectionId, out Guid accountId))
        {
            await SendRememberTokenAsync(sender, OpStatus.Forbidden, [], 0L).ConfigureAwait(false);
            return;
        }

        RememberTokenIssue issued = await s_authService.IssueRememberTokenAsync(accountId, ct).ConfigureAwait(false);
        await SendRememberTokenAsync(sender, OpStatus.Ok, issued.Token, issued.ExpiresAtUtc.ToUnixTimeSeconds()).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x40A2</c>. Signs in with a remember-me token and elevates the session upon success.</summary>
    [PacketOpcode(ItemOpCode.TokenSignInRequest)]
    [PacketPermission(PermissionLevel.NONE)]
    public static async ValueTask HandleTokenSignInAsync(IPacketContext<TokenSignInRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TokenSignInRequest req = context.Packet;
        byte[] token = (byte[])req.Token.Clone();
        ushort protocolVersion = req.ClientProtocolVersion;

        // Captured before the first await: the pooled context may be recycled while the store runs (audit TS-4).
        IConnection connection = context.Connection;
        IPacketSender sender = context.Sender;
        CancellationToken ct = context.CancellationToken;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(connection);
        AuthOutcome outcome = await s_authService
            .LoginWithRememberTokenAsync(token, protocolVersion, ct)
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

        await SendAuthResultAsync(sender, outcome, connectionId).ConfigureAwait(false);
        if (outcome.Status == AuthStatus.Ok)
        {
            await ConfigHandlers.SendManifestAsync(sender, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Opcode <c>0x40A3</c>. Revokes a remember-me token; holding the token is the authority.</summary>
    [PacketOpcode(ItemOpCode.RememberTokenRevoke)]
    [PacketPermission(PermissionLevel.NONE)]
    public static async ValueTask HandleRememberTokenRevokeAsync(IPacketContext<RememberTokenRevoke> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        byte[] token = (byte[])context.Packet.Token.Clone();
        IPacketSender sender = context.Sender;
        CancellationToken ct = context.CancellationToken;
        await s_authService.RevokeRememberTokenAsync(token, ct).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, OpStatus.Ok, ItemOpCode.RememberTokenRevoke).ConfigureAwait(false);
    }

    private static ValueTask SendRememberTokenAsync(IPacketSender sender, OpStatus status, byte[] token, long expiresAtUnixSeconds)
    {
        PacketScope<RememberTokenResult> lease = PacketFactory<RememberTokenResult>.Acquire();
        try
        {
            RememberTokenResult reply = lease.Value;
            reply.Status = status;
            reply.Token = token;
            reply.ExpiresAtUnixSeconds = expiresAtUnixSeconds;
            return sender.ReplyAsync(reply).DisposeOnCompletionAsync(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
