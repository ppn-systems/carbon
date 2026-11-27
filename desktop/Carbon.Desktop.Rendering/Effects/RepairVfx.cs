// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.Effects;

/// <summary>
/// The hammer-on-anvil animation of the repair window (ADR-028), in the forge animation's style
/// (<see cref="ForgeEnhancementVfx"/>): while the server checks the repair the hammer strikes and throws sparks and the
/// durability bar creeps up; when the server's answer arrives (<c>IRepairSession.ResultSerial</c>) a success fills the bar
/// to its new value and flashes, and a refusal clanks and drains the bar back. It only shows the answer; it never
/// decides it. Drawing allocates nothing.
/// </summary>
public sealed class RepairVfx
{
    /// <summary>Seconds of one hammer swing.</summary>
    public const float StrikePeriod = ForgeEnhancementVfx.StrikePeriod;

    /// <summary>Minimum work before the answer plays (three strikes).</summary>
    public const float WorkDuration = StrikePeriod * 3f;

    /// <summary>Seconds the success or refusal effect lasts.</summary>
    public const float ResultDuration = 1.2f;

    /// <summary>Share of the planned gain the bar shows while the server works.</summary>
    public const float WorkFillShare = 0.6f;

    private const float MaxWait = 6f;
    private const float FillTime = 0.45f;
    private const float RaisedAngle = 4.4f;
    private const float StrikeAngle = 3.24f;
    private const float RestAngle = 4.0f;

    private static readonly ColorRgba s_spark = new(255, 196, 64, 255);
    private static readonly ColorRgba s_ember = new(249, 115, 22, 255);
    private static readonly ColorRgba s_mend = new(134, 239, 172, 255);
    private static readonly ColorRgba s_flash = new(240, 253, 244, 255);
    private static readonly ColorRgba s_dull = new(148, 163, 184, 255);

    private Phase _phase;
    private float _time;
    private float _from;
    private float _to;
    private bool _succeeded;
    private bool _hasPending;
    private bool _pendingSuccess;
    private uint _seenSerial;
    private bool _serialSeen;
    private bool _frozen;

    private enum Phase : byte
    {
        Idle = 0,
        Working = 1,
        Result = 2,
    }

    /// <summary>Gets whether the hammer is working or a result is playing.</summary>
    public bool IsActive => _phase != Phase.Idle;

    /// <summary>Gets whether the hammer is still waiting for the server.</summary>
    public bool IsWorking => _phase == Phase.Working;

    /// <summary>Gets whether the effect is a screenshot preview rather than a server answer.</summary>
    public bool IsPreview { get; private set; }

    /// <summary>Gets whether a success is playing.</summary>
    public bool IsPlayingSuccess => _phase == Phase.Result && _succeeded;

    /// <summary>Starts the work when the client sends a repair.</summary>
    /// <param name="fromFraction">Durability share before the repair (<c>0..1</c>).</param>
    /// <param name="toFraction">Durability share the repair would reach.</param>
    public void Begin(float fromFraction, float toFraction)
    {
        _phase = Phase.Working;
        _time = 0f;
        _from = Math.Clamp(fromFraction, 0f, 1f);
        _to = Math.Clamp(toFraction, _from, 1f);
        _hasPending = false;
        _frozen = false;
        this.IsPreview = false;
    }

    /// <summary>
    /// Shows one frame for screenshot review (<c>CARBON_REPAIR_PREVIEW</c>): the work, or a success when
    /// <paramref name="success"/>, at <paramref name="seconds"/>. Visual only; nothing is sent.
    /// </summary>
    /// <param name="success">Whether to show the success instead of the work.</param>
    /// <param name="fromFraction">Durability share before.</param>
    /// <param name="toFraction">Durability share after.</param>
    /// <param name="seconds">Time into the phase.</param>
    /// <param name="freeze">Whether time stays fixed.</param>
    public void Preview(bool success, float fromFraction, float toFraction, float seconds, bool freeze)
    {
        this.Begin(fromFraction, toFraction);
        _phase = success ? Phase.Result : Phase.Working;
        _succeeded = success;
        _time = MathF.Max(0f, seconds);
        _frozen = freeze;
        this.IsPreview = true;
    }

    /// <summary>Advances the effect and picks up a new server answer.</summary>
    /// <param name="dt">Frame seconds.</param>
    /// <param name="resultSerial">The repair session's answer counter.</param>
    /// <param name="lastSucceeded">Whether the latest answer was a success.</param>
    public void Update(float dt, uint resultSerial, bool lastSucceeded)
    {
        if (!_serialSeen)
        {
            _seenSerial = resultSerial;
            _serialSeen = true;
        }
        else if (resultSerial != _seenSerial)
        {
            _seenSerial = resultSerial;
            if (_phase == Phase.Working && _time < WorkDuration)
            {
                _pendingSuccess = lastSucceeded;
                _hasPending = true;
            }
            else if (_phase == Phase.Working)
            {
                this.StartResult(lastSucceeded);
            }
        }

        if (_frozen || _phase == Phase.Idle)
        {
            return;
        }

        _time += MathF.Max(0f, dt);
        if (_phase == Phase.Working)
        {
            if (_hasPending && _time >= WorkDuration)
            {
                this.StartResult(_pendingSuccess);
            }
            else if (_time > MaxWait)
            {
                _phase = Phase.Idle;
            }
        }
        else if (_time >= ResultDuration)
        {
            _phase = Phase.Idle;
        }
    }

    /// <summary>
    /// Returns the durability share the bar shows: <paramref name="idleFraction"/> when idle, creeping toward the planned
    /// value while working, filled to it on success, and drained back on a refusal.
    /// </summary>
    /// <param name="idleFraction">The item's current durability share.</param>
    /// <returns>The share to draw (<c>0..1</c>).</returns>
    public float GetBarFraction(float idleFraction)
    {
        float work = _from + ((_to - _from) * WorkFillShare);
        return _phase switch
        {
            Phase.Working => Lerp(_from, work, EaseOut(MathF.Min(1f, _time / WorkDuration))),
            Phase.Result when _succeeded => Lerp(work, _to, EaseOut(MathF.Min(1f, _time / FillTime))),
            Phase.Result => Lerp(work, _from, EaseOut(MathF.Min(1f, _time / FillTime))),
            Phase.Idle => idleFraction,
            _ => idleFraction,
        };
    }

    /// <summary>Gets the success flash strength (<c>0..1</c>) for the bar and the stage.</summary>
    public float FlashStrength => _phase == Phase.Result && _succeeded && _time >= FillTime * 0.6f
        ? MathF.Max(0f, 1f - ((_time - (FillTime * 0.6f)) / 0.55f))
        : 0f;

    /// <summary>Draws the glow behind the item: a warm glow while working, a green mend glow on success.</summary>
    /// <param name="batch">Geometry batch.</param>
    /// <param name="center">Item center on screen.</param>
    /// <param name="stage">The anvil stage box.</param>
    public void DrawBehind(GeometryBatch batch, Vector2 center, RectangleF stage)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_phase == Phase.Working)
        {
            float p = MathF.Min(1f, _time / WorkDuration);
            float pulse = 0.85f + (0.15f * MathF.Sin(_time * 18f));
            batch.FillCircle(center, (12f + (10f * p)) * pulse, WithAlpha(s_ember, 40 + (int)(90 * p)), 28);
            return;
        }

        if (_phase == Phase.Result && _succeeded)
        {
            float fade = 1f - MathF.Min(1f, _time / ResultDuration);
            float e = EaseOut(MathF.Min(1f, _time / 0.5f));
            batch.FillCircle(center, 16f + (22f * e), WithAlpha(s_mend, (int)(80 * fade)), 32);
            batch.FillRectangle(stage, WithAlpha(s_flash, (int)(110 * this.FlashStrength)));
        }
    }

    /// <summary>Draws the hammer and the sparks of each strike in front of the item.</summary>
    /// <param name="batch">Geometry batch.</param>
    /// <param name="center">Item center on screen.</param>
    public void DrawFront(GeometryBatch batch, Vector2 center)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Vector2 impact = center + new Vector2(4f, -16f);
        if (_phase == Phase.Working)
        {
            int strikes = (int)(_time / StrikePeriod);
            if (strikes > 0)
            {
                ForgeEnhancementVfx.DrawSparks(batch, impact, _time - (strikes * StrikePeriod), seed: (strikes * 23) + 5, count: 8, speed: 0.9f, s_spark);
            }
        }
        else if (_phase == Phase.Result)
        {
            ForgeEnhancementVfx.DrawSparks(batch, impact, _time, seed: _succeeded ? 404 : 505, count: _succeeded ? 12 : 5, speed: _succeeded ? 1.2f : 0.55f, _succeeded ? s_mend : s_dull);
            if (_succeeded)
            {
                float fade = 1f - MathF.Min(1f, _time / ResultDuration);
                batch.DrawCircle(center, 10f + (34f * EaseOut(MathF.Min(1f, _time / 0.7f))), WithAlpha(s_mend, (int)(220 * fade)), 36, 2.5f);
            }
        }

        ForgeEnhancementVfx.DrawHammer(batch, center, this.HammerAngle());
    }

    private float HammerAngle()
    {
        if (_phase == Phase.Working)
        {
            float u = _time % StrikePeriod / StrikePeriod;
            return u < 0.7f
                ? Lerp(StrikeAngle, RaisedAngle, EaseOut(u / 0.7f))
                : Lerp(RaisedAngle, StrikeAngle, (u - 0.7f) / 0.3f);
        }

        if (_phase == Phase.Result)
        {
            float back = MathF.Min(1f, _time / 0.45f);
            return Lerp(StrikeAngle, RestAngle, EaseOut(back));
        }

        return RestAngle;
    }

    private void StartResult(bool succeeded)
    {
        _phase = Phase.Result;
        _succeeded = succeeded;
        _time = 0f;
        _hasPending = false;
    }

    private static float EaseOut(float x) => 1f - ((1f - x) * (1f - x));

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static ColorRgba WithAlpha(ColorRgba color, int alpha) =>
        color with { A = (byte)Math.Clamp(alpha, 0, 255) };
}
