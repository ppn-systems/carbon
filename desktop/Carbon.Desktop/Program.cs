// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Networking;
using Carbon.Desktop.Scenes;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Configuration;
using Carbon.Graphics.Windowing.Window;

namespace Carbon.Desktop;

/// <summary>
/// Composition root for the Carbon Desktop client supporting both offline deterministic worlds
/// and live <c>Nalix.SDK</c> multiplayer sessions.
/// </summary>
public static class Program
{
    /// <summary>Entry point initializing the client world session and Silk.NET window loop.</summary>
    public static async Task<int> Main()
    {
        DesktopClientOptions options = DesktopClientOptions.FromEnvironment();

        IClientWorldSession session = options.IsMultiplayer
            ? new NalixMultiplayerSession(options)
            : new OfflineWorldSession(options);

        try
        {
            await session.InitializeAsync().ConfigureAwait(false);
            RunWindowLoop(session, options);
            return 0;
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void RunWindowLoop(IClientWorldSession session, DesktopClientOptions options)
    {
        WindowSettings settings = new()
        {
            Title = options.IsMultiplayer ? "Carbon MMORPG — Connecting..." : "Carbon MMORPG — Offline Sandbox",
            Width = 1280,
            Height = 720,
            VSync = true,
            IsResizable = true,
        };

        using GameWindow window = new(settings);
        SpriteBatch? spriteBatch = null;
        SceneManager? sceneManager = null;

        try
        {
            window.Load += w =>
            {
                Camera2D camera = new(w.Width, w.Height);
                spriteBatch = new SpriteBatch(w.Device);
                sceneManager = new SceneManager(w, camera);
                sceneManager.ChangeScene(new WorldGameplayScene(session, options, camera));

                Console.WriteLine("=== CARBON DESKTOP CLIENT ===");
                Console.WriteLine($"Mode: {(session.IsOnlineMultiplayer ? "Multiplayer (Nalix WS)" : "Offline Sandbox")} | Seed: {session.WorldSeed}");
                Console.WriteLine("Controls: [WASD] Move | [Shift] Sprint | [Q/E] Zoom | [F] Harvest | [G] Door | [H] House | [B] Colliders | [R] Reseed | [F12] Capture");
            };

            window.Update += (_, dt) => sceneManager?.Update(dt);
            window.Render += (_, dt) =>
            {
                if (sceneManager is not null && spriteBatch is not null)
                {
                    sceneManager.Render(spriteBatch, dt);
                }
            };
            window.Resize += (_, width, height) => sceneManager?.Resize(width, height);
            window.Closing += _ =>
            {
                sceneManager?.Dispose();
                sceneManager = null;
                spriteBatch?.Dispose();
                spriteBatch = null;
            };

            window.Run();
        }
        finally
        {
            sceneManager?.Dispose();
            spriteBatch?.Dispose();
        }
    }
}
