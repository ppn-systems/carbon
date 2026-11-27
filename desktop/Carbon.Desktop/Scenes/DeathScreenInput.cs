// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Rendering.Hud;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Input while the death screen is open (ADR-031): Enter or a click on Respawn asks the server to respawn, and every other
/// gameplay input is swallowed. <see cref="Abstractions.World.IGameplayController.TryRequestRespawn"/> sends at most one request until the server answers.
/// </summary>
internal static class DeathScreenInput
{
    /// <summary>Handles one frame of input; returns <see langword="true"/> while the player is dead (the scene skips the rest).</summary>
    public static bool Handle(IWorldSession session, bool confirmPressed, bool leftClickPressed, Vector2 mouseScreen, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.LocalDeath is null)
        {
            return false;
        }

        bool clickedRespawn = leftClickPressed && HudOverlayRenderer.GetRespawnButtonRect(viewportWidth, viewportHeight).Contains(in mouseScreen);
        if (confirmPressed || clickedRespawn)
        {
            _ = session.TryRequestRespawn();
        }

        return true;
    }
}
