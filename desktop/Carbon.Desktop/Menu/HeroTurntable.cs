// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Animation.Directions;

namespace Carbon.Desktop.Menu;

/// <summary>
/// The character preview's turntable (docs/art/menu-ui.md rule 18): the hero faces south, west, north, east in turn. The
/// player turns it with the buttons or the arrow keys; left alone it turns a quarter every <see cref="AutoStepSeconds"/>
/// so every side is shown, and after a manual turn it waits <see cref="ResumeSeconds"/> before turning on its own again.
/// With motion off it only turns when asked.
/// </summary>
internal sealed class HeroTurntable
{
    /// <summary>Seconds between automatic quarter turns.</summary>
    public const float AutoStepSeconds = 2.5f;

    /// <summary>Seconds after a manual turn before automatic turning resumes.</summary>
    public const float ResumeSeconds = 6f;

    private static readonly Direction8[] s_facings = [Direction8.South, Direction8.West, Direction8.North, Direction8.East];

    private readonly bool _animate;
    private float _sinceManual = ResumeSeconds;
    private float _sinceStep;

    public HeroTurntable(bool animate) => _animate = animate;

    /// <summary>Gets the number of facings.</summary>
    public static int FacingCount => s_facings.Length;

    /// <summary>Gets the facing index (0 = south).</summary>
    public int Index { get; private set; }

    /// <summary>Gets the direction the hero faces.</summary>
    public Direction8 Facing => s_facings[this.Index];

    /// <summary>Turns by <paramref name="step"/> quarters (the player asked) and pauses the automatic turning.</summary>
    public void Turn(int step)
    {
        this.Index = (((this.Index + step) % s_facings.Length) + s_facings.Length) % s_facings.Length;
        _sinceManual = 0f;
        _sinceStep = 0f;
    }

    /// <summary>Advances the automatic turning by <paramref name="dt"/> seconds.</summary>
    public void Advance(float dt)
    {
        if (!_animate || !(dt > 0f))
        {
            return;
        }

        _sinceManual += dt;
        if (_sinceManual < ResumeSeconds)
        {
            return;
        }

        _sinceStep += dt;
        if (_sinceStep >= AutoStepSeconds)
        {
            _sinceStep -= AutoStepSeconds;
            this.Index = (this.Index + 1) % s_facings.Length;
        }
    }
}
