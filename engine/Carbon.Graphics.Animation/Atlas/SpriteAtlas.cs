// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Animation.Atlas;

/// <summary>
/// Manages a collection of named <see cref="AtlasRegion"/> instances associated with a shared <see cref="ITexture"/>.
/// </summary>
public sealed class SpriteAtlas : IEnumerable<AtlasRegion>
{
    private readonly Dictionary<string, AtlasRegion> _regions = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the shared underlying texture for this atlas.
    /// </summary>
    public ITexture Texture { get; }

    /// <summary>
    /// Gets the number of regions defined in this atlas.
    /// </summary>
    public int Count => _regions.Count;

    /// <summary>
    /// Gets the read-only collection of all region names in this atlas.
    /// </summary>
    public IReadOnlyCollection<string> RegionNames => _regions.Keys;

    /// <summary>
    /// Gets the <see cref="AtlasRegion"/> associated with the specified name.
    /// </summary>
    /// <param name="name">The unique region name.</param>
    /// <returns>The found <see cref="AtlasRegion"/>.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the region name does not exist in the atlas.</exception>
    public AtlasRegion this[string name] => this.GetRegion(name);

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteAtlas"/> class.
    /// </summary>
    /// <param name="texture">The underlying texture sheet.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="texture"/> is null.</exception>
    public SpriteAtlas(ITexture texture) => this.Texture = texture ?? throw new ArgumentNullException(nameof(texture));

    /// <summary>
    /// Adds an existing <see cref="AtlasRegion"/> to this atlas.
    /// </summary>
    /// <param name="region">The region to add.</param>
    /// <exception cref="ArgumentException">Thrown if a region with the same name already exists.</exception>
    public void AddRegion(AtlasRegion region)
    {
        ArgumentException.ThrowIfNullOrEmpty(region.Name, nameof(region.Name));
        _regions.Add(region.Name, region);
    }

    /// <summary>
    /// Adds a new rectangular region to this atlas.
    /// </summary>
    /// <param name="name">The name of the region.</param>
    /// <param name="bounds">The pixel rectangle on the texture.</param>
    /// <param name="origin">The optional pivot origin in pixels.</param>
    /// <returns>The created <see cref="AtlasRegion"/>.</returns>
    public AtlasRegion AddRegion(string name, in RectangleF bounds, Vector2? origin = null)
    {
        AtlasRegion region = new(this.Texture, bounds, name, origin);
        this.AddRegion(region);
        return region;
    }

    /// <summary>
    /// Attempts to retrieve a region by name.
    /// </summary>
    /// <param name="name">The region name to search for.</param>
    /// <param name="region">The retrieved region if found; otherwise, <see cref="AtlasRegion.Empty"/>.</param>
    /// <returns><see langword="true"/> if found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetRegion(string name, out AtlasRegion region) =>
        _regions.TryGetValue(name, out region);

    /// <summary>
    /// Retrieves a region by name.
    /// </summary>
    /// <param name="name">The region name to find.</param>
    /// <returns>The found <see cref="AtlasRegion"/>.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the region name is not present in the atlas.</exception>
    public AtlasRegion GetRegion(string name)
    {
        if (!_regions.TryGetValue(name, out AtlasRegion region))
        {
            throw new KeyNotFoundException($"Atlas region '{name}' was not found in the sprite atlas.");
        }

        return region;
    }

    /// <summary>
    /// Checks whether this atlas contains a region with the specified name.
    /// </summary>
    /// <param name="name">The region name to check.</param>
    /// <returns><see langword="true"/> if the region exists; otherwise, <see langword="false"/>.</returns>
    public bool Contains(string name) => _regions.ContainsKey(name);

    /// <summary>
    /// Slices an entire texture into a uniform grid of rectangular regions.
    /// </summary>
    /// <param name="texture">The texture to slice.</param>
    /// <param name="cellWidth">Width of each cell in pixels.</param>
    /// <param name="cellHeight">Height of each cell in pixels.</param>
    /// <param name="margin">Margin around the texture edges in pixels.</param>
    /// <param name="spacing">Spacing between adjacent cells in pixels.</param>
    /// <param name="origin">Optional pivot origin for all generated cells. Defaults to (0, 0).</param>
    /// <param name="prefix">Prefix for region names. Default is "frame". Names will be "{prefix}_{index}".</param>
    /// <returns>A new populated <see cref="SpriteAtlas"/> instance.</returns>
    public static SpriteAtlas CreateGrid(
        ITexture texture,
        int cellWidth,
        int cellHeight,
        int margin = 0,
        int spacing = 0,
        Vector2? origin = null,
        string prefix = "frame")
    {
        ArgumentNullException.ThrowIfNull(texture, nameof(texture));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth, nameof(cellWidth));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight, nameof(cellHeight));

        SpriteAtlas atlas = new(texture);
        int availableWidth = (int)(texture.Width - (margin * 2));
        int availableHeight = (int)(texture.Height - (margin * 2));

        int columns = (availableWidth + spacing) / (cellWidth + spacing);
        int rows = (availableHeight + spacing) / (cellHeight + spacing);

        int index = 0;
        for (int row = 0; row < rows; row++)
        {
            float y = margin + (row * (cellHeight + spacing));

            for (int col = 0; col < columns; col++)
            {
                float x = margin + (col * (cellWidth + spacing));
                RectangleF bounds = new(x, y, cellWidth, cellHeight);
                string name = $"{prefix}_{index}";

                atlas.AddRegion(new AtlasRegion(texture, bounds, name, origin));
                index++;
            }
        }

        return atlas;
    }

    /// <summary>
    /// Slices a texture into a uniform grid using a custom naming function per cell.
    /// </summary>
    /// <param name="texture">The texture to slice.</param>
    /// <param name="cellWidth">Width of each cell in pixels.</param>
    /// <param name="cellHeight">Height of each cell in pixels.</param>
    /// <param name="nameSelector">Delegate mapping (column, row, linearIndex) to a region name. Return null to skip cell.</param>
    /// <param name="margin">Margin around texture edges in pixels.</param>
    /// <param name="spacing">Spacing between cells in pixels.</param>
    /// <param name="origin">Optional pivot origin for all generated cells.</param>
    /// <returns>A new populated <see cref="SpriteAtlas"/> instance.</returns>
    public static SpriteAtlas CreateCustomGrid(
        ITexture texture,
        int cellWidth,
        int cellHeight,
        Func<int, int, int, string?> nameSelector,
        int margin = 0,
        int spacing = 0,
        Vector2? origin = null)
    {
        ArgumentNullException.ThrowIfNull(texture, nameof(texture));
        ArgumentNullException.ThrowIfNull(nameSelector, nameof(nameSelector));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth, nameof(cellWidth));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight, nameof(cellHeight));

        SpriteAtlas atlas = new(texture);
        int availableWidth = (int)(texture.Width - (margin * 2));
        int availableHeight = (int)(texture.Height - (margin * 2));

        int columns = (availableWidth + spacing) / (cellWidth + spacing);
        int rows = (availableHeight + spacing) / (cellHeight + spacing);

        int index = 0;
        for (int row = 0; row < rows; row++)
        {
            float y = margin + (row * (cellHeight + spacing));

            for (int col = 0; col < columns; col++)
            {
                float x = margin + (col * (cellWidth + spacing));
                string? name = nameSelector(col, row, index);

                if (!string.IsNullOrEmpty(name))
                {
                    RectangleF bounds = new(x, y, cellWidth, cellHeight);
                    atlas.AddRegion(new AtlasRegion(texture, bounds, name, origin));
                }

                index++;
            }
        }

        return atlas;
    }

    /// <inheritdoc/>
    public IEnumerator<AtlasRegion> GetEnumerator() => _regions.Values.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
