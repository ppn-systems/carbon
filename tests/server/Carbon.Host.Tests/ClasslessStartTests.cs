// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.Application.Identity;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;
using Carbon.Infrastructure.Persistence.Sqlite;
using Carbon.Infrastructure.Security;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>New characters start classless with nothing; characters created with a class keep it (ADR-030).</summary>
public sealed class ClasslessStartTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"carbon-classless-{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteCharacterStore _characters;
    private readonly AuthService _auth;

    public ClasslessStartTests()
    {
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        new SqliteSchemaMigrator(_factory).MigrateAsync().GetAwaiter().GetResult();
        _characters = new SqliteCharacterStore(_factory);
        _auth = new AuthService(new SqliteAccountStore(_factory), _characters, new Pbkdf2PasswordHasher());
    }

    [Fact]
    public async Task CreateCharacterAsync_NewCharacter_IsClasslessWithAnEmptyBagAndNothingEquipped()
    {
        Guid accountId = await this.RegisterAsync("fresh").ConfigureAwait(true);

        CharacterCreateResult created = await _auth.CreateCharacterAsync(accountId, "Fresh", TestLooks.Human, 0f, 0f).ConfigureAwait(true);

        created.Succeeded.Should().BeTrue();
        CharacterRecord? stored = await _characters.FindByIdAsync(created.Character!.Id).ConfigureAwait(true);
        stored.Should().NotBeNull();
        stored!.Class.Should().Be(HeroArchetypeClass.None, "no class is chosen at creation");
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        InventoryBinaryCodec.TryDeserializeInto(stored.InventoryBlob, bag).Should().BeTrue("an empty bag is still a readable inventory");
        bag.Slots.ToArray().Should().OnlyContain(s => s.IsEmpty, "bag, hotbar and every equipment slot start empty");
        stored.Gold.Should().Be(0);
    }

    [Fact]
    public async Task TryEnterWorld_NewCharacter_ArrivesEmptyHandedAndClassless()
    {
        Guid accountId = await this.RegisterAsync("arrival").ConfigureAwait(true);
        CharacterCreateResult created = await _auth.CreateCharacterAsync(accountId, "Arrival", TestLooks.Human, 0f, 0f).ConfigureAwait(true);
        AuthoritativeWorld world = await this.CreateWorldAsync().ConfigureAwait(true);

        EnterWorldResult entered = world.TryEnterWorld(901UL, (await _characters.FindByIdAsync(created.Character!.Id).ConfigureAwait(true))!);

        entered.Status.Should().Be(OpStatus.Ok);
        entered.ClassId.Should().Be((byte)HeroArchetypeClass.None);
        List<(ushort SlotIndex, ItemSlot Slot)> slots = [];
        world.GetInventorySnapshot(901UL, slots).Should().BeTrue();
        slots.Should().OnlyContain(s => s.Slot.IsEmpty, "the world must not hand a starter kit to a classless character");
    }

    [Fact]
    public async Task TryEnterWorld_CharacterCreatedWithAClass_KeepsItsClassAndItsKit()
    {
        Guid accountId = await this.RegisterAsync("veteran").ConfigureAwait(true);
        InventoryContainer kit = new(PlayerInventoryLayout.TotalCapacity);
        StarterGameCatalog.GrantStarterLoadout(kit, (byte)HeroArchetypeClass.Ranger);
        CharacterRecord veteran = new(
            Guid.NewGuid(), accountId, "Veteran", 7, default, 0f, 0f, 100f, 100f, 2, InventoryBinaryCodec.Serialize(kit), DateTimeOffset.UnixEpoch,
            Class: HeroArchetypeClass.Ranger);
        (await _characters.TryCreateAsync(veteran).ConfigureAwait(true)).Should().BeTrue();
        AuthoritativeWorld world = await this.CreateWorldAsync().ConfigureAwait(true);

        CharacterRecord? loaded = await _characters.FindByIdAsync(veteran.Id).ConfigureAwait(true);
        EnterWorldResult entered = world.TryEnterWorld(902UL, loaded!);

        loaded!.Class.Should().Be(HeroArchetypeClass.Ranger, "existing characters keep their class (no destructive migration)");
        entered.Status.Should().Be(OpStatus.Ok);
        entered.ClassId.Should().Be((byte)HeroArchetypeClass.Ranger);
        List<(ushort SlotIndex, ItemSlot Slot)> slots = [];
        world.GetInventorySnapshot(902UL, slots).Should().BeTrue();
        slots.Should().Contain(s => s.Slot.ItemId == StarterGameCatalog.HuntingBow, "the kit it was created with is untouched");
    }

    [Fact]
    public async Task LoginAsync_ProtocolOneClient_IsRefusedSoItCannotSendTheOldCreatePacket()
    {
        _ = await this.RegisterAsync("oldclient").ConfigureAwait(true);

        AuthOutcome outcome = await _auth.LoginAsync("oldclient", "secret-pass", protocolVersion: 1).ConfigureAwait(true);

        outcome.Status.Should().Be(AuthStatus.ProtocolMismatch, "protocol 1 clients still send a StartingClass byte");
    }

    public void Dispose()
    {
        _auth.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task<Guid> RegisterAsync(string username)
    {
        AuthOutcome registered = await _auth.RegisterAsync(username, "secret-pass", CarbonProtocol.Version).ConfigureAwait(true);
        registered.Status.Should().Be(AuthStatus.Ok);
        return registered.AccountId;
    }

    private async Task<AuthoritativeWorld> CreateWorldAsync()
    {
        AuthoritativeWorld world = new(
            1337UL, 32f, 0.0008f, 30, new SqliteChunkDeltaRepository(_factory), _characters, new AuthoritativeWorldMultiplayerTests.RecordingOutboundSink(),
            entropy: TestGameplayEntropy.Create(1337UL));
        await world.InitializeFromStoreAsync().ConfigureAwait(true);
        return world;
    }
}
