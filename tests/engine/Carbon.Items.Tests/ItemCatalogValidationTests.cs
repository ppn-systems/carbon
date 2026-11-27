// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;
using FluentAssertions;
using Xunit;

namespace Carbon.Items.Tests;

/// <summary>
/// Pins the load-time rules of <see cref="ItemCatalog.Create"/>, including the wire-width rule added for audit DOM-7:
/// item ids travel as <see cref="ushort"/>, so the catalog rejects ids the wire would silently truncate.
/// </summary>
public sealed class ItemCatalogValidationTests
{
    [Fact]
    public void Create_ValidItems_IndexesThemByIdAndCode()
    {
        ItemCatalog catalog = ItemCatalog.Create([Item(1, "stick"), Item(2, "stone")]);

        catalog.ResolveId("stone").Should().Be(2u);
        catalog.TryGet(1u, out ItemCatalogEntry? stick).Should().BeTrue();
        stick!.Code.Should().Be("stick");
    }

    [Theory]
    [InlineData(ItemCatalog.MaxWireItemId)]
    [InlineData(1u)]
    public void Create_IdThatFitsTheWire_IsAccepted(uint id)
    {
        Action act = () => ItemCatalog.Create([Item(id, "edge")]);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(ItemCatalog.MaxWireItemId + 1)]
    [InlineData(uint.MaxValue)]
    public void Create_IdWiderThanTheWire_IsRejectedWithTheLimit(uint id)
    {
        Action act = () => ItemCatalog.Create([Item(1, "ok"), Item(id, "too_wide")]);

        _ = act.Should().Throw<InvalidDataException>().WithMessage($"*'too_wide'*{id}*65535*");
    }

    [Fact]
    public void MaxWireItemId_MatchesTheWireFieldWidth() =>
        ItemCatalog.MaxWireItemId.Should().Be(ushort.MaxValue);

    [Fact]
    public void LoadShippedDefaults_AllShippedItemIdsFitTheWire()
    {
        ItemRules rules = ItemRules.LoadShippedDefaults();

        foreach (ItemCatalogEntry entry in rules.ItemCatalog.All)
        {
            entry.Id.Should().BeInRange(1u, ItemCatalog.MaxWireItemId, $"'{entry.Code}' is sent to clients as a 16-bit id");
        }
    }

    [Theory]
    [InlineData(0u, "zero_id")]
    [InlineData(5u, "")]
    [InlineData(5u, "   ")]
    public void Create_MissingIdOrCode_IsRejected(uint id, string code)
    {
        Action act = () => ItemCatalog.Create([Item(id, code)]);

        _ = act.Should().Throw<InvalidDataException>().WithMessage("*non-zero id and a code*");
    }

    [Fact]
    public void Create_DuplicateId_IsRejected()
    {
        Action act = () => ItemCatalog.Create([Item(3, "a"), Item(3, "b")]);

        _ = act.Should().Throw<InvalidDataException>().WithMessage("Duplicate item id 3*");
    }

    [Fact]
    public void Create_DuplicateCode_IsRejected()
    {
        Action act = () => ItemCatalog.Create([Item(3, "same"), Item(4, "same")]);

        _ = act.Should().Throw<InvalidDataException>().WithMessage("Duplicate item code 'same'*");
    }

    [Theory]
    [InlineData(0, 0, 0f, 0)]
    [InlineData(1, -1, 0f, 0)]
    [InlineData(1, 0, -1f, 0)]
    [InlineData(1, 0, 0f, -1)]
    public void Create_OutOfRangeStackPriceDurabilityOrTier_IsRejected(int maxStack, int basePrice, float maxDurability, int tier)
    {
        ItemCatalogEntry item = Item(7, "bad") with { MaxStack = maxStack, BasePrice = basePrice, MaxDurability = maxDurability, Tier = tier };

        Action act = () => ItemCatalog.Create([item]);

        _ = act.Should().Throw<InvalidDataException>().WithMessage("*maxStack/basePrice/maxDurability/tier*");
    }

    [Theory]
    [InlineData(-0.1f, 0f, 0f, 0f)]
    [InlineData(1.1f, 0f, 0f, 0f)]
    [InlineData(0f, -1f, 0f, 0f)]
    [InlineData(0f, 0f, -1f, 0f)]
    [InlineData(0f, 0f, 0f, -1f)]
    public void Create_OutOfRangeCombatStats_AreRejected(float crit, float range, float projectileSpeed, float manaCost)
    {
        ItemCatalogEntry item = Item(8, "bad") with { CritChance = crit, AttackRange = range, ProjectileSpeed = projectileSpeed, AttackManaCost = manaCost };

        Action act = () => ItemCatalog.Create([item]);

        _ = act.Should().Throw<InvalidDataException>().WithMessage("*critChance/attackRange/projectileSpeed/attackManaCost*");
    }

    [Fact]
    public void Create_CritChanceOfExactlyOne_IsAccepted()
    {
        Action act = () => ItemCatalog.Create([Item(9, "sure_crit") with { CritChance = 1f }]);

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_StackableItemThatWearsOut_IsRejected()
    {
        Action act = () => ItemCatalog.Create([Item(10, "stacked_tool") with { MaxDurability = 50f, MaxStack = 5 }]);

        _ = act.Should().Throw<InvalidDataException>().WithMessage("*wears out*maxStack 1*");
    }

    [Fact]
    public void Create_UnstackableItemThatWearsOut_IsAccepted()
    {
        Action act = () => ItemCatalog.Create([Item(11, "tool") with { MaxDurability = 50f, MaxStack = 1 }]);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void Create_AttackSpeedBonusOutsideZeroToOne_IsRejected(float bonus)
    {
        Action act = () => ItemCatalog.Create([Item(12, "blur_blade") with { AttackSpeedBonus = bonus }]);

        _ = act.Should().Throw<InvalidDataException>().WithMessage("*blur_blade*attackSpeedBonus*");
    }

    [Fact]
    public void Create_UnknownMaterial_IsRejected()
    {
        Action act = () => ItemCatalog.Create([Item(13, "mystery_tool") with { Material = (ItemMaterial)77 }]);

        _ = act.Should().Throw<InvalidDataException>().WithMessage("*mystery_tool*material*");
    }

    private static ItemCatalogEntry Item(uint id, string code) => new() { Id = id, Code = code, MaxStack = 10 };
}
