using System.Numerics;
using System.Runtime.CompilerServices;

namespace SpaceExplorationGame.ECS.Systems.Combat;

/// <summary>
/// A simple unbounded spatial hash grid for accelerating 2-D collision queries.
/// Targets are inserted by position; queries return indices of all targets
/// whose cell is within the requested radius of the query point.
/// Reused across frames via <see cref="Clear"/>.
/// </summary>
internal sealed class SpatialHash
{
    /// <summary>Size of each grid cell in world units.  Should be ≥ the largest collision radius.</summary>
    public const float CellSize = 128f;

    private const float InvCellSize = 1f / CellSize;

    // Buckets keyed by packed (cellX, cellY).
    private readonly Dictionary<long, List<int>> _cells = [];

    // Pool of reusable lists to avoid per-frame allocations.
    private readonly List<List<int>> _listPool = [];
    private int _poolIndex;

    /// <summary>Remove all entries, recycling bucket lists for the next frame.</summary>
    public void Clear()
    {
        foreach (var kvp in _cells)
        {
            kvp.Value.Clear();
            if (_poolIndex < _listPool.Count)
                _listPool[_poolIndex] = kvp.Value;
            else
                _listPool.Add(kvp.Value);
            _poolIndex++;
        }
        _cells.Clear();
        _poolIndex = 0;
    }

    /// <summary>Insert an item index at the given world position.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Insert(Vector2 position, int index)
    {
        long key = CellKey(position);
        if (!_cells.TryGetValue(key, out var list))
        {
            list = RentList();
            _cells[key] = list;
        }
        list.Add(index);
    }

    /// <summary>
    /// Yield all item indices in cells overlapping a square region centred on
    /// <paramref name="center"/> with half-extent <paramref name="radius"/>.
    /// </summary>
    public IEnumerable<int> Query(Vector2 center, float radius)
    {
        int minCX = (int)MathF.Floor((center.X - radius) * InvCellSize);
        int maxCX = (int)MathF.Floor((center.X + radius) * InvCellSize);
        int minCY = (int)MathF.Floor((center.Y - radius) * InvCellSize);
        int maxCY = (int)MathF.Floor((center.Y + radius) * InvCellSize);

        for (int cx = minCX; cx <= maxCX; cx++)
        {
            for (int cy = minCY; cy <= maxCY; cy++)
            {
                long key = PackKey(cx, cy);
                if (_cells.TryGetValue(key, out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                        yield return list[i];
                }
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long CellKey(Vector2 pos)
    {
        int cx = (int)MathF.Floor(pos.X * InvCellSize);
        int cy = (int)MathF.Floor(pos.Y * InvCellSize);
        return PackKey(cx, cy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long PackKey(int cx, int cy) => ((long)cx << 32) | (uint)cy;

    private List<int> RentList()
    {
        if (_poolIndex < _listPool.Count)
            return _listPool[_poolIndex++];
        var list = new List<int>();
        _listPool.Add(list);
        _poolIndex++;
        return list;
    }
}
