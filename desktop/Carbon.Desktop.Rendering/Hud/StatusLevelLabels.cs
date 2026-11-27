// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Globalization;
using Carbon.Desktop.Abstractions.Localization;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Level labels of status effects (ADR-036 D6): Roman numerals I to X from the <c>[status_levels]</c> section, Arabic digits
/// from 11 up, and the localized "name + level" form. Level I has no numeral on the icon and keeps the bare name, as in
/// Minecraft. Every string is cached per level and rebuilt only when the language changes, so drawing allocates nothing.
/// </summary>
internal sealed class StatusLevelLabels
{
    /// <summary>The highest level written as a Roman numeral.</summary>
    public const int MaxRomanLevel = 10;

    private static readonly string[] s_numeralKeys =
    [
        "status_levels.numeral.1",
        "status_levels.numeral.2",
        "status_levels.numeral.3",
        "status_levels.numeral.4",
        "status_levels.numeral.5",
        "status_levels.numeral.6",
        "status_levels.numeral.7",
        "status_levels.numeral.8",
        "status_levels.numeral.9",
        "status_levels.numeral.10",
    ];

    private readonly ILocalizer? _localizer;
    private readonly string?[] _numerals = new string?[byte.MaxValue + 1];
    private readonly CachedText _named;
    private readonly CachedText _panelLine;
    private int _version = int.MinValue;

    /// <summary>Initializes a new instance of the <see cref="StatusLevelLabels"/> class.</summary>
    /// <param name="localizer">Localizer to read; <see langword="null"/> uses <see cref="GameText.Current"/>.</param>
    public StatusLevelLabels(ILocalizer? localizer = null)
    {
        _localizer = localizer;
        _named = new CachedText(localizer);
        _panelLine = new CachedText(localizer);
    }

    /// <summary>Gets the numeral of <paramref name="level"/>: I..X, then Arabic digits; level 0 reads as I.</summary>
    /// <param name="level">Effect level.</param>
    /// <returns>The numeral text.</returns>
    public string Numeral(byte level)
    {
        ILocalizer localizer = _localizer ?? GameText.Current;
        if (localizer.Version != _version)
        {
            System.Array.Clear(_numerals);
            _version = localizer.Version;
        }

        int index = level == 0 ? 1 : level;
        return _numerals[index] ??= index <= MaxRomanLevel
            ? localizer.Text(s_numeralKeys[index - 1])
            : index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the numeral drawn on the icon, or <see langword="null"/> for level I.</summary>
    /// <param name="level">Effect level.</param>
    /// <returns>The numeral, or <see langword="null"/>.</returns>
    public string? IconNumeral(byte level) => level > 1 ? this.Numeral(level) : null;

    /// <summary>Gets the effect name with its level ("Poisoned II"); level I returns <paramref name="name"/> unchanged.</summary>
    /// <param name="name">Localized effect name.</param>
    /// <param name="level">Effect level.</param>
    /// <returns>The display name.</returns>
    public string NameWithLevel(string name, byte level) =>
        level > 1
            ? _named.Format("status_levels.name", new TextArg("name", name), new TextArg("level", this.Numeral(level)))
            : name;

    /// <summary>Gets the details panel line ("Level: II").</summary>
    /// <param name="level">Effect level.</param>
    /// <returns>The line.</returns>
    public string PanelLine(byte level) =>
        _panelLine.Format("status_levels.level", new TextArg("level", this.Numeral(level)));
}
