// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>The lines the death screen draws (ADR-031), localized: the title, the killer or cause, the button and the Enter hint.</summary>
/// <param name="Title">"You died".</param>
/// <param name="Cause">The killer's name or the cause of death.</param>
/// <param name="Button">The Respawn button label (or "Respawning..." while the request is pending).</param>
/// <param name="Hint">The hint under the panel.</param>
internal readonly record struct DeathScreenText(string Title, string Cause, string Button, string Hint);
