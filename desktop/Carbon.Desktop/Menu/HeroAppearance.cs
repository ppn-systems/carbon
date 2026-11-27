// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Identity;
using Carbon.Items.Races;

namespace Carbon.Desktop.Menu;

/// <summary>
/// The creator panel's look (ADR-042): a race, one of its body types and one choice per option, all read from the
/// server's synced <c>races.json</c>. Nothing here names a race or an option, so a race added to the config shows up in
/// the creator without a client change.
/// </summary>
internal sealed class HeroAppearance
{
    private readonly byte[] _choices = new byte[CharacterLook.MaxOptions];
    private RaceCatalog? _catalog;
    private int _raceIndex;
    private int _bodyIndex;

    /// <summary>Gets the catalog the editor is bound to, or <see langword="null"/> before the first sync.</summary>
    public RaceCatalog? Catalog => _catalog;

    /// <summary>Gets the selected race, or <see langword="null"/> before the first sync.</summary>
    public RaceDefinition? Race => _catalog?.Races[_raceIndex];

    /// <summary>Gets the selected body type, or <see langword="null"/> before the first sync.</summary>
    public BodyTypeDefinition? Body => this.Race?.Bodies[_bodyIndex];

    /// <summary>Gets the number of options the selected race offers.</summary>
    public int OptionCount => this.Race?.Options.Count ?? 0;

    /// <summary>Gets the look the creator would send (<see langword="default"/> before the first sync).</summary>
    public CharacterLook Look
    {
        get
        {
            RaceDefinition? race = this.Race;
            return race is null
                ? default
                : new CharacterLook(race.Id, race.Bodies[_bodyIndex].Id, _choices.AsSpan(0, race.Options.Count));
        }
    }

    /// <summary>Gets the choice index of option <paramref name="option"/>.</summary>
    /// <param name="option">Option position in the race's order.</param>
    /// <returns>The choice index (0 past the race's options).</returns>
    public byte GetChoice(int option) => (uint)option < (uint)this.OptionCount ? _choices[option] : (byte)0;

    /// <summary>
    /// Binds the editor to a (new) catalog. The selected race and body stay when they still exist, and every choice is
    /// clamped to what its option offers; otherwise the editor starts on the catalog's first race.
    /// </summary>
    /// <param name="catalog">Synced catalog, or <see langword="null"/> while none has arrived.</param>
    public void Bind(RaceCatalog? catalog)
    {
        if (ReferenceEquals(catalog, _catalog))
        {
            return;
        }

        ushort raceId = this.Race?.Id ?? 0;
        byte bodyId = this.Body?.Id ?? 0;
        _catalog = catalog;
        _raceIndex = 0;
        _bodyIndex = 0;
        if (catalog is null)
        {
            Array.Clear(_choices);
            return;
        }

        int raceIndex = catalog.Races.FindIndex(r => r.Id == raceId);
        if (raceIndex >= 0)
        {
            _raceIndex = raceIndex;
            _bodyIndex = Math.Max(0, catalog.Races[raceIndex].Bodies.FindIndex(b => b.Id == bodyId));
        }

        this.ClampChoices();
    }

    /// <summary>Selects the body type of the current race with code <paramref name="bodyCode"/>, when it has one.</summary>
    /// <param name="bodyCode">Body type code from <c>races.json</c> (e.g. <c>female</c>).</param>
    /// <returns><see langword="true"/> when the race has that body.</returns>
    public bool SelectBody(string bodyCode)
    {
        int index = this.Race?.Bodies.FindIndex(b => string.Equals(b.Code, bodyCode, StringComparison.OrdinalIgnoreCase)) ?? -1;
        if (index < 0)
        {
            return false;
        }

        _bodyIndex = index;
        return true;
    }

    /// <summary>Moves to the next or previous race (<paramref name="step"/> ±1), wrapping; body and choices reset.</summary>
    /// <param name="step">Direction.</param>
    public void CycleRace(int step)
    {
        if (_catalog is null)
        {
            return;
        }

        _raceIndex = Wrap(_raceIndex + step, _catalog.Races.Count);
        _bodyIndex = 0;
        Array.Clear(_choices);
    }

    /// <summary>Moves to the next or previous body type of the race (<paramref name="step"/> ±1), wrapping.</summary>
    /// <param name="step">Direction.</param>
    public void CycleBody(int step)
    {
        if (this.Race is { } race)
        {
            _bodyIndex = Wrap(_bodyIndex + step, race.Bodies.Count);
        }
    }

    /// <summary>Moves option <paramref name="option"/> to its next or previous choice (<paramref name="step"/> ±1), wrapping.</summary>
    /// <param name="option">Option position in the race's order.</param>
    /// <param name="step">Direction.</param>
    public void CycleOption(int option, int step)
    {
        if (this.Race is { } race && (uint)option < (uint)race.Options.Count)
        {
            _choices[option] = (byte)Wrap(_choices[option] + step, race.Options[option].Choices.Count);
        }
    }

    /// <summary>Picks every option of the selected race and body at random; the race and body stay.</summary>
    /// <param name="random">Random source.</param>
    public void Randomize(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (this.Race is not { } race)
        {
            return;
        }

        for (int i = 0; i < race.Options.Count; i++)
        {
            _choices[i] = (byte)random.Next(race.Options[i].Choices.Count);
        }
    }

    private void ClampChoices()
    {
        RaceDefinition? race = this.Race;
        for (int i = 0; i < _choices.Length; i++)
        {
            int count = race is not null && i < race.Options.Count ? race.Options[i].Choices.Count : 0;
            if (_choices[i] >= count)
            {
                _choices[i] = 0;
            }
        }
    }

    private static int Wrap(int value, int count) => count <= 0 ? 0 : ((value % count) + count) % count;
}
