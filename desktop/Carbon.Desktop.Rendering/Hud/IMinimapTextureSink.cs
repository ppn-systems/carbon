// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Receives baked minimap chunk pixels (the GL atlas in the client, a recorder in tests).</summary>
internal interface IMinimapTextureSink
{
    /// <summary>Uploads one chunk's pixels (<c>Chunk2D.Size²</c> RGBA texels, row-major, north first) into atlas slot <c>(slotX, slotY)</c>.</summary>
    void UploadChunk(int slotX, int slotY, ReadOnlySpan<byte> rgba);
}
