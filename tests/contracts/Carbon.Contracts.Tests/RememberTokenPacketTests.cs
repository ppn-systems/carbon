// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Security.Cryptography;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Nalix.Abstractions.Primitives;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>Remember-me token packets (ADR-037) carry the token as a <see cref="Bytes32"/>, byte for byte.</summary>
public sealed class RememberTokenPacketTests
{
    private static Bytes32 RandomToken() => new(RandomNumberGenerator.GetBytes(Bytes32.Size));

    [Fact]
    public void RememberTokenResult_RoundTrip_KeepsTheTokenAndExpiry()
    {
        Bytes32 token = RandomToken();
        using RememberTokenResult result = new() { Status = OpStatus.Ok, Token = token, ExpiresAtUnixSeconds = 1_798_000_000L };

        using RememberTokenResult copy = RememberTokenResult.Deserialize(result.Serialize());

        Assert.Equal(OpStatus.Ok, copy.Status);
        Assert.True(copy.Token == token);
        Assert.True(copy.Token.AsSpan().SequenceEqual(token.AsSpan()));
        Assert.Equal(1_798_000_000L, copy.ExpiresAtUnixSeconds);
    }

    [Fact]
    public void TokenSignInRequestAndRevoke_RoundTrip_KeepTheToken()
    {
        Bytes32 token = RandomToken();
        using TokenSignInRequest signIn = new() { Token = token, ClientProtocolVersion = CarbonProtocol.Version };
        using RememberTokenRevoke revoke = new() { Token = token };

        using TokenSignInRequest signInCopy = TokenSignInRequest.Deserialize(signIn.Serialize());
        using RememberTokenRevoke revokeCopy = RememberTokenRevoke.Deserialize(revoke.Serialize());

        Assert.True(signInCopy.Token == token);
        Assert.Equal(CarbonProtocol.Version, signInCopy.ClientProtocolVersion);
        Assert.True(revokeCopy.Token == token);
    }
}
