// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using Carbon.Desktop.Abstractions.World;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// A reusable, growable list of <see cref="RemoteEntityView"/> exposed as <see cref="IReadOnlyList{T}"/>. The session
/// rebuilds the remote views every frame (interpolated positions change each frame), and used to copy them into a new
/// array each time. Two of these alternate instead, so a frame allocates nothing once capacity is reached, and a list
/// handed out by the previous rebuild stays intact while the next one is written (audit round 2, PERF-5).
/// </summary>
internal sealed class RemoteViewBuffer : IReadOnlyList<RemoteEntityView>
{
    private RemoteEntityView[] _items = new RemoteEntityView[64];
    private int _count;

    public int Count => _count;

    public RemoteEntityView this[int index] =>
        (uint)index < (uint)_count ? _items[index] : throw new ArgumentOutOfRangeException(nameof(index));

    public void Clear() => _count = 0;

    public void Add(in RemoteEntityView view)
    {
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[_count++] = view;
    }

    public IEnumerator<RemoteEntityView> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _items[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
