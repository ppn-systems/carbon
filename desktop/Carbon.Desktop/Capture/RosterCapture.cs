// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Carbon.Contracts.Identity;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;

namespace Carbon.Desktop.Capture;

/// <summary>
/// <c>CARBON_CAPTURE_SCENE=roster</c>: registers the capture account, creates two sample heroes with different looks,
/// then stops at the hero roster, for reviewing roster cards, portraits and the dossier (docs/art/menu-ui.md rule 18).
/// Screenshot tooling only: it runs against the throwaway capture server.
/// </summary>
internal static class RosterCapture
{
    /// <summary>The <c>CARBON_CAPTURE_SCENE</c> value.</summary>
    public const string SceneName = "roster";

    /// <summary>Registers <paramref name="options"/>' account on <paramref name="server"/> and creates the sample heroes.</summary>
    /// <returns>The registration result; heroes are only created after a successful registration.</returns>
    public static async Task<AuthenticationResult> RegisterWithHeroesAsync(IWorldSession session, GameServer server, DesktopOptions options)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(options);
        AuthenticationResult result = await session.RegisterAsync(server.Host, server.Port, options.Username, options.Password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return result;
        }

        // Human (race 1): a female and a male body with different options, so the cards show two distinct portraits.
        _ = await session.CreateCharacterAsync("Ayla", new CharacterLook(1, 1, [2, 1, 2, 1])).ConfigureAwait(false);
        _ = await session.CreateCharacterAsync("Bram", new CharacterLook(1, 0, [1, 2, 1, 3])).ConfigureAwait(false);
        return result;
    }
}
