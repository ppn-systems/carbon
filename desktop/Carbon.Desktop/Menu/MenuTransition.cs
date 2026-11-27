// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Menu;

/// <summary>
/// How a menu panel arrives (docs/art/menu-ui.md rule 16). Screens change with no fade to black: the backdrop and the
/// title stay put, the new panel slides up <see cref="SlidePixels"/> and its contents rise out of the panel body over
/// <see cref="Seconds"/>, easing out (decelerating). With motion off it is there at once. Input is never blocked.
/// </summary>
internal sealed class MenuTransition
{
    /// <summary>Duration of the panel's entrance.</summary>
    public const float Seconds = 0.22f;

    /// <summary>How far below its place the panel starts, in pixels.</summary>
    public const float SlidePixels = 14f;

    private readonly bool _animate;
    private float _elapsed;

    public MenuTransition(bool animate) => _animate = animate;

    /// <summary>Gets the linear progress, 0 (just entered) to 1 (settled).</summary>
    public float Progress => _animate ? MathF.Min(1f, _elapsed / Seconds) : 1f;

    /// <summary>Gets the eased progress (cubic ease-out).</summary>
    public float Eased
    {
        get
        {
            float inverse = 1f - this.Progress;
            return 1f - (inverse * inverse * inverse);
        }
    }

    /// <summary>Gets the panel's vertical offset in pixels (down while arriving).</summary>
    public float Offset => MathF.Round((1f - this.Eased) * SlidePixels);

    /// <summary>Gets the opacity of the body-coloured veil over the panel's contents (1 hides them, 0 shows them).</summary>
    public float Veil => 1f - this.Eased;

    /// <summary>Gets whether the entrance has finished.</summary>
    public bool IsDone => this.Progress >= 1f;

    /// <summary>Advances the entrance by <paramref name="dt"/> seconds.</summary>
    public void Advance(float dt)
    {
        if (dt > 0f)
        {
            _elapsed += dt;
        }
    }

    /// <summary>Starts the entrance again (the scene entered).</summary>
    public void Restart() => _elapsed = 0f;
}
