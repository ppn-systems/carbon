// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Common;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering.Effects;

/// <summary>Sound cues raised by <see cref="ForgeEnhancementVfx"/> for an audio layer (none is wired yet).</summary>
public enum ForgeSfxCue : byte
{
    /// <summary>The player pressed Forge: the hammer starts charging.</summary>
    ChargeStart = 0,

    /// <summary>The hammer hits the item.</summary>
    HammerStrike = 1,

    /// <summary>Success: a bright chime.</summary>
    SuccessChime = 2,

    /// <summary>No change: a dull clank.</summary>
    DullClank = 3,

    /// <summary>Downgrade: the item cracks.</summary>
    Crack = 4,

    /// <summary>Destroyed: the item shatters.</summary>
    Shatter = 5,
}

/// <summary>
/// The hammer-on-anvil animation of the forge window (ADR-020). While the server rolls, the hammer charges up with three
/// spark-throwing strikes. When the server result arrives (<c>IWorldReadModel.ForgeResultSerial</c>) the final strike
/// plays that outcome: a golden glow burst (success), a dull clank (no change), cracks and a red flash (downgrade), or
/// shatter shards (destroyed). It only displays results; it never decides them. Particles are computed from time and a
/// fixed hash, so drawing allocates nothing.
/// </summary>
public sealed class ForgeEnhancementVfx
{
    /// <summary>Seconds of one hammer swing during the charge-up.</summary>
    public const float StrikePeriod = 0.3f;

    /// <summary>Minimum charge-up before the outcome plays (three strikes).</summary>
    public const float ChargeDuration = StrikePeriod * 3f;

    /// <summary>Seconds the outcome effect lasts.</summary>
    public const float OutcomeDuration = 1.6f;

    private const float MaxWait = 6f;
    private const float SparkLife = 0.38f;
    private const float Gravity = 300f;
    private const float RaisedAngle = 4.4f;
    private const float StrikeAngle = 3.24f;
    private const float RestAngle = 4.0f;

    private static readonly ColorRgba s_gold = new(253, 224, 71, 255);
    private static readonly ColorRgba s_spark = new(255, 196, 64, 255);
    private static readonly ColorRgba s_ember = new(249, 115, 22, 255);
    private static readonly ColorRgba s_dull = new(148, 163, 184, 255);
    private static readonly ColorRgba s_red = new(239, 68, 68, 255);
    private static readonly ColorRgba s_crack = new(24, 10, 10, 255);
    private static readonly ColorRgba s_steel = new(203, 213, 225, 255);
    private static readonly ColorRgba s_handle = new(120, 72, 36, 255);

    private Phase _phase;
    private float _time;
    private EnhancementOutcome _outcome;
    private EnhancementOutcome _pending;
    private bool _hasPending;
    private uint _seenSerial;
    private bool _serialSeen;
    private bool _frozen;

    private enum Phase : byte
    {
        Idle = 0,
        Charging = 1,
        Outcome = 2,
    }

    /// <summary>Raised on every sound beat (charge start, each strike, the outcome).</summary>
    public event Action<ForgeSfxCue>? SfxCue;

    /// <summary>Gets whether the hammer is charging or an outcome is playing.</summary>
    public bool IsActive => _phase != Phase.Idle;

    /// <summary>Gets whether the item icon must be hidden (it shattered).</summary>
    public bool HidesItem => _phase == Phase.Outcome && _outcome == EnhancementOutcome.Destroyed && _time > 0.04f;

    /// <summary>Gets whether the effect is a screenshot preview rather than a server result.</summary>
    public bool IsPreview { get; private set; }

    /// <summary>Gets the outcome being played, or <see cref="EnhancementOutcome.None"/>.</summary>
    public EnhancementOutcome PlayingOutcome => _phase == Phase.Outcome ? _outcome : EnhancementOutcome.None;

    /// <summary>Starts the charge-up when the client sends an attempt.</summary>
    public void BeginCharge()
    {
        _phase = Phase.Charging;
        _time = 0f;
        _hasPending = false;
        _frozen = false;
        this.IsPreview = false;
        this.SfxCue?.Invoke(ForgeSfxCue.ChargeStart);
    }

    /// <summary>
    /// Shows one frame of the effect for screenshot review (<c>CARBON_FORGE_PREVIEW</c>): the charge-up when
    /// <paramref name="outcome"/> is <see cref="EnhancementOutcome.None"/>, otherwise that outcome, at
    /// <paramref name="seconds"/>. With <paramref name="freeze"/> the time stays fixed. Visual only; nothing is sent.
    /// </summary>
    /// <param name="outcome">Outcome to show, or none for the charge-up.</param>
    /// <param name="seconds">Time into the phase.</param>
    /// <param name="freeze">Whether time stays fixed.</param>
    public void Preview(EnhancementOutcome outcome, float seconds, bool freeze)
    {
        _phase = outcome == EnhancementOutcome.None ? Phase.Charging : Phase.Outcome;
        _outcome = outcome;
        _time = MathF.Max(0f, seconds);
        _frozen = freeze;
        _hasPending = false;
        this.IsPreview = true;
    }

    /// <summary>Advances the effect and picks up a new server result.</summary>
    /// <param name="dt">Frame seconds.</param>
    /// <param name="resultSerial">The session's forge result counter.</param>
    /// <param name="latestOutcome">The session's latest forge outcome.</param>
    public void Update(float dt, uint resultSerial, EnhancementOutcome latestOutcome)
    {
        if (!_serialSeen)
        {
            _seenSerial = resultSerial;
            _serialSeen = true;
        }
        else if (resultSerial != _seenSerial)
        {
            _seenSerial = resultSerial;
            if (_phase == Phase.Charging && _time < ChargeDuration)
            {
                _pending = latestOutcome;
                _hasPending = true;
            }
            else
            {
                this.StartOutcome(latestOutcome);
            }
        }

        if (_frozen || _phase == Phase.Idle)
        {
            return;
        }

        float previous = _time;
        _time += MathF.Max(0f, dt);
        if (_phase == Phase.Charging)
        {
            if ((int)(_time / StrikePeriod) > (int)(previous / StrikePeriod))
            {
                this.SfxCue?.Invoke(ForgeSfxCue.HammerStrike);
            }

            if (_hasPending && _time >= ChargeDuration)
            {
                this.StartOutcome(_pending);
            }
            else if (_time > MaxWait)
            {
                _phase = Phase.Idle;
            }
        }
        else if (_time >= OutcomeDuration)
        {
            _phase = Phase.Idle;
        }
    }

    /// <summary>Returns the item icon offset (shake on a failure, a dip on each strike).</summary>
    /// <returns>Offset in pixels.</returns>
    public Vector2 GetItemOffset()
    {
        if (_phase == Phase.Charging)
        {
            float age = _time % StrikePeriod;
            return age < 0.08f && _time >= StrikePeriod ? new Vector2(0f, 2f * (1f - (age / 0.08f))) : Vector2.Zero;
        }

        if (_phase == Phase.Outcome && _time < 0.45f &&
            _outcome is EnhancementOutcome.NoChange or EnhancementOutcome.Downgrade)
        {
            float amp = (_outcome == EnhancementOutcome.Downgrade ? 4f : 2.5f) * (1f - (_time / 0.45f));
            return new Vector2(MathF.Sin(_time * 70f) * amp, 0f);
        }

        return Vector2.Zero;
    }

    /// <summary>Draws what goes behind the item: charge glow, success burst, red flash.</summary>
    /// <param name="batch">Geometry batch.</param>
    /// <param name="center">Item center on screen.</param>
    /// <param name="stage">The anvil stage box (flash area).</param>
    public void DrawBehind(GeometryBatch batch, Vector2 center, RectangleF stage)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_phase == Phase.Charging)
        {
            float p = MathF.Min(1f, _time / ChargeDuration);
            float pulse = 0.85f + (0.15f * MathF.Sin(_time * 18f));
            batch.FillCircle(center, (14f + (14f * p)) * pulse, WithAlpha(s_ember, 40 + (int)(110 * p)), 28);
            batch.FillCircle(center, (8f + (8f * p)) * pulse, WithAlpha(s_gold, 30 + (int)(120 * p)), 24);
            return;
        }

        if (_phase != Phase.Outcome)
        {
            return;
        }

        float t = _time;
        switch (_outcome)
        {
            case EnhancementOutcome.Success:
                {
                    float e = EaseOut(MathF.Min(1f, t / 0.6f));
                    float fade = 1f - MathF.Min(1f, t / OutcomeDuration);
                    batch.FillCircle(center, 18f + (30f * e), WithAlpha(s_gold, (int)(150 * fade)), 36);
                    batch.FillCircle(center, 10f + (12f * e), WithAlpha(new ColorRgba(255, 251, 235, 255), (int)(200 * fade)), 28);
                    for (int i = 0; i < 12; i++)
                    {
                        float a = (i * (MathF.Tau / 12f)) + (t * 0.8f);
                        Vector2 dir = new(MathF.Cos(a), MathF.Sin(a));
                        float inner = 12f + (8f * e);
                        float outer = inner + 10f + (30f * e * (0.6f + (0.4f * Hash(i))));
                        batch.DrawLine(center + (dir * inner), center + (dir * outer), WithAlpha(s_gold, (int)(210 * fade)), 2.5f);
                    }

                    break;
                }

            case EnhancementOutcome.Downgrade:
                {
                    float flash = MathF.Max(0f, 1f - (t / 0.55f));
                    batch.FillRectangle(stage, WithAlpha(s_red, (int)(170 * flash)));
                    break;
                }

            case EnhancementOutcome.Destroyed:
                {
                    float flash = MathF.Max(0f, 1f - (t / 0.7f));
                    batch.FillRectangle(stage, WithAlpha(new ColorRgba(127, 29, 29, 255), (int)(190 * flash)));
                    float burst = MathF.Max(0f, 1f - (t / 0.22f));
                    batch.FillCircle(center, 8f + (26f * EaseOut(MathF.Min(1f, t / 0.22f))), WithAlpha(new ColorRgba(254, 226, 226, 255), (int)(230 * burst)), 32);
                    break;
                }

            case EnhancementOutcome.NoChange:
            case EnhancementOutcome.None:
            default:
                break;
        }
    }

    /// <summary>Draws the hammer, sparks and outcome particles in front of the item.</summary>
    /// <param name="batch">Geometry batch.</param>
    /// <param name="center">Item center on screen.</param>
    /// <param name="itemAccent">Accent color of the item (shard tint).</param>
    public void DrawFront(GeometryBatch batch, Vector2 center, ColorRgba itemAccent)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Vector2 impact = center + new Vector2(4f, -16f);
        if (_phase == Phase.Charging)
        {
            int strikes = (int)(_time / StrikePeriod);
            if (strikes > 0)
            {
                DrawSparks(batch, impact, _time - (strikes * StrikePeriod), seed: strikes * 17, count: 9, speed: 1f, s_spark);
            }
        }
        else if (_phase == Phase.Outcome)
        {
            this.DrawOutcomeFront(batch, center, impact, itemAccent);
        }

        DrawHammer(batch, center, this.HammerAngle());
    }

    private void DrawOutcomeFront(GeometryBatch batch, Vector2 center, Vector2 impact, ColorRgba itemAccent)
    {
        float t = _time;
        float fade = 1f - MathF.Min(1f, t / OutcomeDuration);
        switch (_outcome)
        {
            case EnhancementOutcome.Success:
                DrawSparks(batch, impact, t, seed: 101, count: 14, speed: 1.35f, s_gold);
                batch.DrawCircle(center, 12f + (40f * EaseOut(MathF.Min(1f, t / 0.8f))), WithAlpha(s_gold, (int)(230 * fade)), 40, 3f);
                for (int i = 0; i < 16; i++)
                {
                    float life = (t * 0.9f) + Hash(i + 40);
                    float k = life - MathF.Floor(life);
                    Vector2 pos = center + new Vector2(((Hash(i + 60) - 0.5f) * 56f) + (MathF.Sin((t * 4f) + i) * 4f), 18f - (k * 70f));
                    batch.FillCircle(pos, 1.6f + (Hash(i + 80) * 1.6f), WithAlpha(i % 3 == 0 ? new ColorRgba(255, 255, 255, 255) : s_gold, (int)(240 * fade * (1f - k))), 8);
                }

                break;

            case EnhancementOutcome.NoChange:
                DrawSparks(batch, impact, t, seed: 202, count: 5, speed: 0.55f, s_dull);
                batch.DrawCircle(impact, 6f + (20f * EaseOut(MathF.Min(1f, t / 0.35f))), WithAlpha(s_dull, (int)(200 * MathF.Max(0f, 1f - (t / 0.5f)))), 24, 2f);
                for (int i = 0; i < 6; i++)
                {
                    float a = MathF.PI + (i * (MathF.PI / 5f));
                    Vector2 pos = impact + (new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.4f) * (8f + (26f * EaseOut(MathF.Min(1f, t / 0.9f)))));
                    batch.FillCircle(pos, 3f + (5f * MathF.Min(1f, t / 0.9f)), WithAlpha(new ColorRgba(100, 116, 139, 255), (int)(120 * fade)), 12);
                }

                break;

            case EnhancementOutcome.Downgrade:
                DrawSparks(batch, impact, t, seed: 303, count: 10, speed: 1f, s_red);
                DrawCracks(batch, center, MathF.Min(1f, t / 0.3f), (int)(255 * MathF.Min(1f, fade * 1.6f)));
                break;

            case EnhancementOutcome.Destroyed:
                DrawShards(batch, center, t, itemAccent, fade);
                break;

            case EnhancementOutcome.None:
            default:
                break;
        }
    }

    private float HammerAngle()
    {
        if (_phase == Phase.Charging)
        {
            float u = _time % StrikePeriod / StrikePeriod;
            return u < 0.7f
                ? Lerp(StrikeAngle, RaisedAngle, EaseOut(u / 0.7f))
                : Lerp(RaisedAngle, StrikeAngle, (u - 0.7f) / 0.3f);
        }

        if (_phase == Phase.Outcome)
        {
            if (_time < 0.12f)
            {
                return StrikeAngle;
            }

            float back = MathF.Min(1f, (_time - 0.12f) / 0.45f);
            float bounce = _outcome == EnhancementOutcome.NoChange ? MathF.Sin(back * MathF.PI) * 0.35f : 0f;
            return Lerp(StrikeAngle, RestAngle, EaseOut(back)) + bounce;
        }

        return RestAngle;
    }

    private void StartOutcome(EnhancementOutcome outcome)
    {
        _phase = Phase.Outcome;
        _outcome = outcome;
        _time = 0f;
        _hasPending = false;
        this.SfxCue?.Invoke(ForgeSfxCue.HammerStrike);
        this.SfxCue?.Invoke(outcome switch
        {
            EnhancementOutcome.Success => ForgeSfxCue.SuccessChime,
            EnhancementOutcome.Downgrade => ForgeSfxCue.Crack,
            EnhancementOutcome.Destroyed => ForgeSfxCue.Shatter,
            EnhancementOutcome.NoChange or EnhancementOutcome.None => ForgeSfxCue.DullClank,
            _ => ForgeSfxCue.DullClank,
        });
    }

    private static void DrawHammer(GeometryBatch batch, Vector2 center, float angle)
    {
        Vector2 pivot = center + new Vector2(46f, -14f);
        Vector2 dir = new(MathF.Cos(angle), MathF.Sin(angle));
        Vector2 head = pivot + (dir * 42f);
        Vector2 perp = new(-dir.Y, dir.X);
        batch.DrawLine(pivot - (dir * 6f), head, new ColorRgba(30, 16, 8, 255), 5.5f);
        batch.DrawLine(pivot - (dir * 6f), head, s_handle, 3.5f);

        Span<Vector2> quad =
        [
            head + (perp * 8f) - (dir * 5f),
            head + (perp * 8f) + (dir * 5f),
            head - (perp * 9f) + (dir * 5f),
            head - (perp * 9f) - (dir * 5f),
        ];
        batch.FillConvexPolygon(quad, s_steel);
        for (int i = 0; i < 4; i++)
        {
            batch.DrawLine(quad[i], quad[(i + 1) % 4], new ColorRgba(15, 23, 42, 255), 1.2f);
        }

        batch.DrawLine(quad[0] + (dir * 1.5f), quad[1] - (dir * 1.5f), new ColorRgba(248, 250, 252, 255), 1.2f);
    }

    private static void DrawSparks(GeometryBatch batch, Vector2 origin, float age, int seed, int count, float speed, ColorRgba color)
    {
        if (age < 0f || age > SparkLife * 1.6f)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            float life = SparkLife * (0.7f + (0.6f * Hash(seed + (i * 3))));
            if (age > life)
            {
                continue;
            }

            float a = (-MathF.PI / 2f) + ((Hash(seed + (i * 3) + 1) - 0.5f) * 2.6f);
            float v = (70f + (110f * Hash(seed + (i * 3) + 2))) * speed;
            Vector2 vel = new(MathF.Cos(a) * v, (MathF.Sin(a) * v) + (Gravity * age));
            Vector2 pos = origin + new Vector2(MathF.Cos(a) * v * age, (MathF.Sin(a) * v * age) + (0.5f * Gravity * age * age));
            float k = 1f - (age / life);
            batch.DrawLine(pos, pos - (vel * 0.025f), WithAlpha(i % 2 == 0 ? color : s_ember, (int)(255 * k)), 2f);
        }
    }

    private static void DrawCracks(GeometryBatch batch, Vector2 center, float grow, int alpha)
    {
        // Three jagged cracks across the item, revealed segment by segment.
        for (int c = 0; c < 3; c++)
        {
            float a = -1.9f + (c * 1.55f) + (Hash(c + 7) * 0.4f);
            Vector2 p = center + new Vector2(-1f, -2f);
            for (int s = 0; s < 4; s++)
            {
                float segStart = s / 4f;
                if (grow <= segStart)
                {
                    break;
                }

                float len = (8f + (4f * Hash((c * 9) + s))) * MathF.Min(1f, (grow - segStart) * 4f);
                a += (Hash((c * 13) + s + 3) - 0.5f) * 1.2f;
                Vector2 q = p + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * len);
                batch.DrawLine(p, q, WithAlpha(new ColorRgba(255, 237, 213, 255), alpha), 4.6f);
                batch.DrawLine(p, q, WithAlpha(s_crack, alpha), 2.2f);
                p = q;
            }
        }
    }

    private static void DrawShards(GeometryBatch batch, Vector2 center, float t, ColorRgba accent, float fade)
    {
        for (int i = 0; i < 18; i++)
        {
            float a = (i * (MathF.Tau / 18f)) + ((Hash(i + 200) - 0.5f) * 0.5f);
            float v = 60f + (90f * Hash(i + 220));
            Vector2 pos = center + new Vector2(MathF.Cos(a) * v * t, (MathF.Sin(a) * v * t) - (40f * t) + (0.5f * Gravity * t * t));
            float spin = (Hash(i + 240) - 0.5f) * 18f * t;
            float size = 5f + (6f * Hash(i + 260));
            Vector2 d0 = new(MathF.Cos(spin), MathF.Sin(spin));
            Vector2 d1 = new(MathF.Cos(spin + 2.3f), MathF.Sin(spin + 2.3f));
            Vector2 d2 = new(MathF.Cos(spin + 4.1f), MathF.Sin(spin + 4.1f));
            ColorRgba tint = i % 3 == 0 ? accent : (i % 3 == 1 ? s_steel : new ColorRgba(100, 116, 139, 255));
            int shardAlpha = (int)(255 * MathF.Min(1f, fade * 1.5f));
            Vector2 a0 = pos + (d0 * size);
            Vector2 a1 = pos + (d1 * size * 0.8f);
            Vector2 a2 = pos + (d2 * size * 0.6f);
            batch.FillTriangle(a0, a1, a2, WithAlpha(tint, shardAlpha));
            batch.DrawLine(a0, a1, WithAlpha(new ColorRgba(248, 250, 252, 255), shardAlpha), 1f);
        }

        for (int i = 0; i < 5; i++)
        {
            float k = MathF.Min(1f, t / 1.2f);
            Vector2 pos = center + new Vector2((Hash(i + 300) - 0.5f) * 30f, -8f - (24f * k * (0.5f + Hash(i + 310))));
            batch.FillCircle(pos, 5f + (12f * k), WithAlpha(new ColorRgba(30, 30, 36, 255), (int)(110 * fade)), 14);
        }
    }

    private static float Hash(int i)
    {
        float x = MathF.Sin(i * 12.9898f) * 43758.5453f;
        return x - MathF.Floor(x);
    }

    private static float EaseOut(float x) => 1f - ((1f - x) * (1f - x));

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static ColorRgba WithAlpha(ColorRgba color, int alpha) =>
        color with { A = (byte)Math.Clamp(alpha, 0, 255) };
}
