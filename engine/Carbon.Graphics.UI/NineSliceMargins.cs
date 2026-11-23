namespace Carbon.Graphics.UI;

/// <summary>
/// Represents border margins for nine-slice panel rendering.
/// </summary>
/// <param name="Left">Left margin in pixels.</param>
/// <param name="Top">Top margin in pixels.</param>
/// <param name="Right">Right margin in pixels.</param>
/// <param name="Bottom">Bottom margin in pixels.</param>
public readonly record struct NineSliceMargins(float Left, float Top, float Right, float Bottom);
