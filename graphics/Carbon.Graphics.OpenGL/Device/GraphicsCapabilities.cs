// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Device;

/// <summary>
/// Encapsulates queried hardware capabilities and driver properties of the active OpenGL device.
/// </summary>
public sealed class GraphicsCapabilities
{
    /// <summary>
    /// Gets the hardware vendor string reported by OpenGL.
    /// </summary>
    public string Vendor { get; }

    /// <summary>
    /// Gets the renderer string (GPU model name) reported by OpenGL.
    /// </summary>
    public string Renderer { get; }

    /// <summary>
    /// Gets the driver version string.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the supported GLSL shading language version string.
    /// </summary>
    public string ShadingLanguageVersion { get; }

    /// <summary>
    /// Gets the maximum supported texture dimension in pixels.
    /// </summary>
    public int MaxTextureSize { get; }

    /// <summary>
    /// Gets the maximum number of 4-component vertex attributes available to a vertex shader.
    /// </summary>
    public int MaxVertexAttributes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GraphicsCapabilities"/> class by querying the active GL context.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    public unsafe GraphicsCapabilities(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl, nameof(gl));

        this.Vendor = gl.GetStringS(StringName.Vendor) ?? "Unknown";
        this.Renderer = gl.GetStringS(StringName.Renderer) ?? "Unknown";
        this.Version = gl.GetStringS(StringName.Version) ?? "Unknown";
        this.ShadingLanguageVersion = gl.GetStringS(StringName.ShadingLanguageVersion) ?? "Unknown";

        int maxTexSize = 0;
        gl.GetInteger(GetPName.MaxTextureSize, &maxTexSize);
        this.MaxTextureSize = maxTexSize;

        int maxAttribs = 0;
        gl.GetInteger(GetPName.MaxVertexAttribs, &maxAttribs);
        this.MaxVertexAttributes = maxAttribs;
    }
}
