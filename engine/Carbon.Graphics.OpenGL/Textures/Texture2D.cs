using System;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.States;
using Silk.NET.OpenGL;

namespace Carbon.Graphics.OpenGL.Textures;

/// <summary>
/// Encapsulates an OpenGL 2D texture resource with configurable filtering and coordinate wrapping.
/// </summary>
public sealed class Texture2D : ITexture
{
    private readonly GL _gl;
    private readonly GlStateManager _stateManager;
    private bool _disposed;

    /// <inheritdoc/>
    public uint Handle { get; }

    /// <inheritdoc/>
    public uint Width { get; }

    /// <inheritdoc/>
    public uint Height { get; }

    /// <summary>
    /// Gets the texture sampling filter.
    /// </summary>
    public TextureFilter Filter { get; private set; }

    /// <summary>
    /// Gets the texture coordinate wrapping mode.
    /// </summary>
    public TextureWrap Wrap { get; private set; }

    /// <summary>
    /// Gets the pixel format of this texture.
    /// </summary>
    public PixelFormatType Format { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Texture2D"/> class and allocates GPU storage.
    /// </summary>
    /// <param name="gl">The OpenGL API instance.</param>
    /// <param name="stateManager">The state cache manager.</param>
    /// <param name="width">The width of the texture in pixels.</param>
    /// <param name="height">The height of the texture in pixels.</param>
    /// <param name="format">The pixel color format.</param>
    /// <param name="filter">The sampling filter mode.</param>
    /// <param name="wrap">The coordinate wrap mode.</param>
    public unsafe Texture2D(
        GL gl,
        GlStateManager stateManager,
        uint width,
        uint height,
        PixelFormatType format = PixelFormatType.Rgba8,
        TextureFilter filter = TextureFilter.Nearest,
        TextureWrap wrap = TextureWrap.ClampToEdge)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        ArgumentOutOfRangeException.ThrowIfZero(width, nameof(width));
        ArgumentOutOfRangeException.ThrowIfZero(height, nameof(height));

        this.Width = width;
        this.Height = height;
        this.Format = format;
        this.Filter = filter;
        this.Wrap = wrap;

        this.Handle = _gl.GenTexture();
        _stateManager.BindTexture(0, this.Handle);

        this.ApplySamplerParameters(filter, wrap);

        (InternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType) = ResolveFormats(format);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, pixelFormat, pixelType, null);
    }

    /// <inheritdoc/>
    public void Bind(int unit = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stateManager.BindTexture(unit, this.Handle);
    }

    /// <summary>
    /// Updates pixel data for this texture from a contiguous memory span.
    /// </summary>
    /// <param name="pixels">The span of raw pixel data.</param>
    /// <param name="x">The left pixel offset.</param>
    /// <param name="y">The top pixel offset.</param>
    /// <param name="width">The width of the updated region, or 0 to update full width.</param>
    /// <param name="height">The height of the updated region, or 0 to update full height.</param>
    public unsafe void SetData(ReadOnlySpan<byte> pixels, int x = 0, int y = 0, uint width = 0, uint height = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint targetWidth = width == 0 ? this.Width : width;
        uint targetHeight = height == 0 ? this.Height : height;

        this.Bind(0);

        (_, PixelFormat pixelFormat, PixelType pixelType) = ResolveFormats(this.Format);

        fixed (byte* ptr = pixels)
        {
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, targetWidth, targetHeight, pixelFormat, pixelType, ptr);
        }
    }

    /// <summary>
    /// Modifies the sampling filter mode for this texture.
    /// </summary>
    /// <param name="filter">The new sampling filter.</param>
    public void SetFilter(TextureFilter filter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (this.Filter != filter)
        {
            this.Filter = filter;
            this.Bind(0);
            int glFilter = filter == TextureFilter.Nearest ? (int)GLEnum.Nearest : (int)GLEnum.Linear;
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, glFilter);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, glFilter);
        }
    }

    private void ApplySamplerParameters(TextureFilter filter, TextureWrap wrap)
    {
        int glFilter = filter == TextureFilter.Nearest ? (int)GLEnum.Nearest : (int)GLEnum.Linear;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, glFilter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, glFilter);

        int glWrap = wrap switch
        {
            TextureWrap.ClampToEdge => (int)GLEnum.ClampToEdge,
            TextureWrap.Repeat => (int)GLEnum.Repeat,
            TextureWrap.MirroredRepeat => (int)GLEnum.MirroredRepeat,
            _ => (int)GLEnum.ClampToEdge
        };

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, glWrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, glWrap);
    }

    private static (InternalFormat InternalFormat, PixelFormat PixelFormat, PixelType PixelType) ResolveFormats(PixelFormatType format)
    {
        return format switch
        {
            PixelFormatType.Rgba8 => (InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte),
            PixelFormatType.Rgb8 => (InternalFormat.Rgb8, PixelFormat.Rgb, PixelType.UnsignedByte),
            PixelFormatType.R8 => (InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte),
            PixelFormatType.Depth24Stencil8 => (InternalFormat.Depth24Stencil8, PixelFormat.DepthStencil, PixelType.UnsignedInt248),
            _ => (InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte)
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteTexture(this.Handle);
            _disposed = true;
        }
    }
}
