namespace Carbon.Graphics.Batching;

/// <summary>
/// Specifies the depth sorting mode for 2D and 2.5D rendering.
/// </summary>
public enum DepthSortMode
{
    /// <summary>
    /// Objects are drawn in the order they were submitted.
    /// </summary>
    Immediate = 0,

    /// <summary>
    /// Objects are sorted back-to-front based on their explicit layer or Z depth.
    /// </summary>
    BackToFront = 1,

    /// <summary>
    /// Objects are sorted using their Y-axis position for top-down and 2.5D perspective layering.
    /// </summary>
    YSorted = 2
}
