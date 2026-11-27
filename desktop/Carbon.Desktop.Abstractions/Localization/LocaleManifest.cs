// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// <c>resources/localization/locales.json</c>: the selectable locales and their font stacks (ADR-020 D1, D5).
/// </summary>
public sealed class LocaleManifest
{
    /// <summary>Gets or sets the default locale code.</summary>
    public string DefaultLocale { get; set; } = "en";

    /// <summary>Gets or sets the selectable locales in display order.</summary>
    public List<LocaleInfo> Locales { get; set; } = [];
}
