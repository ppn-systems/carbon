// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Desktop.Abstractions.Identity;

namespace Carbon.Desktop.Menu;

/// <summary>
/// The character screen's hero list: the heroes the server last sent, the selected row and the hero to select after the
/// next load (a just-created one).
/// </summary>
internal sealed class CharacterRoster
{
    /// <summary>The most heroes an account holds (one roster slot each).</summary>
    public const int MaxHeroes = 4;

    private string? _selectAfterLoad;

    /// <summary>Gets the heroes last loaded from the server.</summary>
    public IReadOnlyList<CharacterRosterEntry> Heroes { get; private set; } = [];

    /// <summary>Gets the selected row.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>Gets the number of heroes.</summary>
    public int Count => this.Heroes.Count;

    /// <summary>Gets whether the account can create another hero.</summary>
    public bool HasFreeSlot => this.Count < MaxHeroes;

    /// <summary>Gets whether a loaded hero is selected.</summary>
    public bool HasSelection => this.SelectedIndex >= 0 && this.SelectedIndex < this.Count;

    /// <summary>Gets the selected hero; check <see cref="HasSelection"/> first.</summary>
    public CharacterRosterEntry Selected => this.Heroes[this.SelectedIndex];

    /// <summary>Selects the previous row, wrapping from the first to the last.</summary>
    public void SelectPrevious()
    {
        if (this.Count > 0)
        {
            this.SelectedIndex = (this.SelectedIndex + this.Count - 1) % this.Count;
        }
    }

    /// <summary>Selects the next row, wrapping from the last to the first.</summary>
    public void SelectNext()
    {
        if (this.Count > 0)
        {
            this.SelectedIndex = (this.SelectedIndex + 1) % this.Count;
        }
    }

    /// <summary>Selects row <paramref name="index"/> when it holds a hero.</summary>
    /// <returns>Whether the row holds a hero.</returns>
    public bool TrySelect(int index)
    {
        if (index < 0 || index >= this.Count)
        {
            return false;
        }

        this.SelectedIndex = index;
        return true;
    }

    /// <summary>Selects the hero named <paramref name="name"/> (ignoring case) when the next roster arrives.</summary>
    public void SelectAfterNextLoad(string name) => _selectAfterLoad = name;

    /// <summary>
    /// Replaces the heroes. A pending <see cref="SelectAfterNextLoad"/> name selects its hero; otherwise the selection
    /// is clamped to the new list.
    /// </summary>
    public void Load(IReadOnlyList<CharacterRosterEntry> heroes)
    {
        ArgumentNullException.ThrowIfNull(heroes);
        this.Heroes = heroes;
        if (!string.IsNullOrEmpty(_selectAfterLoad))
        {
            for (int i = 0; i < heroes.Count; i++)
            {
                if (string.Equals(heroes[i].Name, _selectAfterLoad, StringComparison.OrdinalIgnoreCase))
                {
                    this.SelectedIndex = i;
                    break;
                }
            }

            _selectAfterLoad = null;
        }
        else if (this.SelectedIndex >= heroes.Count)
        {
            this.SelectedIndex = Math.Max(0, heroes.Count - 1);
        }
    }
}
