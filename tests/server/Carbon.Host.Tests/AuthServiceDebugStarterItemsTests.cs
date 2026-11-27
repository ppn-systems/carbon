// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.Security;
using Carbon.Application.Identity;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>The Debug-only starter items used to screenshot every enhancement tier (ADR-020).</summary>
public sealed class AuthServiceDebugStarterItemsTests
{
    [Fact]
    public async Task CreateCharacterAsync_WithDebugStarterItems_AddsThemToTheBagEnhancedAndFullyRepaired()
    {
        ICharacterStore characters = Substitute.For<ICharacterStore>();
        characters.ListByAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<CharacterRecord>>([]));
        characters.TryCreateAsync(Arg.Any<CharacterRecord>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        using AuthService auth = new(Substitute.For<IAccountStore>(), characters, Substitute.For<IPasswordHasher>())
        {
            DebugStarterItems = [new DebugStarterItem(StarterGameCatalog.IronSword, 1, 9), new DebugStarterItem(StarterGameCatalog.WoodLog, 40, 0)],
        };

        CharacterCreateResult result = await auth.CreateCharacterAsync(Guid.NewGuid(), "Tester", TestLooks.Human, 0f, 0f).ConfigureAwait(true);

        result.Succeeded.Should().BeTrue();
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(result.Character!.InventoryBlob, inv).Should().BeTrue();
        ItemRules rules = ItemRules.LoadShippedDefaults();
        rules.Items.TryGet(StarterGameCatalog.IronSword, out Carbon.Items.Definitions.ItemArchetype? sword).Should().BeTrue();
        bool found = false;
        foreach (ItemSlot slot in inv.Slots)
        {
            if (slot.ItemId == StarterGameCatalog.IronSword && slot.UpgradeLevel == 9)
            {
                found = true;
                slot.Durability.Should().Be(ItemEnhancementPolicy.GetEffectiveMaxDurability(sword!, 9));
            }
        }

        found.Should().BeTrue("the +9 sword is granted on top of the starter loadout");
        inv.CountItem(StarterGameCatalog.WoodLog).Should().BeGreaterThanOrEqualTo(40);
    }
}
