// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>One selectable locale in <see cref="LocaleManifest"/>.</summary>
public sealed class LocaleInfo
{
    /// <summary>Gets or sets the locale code (also the <c>&lt;code&gt;.ini</c> file name).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the language name written in that language (e.g. <c>Tiếng Việt</c>).</summary>
    public string NativeName { get; set; } = string.Empty;

    /// <summary>Gets or sets the ordered font stack (primary first) under <c>resources/fonts</c>.</summary>
    public List<LocaleFontFace> Fonts { get; set; } = [];
}
