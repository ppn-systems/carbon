namespace Carbon.Graphics.Animation;

/// <summary>
/// Defines the playback mode for 2D/2.5D sprite animations.
/// </summary>
public enum AnimationPlaybackMode
{
    /// <summary>
    /// Plays the animation once and stops on the last frame.
    /// </summary>
    Once = 0,

    /// <summary>
    /// Loops the animation continuously from start to end.
    /// </summary>
    Loop = 1,

    /// <summary>
    /// Plays forward to the end, then reverses back to the start.
    /// </summary>
    PingPong = 2
}
