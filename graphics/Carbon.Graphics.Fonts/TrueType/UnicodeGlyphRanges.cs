// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Graphics.Fonts.TrueType;

/// <summary>
/// Standard code-point ranges baked into UI font atlases (ADR-020 D5).
/// </summary>
public static class UnicodeGlyphRanges
{
    /// <summary>Printable Basic Latin (<c>U+0020..U+007E</c>).</summary>
    public static readonly CodePointRange PrintableAscii = new(0x20, 0x7E);

    /// <summary>Latin-1 Supplement letters and symbols (<c>U+00A0..U+00FF</c>).</summary>
    public static readonly CodePointRange Latin1Supplement = new(0xA0, 0xFF);

    /// <summary>Latin Extended-A (<c>U+0100..U+017F</c>), including <c>ă đ ĩ ũ</c>.</summary>
    public static readonly CodePointRange LatinExtendedA = new(0x100, 0x17F);

    /// <summary>Latin Extended-B (<c>U+0180..U+024F</c>), including <c>ơ ư</c>.</summary>
    public static readonly CodePointRange LatinExtendedB = new(0x180, 0x24F);

    /// <summary>Combining Diacritical Marks (<c>U+0300..U+036F</c>).</summary>
    public static readonly CodePointRange CombiningDiacriticalMarks = new(0x300, 0x36F);

    /// <summary>Latin Extended Additional (<c>U+1E00..U+1EFF</c>), including the precomposed Vietnamese tone letters.</summary>
    public static readonly CodePointRange LatinExtendedAdditional = new(0x1E00, 0x1EFF);

    /// <summary>Dashes, quotes, bullets and ellipsis from General Punctuation (<c>U+2010..U+203A</c>).</summary>
    public static readonly CodePointRange GeneralPunctuation = new(0x2010, 0x203A);

    /// <summary>Gets printable ASCII only (the legacy atlas set).</summary>
    public static IReadOnlyList<CodePointRange> Ascii { get; } = [PrintableAscii];

    /// <summary>Gets every Latin range needed by English and Vietnamese text.</summary>
    public static IReadOnlyList<CodePointRange> LatinFull { get; } =
    [
        PrintableAscii,
        Latin1Supplement,
        LatinExtendedA,
        LatinExtendedB,
        CombiningDiacriticalMarks,
        LatinExtendedAdditional,
        GeneralPunctuation,
    ];
}
