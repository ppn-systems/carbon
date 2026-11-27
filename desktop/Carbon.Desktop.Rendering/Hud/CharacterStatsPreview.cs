// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Text;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Progression;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Character panel model (ADR-021): the derived stats of the local character and, while a <c>+</c> button is hovered,
/// the stats one more point in that attribute would give. Everything comes from the synced <c>progression.json</c>
/// and <c>items.json</c> through <see cref="CharacterStatCalculator"/>, the same code the server runs, so the preview
/// matches what the server will compute. The server still decides whether a point can be spent.
/// </summary>
internal sealed class CharacterStatsPreview
{
    /// <summary>Allocatable attributes in panel order.</summary>
    public static readonly PrimaryAttributeId[] Attributes =
    [
        PrimaryAttributeId.Vitality,
        PrimaryAttributeId.Power,
        PrimaryAttributeId.Dexterity,
        PrimaryAttributeId.Agility,
        PrimaryAttributeId.Wisdom,
    ];

    /// <summary>Number of derived stat lines shown.</summary>
    public const int StatLineCount = 14;

    private readonly CharacterProgressionState _current = new();
    private readonly CharacterProgressionState _preview = new();
    private ProgressionConfig? _describedConfig;
    private readonly string[] _perPoint = new string[Attributes.Length];
    private InventoryContainer? _inventory;

    /// <summary>Gets the config the last <c>Update</c> used (<see langword="null"/> before the first sync).</summary>
    public ProgressionConfig? Config { get; private set; }

    /// <summary>Gets the current derived stats.</summary>
    public DerivedCharacterStats Current { get; private set; }

    /// <summary>Gets the stats with one more point in <see cref="PreviewAttribute"/> (equal to <see cref="Current"/> without a preview).</summary>
    public DerivedCharacterStats Preview { get; private set; }

    /// <summary>Gets the attribute being previewed, if any.</summary>
    public PrimaryAttributeId? PreviewAttribute { get; private set; }

    /// <summary>Gets the character level used.</summary>
    public int Level { get; private set; } = 1;

    /// <summary>Gets the attribute names shown in the panel.</summary>
    /// <param name="attribute">Attribute.</param>
    /// <returns>Display name.</returns>
    public static string NameOf(PrimaryAttributeId attribute) => attribute switch
    {
        PrimaryAttributeId.Vitality => "Vitality",
        PrimaryAttributeId.Wisdom => "Wisdom",
        PrimaryAttributeId.Power => "Power",
        PrimaryAttributeId.Agility => "Agility",
        PrimaryAttributeId.Dexterity => "Dexterity",
        _ => attribute.ToString(),
    };

    /// <summary>
    /// Recomputes from the read model. Returns <see langword="false"/> (and keeps no stats) until <c>progression.json</c>
    /// has been synced.
    /// </summary>
    /// <param name="world">Local read model.</param>
    /// <param name="config">Synced progression config.</param>
    /// <param name="hovered">Attribute whose <c>+</c> button is hovered, if any.</param>
    /// <returns>Whether stats are available.</returns>
    public bool Update(IWorldReadModel world, ProgressionConfig? config, PrimaryAttributeId? hovered)
    {
        ArgumentNullException.ThrowIfNull(world);
        Span<ushort> points = stackalloc ushort[Attributes.Length];
        for (int i = 0; i < Attributes.Length; i++)
        {
            points[i] = world.GetAttributePoints(Attributes[i]);
        }

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        return this.Update(config, world.Items.Rules, world.LocalPlayerLevel, world.LocalUnspentStatPoints, points, slots, world.SelectedHotbarSlot, hovered);
    }

    /// <summary>Recomputes from explicit inputs (see the read-model overload).</summary>
    /// <param name="config">Synced progression config.</param>
    /// <param name="rules">Synced item rules.</param>
    /// <param name="level">Character level.</param>
    /// <param name="unspent">Unspent points reported by the server.</param>
    /// <param name="attributePoints">Allocated points, in <see cref="Attributes"/> order.</param>
    /// <param name="slots">Inventory slots (equipment included).</param>
    /// <param name="selectedHotbarSlot">Held hotbar slot.</param>
    /// <param name="hovered">Attribute whose <c>+</c> button is hovered, if any.</param>
    /// <returns>Whether stats are available.</returns>
    public bool Update(
        ProgressionConfig? config,
        ItemRules rules,
        int level,
        ushort unspent,
        ReadOnlySpan<ushort> attributePoints,
        ReadOnlySpan<ItemSlot> slots,
        byte selectedHotbarSlot,
        PrimaryAttributeId? hovered)
    {
        ArgumentNullException.ThrowIfNull(rules);
        this.Config = config;
        if (config is null)
        {
            this.PreviewAttribute = null;
            return false;
        }

        this.Level = Math.Clamp(level, 1, config.MaxLevel);
        for (int i = 0; i < Attributes.Length; i++)
        {
            _current.SetAttribute(Attributes[i], i < attributePoints.Length ? attributePoints[i] : (ushort)0);
        }

        InventoryContainer inventory = this.FillInventory(slots);
        this.Current = CharacterStatCalculator.Compute(rules, config, (ushort)this.Level, inventory, selectedHotbarSlot, _current);

        if (hovered is PrimaryAttributeId attr && unspent > 0 && config.Attributes.Get(attr) is not null)
        {
            _current.CopyTo(_preview);
            _preview.AddAttribute(attr, 1);
            this.Preview = CharacterStatCalculator.Compute(rules, config, (ushort)this.Level, inventory, selectedHotbarSlot, _preview);
            this.PreviewAttribute = attr;
        }
        else
        {
            this.Preview = this.Current;
            this.PreviewAttribute = null;
        }

        return true;
    }

    /// <summary>Gets the per-point effect line of <paramref name="attribute"/>, e.g. <c>"+14 HP  +0.7 DEF"</c>.</summary>
    /// <param name="attribute">Attribute.</param>
    /// <returns>The line (empty before the first sync).</returns>
    public string DescribePerPoint(PrimaryAttributeId attribute)
    {
        ProgressionConfig? config = this.Config;
        if (config is null)
        {
            return string.Empty;
        }

        if (!ReferenceEquals(config, _describedConfig))
        {
            for (int i = 0; i < Attributes.Length; i++)
            {
                StatBlock? block = config.Attributes.Get(Attributes[i]);
                _perPoint[i] = block is null ? string.Empty : Describe(block);
            }

            _describedConfig = config;
        }

        int index = Array.IndexOf(Attributes, attribute);
        return index < 0 ? string.Empty : _perPoint[index];
    }

    /// <summary>Gets the label of stat line <paramref name="index"/>.</summary>
    /// <param name="index">Line index (<c>0..StatLineCount-1</c>).</param>
    /// <returns>Label.</returns>
    public static string StatLabel(int index) => index switch
    {
        0 => "Max HP",
        1 => "Max MP",
        2 => "Stamina",
        3 => "ATK",
        4 => "MATK",
        5 => "DEF",
        6 => "MRES",
        7 => "Crit",
        8 => "Crit Dmg",
        9 => "Atk Spd",
        10 => "Move Spd",
        11 => "Evasion",
        12 => "HP Regen",
        13 => "MP Regen",
        _ => string.Empty,
    };

    /// <summary>
    /// Formats stat line <paramref name="index"/> of <paramref name="stats"/>. Defense, magic resist and evasion show the
    /// reduction or dodge chance against a monster of the character's own level.
    /// </summary>
    /// <param name="index">Line index.</param>
    /// <param name="stats">Stats to format.</param>
    /// <returns>The value text.</returns>
    public string FormatStat(int index, in DerivedCharacterStats stats)
    {
        ProgressionConfig? c = this.Config;
        int lv = this.Level;
        return index switch
        {
            0 => Whole(stats.MaxHealth),
            1 => Whole(stats.MaxMana),
            2 => Whole(stats.MaxStamina),
            3 => Percent(stats.AttackMultiplier),
            4 => Percent(stats.SpellPowerMultiplier),
            5 => c is null ? Whole(stats.Defense) : $"{Whole(stats.Defense)} ({WholePct(c.Mitigation.Physical.Evaluate(stats.Defense, lv))})",
            6 => c is null ? Whole(stats.MagicResist) : $"{Whole(stats.MagicResist)} ({WholePct(c.Mitigation.Magical.Evaluate(stats.MagicResist, lv))})",
            7 => Pct(stats.CritChance),
            8 => Pct(stats.CritDamage),
            9 => Percent(stats.AttackSpeedMultiplier),
            10 => Percent(stats.MoveSpeedMultiplier),
            11 => c is null ? Whole(stats.Evasion) : Pct(c.GetEvasionChance(in stats, lv)),
            12 => string.Create(CultureInfo.InvariantCulture, $"{stats.HealthRegenPerSecond:0.#}/s"),
            13 => string.Create(CultureInfo.InvariantCulture, $"{stats.ManaRegenPerSecond:0.#}/s"),
            _ => string.Empty,
        };
    }

    /// <summary>Gets whether stat line <paramref name="index"/> would change with the previewed point.</summary>
    /// <param name="index">Line index.</param>
    /// <returns><see langword="true"/> when the preview differs.</returns>
    public bool PreviewChanges(int index) =>
        this.PreviewAttribute is not null &&
        !string.Equals(this.FormatStat(index, this.Current), this.FormatStat(index, this.Preview), StringComparison.Ordinal);

    internal static string Describe(StatBlock b)
    {
        StringBuilder sb = new(48);
        Append(sb, b.MaxHealth, "HP", 1f);
        Append(sb, b.MaxMana, "MP", 1f);
        Append(sb, b.Attack, "% ATK", 100f);
        Append(sb, b.SpellPower, "% MATK", 100f);
        Append(sb, b.Defense, "DEF", 1f);
        Append(sb, b.MagicResist, "MRES", 1f);
        Append(sb, b.CritChance, "% Crit", 100f);
        Append(sb, b.CritDamage, "% CritDmg", 100f);
        Append(sb, b.AttackSpeed, "% ASPD", 100f);
        Append(sb, b.MoveSpeed, "% MSPD", 100f);
        Append(sb, b.Evasion, "EVA", 1f);
        Append(sb, b.MaxStamina, "STA", 1f);
        Append(sb, b.HealthRegen, "HP/s", 1f);
        Append(sb, b.ManaRegen, "MP/s", 1f);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, float value, string unit, float scale)
    {
        if (value <= 0f)
        {
            return;
        }

        if (sb.Length > 0)
        {
            _ = sb.Append(' ');
        }

        bool percent = unit.StartsWith('%');
        _ = sb.Append(CultureInfo.InvariantCulture, $"+{value * scale:0.##}").Append(percent ? string.Empty : " ").Append(unit);
    }

    private static string Whole(float v) => ((int)MathF.Round(v)).ToString(CultureInfo.InvariantCulture);

    private static string WholePct(float fraction) => string.Create(CultureInfo.InvariantCulture, $"{fraction * 100f:0}%");

    private static string Pct(float fraction) => string.Create(CultureInfo.InvariantCulture, $"{fraction * 100f:0.#}%");

    private static string Percent(float multiplier) => string.Create(CultureInfo.InvariantCulture, $"{multiplier * 100f:0}%");

    private InventoryContainer FillInventory(ReadOnlySpan<ItemSlot> slots)
    {
        _inventory ??= new InventoryContainer(PlayerInventoryLayout.TotalCapacity);
        for (int i = 0; i < slots.Length && i < _inventory.Capacity; i++)
        {
            _inventory.SetSlot(i, in slots[i]);
        }

        return _inventory;
    }
}
