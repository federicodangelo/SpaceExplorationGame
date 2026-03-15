using System;
using System.Collections.Generic;

namespace SpaceExplorationGame.Rendering.Base;

/// <summary>
/// Collects deferred draw calls with a Y sort key, then flushes them in
/// ascending Y order (top-to-bottom) for correct top-down overlap.
/// </summary>
public sealed class YSortedDrawList
{
    private readonly List<(float SortY, Action Draw)> _entries = new();

    public void Add(float sortY, Action draw)
    {
        _entries.Add((sortY, draw));
    }

    public void Flush()
    {
        _entries.Sort(static (a, b) => a.SortY.CompareTo(b.SortY));
        foreach (var entry in _entries)
            entry.Draw();
        _entries.Clear();
    }
}
