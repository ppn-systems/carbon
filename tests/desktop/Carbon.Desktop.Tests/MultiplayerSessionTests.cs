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
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Host;
using Carbon.Infrastructure.Configuration;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Nalix.Hosting;
using Xunit;
using Carbon.Desktop.Networking.Session;

namespace Carbon.Desktop.Tests;

public sealed class MultiplayerSessionTests
{
    [Fact]
    public void DesktopAbstractionsAndNetworking_MustStayDecoupledFromOpenGlAndWindowing()
    {
        Assembly[] headlessAssemblies =
        [
            typeof(IWorldSession).Assembly,
            typeof(MultiplayerSession).Assembly,
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
    public async Task MultiplayerSession_OverLiveTcpAndAuthenticatedUdp_ShouldAuthenticateManageCharactersAndSyncWorld()
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
            SessionTransportRegistry transportRegistry = new();
            WorldOutboundSink outboundSink = new(transportRegistry, NullLogger<WorldOutboundSink>.Instance);

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

            DesktopOptions clientOptions = new()
            {
                ServerHost = "127.0.0.1",
                ServerTcpPort = tcpPort,
                Username = "playerA",
                Password = "password123",
                CharacterName = "HeroA",
            };

            MultiplayerSession clientA = new(clientOptions);
            MultiplayerSession clientB = new(clientOptions);

            try
            {
                // 1. Unknown user login must fail cleanly over encrypted TCP.
                AuthenticationResult badLogin = await clientA.LoginAsync("127.0.0.1", tcpPort, "playerA", "password123").ConfigureAwait(true);
                badLogin.Succeeded.Should().BeFalse();

                // 2. Register both accounts + create hero rosters.
                AuthenticationResult regA = await clientA.RegisterAsync("127.0.0.1", tcpPort, "playerA", "password123").ConfigureAwait(true);
                regA.Succeeded.Should().BeTrue();
                clientA.IsAuthenticated.Should().BeTrue();

                (bool createdA, _) = await clientA.CreateCharacterAsync("HeroA", appearancePreset: 1).ConfigureAwait(true);
                createdA.Should().BeTrue();
                IReadOnlyList<CharacterRosterEntry> rosterA = await clientA.ListCharactersAsync().ConfigureAwait(true);
                rosterA.Should().ContainSingle(c => c.Name == "HeroA" && c.AppearancePreset == 1);

                AuthenticationResult regB = await clientB.RegisterAsync("127.0.0.1", tcpPort, "playerB", "password123").ConfigureAwait(true);
                regB.Succeeded.Should().BeTrue();
                (bool createdB, _) = await clientB.CreateCharacterAsync("HeroB", appearancePreset: 2, startingClass: (byte)Contracts.Identity.HeroArchetypeClass.Arcanist).ConfigureAwait(true);
                createdB.Should().BeTrue();
                IReadOnlyList<CharacterRosterEntry> rosterB = await clientB.ListCharactersAsync().ConfigureAwait(true);
                rosterB.Should().ContainSingle(c =>
                    c.Name == "HeroB" &&
                    c.AppearancePreset == 2 &&
                    c.Class == Contracts.Identity.HeroArchetypeClass.Arcanist);

                float initialX = rosterA[0].PositionX;

                // 3. Enter world and verify Authenticated UDP (NDP) activates cleanly.
                (bool enteredA, _) = await clientA.EnterWorldAsync(rosterA[0].CharacterId).ConfigureAwait(true);
                enteredA.Should().BeTrue();
                clientA.IsReady.Should().BeTrue();
                clientA.IsUdpActive.Should().BeTrue("DatagramSession should establish Authenticated UDP over NDP");
                clientA.TransportLabel.Should().Be("TCP+UDP");
                clientA.ActiveCharacterName.Should().Be("HeroA");
                clientA.LocalPlayerAppearancePreset.Should().Be(1);
                clientA.LocalPlayerLevel.Should().BeGreaterThanOrEqualTo(1);
                clientA.LocalPlayerMaxMana.Should().BeGreaterThan(0f);
                clientA.LocalPlayerMaxStamina.Should().BeGreaterThan(0f);

                ItemSlot[] copiedSlots = new ItemSlot[PlayerInventoryLayout.TotalCapacity];
                clientA.CopyInventorySlots(copiedSlots);
                copiedSlots[0].ItemId.Should().Be(StarterGameCatalog.WoodAxe);

                for (int t = 0; t < 4; t++)
                {
                    clientA.Update(1.0 / 30.0, Vector2.Zero, sprint: false);
                    world.StepFixedTick();
                    await Task.Delay(15).ConfigureAwait(true);
                }

                clientA.CycleNearestHostileTarget(32f).Should().BeFalse();
                clientA.LockedTargetNetworkId.Should().Be(0UL);
                clientA.IsNearForgeStation().Should().BeTrue("sanctuary starter area hosts Master Blacksmith");

                (bool enteredB, _) = await clientB.EnterWorldAsync(rosterB[0].CharacterId).ConfigureAwait(true);
                enteredB.Should().BeTrue();
                clientB.LocalPlayerClass.Should().Be(Contracts.Identity.HeroArchetypeClass.Arcanist);
                clientB.LocalCharacterTraits.HasFlag(Contracts.Common.CharacterTraits.StarterLoadoutGranted).Should().BeTrue();
                clientB.IsUdpActive.Should().BeTrue();
                clientB.CopyInventorySlots(copiedSlots);
                copiedSlots[2].ItemId.Should().Be(StarterGameCatalog.ArcaneStaff);
                clientB.SelectHotbarSlot(2);
                _ = clientB.TriggerPrimaryAction(clientB.LocalPlayerInterpolatedPosition + new Vector2(64f, 0f), out _);
                clientB.CurrentAction.Kind.Should().Be(ActionAnimationKind.StaffCast);

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
                heroA.ArchetypeId.Should().Be(1);

                // 4b. Verify hotbar slots classify distinct client animations for Axe, Pickaxe, and Sword.
                Vector2 swingAim = clientA.LocalPlayerInterpolatedPosition + new Vector2(32f, 0f);
                clientA.SelectHotbarSlot(0);
                _ = clientA.TriggerPrimaryAction(swingAim, out _);
                clientA.CurrentAction.Kind.Should().Be(ActionAnimationKind.AxeChop);
                clientA.AttackSwingTimer.Should().BeGreaterThan(0f);

                clientA.SelectHotbarSlot(1);
                _ = clientA.TriggerPrimaryAction(swingAim, out _);
                clientA.CurrentAction.Kind.Should().Be(ActionAnimationKind.PickaxeMine);
                clientA.AttackSwingTimer.Should().BeGreaterThan(0f);

                clientA.SelectHotbarSlot(2);
                _ = clientA.TriggerPrimaryAction(swingAim, out _);
                clientA.CurrentAction.Kind.Should().Be(ActionAnimationKind.SwordSlash);
                clientA.AttackSwingTimer.Should().BeGreaterThan(0f);

                for (int i = 0; i < 18; i++)
                {
                    world.StepFixedTick();
                    await Task.Delay(10).ConfigureAwait(true);
                }

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
                clientA.PreviewPlacement(placeX, placeY, StructureType.WoodWall, StructureRotation.North, out _, out _)
                    .Should().Be(BuildResultCode.Ok, "the client preview runs the same rules as the server");
                Vector2 placeAim = new((placeX + 0.5f) * 32f, (placeY + 0.5f) * 32f);
                clientA.TriggerPrimaryAction(placeAim, out _).Should().BeTrue();

                // The kit places a blueprint; the 3 starter wood logs are delivered and 120 work takes 60 ticks.
                // Meanwhile both clients receive the site and its progress as MapConstructionChanged pushes.
                List<ConstructionMarker> markers = [];
                bool sawSite = false;
                float maxProgress = 0f;
                for (int i = 0; i < 120 && !clientB.ChunkWorld.Buildings.TryGetStructure(placeX, placeY, out _); i++)
                {
                    world.StepFixedTick();
                    await Task.Delay(20).ConfigureAwait(true);
                    clientB.CopyConstructionMarkers(markers);
                    foreach (ConstructionMarker marker in markers.Where(m => m.AnchorX == placeX && m.AnchorY == placeY && !m.IsDeconstruction))
                    {
                        sawSite = true;
                        maxProgress = MathF.Max(maxProgress, marker.Progress);
                    }
                }

                clientB.ChunkWorld.Buildings.TryGetStructure(placeX, placeY, out StructureTile placed).Should().BeTrue();
                placed.Type.Should().Be(StructureType.WoodWall);
                sawSite.Should().BeTrue("the blueprint/frame was pushed before the wall completed");
                maxProgress.Should().BeGreaterThan(0.3f, "construction progress is pushed while the builder works");

                for (int i = 0; i < 10; i++)
                {
                    world.StepFixedTick();
                    await Task.Delay(20).ConfigureAwait(true);
                }

                clientB.CopyConstructionMarkers(markers);
                markers.Should().NotContain(m => m.AnchorX == placeX && m.AnchorY == placeY && !m.IsDeconstruction, "the site marker is cleared on completion");
                clientB.PreviewPlacement(placeX, placeY, StructureType.WoodWall, StructureRotation.North, out _, out _)
                    .Should().Be(BuildResultCode.BlockedByExistingStructure);

                // 5.5 Drop UDP transport and verify Update() automatically reconnects UDP on the next heartbeat.
                await clientA.DropUdpTransportForTestingAsync().ConfigureAwait(true);
                clientA.IsUdpActive.Should().BeFalse();

                for (int i = 0; i < 60 && !clientA.IsUdpActive; i++)
                {
                    clientA.Update(0.25, Vector2.Zero, sprint: false);
                    world.StepFixedTick();
                    await Task.Delay(35).ConfigureAwait(true);
                }

                clientA.IsUdpActive.Should().BeTrue("UDP offline state must automatically reconnect without manual login");
                clientA.TransportLabel.Should().Be("TCP+UDP");

                // 5.6 Drop ALL transports (OFFLINE) and verify Update() automatically reconnects TCP, re-authenticates, re-enters world, and restores UDP.
                await clientA.DropAllTransportsForTestingAsync().ConfigureAwait(true);
                clientA.IsConnected.Should().BeFalse();
                clientA.IsUdpActive.Should().BeFalse();
                clientA.TransportLabel.Should().Be("OFFLINE");
                clientA.IsReady.Should().BeTrue("local world simulation stays alive while reconnecting");

                for (int i = 0; i < 80 && (!clientA.IsConnected || !clientA.IsAuthenticated || !clientA.IsUdpActive); i++)
                {
                    clientA.Update(0.25, Vector2.Zero, sprint: false);
                    world.StepFixedTick();
                    await Task.Delay(35).ConfigureAwait(true);
                }

                clientA.IsConnected.Should().BeTrue("OFFLINE state must automatically reconnect TCP");
                clientA.IsAuthenticated.Should().BeTrue("reconnect must re-authenticate session");
                clientA.IsUdpActive.Should().BeTrue("reconnect must restore authenticated UDP");
                clientA.TransportLabel.Should().Be("TCP+UDP");

                // 6. Logout disconnects UDP cleanly and allows subsequent re-login.
                await clientA.LogoutAsync().ConfigureAwait(true);
                clientA.IsReady.Should().BeFalse();
                clientA.IsUdpActive.Should().BeFalse();
                clientA.IsAuthenticated.Should().BeFalse();

                AuthenticationResult relogin = await clientA.LoginAsync("127.0.0.1", tcpPort, "playerA", "password123").ConfigureAwait(true);
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
