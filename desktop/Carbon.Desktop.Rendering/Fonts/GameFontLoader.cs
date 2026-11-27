// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.Fonts.TrueType;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.Textures;

namespace Carbon.Desktop.Rendering.Fonts;

/// <summary>
/// Loads the game's rasterized UI font for the active locale's font stack (<c>resources/localization/locales.json</c>, ADR-023 D5)
/// from <c>resources/fonts</c> (with an automatic embedded crisp pixel fallback when running outside asset folders) and allocates
/// the shared 1x1 white pixel texture on <see cref="IGraphicsDevice"/>.
/// </summary>
public static class GameFontLoader
{
    private const int CellSize = 8;
    private const int Columns = 16;
    private const int FallbackAtlasWidth = Columns * CellSize;
    private const int FallbackAtlasHeight = 6 * CellSize;

    /// <summary>Searches standard runtime directories for the game's bundled TrueType font.</summary>
    public static string? TryFindFontPath()
    {
        string[] roots = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
        for (int i = 0; i < roots.Length; i++)
        {
            DirectoryInfo? cursor = new(roots[i]);
            while (cursor is not null)
            {
                string fontsDir = Path.Combine(cursor.FullName, "resources", "fonts");
                if (Directory.Exists(fontsDir))
                {
                    string preferred = Path.Combine(fontsDir, "thaleah_fat.ttf");
                    if (File.Exists(preferred))
                    {
                        return preferred;
                    }

                    string[] ttfFiles = Directory.GetFiles(fontsDir, "*.ttf");
                    if (ttfFiles.Length > 0)
                    {
                        return ttfFiles[0];
                    }
                }

                cursor = cursor.Parent;
            }
        }

        return null;
    }

    /// <summary>Searches standard runtime directories for <c>resources/fonts</c>.</summary>
    /// <returns>The fonts directory, or <see langword="null"/> when not found.</returns>
    public static string? TryFindFontsDirectory()
    {
        string? path = TryFindFontPath();
        return path is null ? null : Path.GetDirectoryName(path);
    }

    /// <summary>
    /// Builds the ordered face stack of <paramref name="locale"/> from the files present in <paramref name="fontsDirectory"/>.
    /// Missing files are skipped.
    /// </summary>
    /// <param name="locale">The locale whose font stack to load.</param>
    /// <param name="fontsDirectory">The <c>resources/fonts</c> directory.</param>
    /// <returns>The faces, primary first (empty when no listed file exists).</returns>
    public static List<FontFaceSource> LoadFaces(LocaleInfo locale, string fontsDirectory)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentException.ThrowIfNullOrEmpty(fontsDirectory, nameof(fontsDirectory));
        List<FontFaceSource> faces = new(locale.Fonts.Count);
        for (int i = 0; i < locale.Fonts.Count; i++)
        {
            LocaleFontFace face = locale.Fonts[i];
            string path = Path.Combine(fontsDirectory, face.File);
            if (File.Exists(path))
            {
                faces.Add(new FontFaceSource(face.File, File.ReadAllBytes(path), face.PixelSize));
            }
        }

        return faces;
    }

    /// <summary>
    /// Loads the UI <see cref="BitmapFont"/> for <paramref name="locale"/> (defaults to the active <see cref="GameText"/> locale),
    /// baking every Latin range English and Vietnamese need.
    /// </summary>
    /// <param name="device">Active OpenGL graphics device.</param>
    /// <param name="locale">The locale whose font stack to load.</param>
    /// <returns>The ready-to-render UI font.</returns>
    public static BitmapFont LoadFont(IGraphicsDevice device, LocaleInfo? locale = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device is not GraphicsDevice concrete)
        {
            throw new ArgumentException("Requires concrete GraphicsDevice to allocate font textures.", nameof(device));
        }

        LocaleInfo active = locale ?? GameText.Current.Locale;
        string? fontsDir = TryFindFontsDirectory();
        if (fontsDir is not null)
        {
            List<FontFaceSource> faces = LoadFaces(active, fontsDir);
            if (faces.Count > 0)
            {
                return TrueTypeFontLoader.CreateFromTrueType(active.Code, concrete, faces, UnicodeGlyphRanges.LatinFull);
            }

            string? anyFont = TryFindFontPath();
            if (anyFont is not null)
            {
                return TrueTypeFontLoader.CreateFromTrueType("ThaleahFat", concrete, File.ReadAllBytes(anyFont), pixelSize: 16f);
            }
        }

        return CreateEmbeddedFallback(concrete);
    }

    /// <summary>
    /// Loads the game UI <see cref="BitmapFont"/> for the active locale and shared 1x1 white pixel texture on <paramref name="device"/>.
    /// </summary>
    /// <param name="device">Active OpenGL graphics device.</param>
    /// <returns>The ready-to-render UI font and white pixel texture.</returns>
    public static (BitmapFont Font, Texture2D WhitePixel) Load(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device is not GraphicsDevice concrete)
        {
            throw new ArgumentException("Requires concrete GraphicsDevice to allocate font textures.", nameof(device));
        }

        Texture2D? whitePixel = null;
        BitmapFont? font = null;

        try
        {
            whitePixel = new Texture2D(
                concrete.Gl,
                concrete.StateCache,
                1,
                1,
                TextureFormat.Rgba8,
                TextureFilter.Nearest,
                TextureWrap.ClampToEdge);
            ReadOnlySpan<byte> white = [255, 255, 255, 255];
            whitePixel.SetData(white);

            font = LoadFont(concrete);

            BitmapFont resultFont = font;
            Texture2D resultWhite = whitePixel;
            font = null;
            whitePixel = null;
            return (resultFont, resultWhite);
        }
        finally
        {
            font?.Dispose();
            whitePixel?.Dispose();
        }
    }

    private static BitmapFont CreateEmbeddedFallback(GraphicsDevice concrete)
    {
        byte[] pixels = new byte[FallbackAtlasWidth * FallbackAtlasHeight * 4];
        Texture2D? atlasTex = null;

        try
        {
            atlasTex = new Texture2D(
                concrete.Gl,
                concrete.StateCache,
                FallbackAtlasWidth,
                FallbackAtlasHeight,
                TextureFormat.Rgba8,
                TextureFilter.Nearest,
                TextureWrap.ClampToEdge);

            for (int code = 32; code <= 126; code++)
            {
                int glyphIdx = code - 32;
                int col = glyphIdx % Columns;
                int row = glyphIdx / Columns;
                int cellX = col * CellSize;
                int cellY = row * CellSize;

                uint[] rows = GetGlyphRows((char)code);
                for (int r = 0; r < 7; r++)
                {
                    uint bits = rows[r];
                    for (int c = 0; c < 5; c++)
                    {
                        if ((bits & (1U << (4 - c))) == 0)
                        {
                            continue;
                        }

                        int px = cellX + c;
                        int py = cellY + r;
                        int byteIdx = ((py * FallbackAtlasWidth) + px) * 4;
                        pixels[byteIdx] = 255;
                        pixels[byteIdx + 1] = 255;
                        pixels[byteIdx + 2] = 255;
                        pixels[byteIdx + 3] = 255;
                    }
                }
            }

            atlasTex.SetData(pixels);
            BitmapFontAtlas atlas = new(atlasTex);
            atlasTex = null;

            for (int code = 32; code <= 126; code++)
            {
                int glyphIdx = code - 32;
                int col = glyphIdx % Columns;
                int row = glyphIdx / Columns;
                RectangleF rect = new(col * CellSize, row * CellSize, 5f, 7f);
                atlas.AddGlyph(new FontGlyph(code, in rect, 0f, 1f, 6f));
            }

            FontMetrics metrics = new(fontSize: 8f, lineHeight: 10f, baseLine: 7f, spaceWidth: 5f, tabWidth: 20f);
            return new BitmapFont("CarbonPixelFallback", atlas, in metrics);
        }
        finally
        {
            atlasTex?.Dispose();
        }
    }

    private static uint[] GetGlyphRows(char ch)
    {
        if (s_glyphs.TryGetValue(ch, out uint[]? rows))
        {
            return rows;
        }

        char upper = char.ToUpperInvariant(ch);
        return s_glyphs.TryGetValue(upper, out uint[]? upperRows) ? upperRows : s_glyphs[' '];
    }

    private static readonly Dictionary<char, uint[]> s_glyphs = new()
    {
        [' '] = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
        ['!'] = [0x04, 0x04, 0x04, 0x04, 0x00, 0x04, 0x00],
        ['"'] = [0x0A, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00],
        ['#'] = [0x0A, 0x1F, 0x0A, 0x0A, 0x1F, 0x0A, 0x00],
        ['$'] = [0x04, 0x0F, 0x14, 0x0E, 0x05, 0x1E, 0x04],
        ['%'] = [0x18, 0x19, 0x02, 0x04, 0x08, 0x13, 0x03],
        ['&'] = [0x0C, 0x12, 0x14, 0x08, 0x15, 0x12, 0x0D],
        ['\''] = [0x04, 0x04, 0x08, 0x00, 0x00, 0x00, 0x00],
        ['('] = [0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02],
        [')'] = [0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08],
        ['*'] = [0x00, 0x04, 0x15, 0x0E, 0x15, 0x04, 0x00],
        ['+'] = [0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00],
        [','] = [0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x08],
        ['-'] = [0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00],
        ['.'] = [0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x00],
        ['/'] = [0x01, 0x02, 0x02, 0x04, 0x08, 0x08, 0x10],
        ['0'] = [0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E],
        ['1'] = [0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E],
        ['2'] = [0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F],
        ['3'] = [0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E],
        ['4'] = [0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02],
        ['5'] = [0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E],
        ['6'] = [0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E],
        ['7'] = [0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08],
        ['8'] = [0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E],
        ['9'] = [0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C],
        [':'] = [0x00, 0x04, 0x00, 0x00, 0x04, 0x00, 0x00],
        ['<'] = [0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02],
        ['='] = [0x00, 0x00, 0x1F, 0x00, 0x1F, 0x00, 0x00],
        ['>'] = [0x08, 0x04, 0x02, 0x01, 0x02, 0x04, 0x08],
        ['?'] = [0x0E, 0x11, 0x01, 0x02, 0x04, 0x00, 0x04],
        ['@'] = [0x0E, 0x11, 0x17, 0x15, 0x17, 0x10, 0x0E],
        ['['] = [0x0E, 0x08, 0x08, 0x08, 0x08, 0x08, 0x0E],
        ['\\'] = [0x10, 0x08, 0x08, 0x04, 0x02, 0x02, 0x01],
        [']'] = [0x0E, 0x02, 0x02, 0x02, 0x02, 0x02, 0x0E],
        ['_'] = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F],
        ['|'] = [0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04],
        ['A'] = [0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11],
        ['B'] = [0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E],
        ['C'] = [0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E],
        ['D'] = [0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C],
        ['E'] = [0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F],
        ['F'] = [0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10],
        ['G'] = [0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F],
        ['H'] = [0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11],
        ['I'] = [0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E],
        ['J'] = [0x07, 0x02, 0x02, 0x02, 0x12, 0x12, 0x0C],
        ['K'] = [0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11],
        ['L'] = [0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F],
        ['M'] = [0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11],
        ['N'] = [0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11],
        ['O'] = [0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E],
        ['P'] = [0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10],
        ['Q'] = [0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D],
        ['R'] = [0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11],
        ['S'] = [0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E],
        ['T'] = [0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04],
        ['U'] = [0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E],
        ['V'] = [0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04],
        ['W'] = [0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11],
        ['X'] = [0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11],
        ['Y'] = [0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04],
        ['Z'] = [0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F],
        ['a'] = [0x00, 0x00, 0x0E, 0x01, 0x0F, 0x11, 0x0F],
        ['b'] = [0x10, 0x10, 0x1E, 0x11, 0x11, 0x11, 0x1E],
        ['c'] = [0x00, 0x00, 0x0E, 0x11, 0x10, 0x11, 0x0E],
        ['d'] = [0x01, 0x01, 0x0F, 0x11, 0x11, 0x11, 0x0F],
        ['e'] = [0x00, 0x00, 0x0E, 0x11, 0x1F, 0x10, 0x0E],
        ['f'] = [0x06, 0x08, 0x1E, 0x08, 0x08, 0x08, 0x08],
        ['g'] = [0x00, 0x0F, 0x11, 0x11, 0x0F, 0x01, 0x0E],
        ['h'] = [0x10, 0x10, 0x1E, 0x11, 0x11, 0x11, 0x11],
        ['i'] = [0x04, 0x00, 0x0C, 0x04, 0x04, 0x04, 0x0E],
        ['j'] = [0x02, 0x00, 0x06, 0x02, 0x02, 0x12, 0x0C],
        ['k'] = [0x10, 0x10, 0x12, 0x14, 0x18, 0x14, 0x12],
        ['l'] = [0x0C, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E],
        ['m'] = [0x00, 0x00, 0x1A, 0x15, 0x15, 0x11, 0x11],
        ['n'] = [0x00, 0x00, 0x1E, 0x11, 0x11, 0x11, 0x11],
        ['o'] = [0x00, 0x00, 0x0E, 0x11, 0x11, 0x11, 0x0E],
        ['p'] = [0x00, 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10],
        ['q'] = [0x00, 0x0F, 0x11, 0x11, 0x0F, 0x01, 0x01],
        ['r'] = [0x00, 0x00, 0x16, 0x19, 0x10, 0x10, 0x10],
        ['s'] = [0x00, 0x0F, 0x10, 0x0E, 0x01, 0x1E, 0x00],
        ['t'] = [0x08, 0x08, 0x1E, 0x08, 0x08, 0x09, 0x06],
        ['u'] = [0x00, 0x00, 0x11, 0x11, 0x11, 0x11, 0x0F],
        ['v'] = [0x00, 0x00, 0x11, 0x11, 0x11, 0x0A, 0x04],
        ['w'] = [0x00, 0x00, 0x11, 0x11, 0x15, 0x15, 0x0A],
        ['x'] = [0x00, 0x00, 0x11, 0x0A, 0x04, 0x0A, 0x11],
        ['y'] = [0x00, 0x11, 0x11, 0x0F, 0x01, 0x12, 0x0C],
        ['z'] = [0x00, 0x00, 0x1F, 0x02, 0x04, 0x08, 0x1F],
    };
}
