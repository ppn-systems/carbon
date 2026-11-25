// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Maps.Navigation;

/// <summary>
/// Reusable, zero-allocation 8-directional A* pathfinder operating over <see cref="ITileWalkabilityGrid"/>.
/// Respects terrain traversal costs, diagonal corner-cutting guards, cliff transitions, ramps, and doors.
/// </summary>
public sealed class GridPathfinder
{
    private const float DiagonalCost = 1.41421356f;

    private struct PathNode
    {
        public int X;
        public int Y;
        public int ParentIndex;
        public int HeapIndex;
        public float G;
        public float H;

        public readonly float F => this.G + this.H;

        public bool Closed;
    }

    private readonly PathNode[] _nodes;
    private readonly int[] _openHeap;
    private readonly long[] _tableKeys;
    private readonly int[] _tableNodeIndex;
    private readonly int[] _tableStamp;
    private readonly int _hashMask;

    private int _currentStamp;
    private int _nodeCount;
    private int _heapCount;

    /// <summary>
    /// Gets the maximum number of nodes this pathfinder instance can allocate per search.
    /// </summary>
    public int MaxNodes => _nodes.Length;

    /// <summary>
    /// Initializes a new instance of the <see cref="GridPathfinder"/> class with pre-allocated reusable buffers.
    /// </summary>
    /// <param name="maxNodes">Maximum node pool capacity per search (default 2048).</param>
    public GridPathfinder(int maxNodes = 2048)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNodes);

        _nodes = new PathNode[maxNodes];
        _openHeap = new int[maxNodes];

        int hashCapacity = (int)BitOperations.RoundUpToPowerOf2((uint)maxNodes * 4U);
        _tableKeys = new long[hashCapacity];
        _tableNodeIndex = new int[hashCapacity];
        _tableStamp = new int[hashCapacity];
        _hashMask = hashCapacity - 1;
    }

    /// <summary>
    /// Finds a lowest-cost tile path from <paramref name="start"/> to <paramref name="goal"/> on <paramref name="grid"/>,
    /// writing the tile coordinates in forward order into <paramref name="pathBuffer"/> with zero heap allocations.
    /// </summary>
    /// <param name="grid">Spatial navigation grid supplying walkability, traversal rules, and move costs.</param>
    /// <param name="start">Starting world tile coordinate.</param>
    /// <param name="goal">Target world tile coordinate.</param>
    /// <param name="pathBuffer">Caller-owned buffer receiving the resulting tile coordinates (inclusive of start and goal).</param>
    /// <param name="pathLength">Output number of tile coordinates written to <paramref name="pathBuffer"/>.</param>
    /// <param name="maxExpandedNodes">Optional cap on expanded nodes for this search; defaults to <see cref="MaxNodes"/>.</param>
    /// <returns>The <see cref="PathSearchStatus"/> describing the outcome.</returns>
    public PathSearchStatus FindPath(
        ITileWalkabilityGrid grid,
        (int X, int Y) start,
        (int X, int Y) goal,
        Span<(int X, int Y)> pathBuffer,
        out int pathLength,
        int? maxExpandedNodes = null)
    {
        ArgumentNullException.ThrowIfNull(grid);

        pathLength = 0;

        if (!grid.IsWalkable(start.X, start.Y) || !grid.IsWalkable(goal.X, goal.Y))
        {
            return PathSearchStatus.StartOrGoalBlocked;
        }

        if (start == goal)
        {
            if (pathBuffer.Length < 1)
            {
                return PathSearchStatus.BufferTooSmall;
            }

            pathBuffer[0] = start;
            pathLength = 1;
            return PathSearchStatus.Found;
        }

        this.ResetSearch();

        int expandBudget = maxExpandedNodes.HasValue
            ? Math.Clamp(maxExpandedNodes.Value, 1, _nodes.Length)
            : _nodes.Length;

        if (!this.TryGetOrCreateNode(start.X, start.Y, out int startIdx))
        {
            return PathSearchStatus.SearchLimitReached;
        }

        _nodes[startIdx].G = 0f;
        _nodes[startIdx].H = OctileDistance(start.X, start.Y, goal.X, goal.Y);
        this.PushHeap(startIdx);

        int expanded = 0;

        while (_heapCount > 0)
        {
            int currentIdx = this.PopHeap();
            ref PathNode current = ref _nodes[currentIdx];

            if (current.X == goal.X && current.Y == goal.Y)
            {
                return this.ReconstructPath(currentIdx, pathBuffer, out pathLength);
            }

            if (expanded >= expandBudget)
            {
                return PathSearchStatus.SearchLimitReached;
            }

            current.Closed = true;
            expanded++;

            int cx = current.X;
            int cy = current.Y;
            float currentG = current.G;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (!grid.CanTraverse(cx, cy, nx, ny))
                    {
                        continue;
                    }

                    float moveCost = grid.GetMoveCost(nx, ny);
                    if (float.IsPositiveInfinity(moveCost) || moveCost <= 0f)
                    {
                        continue;
                    }

                    if (!this.TryGetOrCreateNode(nx, ny, out int neighborIdx))
                    {
                        return PathSearchStatus.SearchLimitReached;
                    }

                    ref PathNode neighbor = ref _nodes[neighborIdx];
                    if (neighbor.Closed)
                    {
                        continue;
                    }

                    float stepScale = (dx != 0 && dy != 0) ? DiagonalCost : 1f;
                    float tentativeG = currentG + (stepScale * moveCost);

                    if (tentativeG < neighbor.G)
                    {
                        neighbor.G = tentativeG;
                        neighbor.H = OctileDistance(nx, ny, goal.X, goal.Y);
                        neighbor.ParentIndex = currentIdx;

                        if (neighbor.HeapIndex >= 0)
                        {
                            this.SiftUp(neighbor.HeapIndex);
                        }
                        else
                        {
                            this.PushHeap(neighborIdx);
                        }
                    }
                }
            }
        }

        return PathSearchStatus.Unreachable;
    }

    private PathSearchStatus ReconstructPath(int goalIdx, Span<(int X, int Y)> pathBuffer, out int pathLength)
    {
        int length = 0;
        int cursor = goalIdx;
        while (cursor >= 0)
        {
            length++;
            cursor = _nodes[cursor].ParentIndex;
        }

        if (length > pathBuffer.Length)
        {
            pathLength = 0;
            return PathSearchStatus.BufferTooSmall;
        }

        cursor = goalIdx;
        for (int i = length - 1; i >= 0; i--)
        {
            pathBuffer[i] = (_nodes[cursor].X, _nodes[cursor].Y);
            cursor = _nodes[cursor].ParentIndex;
        }

        pathLength = length;
        return PathSearchStatus.Found;
    }

    private void ResetSearch()
    {
        _nodeCount = 0;
        _heapCount = 0;
        if (_currentStamp == int.MaxValue)
        {
            Array.Clear(_tableStamp);
            _currentStamp = 1;
        }
        else
        {
            _currentStamp++;
        }
    }

    private bool TryGetOrCreateNode(int x, int y, out int nodeIndex)
    {
        long key = ((long)x << 32) | (uint)y;
        int slot = (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32) & _hashMask;

        while (true)
        {
            if (_tableStamp[slot] != _currentStamp)
            {
                if (_nodeCount >= _nodes.Length)
                {
                    nodeIndex = -1;
                    return false;
                }

                nodeIndex = _nodeCount++;
                _tableStamp[slot] = _currentStamp;
                _tableKeys[slot] = key;
                _tableNodeIndex[slot] = nodeIndex;
                _nodes[nodeIndex] = new PathNode
                {
                    X = x,
                    Y = y,
                    ParentIndex = -1,
                    HeapIndex = -1,
                    G = float.PositiveInfinity,
                    H = 0f,
                    Closed = false
                };
                return true;
            }

            if (_tableKeys[slot] == key)
            {
                nodeIndex = _tableNodeIndex[slot];
                return true;
            }

            slot = (slot + 1) & _hashMask;
        }
    }

    private void PushHeap(int nodeIndex)
    {
        int heapPos = _heapCount++;
        _openHeap[heapPos] = nodeIndex;
        _nodes[nodeIndex].HeapIndex = heapPos;
        this.SiftUp(heapPos);
    }

    private int PopHeap()
    {
        int rootNode = _openHeap[0];
        _nodes[rootNode].HeapIndex = -1;

        _heapCount--;
        if (_heapCount > 0)
        {
            int lastNode = _openHeap[_heapCount];
            _openHeap[0] = lastNode;
            _nodes[lastNode].HeapIndex = 0;
            this.SiftDown(0);
        }

        return rootNode;
    }

    private void SiftUp(int index)
    {
        int nodeIdx = _openHeap[index];
        while (index > 0)
        {
            int parentPos = (index - 1) >> 1;
            int parentNodeIdx = _openHeap[parentPos];
            if (!this.IsHigherPriority(nodeIdx, parentNodeIdx))
            {
                break;
            }

            _openHeap[index] = parentNodeIdx;
            _nodes[parentNodeIdx].HeapIndex = index;
            index = parentPos;
        }

        _openHeap[index] = nodeIdx;
        _nodes[nodeIdx].HeapIndex = index;
    }

    private void SiftDown(int index)
    {
        int nodeIdx = _openHeap[index];
        int half = _heapCount >> 1;

        while (index < half)
        {
            int childPos = (index << 1) + 1;
            int childNodeIdx = _openHeap[childPos];
            int rightPos = childPos + 1;

            if (rightPos < _heapCount)
            {
                int rightNodeIdx = _openHeap[rightPos];
                if (this.IsHigherPriority(rightNodeIdx, childNodeIdx))
                {
                    childPos = rightPos;
                    childNodeIdx = rightNodeIdx;
                }
            }

            if (!this.IsHigherPriority(childNodeIdx, nodeIdx))
            {
                break;
            }

            _openHeap[index] = childNodeIdx;
            _nodes[childNodeIdx].HeapIndex = index;
            index = childPos;
        }

        _openHeap[index] = nodeIdx;
        _nodes[nodeIdx].HeapIndex = index;
    }

    private bool IsHigherPriority(int candidateIdx, int otherIdx)
    {
        ref readonly PathNode a = ref _nodes[candidateIdx];
        ref readonly PathNode b = ref _nodes[otherIdx];
        float fa = a.F;
        float fb = b.F;
        return fa < fb || (fa == fb && a.H < b.H);
    }

    private static float OctileDistance(int x1, int y1, int x2, int y2)
    {
        int dx = Math.Abs(x2 - x1);
        int dy = Math.Abs(y2 - y1);
        return dx + dy + ((DiagonalCost - 2f) * Math.Min(dx, dy));
    }
}
