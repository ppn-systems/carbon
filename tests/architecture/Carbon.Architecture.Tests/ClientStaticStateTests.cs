// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Extends the static-mutable-state rule of <see cref="CodeHygieneTests"/> to the client (desktop/, graphics/), as the
/// first audit suggested once TS-11 was known (audit round 2, TS-11 / COUP-*).
/// </summary>
public sealed class ClientStaticStateTests
{
    /// <summary>Existing static mutable fields in desktop/ and graphics/. Entry format: <c>Namespace.Type.field</c>. Do NOT add entries.</summary>
    private static readonly HashSet<string> s_allowlist = new(StringComparer.Ordinal)
    {
        // Ambient "current" globals written on config sync / locale change and read by widgets directly.
        // Audit round 2 COUP-2: expose through IWorldReadModel / an injected ILocalizer instead.
        "Carbon.Desktop.Abstractions.Configuration.SyncedFoodCatalog.s_current",
        "Carbon.Desktop.Abstractions.Configuration.SyncedProgressionConfig.s_current",
        "Carbon.Desktop.Abstractions.Localization.GameText.s_current",

        // Lazily filled key caches (ADR-023). Audit: build once per catalog and make them readonly (FrozenDictionary).
        "Carbon.Desktop.Abstractions.Localization.GameText.s_itemHintKeys",
        "Carbon.Desktop.Abstractions.Localization.GameText.s_itemNameKeys",
        "Carbon.Desktop.Abstractions.Localization.GameText.s_monsterNameKeys",

        // Shared StringBuilder scratch; not thread-safe. Audit: [ThreadStatic] t_builder or a per-call stackalloc buffer.
        "Carbon.Desktop.Abstractions.Localization.TextTemplate.s_builder",

        // Glyph set accumulated across font loads. Audit: per-loader instance state.
        "Carbon.Desktop.Rendering.Fonts.GameFontLoader.s_glyphs",

        // Process-wide terrain bake scratch lists (first audit TS-11). Audit: instance fields on the renderer.
        "Carbon.Desktop.Rendering.World.ChunkTerrainRenderer.s_bakingRipples",
        "Carbon.Desktop.Rendering.World.ChunkTerrainRenderer.s_bakingWaterfalls",
        "Carbon.Desktop.Rendering.World.ChunkTerrainRenderer.s_roadLinks",
    };

    [Fact]
    public void DesktopAndGraphics_ShouldNotHoldStaticMutableState()
    {
        Violations.ShouldBeEmpty(
            CodeHygieneTests.FindStaticMutableFields("desktop", "graphics").Where(key => !s_allowlist.Contains(key)),
            "static mutable state is shared by every renderer and scene instance; own it on an instance (audit TS-11)");
    }

    [Fact]
    public void Allowlist_ShouldNotContainStaleEntries()
    {
        HashSet<string> found = [.. CodeHygieneTests.FindStaticMutableFields("desktop", "graphics")];
        Violations.ShouldBeEmpty(s_allowlist.Where(e => !found.Contains(e)), "fixed entries must be removed from the allowlist");
    }
}
