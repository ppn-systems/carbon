// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Capture;
using Carbon.Desktop.Diagnostics;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Scenes;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Configuration;
using Carbon.Graphics.Windowing.Window;
using Carbon.Desktop.Networking.Session;

namespace Carbon.Desktop;

/// <summary>
/// Composition root for the online Carbon Desktop client using live <c>Nalix.SDK</c>
/// dual-transport (<c>TcpSession</c> + Authenticated UDP <c>DatagramSession</c>).
/// </summary>
public static class Program
{
    /// <summary>Entry point initializing the client session and Silk.NET window loop.</summary>
    public static async Task<int> Main(string[] args)
    {
        _ = GameText.Initialize(UiSettings.LoadOrDefault().Language);
        if (args is { Length: >= 2 } && string.Equals(args[0], "--showcase", StringComparison.OrdinalIgnoreCase))
        {
            return ArtShowcaseRunner.Run(args[1]);
        }

        DesktopOptions options = DesktopOptions.FromEnvironment();
        MultiplayerSession session = new(options);

        try
        {
            RunWindowLoop(session, options);
            return 0;
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void RunWindowLoop(IWorldSession session, DesktopOptions options)
    {
        WindowSettings settings = new()
        {
            Title = "Carbon MMORPG — Online Client",
            Width = options.WindowSize.Width,
            Height = options.WindowSize.Height,
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
                sceneManager.ChangeScene(new StartScene(MenuFlow.Create(session, options, camera)));

                Console.WriteLine("=== CARBON ONLINE DESKTOP CLIENT ===");
                Console.WriteLine($"Target Server: {options.ServerHost}:{options.ServerTcpPort} (TCP + Authenticated UDP)");
            };

            FrameStatsSampler? frameStats = options.FrameStatsFrames > 0
                ? new FrameStatsSampler(() => (window.Device as GraphicsDevice)?.DrawCallCount ?? 0L)
                : null;

            window.Update += (_, dt) =>
            {
                frameStats?.BeginFrame();
                sceneManager?.Update(dt);
            };
            window.Render += (_, dt) =>
            {
                if (sceneManager is not null && spriteBatch is not null)
                {
                    sceneManager.Render(spriteBatch, dt);
                    // Render can close the window (auto-exit), and Closing nulls sceneManager: re-read it.
                    frameStats?.EndFrame(sceneManager?.CurrentScene?.GetType().Name ?? "none");
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
