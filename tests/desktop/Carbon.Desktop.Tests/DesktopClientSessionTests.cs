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
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
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
            _ = session.TriggerPrimaryAction(session.LocalPlayerInterpolatedPosition + new Vector2(32f, 0f), out _);
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

    [Fact]
    public async Task OfflineWorldSession_ShouldPlaceStructureAndHarvestPropViaHotbarPrimaryAction()
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

            Vector2 playerPos = session.LocalPlayerInterpolatedPosition;
            int ptx = (int)MathF.Floor(playerPos.X / options.TileSize);
            int pty = (int)MathF.Floor(playerPos.Y / options.TileSize);

            int placeX = ptx + 1;
            int placeY = pty;
            bool foundPlace = false;
            for (int dy = -2; dy <= 2 && !foundPlace; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int tx = ptx + dx;
                    int ty = pty + dy;
                    MapTile tile = session.ChunkWorld.GetTileAtWorld(tx, ty);
                    if (tile.Biome is not BiomeType.DeepOcean and not BiomeType.ShallowWater &&
                        !session.ChunkWorld.HasSolidPropAt(tx, ty) &&
                        !session.ChunkWorld.Buildings.TryGetStructure(tx, ty, out _))
                    {
                        placeX = tx;
                        placeY = ty;
                        foundPlace = true;
                        break;
                    }
                }
            }

            foundPlace.Should().BeTrue();

            session.SelectHotbarSlot(4);
            int initialWallKits = session.Inventory.GetSlot(4).Quantity;
            initialWallKits.Should().BeGreaterThan(0);

            Vector2 placeAim = new((placeX + 0.5f) * options.TileSize, (placeY + 0.5f) * options.TileSize);
            bool placedModified = session.TriggerPrimaryAction(placeAim, out _);
            placedModified.Should().BeTrue();
            session.ChunkWorld.Buildings.TryGetStructure(placeX, placeY, out StructureTile placedTile).Should().BeTrue();
            placedTile.Type.Should().Be(StructureType.WoodWall);
            session.Inventory.GetSlot(4).Quantity.Should().Be(initialWallKits - 1);

            for (int i = 0; i < 20; i++)
            {
                session.Update(1.0 / 30.0, Vector2.Zero, sprint: false);
            }

            // Locate nearest natural solid prop within loaded region and walk into interact range
            int propX = 0;
            int propY = 0;
            int bestDistSq = int.MaxValue;
            bool foundProp = false;
            for (int dy = -24; dy <= 24; dy++)
            {
                for (int dx = -24; dx <= 24; dx++)
                {
                    int tx = ptx + dx;
                    int ty = pty + dy;
                    if (session.ChunkWorld.HasSolidPropAt(tx, ty))
                    {
                        int dSq = (dx * dx) + (dy * dy);
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            propX = tx;
                            propY = ty;
                            foundProp = true;
                        }
                    }
                }
            }

            foundProp.Should().BeTrue();
            Vector2 propAim = new((propX + 0.5f) * options.TileSize, (propY + 0.5f) * options.TileSize);
            float maxHarvestDist = 4.5f * options.TileSize;
            float maxHarvestDistSq = maxHarvestDist * maxHarvestDist;
            for (int step = 0; step < 120; step++)
            {
                Vector2 curPos = session.LocalPlayerInterpolatedPosition;
                Vector2 toProp = propAim - curPos;
                if (step >= 20 && toProp.LengthSquared() <= maxHarvestDistSq)
                {
                    break;
                }

                Vector2 walkDir = toProp.LengthSquared() > maxHarvestDistSq ? Vector2.Normalize(toProp) : Vector2.Zero;
                session.Update(1.0 / 30.0, walkDir, sprint: true);
            }

            session.SelectHotbarSlot(1);
            bool harvestModified = session.TriggerPrimaryAction(propAim, out _);
            harvestModified.Should().BeTrue();
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(true);
        }
    }
}
