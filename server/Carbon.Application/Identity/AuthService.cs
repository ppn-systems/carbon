// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Configuration;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.Security;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Progression;
using Carbon.Items.Serialization;

namespace Carbon.Application.Identity;

/// <summary>
/// Application identity service implementing account registration, password verification,
/// and character creation without persistence-driver or transport coupling.
/// </summary>
public sealed class AuthService : IAuthService, IDisposable
{
    /// <summary>Maximum number of characters permitted per account.</summary>
    public const int MaxCharactersPerAccount = 4;

    /// <summary>Maximum accepted password length, for both registration and login.</summary>
    public const int MaxPasswordLength = 128;

    /// <summary>Failed logins allowed per account within <see cref="LoginFailureWindow"/> before it is locked.</summary>
    public const int MaxFailedLoginsPerWindow = 5;

    /// <summary>Window over which failed logins are counted; a locked account unlocks when it expires.</summary>
    public static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Failed logins allowed per client address within <see cref="LoginFailureWindow"/>, across all usernames, before
    /// that address is refused. Higher than the per-account limit so players behind one NAT are not locked out together.
    /// </summary>
    public const int MaxFailedLoginsPerAddressWindow = 20;

    /// <summary>Upper bound on tracked client addresses; expired entries are pruned when it is exceeded.</summary>
    public const int MaxTrackedAddresses = 50_000;

    private readonly IAccountStore _accounts;
    private readonly ICharacterStore _characters;
    private readonly IPasswordHasher _hasher;
    private readonly TimeProvider _time;
    private readonly Func<ItemRules> _itemRules;
    private readonly Func<ProgressionConfig> _progression;
    private readonly ConcurrentDictionary<Guid, LoginFailures> _loginFailures = new();
    private readonly ConcurrentDictionary<string, LoginFailures> _addressFailures = new(StringComparer.Ordinal);
    private readonly Lazy<(byte[] Hash, byte[] Salt)> _dummyCredentials;

    /// <summary>
    /// Bounds concurrent password hashing so a flood of logins cannot occupy every core; the hashing itself runs on
    /// the thread pool instead of the network I/O thread that called in.
    /// </summary>
    private readonly SemaphoreSlim _hashGate = new(Math.Max(1, Environment.ProcessorCount / 2));

    /// <summary>
    /// Gets extra items granted to new characters. Set only by Debug hosts from <c>CARBON_DEBUG_STARTER_ITEMS</c>
    /// (screenshot review); empty otherwise.
    /// </summary>
    public IReadOnlyList<DebugStarterItem> DebugStarterItems { get; init; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthService"/> class.
    /// </summary>
    /// <param name="accounts">Account repository.</param>
    /// <param name="characters">Character repository.</param>
    /// <param name="hasher">Password hashing service.</param>
    /// <param name="time">Time provider (defaults to <see cref="TimeProvider.System"/>).</param>
    /// <param name="itemRules">
    /// Returns the item rules new characters' starting stats are computed with (the world's current rules, so a hot reload
    /// applies); defaults to the shipped item tables.
    /// </param>
    /// <param name="progression">Returns the world's current progression tuning; defaults to the shipped <c>progression.json</c>.</param>
    public AuthService(
        IAccountStore accounts,
        ICharacterStore characters,
        IPasswordHasher hasher,
        TimeProvider? time = null,
        Func<ItemRules>? itemRules = null,
        Func<ProgressionConfig>? progression = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _time = time ?? TimeProvider.System;
        _dummyCredentials = new Lazy<(byte[], byte[])>(this.CreateDummyCredentials);
        if (itemRules is null)
        {
            ItemRules defaults = ItemRules.LoadShippedDefaults();
            itemRules = () => defaults;
        }

        _itemRules = itemRules;
        if (progression is null)
        {
            ProgressionConfig defaults = ProgressionConfig.LoadShippedDefaults();
            progression = () => defaults;
        }

        _progression = progression;
    }

    /// <inheritdoc/>
    public async Task<AuthOutcome> RegisterAsync(string username, string password, ushort protocolVersion, CancellationToken ct = default)
    {
        if (protocolVersion != Carbon.Contracts.Common.CarbonProtocol.Version)
        {
            return new AuthOutcome(AuthStatus.ProtocolMismatch, Guid.Empty);
        }

        if (!IsValidIdentifier(username, 3, 24) || string.IsNullOrEmpty(password) || password.Length < 6 || password.Length > MaxPasswordLength)
        {
            return new AuthOutcome(AuthStatus.InvalidInput, Guid.Empty);
        }

        string trimmed = username.Trim();
        string normalized = trimmed.ToUpperInvariant();
        (byte[] hash, byte[] salt) = await this.RunHashAsync(
            () =>
            {
                _hasher.HashPassword(password, out byte[] h, out byte[] s);
                return (h, s);
            },
            ct).ConfigureAwait(false);

        AccountRecord account = new(
            Guid.NewGuid(),
            trimmed,
            normalized,
            hash,
            salt,
            _time.GetUtcNow(),
            IsBanned: false);

        bool created = await _accounts.TryCreateAsync(account, ct).ConfigureAwait(false);
        return created
            ? new AuthOutcome(AuthStatus.Ok, account.Id)
            : new AuthOutcome(AuthStatus.AccountExists, Guid.Empty);
    }

    /// <inheritdoc/>
    public Task<AuthOutcome> LoginAsync(string username, string password, ushort protocolVersion, CancellationToken ct = default) =>
        this.LoginAsync(username, password, protocolVersion, clientAddress: null, ct);

    /// <inheritdoc/>
    public async Task<AuthOutcome> LoginAsync(string username, string password, ushort protocolVersion, string? clientAddress, CancellationToken ct = default)
    {
        if (protocolVersion != Carbon.Contracts.Common.CarbonProtocol.Version)
        {
            return new AuthOutcome(AuthStatus.ProtocolMismatch, Guid.Empty);
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength)
        {
            return new AuthOutcome(AuthStatus.InvalidInput, Guid.Empty);
        }

        DateTimeOffset now = _time.GetUtcNow();
        string? address = string.IsNullOrWhiteSpace(clientAddress) ? null : clientAddress;
        if (address is not null && IsLockedOut(_addressFailures, address, MaxFailedLoginsPerAddressWindow, now))
        {
            // Rejected before the lookup and the hash: an address spraying usernames costs the server nothing.
            return new AuthOutcome(AuthStatus.Locked, Guid.Empty);
        }

        string normalized = username.Trim().ToUpperInvariant();
        AccountRecord? existing = await _accounts.FindByUsernameAsync(normalized, ct).ConfigureAwait(false);
        if (existing is null)
        {
            // Verify against a dummy hash so an unknown username takes as long as a wrong password (audit SEC-2).
            _ = await this.RunHashAsync(
                () =>
                {
                    (byte[] dummyHash, byte[] dummySalt) = _dummyCredentials.Value;
                    return _hasher.Verify(password, dummyHash, dummySalt);
                },
                ct).ConfigureAwait(false);
            this.RecordAddressFailure(address, now);
            return new AuthOutcome(AuthStatus.InvalidCredentials, Guid.Empty);
        }

        if (existing.IsBanned)
        {
            return new AuthOutcome(AuthStatus.Locked, Guid.Empty);
        }

        if (IsLockedOut(_loginFailures, existing.Id, MaxFailedLoginsPerWindow, now))
        {
            // Rejected before hashing, so brute force against a locked account costs the server nothing.
            return new AuthOutcome(AuthStatus.Locked, Guid.Empty);
        }

        byte[] expectedHash = existing.PasswordHash;
        byte[] salt = existing.PasswordSalt;
        bool verified = await this.RunHashAsync(() => _hasher.Verify(password, expectedHash, salt), ct).ConfigureAwait(false);
        if (!verified)
        {
            RecordFailure(_loginFailures, existing.Id, now);
            this.RecordAddressFailure(address, now);
            return new AuthOutcome(AuthStatus.InvalidCredentials, Guid.Empty);
        }

        _ = _loginFailures.TryRemove(existing.Id, out _);
        return new AuthOutcome(AuthStatus.Ok, existing.Id);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CharacterRecord>> ListCharactersAsync(Guid accountId, CancellationToken ct = default) =>
        _characters.ListByAccountAsync(accountId, ct);

    private const long DebugForgeKitGold = 50_000L;

    /// <summary>
    /// Gets or sets whether new characters receive the forge review kit (Debug host only, <c>CARBON_DEBUG_FORGE_KIT</c>):
    /// a +8 iron sword in the selected hotbar slot, enhancement materials, protection scrolls and gold.
    /// </summary>
    public bool DebugForgeKit { get; init; }

    /// <summary>
    /// Gets or sets whether the gear the Debug kits grant new characters starts worn to 40 % durability (Debug host only,
    /// <c>CARBON_DEBUG_WORN_GEAR</c>), for review of the blacksmith's repair mode.
    /// </summary>
    public bool DebugWornGear { get; init; }

    /// <summary>
    /// Gets or sets the durability shares (percent of max) the Debug kits' durable items start at, in slot order with the
    /// last repeating (Debug host only, <c>CARBON_DEBUG_WORN_GEAR=p1,p2,...</c>), for review of the ADR-035 warning colours.
    /// </summary>
    public IReadOnlyList<int> DebugWornGearPercents { get; init; } = [];

    /// <inheritdoc/>
    public async Task<CharacterCreateResult> CreateCharacterAsync(
        Guid accountId,
        string characterName,
        byte appearancePreset,
        float spawnX,
        float spawnY,
        CancellationToken ct = default)
    {
        if (!IsValidIdentifier(characterName, 2, 20))
        {
            return new CharacterCreateResult(false, "invalid_name", null);
        }

        IReadOnlyList<CharacterRecord> existing = await _characters.ListByAccountAsync(accountId, ct).ConfigureAwait(false);
        if (existing.Count >= MaxCharactersPerAccount)
        {
            return new CharacterCreateResult(false, "character_limit_reached", null);
        }

        // Every character starts with nothing (ADR-030); only Debug hosts add review items.
        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        this.GrantDebugStarterItems(inv);
        const byte selectedSlot = 2;
        if (this.DebugForgeKit)
        {
            GrantForgeKit(_itemRules(), inv, selectedSlot);
        }

        if (this.DebugWornGear)
        {
            WearGear(inv);
        }
        else if (this.DebugWornGearPercents.Count > 0)
        {
            WearGearTo(inv, this.DebugWornGearPercents);
        }

        DerivedCharacterStats stats = CharacterStatCalculator.Compute(_itemRules(), _progression(), 1, inv, selectedSlot, progression: null);
        byte[] invBlob = InventoryBinaryCodec.Serialize(inv);

        CharacterRecord created = new(
            Guid.NewGuid(),
            accountId,
            characterName.Trim(),
            Level: 1,
            appearancePreset,
            spawnX,
            spawnY,
            CurrentHealth: stats.MaxHealth,
            MaxHealth: stats.MaxHealth,
            SelectedHotbarSlot: selectedSlot,
            InventoryBlob: invBlob,
            UpdatedAtUtc: _time.GetUtcNow(),
            Experience: 0U,
            CurrentMana: stats.MaxMana,
            MaxMana: stats.MaxMana,
            CurrentStamina: stats.MaxStamina,
            MaxStamina: stats.MaxStamina,
            Class: HeroArchetypeClass.None,
            Flags: CharacterTraits.StarterLoadoutGranted,
            UnlockedSkills: CharacterSkillMask.None,
            ProgressionBlob: [],
            Gold: this.DebugForgeKit ? DebugForgeKitGold : 0L);

        bool inserted = await _characters.TryCreateAsync(created, ct).ConfigureAwait(false);
        return inserted
            ? new CharacterCreateResult(true, null, created)
            : new CharacterCreateResult(false, "name_taken", null);
    }

    /// <inheritdoc/>
    public async Task<CharacterRecord?> FindOwnedCharacterAsync(Guid accountId, Guid characterId, CancellationToken ct = default)
    {
        CharacterRecord? character = await _characters.FindByIdAsync(characterId, ct).ConfigureAwait(false);
        return character is not null && character.AccountId == accountId ? character : null;
    }

    /// <inheritdoc/>
    public void Dispose() => _hashGate.Dispose();

    private static bool IsLockedOut<TKey>(ConcurrentDictionary<TKey, LoginFailures> failures, TKey key, int limit, DateTimeOffset now)
        where TKey : notnull =>
        failures.TryGetValue(key, out LoginFailures entry) &&
        entry.Count >= limit &&
        now - entry.WindowStart < LoginFailureWindow;

    private static void RecordFailure<TKey>(ConcurrentDictionary<TKey, LoginFailures> failures, TKey key, DateTimeOffset now)
        where TKey : notnull =>
        failures.AddOrUpdate(
            key,
            static (_, at) => new LoginFailures(1, at),
            static (_, current, at) => at - current.WindowStart >= LoginFailureWindow
                ? new LoginFailures(1, at)
                : current with { Count = current.Count + 1 },
            now);

    private void RecordAddressFailure(string? address, DateTimeOffset now)
    {
        if (address is null)
        {
            return;
        }

        if (_addressFailures.Count >= MaxTrackedAddresses)
        {
            foreach (KeyValuePair<string, LoginFailures> entry in _addressFailures)
            {
                if (now - entry.Value.WindowStart >= LoginFailureWindow)
                {
                    _ = _addressFailures.TryRemove(entry);
                }
            }
        }

        RecordFailure(_addressFailures, address, now);
    }

    private void GrantDebugStarterItems(InventoryContainer inventory)
    {
        if (this.DebugStarterItems.Count == 0)
        {
            return;
        }

        ItemRules rules = _itemRules();
        foreach (DebugStarterItem extra in this.DebugStarterItems)
        {
            if (!rules.Items.TryGet(extra.ItemId, out ItemArchetype? def))
            {
                continue;
            }

            ItemSlot slot = new(extra.ItemId, extra.Quantity, 0UL, def.MaxDurability);
            if (extra.UpgradeLevel > 0)
            {
                slot = ItemEnhancementPolicy.WithUpgradeLevel(in slot, extra.UpgradeLevel) with
                {
                    Durability = ItemEnhancementPolicy.GetEffectiveMaxDurability(def, extra.UpgradeLevel),
                };
            }

            _ = PlayerInventoryLayout.TryAddBagItem(inventory, in slot, rules.GetMaxStackSize(extra.ItemId));
        }
    }

    private (byte[] Hash, byte[] Salt) CreateDummyCredentials()
    {
        _hasher.HashPassword(Guid.NewGuid().ToString("N"), out byte[] hash, out byte[] salt);
        return (hash, salt);
    }

    private async Task<T> RunHashAsync<T>(Func<T> work, CancellationToken ct)
    {
        await _hashGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(work, ct).ConfigureAwait(false);
        }
        finally
        {
            _ = _hashGate.Release();
        }
    }

    private static bool IsValidIdentifier(string? value, int minLen, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.Length < minLen || trimmed.Length > maxLen)
        {
            return false;
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool ok = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Failed-login count for one account and when its counting window started.</summary>
    private readonly record struct LoginFailures(int Count, DateTimeOffset WindowStart);

    private static void WearGearTo(InventoryContainer inv, IReadOnlyList<int> percents)
    {
        int next = 0;
        for (int i = 0; i < inv.Capacity; i++)
        {
            ItemSlot slot = inv.GetSlot(i);
            if (!slot.IsEmpty && slot.Durability > 0f)
            {
                int percent = percents[Math.Min(next++, percents.Count - 1)];
                inv.SetSlot(i, slot with { Durability = slot.Durability * percent / 100f });
            }
        }
    }

    private static void WearGear(InventoryContainer inv)
    {
        for (int i = 0; i < inv.Capacity; i++)
        {
            ItemSlot slot = inv.GetSlot(i);
            if (!slot.IsEmpty && slot.Durability > 0f)
            {
                inv.SetSlot(i, slot with { Durability = slot.Durability * 0.4f });
            }
        }
    }

    private static void GrantForgeKit(ItemRules rules, InventoryContainer inv, byte selectedSlot)
    {
        ItemCatalog items = rules.ItemCatalog;
        uint sword = items.ResolveId("iron_sword");
        float durability = rules.Items.TryGet(sword, out ItemArchetype? def) ? ItemEnhancementPolicy.GetEffectiveMaxDurability(def, 8) : 0f;
        inv.SetSlot(selectedSlot, new ItemSlot(sword, 1, Durability: durability, UpgradeLevel: 8));
        foreach ((string code, int quantity) in (ReadOnlySpan<(string, int)>)[("iron_ingot", 40), ("mana_crystal", 60), ("rough_gem", 10), ("forge_ward_scroll", 5), ("blessed_forge_scroll", 3)])
        {
            _ = PlayerInventoryLayout.TryAddBagItem(inv, new ItemSlot(items.ResolveId(code), quantity), items.GetMaxStack(items.ResolveId(code)));
        }
    }
}
