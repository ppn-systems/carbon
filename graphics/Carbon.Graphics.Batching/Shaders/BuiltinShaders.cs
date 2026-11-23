// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;
using GlShader = Carbon.Graphics.OpenGL.Shaders.Shader;

namespace Carbon.Graphics.Batching.Shaders;

/// <summary>
/// Provides default built-in shaders for 2D/2.5D sprite batching and colored geometry.
/// </summary>
public static class BuiltinShaders
{
    private const string SPRITE_VERTEX_SHADER = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec4 aColor;
        layout(location = 2) in vec2 aTexCoord;

        uniform mat4 uViewProjection;

        out vec4 vColor;
        out vec2 vTexCoord;

        void main()
        {
            vColor = aColor;
            vTexCoord = aTexCoord;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    private const string SPRITE_FRAGMENT_SHADER = """
        #version 330 core
        in vec4 vColor;
        in vec2 vTexCoord;

        uniform sampler2D uTexture;

        out vec4 FragColor;

        void main()
        {
            FragColor = texture(uTexture, vTexCoord) * vColor;
        }
        """;

    private const string COLOR_VERTEX_SHADER = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec4 aColor;

        uniform mat4 uViewProjection;

        out vec4 vColor;

        void main()
        {
            vColor = aColor;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    private const string COLOR_FRAGMENT_SHADER = """
        #version 330 core
        in vec4 vColor;

        out vec4 FragColor;

        void main()
        {
            FragColor = vColor;
        }
        """;

    /// <summary>
    /// Compiles and creates a new default 2.5D sprite shader.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="stateCache">The pipeline state cache.</param>
    /// <returns>A new compiled and linked <see cref="Shader"/> instance.</returns>
    public static GlShader CreateSpriteShader(GL gl, GlStateCache stateCache) =>
        new(gl, stateCache, SPRITE_VERTEX_SHADER, SPRITE_FRAGMENT_SHADER);

    /// <summary>
    /// Compiles and creates a new default colored geometry shader for procedural shapes and lines.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="stateCache">The pipeline state cache.</param>
    /// <returns>A new compiled and linked <see cref="GlShader"/> instance.</returns>
    public static GlShader CreateColorShader(GL gl, GlStateCache stateCache) =>
        new(gl, stateCache, COLOR_VERTEX_SHADER, COLOR_FRAGMENT_SHADER);
}
