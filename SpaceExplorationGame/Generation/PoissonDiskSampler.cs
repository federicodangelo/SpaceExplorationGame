using System.Numerics;

namespace SpaceExplorationGame.Generation;

/// <summary>
/// Generates a set of 2D points using Poisson disk sampling (Bridson's algorithm).
/// Points are guaranteed to be at least <paramref name="minDist"/> apart while
/// still covering the domain far more uniformly than purely random placement.
/// </summary>
public static class PoissonDiskSampler
{
    /// <summary>
    /// Sample points in the rectangle [<paramref name="x0"/>, <paramref name="x1"/>]
    /// × [<paramref name="y0"/>, <paramref name="y1"/>].
    /// </summary>
    /// <param name="rng">Seeded RNG for determinism.</param>
    /// <param name="x0">Left bound (inclusive).</param>
    /// <param name="y0">Top bound (inclusive).</param>
    /// <param name="x1">Right bound (exclusive).</param>
    /// <param name="y1">Bottom bound (exclusive).</param>
    /// <param name="minDist">Minimum distance between any two generated points.</param>
    /// <param name="maxCandidates">
    /// Candidates tried per active point before it is retired (default 30).
    /// Higher values → denser packing, more CPU time.
    /// </param>
    /// <returns>List of sampled positions; order is implementation-defined.</returns>
    public static List<Vector2> Sample(
        SeededRandom rng,
        float x0, float y0, float x1, float y1,
        float minDist,
        int maxCandidates = 30)
    {
        float w = x1 - x0;
        float h = y1 - y0;

        // Grid cell size: minDist / sqrt(2) guarantees at most one sample per cell.
        float cellSize = minDist / MathF.Sqrt(2f);
        int cols = (int)MathF.Ceiling(w / cellSize) + 1;
        int rows = (int)MathF.Ceiling(h / cellSize) + 1;

        // Grid stores the index into `points`, or -1 for empty.
        var grid = new int[cols * rows];
        grid.AsSpan().Fill(-1);

        var points = new List<Vector2>(256);
        var active = new List<int>(64);

        // Seed: first point at a random location inside the domain.
        var first = new Vector2(
            rng.NextFloat(x0, x1),
            rng.NextFloat(y0, y1));
        AddPoint(first, points, active, grid, x0, y0, cellSize, cols);

        while (active.Count > 0)
        {
            int listIdx = rng.NextInt(0, active.Count);
            var origin = points[active[listIdx]];
            bool found = false;

            for (int k = 0; k < maxCandidates; k++)
            {
                // Uniform random point in the annulus [minDist, 2·minDist].
                float angle = rng.NextFloat(0f, MathF.PI * 2f);
                float dist = rng.NextFloat(minDist, minDist * 2f);
                var candidate = new Vector2(
                    origin.X + MathF.Cos(angle) * dist,
                    origin.Y + MathF.Sin(angle) * dist);

                if (candidate.X < x0 || candidate.X >= x1 ||
                    candidate.Y < y0 || candidate.Y >= y1)
                    continue;

                if (IsFarEnough(candidate, points, grid, x0, y0, cellSize, cols, rows, minDist * minDist))
                {
                    AddPoint(candidate, points, active, grid, x0, y0, cellSize, cols);
                    found = true;
                    break;
                }
            }

            if (!found)
                active.RemoveAt(listIdx);
        }

        return points;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void AddPoint(
        Vector2 p, List<Vector2> points, List<int> active,
        int[] grid, float x0, float y0, float cellSize, int cols)
    {
        int ci = (int)((p.X - x0) / cellSize);
        int ri = (int)((p.Y - y0) / cellSize);
        grid[ri * cols + ci] = points.Count;
        active.Add(points.Count);
        points.Add(p);
    }

    private static bool IsFarEnough(
        Vector2 p, List<Vector2> points,
        int[] grid,
        float x0, float y0, float cellSize, int cols, int rows,
        float minDistSq)
    {
        int ci = (int)((p.X - x0) / cellSize);
        int ri = (int)((p.Y - y0) / cellSize);

        const int Search = 2;
        for (int dy = -Search; dy <= Search; dy++)
        {
            int ny = ri + dy;
            if (ny < 0 || ny >= rows) continue;
            for (int dx = -Search; dx <= Search; dx++)
            {
                int nx = ci + dx;
                if (nx < 0 || nx >= cols) continue;
                int idx = grid[ny * cols + nx];
                if (idx < 0) continue;
                var diff = p - points[idx];
                if (diff.X * diff.X + diff.Y * diff.Y < minDistSq)
                    return false;
            }
        }
        return true;
    }
}
