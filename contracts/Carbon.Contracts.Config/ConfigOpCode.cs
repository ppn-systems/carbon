// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Config;

/// <summary>
/// Carbon server-authoritative configuration sync opcodes.
/// <para>Range <c>0x0200–0x02FF</c> (Account band, before world entry) is reserved for the Config module.</para>
/// </summary>
/// <remarks>
/// Opcode layout within the Config range:
/// <list type="table">
///   <item><term>0x0201–0x020F</term><description>C2S — client to server</description></item>
///   <item><term>0x0210–0x021F</term><description>S2C — server push (manifest, also re-sent after hot reload)</description></item>
///   <item><term>0x0220–0x022F</term><description>S2C — server stream (document chunks)</description></item>
/// </list>
/// </remarks>
public static class ConfigOpCode
{
    // ── C2S: Client → Server (0x0201–0x020F) ────────────────────────────────

    /// <summary>Client asks for the documents whose cached hash does not match the manifest.</summary>
    public const ushort Request = 0x0201;

    // ── S2C: Server → Client push (0x0210–0x021F) ───────────────────────────

    /// <summary>Server announces every config document with its content hash.</summary>
    public const ushort Manifest = 0x0210;

    // ── S2C: Server → Client stream (0x0220–0x022F) ─────────────────────────

    /// <summary>One chunk of a config document in a stream response to <see cref="Request"/>.</summary>
    public const ushort Document = 0x0220;
}
