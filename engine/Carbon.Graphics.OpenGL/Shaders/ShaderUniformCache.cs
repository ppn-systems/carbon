using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Shaders;

/// <summary>
/// High-performance uniform location cache that completely removes glGetUniformLocation queries from the render loop.
/// </summary>
public sealed class ShaderUniformCache
{
    private readonly GL _gl;
    private readonly uint _program;
    private readonly Dictionary<string, int> _locations = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ShaderUniformCache"/> class.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="program">The compiled and linked shader program handle.</param>
    public ShaderUniformCache(GL gl, uint program)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _program = program;
    }

    /// <summary>
    /// Gets the uniform location index, querying OpenGL only on first access and caching thereafter.
    /// </summary>
    /// <param name="name">The exact uniform name defined in GLSL source.</param>
    /// <returns>The uniform location index, or -1 if the uniform does not exist or was optimized away.</returns>
    public int GetLocation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));

        if (_locations.TryGetValue(name, out int location))
        {
            return location;
        }

        location = _gl.GetUniformLocation(_program, name);
        _locations[name] = location;
        return location;
    }
}
