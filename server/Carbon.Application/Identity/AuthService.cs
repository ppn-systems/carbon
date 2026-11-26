// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.Security;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;

namespace Carbon.Application.Identity;

/// <summary>
/// Application identity service implementing account registration, password verification,
/// and character creation without persistence-driver or transport coupling.
/// </summary>
public sealed class AuthService : IAuthService
{
    /// <summary>Maximum number of characters permitted per account.</summary>
    public const int MaxCharactersPerAccount = 4;

    private readonly IAccountStore _accounts;
    private readonly ICharacterStore _characters;
    private readonly IPasswordHasher _hasher;
    private readonly TimeProvider _time;
    private readonly Func<ItemRules> _itemRules;

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
    public AuthService(
        IAccountStore accounts,
        ICharacterStore characters,
        IPasswordHasher hasher,
        TimeProvider? time = null,
        Func<ItemRules>? itemRules = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _time = time ?? TimeProvider.System;
        if (itemRules is null)
        {
            ItemRules defaults = ItemRules.LoadShippedDefaults();
            itemRules = () => defaults;
        }

        _itemRules = itemRules;
    }

    /// <inheritdoc/>
    public async Task<AuthOutcome> RegisterAsync(string username, string password, ushort protocolVersion, CancellationToken ct = default)
    {
        if (protocolVersion != Carbon.Contracts.Common.CarbonProtocol.Version)
        {
            return new AuthOutcome(AuthStatus.ProtocolMismatch, Guid.Empty);
        }

        if (!IsValidIdentifier(username, 3, 24) || string.IsNullOrEmpty(password) || password.Length < 6 || password.Length > 128)
        {
            return new AuthOutcome(AuthStatus.InvalidInput, Guid.Empty);
        }

        string trimmed = username.Trim();
        string normalized = trimmed.ToUpperInvariant();
        _hasher.HashPassword(password, out byte[] hash, out byte[] salt);

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
    public async Task<AuthOutcome> LoginAsync(string username, string password, ushort protocolVersion, CancellationToken ct = default)
    {
        if (protocolVersion != Carbon.Contracts.Common.CarbonProtocol.Version)
        {
            return new AuthOutcome(AuthStatus.ProtocolMismatch, Guid.Empty);
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new AuthOutcome(AuthStatus.InvalidInput, Guid.Empty);
        }

        string normalized = username.Trim().ToUpperInvariant();
        AccountRecord? existing = await _accounts.FindByUsernameAsync(normalized, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return new AuthOutcome(AuthStatus.InvalidCredentials, Guid.Empty);
        }

        if (existing.IsBanned)
        {
            return new AuthOutcome(AuthStatus.Locked, Guid.Empty);
        }

        return _hasher.Verify(password, existing.PasswordHash, existing.PasswordSalt)
            ? new AuthOutcome(AuthStatus.Ok, existing.Id)
            : new AuthOutcome(AuthStatus.InvalidCredentials, Guid.Empty);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CharacterRecord>> ListCharactersAsync(Guid accountId, CancellationToken ct = default) =>
        _characters.ListByAccountAsync(accountId, ct);

    /// <inheritdoc/>
    public async Task<CharacterCreateResult> CreateCharacterAsync(
        Guid accountId,
        string characterName,
        byte appearancePreset,
        float spawnX,
        float spawnY,
        byte startingClass = 0,
        CancellationToken ct = default)
    {
        if (!IsValidIdentifier(characterName, 2, 20))
        {
            return new CharacterCreateResult(false, "invalid_name", null);
        }

        if (!Enum.IsDefined((HeroArchetypeClass)startingClass))
        {
            return new CharacterCreateResult(false, "invalid_class", null);
        }

        IReadOnlyList<CharacterRecord> existing = await _characters.ListByAccountAsync(accountId, ct).ConfigureAwait(false);
        if (existing.Count >= MaxCharactersPerAccount)
        {
            return new CharacterCreateResult(false, "character_limit_reached", null);
        }

        InventoryContainer inv = new(PlayerInventoryLayout.TotalCapacity);
        StarterGameCatalog.GrantStarterLoadout(inv, startingClass);
        const byte selectedSlot = 2;
        DerivedCharacterStats stats = CharacterProgressionPolicy.ComputeStats(_itemRules(), 1, inv, selectedSlot);
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
            Class: (HeroArchetypeClass)startingClass,
            Flags: CharacterTraits.StarterLoadoutGranted,
            UnlockedSkills: CharacterSkillMask.None,
            ProgressionBlob: []);

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
}
