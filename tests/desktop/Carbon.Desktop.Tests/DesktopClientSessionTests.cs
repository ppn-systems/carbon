// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Application.Identity;
using Carbon.Application.World;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Networking;
using Carbon.Host;
using Carbon.Infrastructure.Configuration;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Nalix.Hosting;
using Xunit;

namespace Carbon.Desktop.Tests;

public sealed class DesktopClientSessionTests
{
    [Fact]
    public void DesktopAbstractionsAndNetworking_MustStayDecoupledFromOpenGlAndWindowing()
    {
        Assembly[] headlessAssemblies =
        [
            typeof(IClientWorldSession).Assembly,
            typeof(NalixMultiplayerSession).Assembly,
        ];

        foreach (Assembly asm in headlessAssemblies)
        {
            foreach (AssemblyName reference in asm.GetReferencedAssemblies())
            {
                string refName = reference.Name ?? string.Empty;
                refName.Should().NotContain("Silk.NET", $"{asm.GetName().Name} must remain window/OpenGL independent");
                refName.Should().NotContain("Carbon.Graphics.OpenGL", $"{asm.GetName().Name} must remain GPU independent");
            }
        }
    }

    [Fact]
    public async Task NalixMultiplayerSession_OverLiveTcpAndAuthenticatedUdp_ShouldAuthenticateManageCharactersAndSyncWorld()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"carbon-e2e-udp-{Guid.NewGuid():N}.db");
        ushort tcpPort = AllocateFreeTcpPort();
        ushort udpPort = AllocateFreeUdpPort();

        try
        {
            using SqliteConnectionFactory dbFactory = new($"Data Source={dbPath}");
            SqliteSchemaMigrator migrator = new(dbFactory);
            await migrator.MigrateAsync().ConfigureAwait(true);

            SqliteAccountStore accounts = new(dbFactory);
            SqliteCharacterStore characters = new(dbFactory);
            SqliteChunkDeltaRepository chunkDeltas = new(dbFactory);
            Pbkdf2PasswordHasher hasher = new();
            AuthService authService = new(accounts, characters, hasher);
            NalixSessionTransportRegistry transportRegistry = new();
            NalixWorldOutboundSink outboundSink = new(transportRegistry, NullLogger<NalixWorldOutboundSink>.Instance);

            HostRuntimeOptions hostOptions = new()
            {
                WorldSeed = 1337UL,
                TileSize = 32f,
                Frequency = 0.0008f,
                TickRate = 30,
                WebSocketPort = 0,
                TcpPort = tcpPort,
                UdpPort = udpPort,
            };

            AuthoritativeWorld world = new(
                hostOptions.WorldSeed,
                hostOptions.TileSize,
                hostOptions.Frequency,
                hostOptions.TickRate,
                chunkDeltas,
                characters,
                outboundSink);

            await world.InitializeFromStoreAsync().ConfigureAwait(true);

            NetworkApplication app = Startup.Build(
                hostOptions,
                authService,
                world,
                outboundSink,
                transportRegistry,
                NullLogger.Instance);

            await app.ActivateAsync(CancellationToken.None).ConfigureAwait(true);

            DesktopClientOptions clientOptions = new()
            {
                ServerHost = "127.0.0.1",
                ServerTcpPort = tcpPort,
                Username = "playerA",
                Password = "password123",
                CharacterName = "HeroA",
            };

            NalixMultiplayerSession clientA = new(clientOptions);
            NalixMultiplayerSession clientB = new(clientOptions);

            try
            {
                // 1. Unknown user login must fail cleanly over encrypted TCP.
                ClientAuthResult badLogin = await clientA.LoginAsync("127.0.0.1", tcpPort, "playerA", "password123").ConfigureAwait(true);
                badLogin.Succeeded.Should().BeFalse();

                // 2. Register both accounts + create hero rosters.
                ClientAuthResult regA = await clientA.RegisterAsync("127.0.0.1", tcpPort, "playerA", "password123").ConfigureAwait(true);
                regA.Succeeded.Should().BeTrue();
                clientA.IsAuthenticated.Should().BeTrue();

                (bool createdA, _) = await clientA.CreateCharacterAsync("HeroA", appearancePreset: 1).ConfigureAwait(true);
                createdA.Should().BeTrue();
                IReadOnlyList<ClientCharacterSummary> rosterA = await clientA.ListCharactersAsync().ConfigureAwait(true);
                rosterA.Should().ContainSingle(c => c.Name == "HeroA");

                ClientAuthResult regB = await clientB.RegisterAsync("127.0.0.1", tcpPort, "playerB", "password123").ConfigureAwait(true);
                regB.Succeeded.Should().BeTrue();
                (bool createdB, _) = await clientB.CreateCharacterAsync("HeroB", appearancePreset: 2).ConfigureAwait(true);
                createdB.Should().BeTrue();
                IReadOnlyList<ClientCharacterSummary> rosterB = await clientB.ListCharactersAsync().ConfigureAwait(true);
                rosterB.Should().ContainSingle(c => c.Name == "HeroB");

                float initialX = rosterA[0].PositionX;

                // 3. Enter world and verify Authenticated UDP (NDP) activates cleanly.
                (bool enteredA, _) = await clientA.EnterWorldAsync(rosterA[0].CharacterId).ConfigureAwait(true);
                enteredA.Should().BeTrue();
                clientA.IsReady.Should().BeTrue();
                clientA.IsUdpActive.Should().BeTrue("DatagramSession should establish Authenticated UDP over NDP");
                clientA.TransportLabel.Should().Be("TCP+UDP");
                clientA.ActiveCharacterName.Should().Be("HeroA");

                (bool enteredB, _) = await clientB.EnterWorldAsync(rosterB[0].CharacterId).ConfigureAwait(true);
                enteredB.Should().BeTrue();
                clientB.IsUdpActive.Should().BeTrue();

                // 4. Drive UDP movement + fixed server ticks until peer spawns and moves over AoI.
                for (int i = 0; i < 20; i++)
                {
                    clientA.Update(1.0 / 30.0, new Vector2(1f, 0f), sprint: true);
                    world.StepFixedTick();
                    await Task.Delay(15).ConfigureAwait(true);
                }

                clientB.RemoteEntities.Should().Contain(
                    e => e.Name == "HeroA",
                    "peer spawn over encrypted TCP and UDP delta updates should replicate HeroA to clientB");
                RemoteEntityView heroA = clientB.RemoteEntities.First(e => e.Name == "HeroA");
                heroA.Position.X.Should().BeGreaterThan(initialX + 32f);

                // 5. Place wood wall using hotbar slot 4 via Authenticated UDP primary action.
                Vector2 posA = clientA.LocalPlayerInterpolatedPosition;
                int ptx = (int)MathF.Floor(posA.X / 32f);
                int pty = (int)MathF.Floor(posA.Y / 32f);
                int placeX = ptx + 2;
                int placeY = pty;
                bool foundPlaceTile = false;
                for (int dy = -2; dy <= 2 && !foundPlaceTile; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        int tx = ptx + dx;
                        int ty = pty + dy;
                        MapTile tile = clientA.ChunkWorld.GetTileAtWorld(tx, ty);
                        if (tile.Biome is not BiomeType.DeepOcean and not BiomeType.ShallowWater &&
                            !clientA.ChunkWorld.HasSolidPropAt(tx, ty) &&
                            !clientA.ChunkWorld.Buildings.TryGetStructure(tx, ty, out _))
                        {
                            placeX = tx;
                            placeY = ty;
                            foundPlaceTile = true;
                            break;
                        }
                    }
                }

                clientA.SelectHotbarSlot(4);
                Vector2 placeAim = new((placeX + 0.5f) * 32f, (placeY + 0.5f) * 32f);
                clientA.TriggerPrimaryAction(placeAim, out _).Should().BeTrue();

                for (int i = 0; i < 12; i++)
                {
                    world.StepFixedTick();
                    await Task.Delay(20).ConfigureAwait(true);
                }

                clientB.ChunkWorld.Buildings.TryGetStructure(placeX, placeY, out StructureTile placed).Should().BeTrue();
                placed.Type.Should().Be(StructureType.WoodWall);

                // 6. Logout disconnects UDP cleanly and allows subsequent re-login.
                await clientA.LogoutAsync().ConfigureAwait(true);
                clientA.IsReady.Should().BeFalse();
                clientA.IsUdpActive.Should().BeFalse();
                clientA.IsAuthenticated.Should().BeFalse();

                ClientAuthResult relogin = await clientA.LoginAsync("127.0.0.1", tcpPort, "playerA", "password123").ConfigureAwait(true);
                relogin.Succeeded.Should().BeTrue();
            }
            finally
            {
                await clientA.DisposeAsync().ConfigureAwait(true);
                await clientB.DisposeAsync().ConfigureAwait(true);
                await app.DeactivateAsync(CancellationToken.None).ConfigureAwait(true);
                await app.DisposeAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static ushort AllocateFreeTcpPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static ushort AllocateFreeUdpPort()
    {
        using UdpClient udp = new(new IPEndPoint(IPAddress.Loopback, 0));
        return (ushort)((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
}
