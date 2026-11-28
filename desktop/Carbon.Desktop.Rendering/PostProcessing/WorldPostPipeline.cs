// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.States;
using Carbon.Graphics.OpenGL.Textures;
using Silk.NET.OpenGL;
using GlShader = Carbon.Graphics.OpenGL.Shaders.Shader;
using VertexArray = Carbon.Graphics.OpenGL.Buffers.VertexArray;

namespace Carbon.Desktop.Rendering.PostProcessing;

/// <summary>
/// The world's offscreen targets and its single composite pass (ADR-041). The world renders into a full-resolution
/// scene target; lights render additively into a half-resolution light map cleared to the ambient colour; one
/// full-screen triangle then writes <c>grade(scene × light) + bloom</c>, vignetted and dithered, to the back buffer.
/// The light map stores half the light so torches can push the scene up to 2× (warm, overbright pools at night).
/// Targets are created on first use and again only when the viewport size changes; frames allocate nothing.
/// </summary>
internal sealed class WorldPostPipeline : IDisposable
{
    private const string VertexSource = """
        #version 330 core
        void main()
        {
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4((p * 2.0) - 1.0, 0.0, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 330 core
        uniform sampler2D uScene;
        uniform sampler2D uLight;
        uniform vec2 uViewport;
        uniform vec2 uLightTexel;
        uniform vec3 uLift;
        uniform vec3 uGain;
        uniform float uContrast;
        uniform float uSaturation;
        uniform float uVignette;
        uniform float uBloom;
        uniform float uAmbientLuma;
        out vec4 FragColor;

        vec3 overbright(vec2 uv)
        {
            return max((texture(uLight, uv).rgb * 2.0) - 1.0, 0.0);
        }

        void main()
        {
            vec2 uv = gl_FragCoord.xy / uViewport;
            vec3 scene = texture(uScene, uv).rgb;
            vec3 light = texture(uLight, uv).rgb * 2.0;
            vec3 c = scene * light;
            // How much a point light (not the ambient) lights this pixel: lit pools keep their colour at night.
            float lit = clamp((dot(light, vec3(0.2126, 0.7152, 0.0722)) - uAmbientLuma) * 2.5, 0.0, 1.0);

            if (uBloom > 0.0)
            {
                // Light bloom: 12 bilinear taps on two rings of the half-res light map (a wide, soft halo).
                vec3 b = vec3(0.0);
                for (int i = 0; i < 6; i++)
                {
                    float a = float(i) * 1.0471976;
                    vec2 d = vec2(cos(a), sin(a)) * uLightTexel;
                    b += overbright(uv + (d * 6.0));
                    b += overbright(uv + (vec2(-d.y, d.x) * 14.0)) * 0.6;
                }

                c += b * (uBloom / 9.6);
            }

            // Soft shoulder on luminance (hue kept): lit snow rolls off toward white instead of clipping flat.
            float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
            if (lum > 0.7)
            {
                float rolled = 0.7 + (0.3 * (1.0 - exp(-(lum - 0.7) / 0.3)));
                c *= rolled / lum;
            }

            c = uLift + (c * (uGain - uLift));
            c = ((c - 0.5) * uContrast) + 0.5;
            float luma = dot(c, vec3(0.2126, 0.7152, 0.0722));
            c = mix(vec3(luma), c, mix(uSaturation, max(uSaturation, 1.04), lit));

            vec2 q = (uv - 0.5) * vec2(uViewport.x / uViewport.y, 1.0);
            float v = smoothstep(0.42, 1.05, length(q));
            c *= 1.0 - (uVignette * v);

            // Half-LSB triangular-ish dither so dark night gradients do not band in 8-bit.
            float n = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);
            c += (n - 0.5) / 255.0;
            FragColor = vec4(clamp(c, 0.0, 1.0), 1.0);
        }
        """;

    // Share of the light map's averaged overbright halo added on High (LearnOpenGL suggests 0.03–0.15 for HDR bloom;
    // this source is already only the overbright light, so it can be stronger).
    private const float BloomStrength = 0.3f;

    private readonly GraphicsDevice _device;
    private readonly GlShader _shader;
    private readonly VertexArray _emptyVao;
    private readonly int _uViewport;
    private readonly int _uLightTexel;
    private readonly int _uLift;
    private readonly int _uGain;
    private readonly int _uContrast;
    private readonly int _uSaturation;
    private readonly int _uVignette;
    private readonly int _uBloom;
    private readonly int _uAmbientLuma;
    private RenderTarget2D? _scene;
    private RenderTarget2D? _light;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="WorldPostPipeline"/> class (compiles the composite shader).</summary>
    /// <param name="device">The graphics device.</param>
    public WorldPostPipeline(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _shader = new GlShader(device.Gl, device.StateCache, VertexSource, FragmentSource);
        _emptyVao = new VertexArray(device.Gl, device.StateCache);
        _shader.Bind();
        _shader.SetUniform(_shader.GetUniformLocation("uScene"), 0);
        _shader.SetUniform(_shader.GetUniformLocation("uLight"), 1);
        _uViewport = _shader.GetUniformLocation("uViewport");
        _uLightTexel = _shader.GetUniformLocation("uLightTexel");
        _uLift = _shader.GetUniformLocation("uLift");
        _uGain = _shader.GetUniformLocation("uGain");
        _uContrast = _shader.GetUniformLocation("uContrast");
        _uSaturation = _shader.GetUniformLocation("uSaturation");
        _uVignette = _shader.GetUniformLocation("uVignette");
        _uBloom = _shader.GetUniformLocation("uBloom");
        _uAmbientLuma = _shader.GetUniformLocation("uAmbientLuma");
    }

    /// <summary>Binds the scene target (created or resized to the viewport) and clears it to <paramref name="clear"/>.</summary>
    /// <param name="plan">The active pass plan.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="clear">Clear colour.</param>
    public void BeginScene(in PostPassPlan plan, int viewportWidth, int viewportHeight, in Vector4 clear)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        this.Ensure(ref _scene, (uint)viewportWidth, (uint)viewportHeight);
        (uint lw, uint lh) = plan.LightMapSize(viewportWidth, viewportHeight);
        this.Ensure(ref _light, lw, lh);
        _scene!.Bind();
        _device.SetClearColor(in clear);
        _device.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <summary>Binds the light map and clears it to the ambient light (stored halved, see the class remarks).</summary>
    /// <param name="ambient">Ambient light, 0..1 per channel.</param>
    public void BeginLightMap(Vector3 ambient)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _light!.Bind();
        Vector3 half = Vector3.Clamp(ambient, Vector3.Zero, Vector3.One) * 0.5f;
        _device.SetClearColor(new Vector4(half, 1f));
        _device.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <summary>Composites the scene and light map onto the back buffer with one full-screen triangle.</summary>
    /// <param name="plan">The active pass plan.</param>
    /// <param name="grade">The colour grade (ignored unless the plan grades).</param>
    /// <param name="ambient">The ambient light the light map was cleared to.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    public void Composite(in PostPassPlan plan, in ColorGrade grade, Vector3 ambient, int viewportWidth, int viewportHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GlStateCache cache = _device.StateCache;
        cache.BindFramebuffer(0);
        _device.SetViewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);

        ColorGrade g = plan.ColorGrading ? grade : ColorGrade.Identity;
        _shader.Bind();
        _shader.SetUniform(_uViewport, new Vector2(viewportWidth, viewportHeight));
        _shader.SetUniform(_uLightTexel, new Vector2(1f / _light!.Width, 1f / _light.Height));
        _shader.SetUniform(_uLift, g.Lift);
        _shader.SetUniform(_uGain, g.Gain);
        _shader.SetUniform(_uContrast, g.Contrast);
        _shader.SetUniform(_uSaturation, g.Saturation);
        _shader.SetUniform(_uVignette, plan.Vignette ? g.Vignette : 0f);
        _shader.SetUniform(_uBloom, plan.Bloom ? BloomStrength : 0f);
        Vector3 a = Vector3.Clamp(ambient, Vector3.Zero, Vector3.One);
        _shader.SetUniform(_uAmbientLuma, Vector3.Dot(a, new Vector3(0.2126f, 0.7152f, 0.0722f)));
        _scene!.Texture.Bind(0);
        _light.Texture.Bind(1);
        cache.SetBlendState(BlendState.Opaque);
        cache.SetDepthStencilState(DepthStencilState.None);
        _emptyVao.Bind();
        _device.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _scene?.Dispose();
            _light?.Dispose();
            _emptyVao.Dispose();
            _shader.Dispose();
        }
    }

    private void Ensure(ref RenderTarget2D? target, uint width, uint height)
    {
        if (width == 0 || height == 0 || (target is not null && target.Width == width && target.Height == height))
        {
            return;
        }

        target?.Dispose();
        target = new RenderTarget2D(_device.Gl, _device.StateCache, width, height);
    }
}
