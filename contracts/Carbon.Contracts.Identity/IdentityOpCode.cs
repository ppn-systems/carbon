// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Identity;

/// <summary>
/// Carbon identity, authentication, and character selection protocol opcodes.
/// <para>Range <c>0x0100–0x01FF</c> is reserved for the Identity module.</para>
/// </summary>
public static class IdentityOpCode
{
    // ── C2S: Client → Server (0x0101–0x010F) ────────────────────────────────

    /// <summary>Registers a new player account.</summary>
    public const ushort Register = 0x0101;

    /// <summary>Authenticates an existing player account.</summary>
    public const ushort Login = 0x0102;

    /// <summary>Requests the authenticated account's character roster.</summary>
    public const ushort CharacterListRequest = 0x0103;

    /// <summary>Creates a new playable character under the authenticated account.</summary>
    public const ushort CharacterCreate = 0x0104;

    /// <summary>Selects a character to enter the authoritative world simulation.</summary>
    public const ushort CharacterSelect = 0x0105;

    /// <summary>Logs out the active session and clears identity binding.</summary>
    public const ushort Logout = 0x0106;

    // ── S2C: Server → Client request-response (0x0110–0x011F) ───────────────

    /// <summary>Server response to <see cref="Register"/> or <see cref="Login"/>.</summary>
    public const ushort AuthResult = 0x0110;

    /// <summary>Server response to <see cref="CharacterSelect"/> authorizing world entry.</summary>
    public const ushort CharacterEnterWorld = 0x0111;

    // ── S2C: Server → Client stream (0x0120–0x012F) ─────────────────────────

    /// <summary>One character summary item streamed in response to <see cref="CharacterListRequest"/>.</summary>
    public const ushort CharacterSummary = 0x0120;
}
