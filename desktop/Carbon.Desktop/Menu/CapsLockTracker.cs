// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Menu;

/// <summary>
/// Infers the caps-lock state from typed letters, because the window layer reports keys and characters but not the
/// lock state. An upper-case letter typed without Shift (or a lower-case one with Shift) means caps lock is on; the
/// opposite means it is off; pressing Caps Lock flips a known state. Until a letter is typed the state is unknown and
/// no warning shows. Only the case of the last letter is kept, never the letter.
/// </summary>
internal sealed class CapsLockTracker
{
    private bool? _on;

    /// <summary>Gets whether caps lock is known to be on.</summary>
    public bool IsOn => _on == true;

    /// <summary>Gets whether a letter has been seen since the last reset.</summary>
    public bool IsKnown => _on.HasValue;

    /// <summary>Learns from a typed character and whether Shift was held when it was typed.</summary>
    public void ObserveCharacter(char ch, bool shiftDown)
    {
        if (!char.IsLetter(ch) || char.IsUpper(ch) == char.IsLower(ch))
        {
            // Digits, symbols and letters without case say nothing about caps lock.
            return;
        }

        _on = char.IsUpper(ch) != shiftDown;
    }

    /// <summary>The Caps Lock key was pressed: flips a known state.</summary>
    public void ObserveCapsLockKey()
    {
        if (_on is bool known)
        {
            _on = !known;
        }
    }

    /// <summary>Forgets the state (e.g. when the window loses focus).</summary>
    public void Reset() => _on = null;
}
