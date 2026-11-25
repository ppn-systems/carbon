// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Networking;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Building;
using FluentAssertions;
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
            typeof(OfflineWorldSession).Assembly,
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
    public async Task OfflineWorldSession_ShouldSimulateMovementHousePlacementDoorToggleCraftingCombatAndReseed()
    {
        DesktopClientOptions options = new()
        {
            Seed = 1337UL,
            TileSize = 32f,
            MacroFrequency = 0.0008f,
        };

        OfflineWorldSession session = new(options);
        try
        {
            await session.InitializeAsync().ConfigureAwait(true);
            session.IsReady.Should().BeTrue();
            session.IsOnlineMultiplayer.Should().BeFalse();
            session.Inventory.GetSlot(0).ItemId.Should().Be(StarterGameCatalog.WoodAxe);
            session.Inventory.GetSlot(2).ItemId.Should().Be(StarterGameCatalog.IronSword);

            Vector2 startPos = session.LocalPlayerInterpolatedPosition;
            for (int i = 0; i < 15; i++)
            {
                session.Update(1.0 / 30.0, new Vector2(1f, 0f), sprint: true);
            }

            session.LocalPlayerInterpolatedPosition.Should().NotBe(startPos);

            session.SelectHotbarSlot(2);
            session.SelectedHotbarSlot.Should().Be(2);
            session.TriggerPrimaryAction(session.LocalPlayerInterpolatedPosition + new Vector2(32f, 0f));
            session.AttackSwingTimer.Should().BeGreaterThan(0f);

            _ = session.Inventory.TryAdd(new ItemSlot(StarterGameCatalog.WoodLog, 6), 99);
            session.TryCraftRecipe(1).Should().BeTrue();
            List<ClientCombatEvent> feedback = [];
            session.DrainCombatEvents(feedback);
            feedback.Should().NotBeEmpty();

            session.TryPlaceTestHouse(out _).Should().BeTrue();
            Vector2 afterHousePos = session.LocalPlayerInterpolatedPosition;
            int doorTileX = (int)MathF.Floor(afterHousePos.X / options.TileSize) + 1;
            int doorTileY = (int)MathF.Floor(afterHousePos.Y / options.TileSize);
            session.ChunkWorld.Buildings.ForcePlaceStructure(doorTileX, doorTileY, StructureType.WoodDoor);
            session.TryToggleNearbyDoor(out _).Should().BeTrue();

            session.ReseedOffline(9001UL);
            session.WorldSeed.Should().Be(9001UL);
            session.IsReady.Should().BeTrue();
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(true);
        }
    }
}
