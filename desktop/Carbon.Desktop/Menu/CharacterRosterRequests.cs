// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.Localization;

namespace Carbon.Desktop.Menu;

/// <summary>What finishing the character screen's requests asks the scene to do.</summary>
internal enum RosterRequestOutcome
{
    /// <summary>Stay on the character screen.</summary>
    None,

    /// <summary>The sign-out finished: go back to the start screen.</summary>
    SignedOut,

    /// <summary>The server accepted the hero: enter the world.</summary>
    EnteredWorld,
}

/// <summary>The result of one <see cref="CharacterRosterRequests.Poll"/>.</summary>
/// <param name="Outcome">Whether the scene should leave.</param>
/// <param name="HeroCreated">A hero was created; the roster is being reloaded.</param>
/// <param name="RosterLoadedEmpty">A roster arrived with no hero.</param>
internal readonly record struct RosterPollResult(RosterRequestOutcome Outcome, bool HeroCreated, bool RosterLoadedEmpty);

/// <summary>
/// The character screen's server requests (list, create, enter world, sign out) and the status line they drive. Each
/// request starts here; <see cref="Poll"/> applies whatever finished: the roster goes to <see cref="CharacterRoster"/>,
/// failures become player-facing messages, and exception text only reaches the console.
/// </summary>
internal sealed class CharacterRosterRequests
{
    private readonly ICharacterRosterService _service;
    private readonly Func<Task> _signOut;
    private readonly CharacterRoster _roster;
    private Task<IReadOnlyList<CharacterRosterEntry>>? _listTask;
    private Task<(bool Succeeded, string Message)>? _createTask;
    private Task<(bool Succeeded, string Message)>? _enterTask;
    private Task? _signOutTask;

    /// <summary>Initializes a new instance of the <see cref="CharacterRosterRequests"/> class.</summary>
    /// <param name="service">The roster service (the session).</param>
    /// <param name="signOut">Signs out and forgets a remembered account (<see cref="MenuFlow.SignOutAsync"/>).</param>
    /// <param name="roster">Receives every roster that arrives.</param>
    public CharacterRosterRequests(ICharacterRosterService service, Func<Task> signOut, CharacterRoster roster)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _signOut = signOut ?? throw new ArgumentNullException(nameof(signOut));
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
    }

    /// <summary>Gets the status line under the panels.</summary>
    public string StatusMessage { get; private set; } = GameText.Get("character.status.loading");

    /// <summary>Gets whether <see cref="StatusMessage"/> is an error.</summary>
    public bool StatusIsError { get; private set; }

    /// <summary>Gets whether a roster request is in flight.</summary>
    public bool IsLoadingRoster => _listTask is not null;

    /// <summary>Gets whether a create request is in flight.</summary>
    public bool IsCreating => _createTask is not null;

    /// <summary>Gets whether an enter-world request is in flight.</summary>
    public bool IsEnteringWorld => _enterTask is not null;

    /// <summary>Gets whether the sign-out is in flight.</summary>
    public bool IsSigningOut => _signOutTask is not null;

    /// <summary>Asks the server for the account's heroes.</summary>
    public void LoadRoster() => _listTask = _service.ListCharactersAsync();

    /// <summary>Asks the server to create <paramref name="name"/>; the roster selects it once it reloads.</summary>
    public void Create(string name, byte appearancePreset)
    {
        ArgumentNullException.ThrowIfNull(name);
        string trimmed = name.Trim();
        _roster.SelectAfterNextLoad(trimmed);
        _createTask = _service.CreateCharacterAsync(name, appearancePreset);
        this.SetStatus(GameText.Format("character.status.creating", new TextArg("name", trimmed)), isError: false);
    }

    /// <summary>Asks the server to enter the world as <paramref name="hero"/>.</summary>
    public void EnterWorld(CharacterRosterEntry hero)
    {
        ArgumentNullException.ThrowIfNull(hero);
        _enterTask = _service.EnterWorldAsync(hero.CharacterId);
        this.SetStatus(GameText.Format("character.status.entering", new TextArg("name", hero.Name)), isError: false);
    }

    /// <summary>Signs out.</summary>
    public void SignOut()
    {
        _signOutTask = _signOut();
        this.SetStatus(GameText.Get("character.status.signing_out"), isError: false);
    }

    /// <summary>Applies every request that has finished since the last poll.</summary>
    public RosterPollResult Poll()
    {
        if (_signOutTask is { IsCompleted: true } signOut)
        {
            _signOutTask = null;
            try
            {
                signOut.GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Always return cleanly to the start screen.
            }

            return new RosterPollResult(RosterRequestOutcome.SignedOut, false, false);
        }

        if (_enterTask is { IsCompleted: true } enter)
        {
            _enterTask = null;
            try
            {
                (bool ok, string message) = enter.GetAwaiter().GetResult();
                if (ok)
                {
                    return new RosterPollResult(RosterRequestOutcome.EnteredWorld, false, false);
                }

                this.SetStatus(message, isError: true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"[Characters] Request failed: {ex.Message}");
                this.SetStatus(GameText.Get("auth.server_unreachable"), isError: true);
            }
        }

        bool created = false;
        if (_createTask is { IsCompleted: true } create)
        {
            _createTask = null;
            try
            {
                (bool ok, string message) = create.GetAwaiter().GetResult();
                this.SetStatus(message, isError: !ok);
                if (ok)
                {
                    created = true;
                    this.LoadRoster();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"[Characters] Request failed: {ex.Message}");
                this.SetStatus(GameText.Get("auth.server_unreachable"), isError: true);
            }
        }

        bool loadedEmpty = false;
        if (_listTask is { IsCompleted: true } list)
        {
            _listTask = null;
            try
            {
                _roster.Load(list.GetAwaiter().GetResult());
                loadedEmpty = _roster.Count == 0;
                if (_createTask is null)
                {
                    this.SetStatus(
                        _roster.Count > 0 ? GameText.Plural("character.status.found", _roster.Count) : GameText.Get("character.status.welcome"),
                        isError: false);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"[Characters] Roster load failed: {ex.Message}");
                this.SetStatus(GameText.Get("character.status.load_failed"), isError: true);
            }
        }

        return new RosterPollResult(RosterRequestOutcome.None, created, loadedEmpty);
    }

    private void SetStatus(string message, bool isError)
    {
        this.StatusMessage = message;
        this.StatusIsError = isError;
    }
}
