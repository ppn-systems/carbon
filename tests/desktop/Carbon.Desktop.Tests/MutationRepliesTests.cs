// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Contracts.Items;
using Carbon.Contracts.Maps;
using Carbon.Desktop.Networking.Session;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-038: a wait for a <c>MutationResult</c> takes only the reply to its own request.</summary>
public sealed class MutationRepliesTests
{
    public static TheoryData<string, ushort> Filters => new()
    {
        { nameof(MutationReplies.CharacterCreate), IdentityOpCode.CharacterCreate },
        { nameof(MutationReplies.Logout), IdentityOpCode.Logout },
        { nameof(MutationReplies.Respawn), ItemOpCode.RespawnRequest },
        { nameof(MutationReplies.RememberTokenRevoke), ItemOpCode.RememberTokenRevoke },
    };

    [Theory]
    [MemberData(nameof(Filters))]
    public void Filter_MatchesItsOwnRequest_AndNotAMapEditsReply(string name, ushort requestOpCode)
    {
        Func<MutationResult, bool> filter = name switch
        {
            nameof(MutationReplies.CharacterCreate) => MutationReplies.CharacterCreate,
            nameof(MutationReplies.Logout) => MutationReplies.Logout,
            nameof(MutationReplies.Respawn) => MutationReplies.Respawn,
            _ => MutationReplies.RememberTokenRevoke,
        };
        using MutationResult own = new() { Status = OpStatus.Ok, RequestOpCode = requestOpCode };
        using MutationResult mapEdit = new() { Status = OpStatus.Ok, RequestOpCode = MapOpCode.PropHarvest };

        filter(own).Should().BeTrue();
        filter(mapEdit).Should().BeFalse("a harvest sent without waiting answers with a MutationResult too");
    }
}
