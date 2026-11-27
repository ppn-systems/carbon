// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Hero combo step (<c>docs/art/combat-animation.md</c> §3.3): consecutive swings of the same action kind within
/// <see cref="WindowSeconds"/> of each other advance the step (jab, hook; forehand, backhand, thrust); a pause or a
/// different kind starts over at step 0. Visual only: the server resolves every swing the same way.
/// </summary>
public struct AttackComboCounter
{
    /// <summary>Longest pause between swings that keeps the combo going.</summary>
    public const float WindowSeconds = 0.9f;

    private ActionAnimationKind _lastKind;
    private float _lastStart;
    private byte _step;
    private bool _started;

    /// <summary>Registers a swing of <paramref name="kind"/> starting at <paramref name="nowSeconds"/>.</summary>
    /// <param name="kind">Action kind of the swing.</param>
    /// <param name="nowSeconds">Monotonic client time in seconds.</param>
    /// <returns>The combo step (0-based) of this swing.</returns>
    public byte Next(ActionAnimationKind kind, float nowSeconds)
    {
        bool continues = _started && kind == _lastKind && nowSeconds - _lastStart <= WindowSeconds && nowSeconds >= _lastStart;
        _step = continues ? (byte)((_step + 1) % 6) : (byte)0;
        _lastKind = kind;
        _lastStart = nowSeconds;
        _started = true;
        return _step;
    }

    /// <summary>Forgets the running combo (respawn, reconnect).</summary>
    public void Reset() => this = default;
}
