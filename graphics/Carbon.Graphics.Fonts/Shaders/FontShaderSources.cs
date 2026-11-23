// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.Fonts.Shaders;

/// <summary>
/// Provides GLSL 330 core and OpenGL ES 3.0 shader source code for Signed Distance Field (SDF) font rendering.
/// </summary>
public static class FontShaderSources
{
    /// <summary>
    /// GLSL 330 core fragment shader for smooth anti-aliased Signed Distance Field (SDF) font rendering with optional outlines.
    /// </summary>
    public const string SdfFragmentShader330 = @"#version 330 core
in vec4 vColor;
in vec2 vTexCoords;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform float uDistanceThreshold = 0.5;
uniform float uSmoothing = 0.05;
uniform vec4 uOutlineColor = vec4(0.0, 0.0, 0.0, 1.0);
uniform float uOutlineThreshold = 0.0;

void main()
{
    float dist = texture(uTexture, vTexCoords).a;

    if (uOutlineThreshold > 0.0 && uOutlineThreshold < uDistanceThreshold)
    {
        float textAlpha = smoothstep(uDistanceThreshold - uSmoothing, uDistanceThreshold + uSmoothing, dist);
        float outlineAlpha = smoothstep(uOutlineThreshold - uSmoothing, uOutlineThreshold + uSmoothing, dist);

        vec4 textCol = vec4(vColor.rgb, vColor.a * textAlpha);
        vec4 outlineCol = vec4(uOutlineColor.rgb, uOutlineColor.a * outlineAlpha);

        FragColor = mix(outlineCol, textCol, textAlpha);
    }
    else
    {
        float alpha = smoothstep(uDistanceThreshold - uSmoothing, uDistanceThreshold + uSmoothing, dist);
        FragColor = vec4(vColor.rgb, vColor.a * alpha);
    }
}
";

    /// <summary>
    /// OpenGL ES 3.0 fragment shader for mobile and WebAssembly Signed Distance Field (SDF) text rendering.
    /// </summary>
    public const string SdfFragmentShaderEs300 = @"#version 300 es
precision mediump float;

in vec4 vColor;
in vec2 vTexCoords;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform float uDistanceThreshold;
uniform float uSmoothing;

void main()
{
    float dist = texture(uTexture, vTexCoords).a;
    float alpha = smoothstep(uDistanceThreshold - uSmoothing, uDistanceThreshold + uSmoothing, dist);
    FragColor = vec4(vColor.rgb, vColor.a * alpha);
}
";
}
