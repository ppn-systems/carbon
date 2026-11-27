// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Carbon.Contracts.Identity;
using Carbon.Items.Progression;
using Carbon.Items.Serialization;

namespace Carbon.Items.Races;

/// <summary>
/// Playable races, their body types and appearance options (ADR-042), loaded from <c>resources/config/races.json</c>.
/// The server validates every character look against it; clients get the same document through the config sync
/// (ADR-014) and build the creator and pick rigs from it. A new race is a new entry here plus its rig art: no code,
/// wire or schema change.
/// </summary>
public sealed class RaceCatalog
{
    /// <summary>Config document name (<c>races.json</c>).</summary>
    public const string DocumentName = "races";

    /// <summary>The only schema version this build reads.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Most body types one race may allow.</summary>
    public const int MaxBodies = 8;

    /// <summary>Most choices one option may offer (a choice index is a byte).</summary>
    public const int MaxChoices = 256;

    /// <summary>Most palette colours one choice may carry.</summary>
    public const int MaxColors = 4;

    private readonly Dictionary<ushort, RaceDefinition> _byId = [];

    /// <summary>Gets the schema version (must be <see cref="CurrentSchemaVersion"/>).</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Gets the races in creator order; the first is the creator's default.</summary>
    public List<RaceDefinition> Races { get; set; } = [];

    /// <summary>Parses and validates <c>races.json</c>.</summary>
    /// <param name="json">JSON object text.</param>
    /// <returns>The validated catalog.</returns>
    /// <exception cref="InvalidDataException">Thrown when the payload is empty, malformed or a value is invalid.</exception>
    public static RaceCatalog LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        RaceCatalog catalog;
        try
        {
            catalog = JsonSerializer.Deserialize(json, ItemsJsonContext.Default.RaceCatalog)
                ?? throw new InvalidDataException("races.json is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"races.json is malformed: {ex.Message}", ex);
        }

        catalog.Validate();
        return catalog;
    }

    /// <summary>Loads the build-time copy of <c>resources/config/races.json</c>.</summary>
    /// <returns>The validated shipped catalog.</returns>
    public static RaceCatalog LoadShippedDefaults()
    {
        using Stream stream = typeof(RaceCatalog).Assembly.GetManifestResourceStream("Carbon.Items.Config.races.json")
            ?? throw new InvalidOperationException("Embedded default config 'races.json' is missing.");
        using StreamReader reader = new(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    /// <summary>Finds a race by id.</summary>
    /// <param name="raceId">Race id.</param>
    /// <returns>The race, or <see langword="null"/> when the catalog has none with that id.</returns>
    public RaceDefinition? FindRace(ushort raceId) => _byId.GetValueOrDefault(raceId);

    /// <summary>Checks that every value of <paramref name="look"/> names something this catalog defines.</summary>
    /// <param name="look">Look to check.</param>
    /// <returns><see cref="CharacterLookStatus.Valid"/>, or the first problem found.</returns>
    public CharacterLookStatus Check(in CharacterLook look)
    {
        RaceDefinition? race = this.FindRace(look.RaceId);
        if (race is null)
        {
            return CharacterLookStatus.UnknownRace;
        }

        if (race.FindBody(look.BodyType) is null)
        {
            return CharacterLookStatus.UnknownBodyType;
        }

        if (look.OptionCount != race.Options.Count)
        {
            return CharacterLookStatus.OptionCountMismatch;
        }

        for (int i = 0; i < race.Options.Count; i++)
        {
            if (look.GetOption(i) >= race.Options[i].Choices.Count)
            {
                return CharacterLookStatus.OptionOutOfRange;
            }
        }

        return CharacterLookStatus.Valid;
    }

    /// <summary>Gets the stat changes of the look's race, or <see langword="null"/> for an unknown race.</summary>
    /// <param name="look">Character look.</param>
    /// <returns>The race's stat modifiers.</returns>
    public StatBlock? GetStatModifiers(in CharacterLook look) => this.FindRace(look.RaceId)?.StatModifiers;

    /// <summary>Gets the creator's default look: first race, its first body type, first choice of every option.</summary>
    /// <returns>The default look.</returns>
    public CharacterLook DefaultLook()
    {
        RaceDefinition race = this.Races[0];
        Span<byte> options = stackalloc byte[race.Options.Count];
        options.Clear();
        return new CharacterLook(race.Id, race.Bodies[0].Id, options);
    }

    /// <summary>
    /// Rejects a hot reload that would orphan stored looks: races are append-only, like wire fields. Every race and body
    /// type of <paramref name="previous"/> must still exist, every option must keep its code and position, and no option
    /// may lose choices. New races, bodies, trailing options and trailing choices are fine.
    /// </summary>
    /// <param name="previous">The catalog the server runs now.</param>
    /// <exception cref="InvalidDataException">Thrown when something stored characters may use was removed or moved.</exception>
    public void RequireCompatibleWith(RaceCatalog previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        foreach (RaceDefinition old in previous.Races)
        {
            RaceDefinition race = this.FindRace(old.Id)
                ?? throw new InvalidDataException($"races.json removes race {old.Id} ('{old.Code}'), which characters may use.");
            foreach (BodyTypeDefinition body in old.Bodies)
            {
                if (race.FindBody(body.Id) is null)
                {
                    throw new InvalidDataException($"races.json removes body type {body.Id} ('{body.Code}') of race '{old.Code}'.");
                }
            }

            if (race.Options.Count < old.Options.Count)
            {
                throw new InvalidDataException($"races.json removes appearance options from race '{old.Code}'.");
            }

            for (int i = 0; i < old.Options.Count; i++)
            {
                if (!string.Equals(race.Options[i].Code, old.Options[i].Code, StringComparison.Ordinal) ||
                    race.Options[i].Choices.Count < old.Options[i].Choices.Count)
                {
                    throw new InvalidDataException($"races.json moves or shrinks option '{old.Options[i].Code}' of race '{old.Code}'.");
                }
            }
        }
    }

    /// <summary>Validates the catalog and builds its lookups.</summary>
    /// <exception cref="InvalidDataException">Thrown when a value is invalid.</exception>
    public void Validate()
    {
        if (this.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"races.json schemaVersion must be {CurrentSchemaVersion} (was {this.SchemaVersion}).");
        }

        if (this.Races.Count == 0)
        {
            throw new InvalidDataException("races.json needs at least one race.");
        }

        _byId.Clear();
        HashSet<string> codes = new(StringComparer.Ordinal);
        for (int r = 0; r < this.Races.Count; r++)
        {
            RaceDefinition race = this.Races[r] ?? throw new InvalidDataException($"races.json races[{r}] is null.");
            string at = $"races[{r}]";
            if (race.Id == 0)
            {
                throw new InvalidDataException($"races.json {at} id must be 1..65535 (0 means no race).");
            }

            RequireCode(race.Code, $"{at}.code");
            RequireKey(race.NameKey, $"{at}.nameKey");
            if (!_byId.TryAdd(race.Id, race))
            {
                throw new InvalidDataException($"races.json {at} repeats race id {race.Id}.");
            }

            if (!codes.Add(race.Code))
            {
                throw new InvalidDataException($"races.json {at} repeats race code '{race.Code}'.");
            }

            ValidateBodies(race, at);
            ValidateOptions(race, at);
            ValidateModifiers(race.StatModifiers ?? throw new InvalidDataException($"races.json {at}.statModifiers is null."), at);
        }
    }

    private static void ValidateBodies(RaceDefinition race, string at)
    {
        if (race.Bodies.Count is 0 or > MaxBodies)
        {
            throw new InvalidDataException($"races.json {at}.bodies needs 1..{MaxBodies} body types.");
        }

        HashSet<byte> ids = [];
        HashSet<string> codes = new(StringComparer.Ordinal);
        for (int b = 0; b < race.Bodies.Count; b++)
        {
            BodyTypeDefinition body = race.Bodies[b] ?? throw new InvalidDataException($"races.json {at}.bodies[{b}] is null.");
            string bat = $"{at}.bodies[{b}]";
            RequireCode(body.Code, $"{bat}.code");
            RequireKey(body.NameKey, $"{bat}.nameKey");
            RequireRigPath(body.Rig, $"{bat}.rig");
            if (!ids.Add(body.Id))
            {
                throw new InvalidDataException($"races.json {bat} repeats body id {body.Id}.");
            }

            if (!codes.Add(body.Code))
            {
                throw new InvalidDataException($"races.json {bat} repeats body code '{body.Code}'.");
            }
        }
    }

    private static void ValidateOptions(RaceDefinition race, string at)
    {
        if (race.Options.Count > CharacterLook.MaxOptions)
        {
            throw new InvalidDataException($"races.json {at}.options allows at most {CharacterLook.MaxOptions} options.");
        }

        HashSet<string> codes = new(StringComparer.Ordinal);
        for (int o = 0; o < race.Options.Count; o++)
        {
            AppearanceOptionDefinition option = race.Options[o] ?? throw new InvalidDataException($"races.json {at}.options[{o}] is null.");
            string oat = $"{at}.options[{o}]";
            RequireCode(option.Code, $"{oat}.code");
            RequireKey(option.NameKey, $"{oat}.nameKey");
            RequireCode(option.Role, $"{oat}.role");
            if (!codes.Add(option.Code))
            {
                throw new InvalidDataException($"races.json {oat} repeats option code '{option.Code}'.");
            }

            if (option.Choices.Count is 0 or > MaxChoices)
            {
                throw new InvalidDataException($"races.json {oat}.choices needs 1..{MaxChoices} choices.");
            }

            bool isPart = option.Choices[0]?.Part is not null;
            for (int c = 0; c < option.Choices.Count; c++)
            {
                AppearanceChoiceDefinition choice = option.Choices[c] ?? throw new InvalidDataException($"races.json {oat}.choices[{c}] is null.");
                ValidateChoice(choice, isPart, $"{oat}.choices[{c}]");
            }
        }
    }

    private static void ValidateChoice(AppearanceChoiceDefinition choice, bool isPart, string at)
    {
        RequireKey(choice.NameKey, $"{at}.nameKey");
        if (isPart)
        {
            if (choice.Part is null || choice.Colors.Count != 0)
            {
                throw new InvalidDataException($"races.json {at} must name a 'part' and no 'colors', like the option's first choice.");
            }

            RequireCode(choice.Part, $"{at}.part");
            return;
        }

        if (choice.Part is not null || choice.Colors.Count is 0 or > MaxColors)
        {
            throw new InvalidDataException($"races.json {at} must carry 1..{MaxColors} 'colors' and no 'part', like the option's first choice.");
        }

        uint[] rgb = new uint[choice.Colors.Count];
        for (int i = 0; i < rgb.Length; i++)
        {
            string hex = choice.Colors[i];
            if (hex is null || hex.Length != 7 || hex[0] != '#' ||
                !uint.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb[i]))
            {
                throw new InvalidDataException($"races.json {at}.colors[{i}] must be '#RRGGBB'.");
            }
        }

        choice.Rgb = rgb;
    }

    private static void ValidateModifiers(StatBlock m, string at)
    {
        ReadOnlySpan<float> values =
        [
            m.MaxHealth, m.MaxMana, m.MaxStamina, m.Defense, m.MagicResist, m.Attack, m.SpellPower, m.CritChance,
            m.CritDamage, m.AttackSpeed, m.MoveSpeed, m.Evasion, m.HealthRegen, m.ManaRegen,
        ];
        for (int i = 0; i < values.Length; i++)
        {
            if (!float.IsFinite(values[i]) || MathF.Abs(values[i]) > 10_000f)
            {
                throw new InvalidDataException($"races.json {at}.statModifiers must be finite and within ±10000.");
            }
        }
    }

    private static void RequireCode(string? value, string at)
    {
        if (!IsCode(value))
        {
            throw new InvalidDataException($"races.json {at} must match [a-z][a-z0-9_]{{0,31}} (was '{value}').");
        }
    }

    private static void RequireKey(string? value, string at)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 96)
        {
            throw new InvalidDataException($"races.json {at} must be a localization key (dotted codes, at most 96 characters).");
        }

        foreach (string part in value.Split('.'))
        {
            if (!IsCode(part))
            {
                throw new InvalidDataException($"races.json {at} must be a localization key of dotted codes (was '{value}').");
            }
        }
    }

    private static void RequireRigPath(string? value, string at)
    {
        if (string.IsNullOrEmpty(value) || !value.EndsWith(".cvg.json", StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) || value.StartsWith('/') || value.Contains("..", StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"races.json {at} must be a relative '/'-separated .cvg.json path under the art root (was '{value}').");
        }
    }

    private static bool IsCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 32 || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
