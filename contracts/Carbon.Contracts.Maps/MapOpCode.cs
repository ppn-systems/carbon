// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Maps;

/// <summary>
/// Carbon map / world-terrain protocol opcodes.
/// <para>Range <c>0x1100–0x11FF</c> (inside the World State band) is reserved for the Map module.</para>
/// </summary>
/// <remarks>
/// Opcode layout within the Map range:
/// <list type="table">
///   <item><term>0x1101–0x110F</term><description>C2S — client to server</description></item>
///   <item><term>0x1110–0x111F</term><description>S2C — server response (request-response)</description></item>
///   <item><term>0x1120–0x112F</term><description>S2C — server stream (multi-packet)</description></item>
///   <item><term>0x1130–0x11FF</term><description>S2C — server push (async events, AoI broadcast)</description></item>
/// </list>
/// Write requests (harvest, place, demolish, door) are answered with
/// <see cref="Common.MutationResult"/>; the resulting world change reaches every client in range as a push.
/// </remarks>
public static class MapOpCode
{
    // ── C2S: Client → Server (0x1101–0x110F) ────────────────────────────────

    /// <summary>Client asks for the world generation parameters (seed, version, spawn).</summary>
    public const ushort WorldInfoRequest = 0x1101;

    /// <summary>Client asks for the edits of one chunk (stream response of <see cref="ChunkDelta"/>).</summary>
    public const ushort ChunkRequest = 0x1102;

    /// <summary>Client damages the natural prop on a tile (chop / mine).</summary>
    public const ushort PropHarvest = 0x1103;

    /// <summary>Client places a structure on a tile.</summary>
    public const ushort StructurePlace = 0x1104;

    /// <summary>Client demolishes the structure on a tile.</summary>
    public const ushort StructureDemolish = 0x1105;

    /// <summary>Client opens or closes the door on a tile.</summary>
    public const ushort DoorToggle = 0x1106;

    // ── S2C: Server → Client request-response (0x1110–0x111F) ───────────────

    /// <summary>Server answers <see cref="WorldInfoRequest"/>.</summary>
    public const ushort WorldInfo = 0x1110;

    // ── S2C: Server → Client stream (0x1120–0x112F) ─────────────────────────

    /// <summary>One chunk's edits in a stream response to <see cref="ChunkRequest"/>.</summary>
    public const ushort ChunkDelta = 0x1120;

    // ── S2C: Server → Client push (0x1130–0x11FF) ───────────────────────────

    /// <summary>A terrain tile changed (AoI broadcast).</summary>
    public const ushort TileChanged = 0x1130;

    /// <summary>A natural prop was damaged or destroyed (AoI broadcast).</summary>
    public const ushort PropChanged = 0x1131;

    /// <summary>A structure was placed, damaged, toggled or removed (AoI broadcast).</summary>
    public const ushort StructureChanged = 0x1132;


    /// <summary>A construction site (blueprint / frame) or a deconstruction changed stage or progress (AoI broadcast).</summary>
    public const ushort ConstructionChanged = 0x1133;
}
