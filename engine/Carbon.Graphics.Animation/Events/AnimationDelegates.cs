// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Animation.Controllers;

namespace Carbon.Graphics.Animation.Events;

/// <summary>
/// Delegate invoked when the active animation frame index changes during playback.
/// </summary>
/// <param name="animator">The animator triggering the event.</param>
/// <param name="frameIndex">The newly activated frame index.</param>
public delegate void FrameChangedCallback(SpriteAnimator animator, int frameIndex);

/// <summary>
/// Delegate invoked when a non-looping animation reaches its completion.
/// </summary>
/// <param name="animator">The animator triggering the event.</param>
public delegate void AnimationCompletedCallback(SpriteAnimator animator);

/// <summary>
/// Delegate invoked when a looping animation wraps from the end back to the start.
/// </summary>
/// <param name="animator">The animator triggering the event.</param>
public delegate void AnimationLoopedCallback(SpriteAnimator animator);

/// <summary>
/// Delegate invoked when an animation frame with an attached event tag becomes active.
/// </summary>
/// <param name="animator">The animator triggering the event.</param>
/// <param name="eventTag">The event tag attached to the frame.</param>
public delegate void AnimationCustomEventCallback(SpriteAnimator animator, string eventTag);
