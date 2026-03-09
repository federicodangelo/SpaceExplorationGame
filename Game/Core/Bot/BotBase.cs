using System.Numerics;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation;

namespace SpaceExplorationGame.Core.Bot;

/// <summary>
/// Shared infrastructure for all autoplay sub-bots: status strings, RNG, pathfinding utilities,
/// and the HUD renderer. All sub-bots inherit from this class.
/// </summary>
internal abstract class BotBase
{
    // ── Shared timing constants ──────────────────────────────────────
    protected const float ActionDelay = 3.0f;
    protected const float StoppedSpeed = 15f;
    protected const float StuckTimeout = 2.0f;
    protected const float DialogueLineDelay = 1.0f;

    public bool Enabled { get; set; }

    protected string _statusGoal = "";
    protected string _statusAction = "";

    protected readonly Random _rng;

    internal string StatusGoal => _statusGoal;
    internal string StatusAction => _statusAction;

    protected BotBase(Random rng) => _rng = rng;

    protected float RandRange(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

    /// <summary>
    /// Renders the bot's current goal and action as text in the bottom-left corner.
    /// </summary>
    public void RenderStatus(ISpriteRenderer renderer)
    {
        if (!Enabled) return;

        const float scale = 1.5f;
        const float pad = 8f;
        const float lineHeight = 14f;
        float y = renderer.WindowHeight - pad - lineHeight * 3;
        float x = pad;

        var labelColor = new Color4(120, 220, 255, 200);
        var goalColor = new Color4(255, 255, 255, 220);
        var actionColor = new Color4(200, 255, 180, 200);

        renderer.DrawTextScreen(x, y, "[AUTOPLAY]", labelColor, scale);
        y += lineHeight;
        if (!string.IsNullOrEmpty(_statusGoal))
        {
            renderer.DrawTextScreen(x, y, _statusGoal, goalColor, scale);
            y += lineHeight;
        }
        if (!string.IsNullOrEmpty(_statusAction))
            renderer.DrawTextScreen(x, y, _statusAction, actionColor, scale);
    }

    // ════════════════════════════════════════════════════════════════
    //  SHARED PATHFINDING UTILITIES
    // ════════════════════════════════════════════════════════════════

    protected static Vector2 TilePosToWorld(TilePos tile) =>
        new Vector2((tile.X + 0.5f) * WindowConfig.TileSize, (tile.Y + 0.5f) * WindowConfig.TileSize);

    /// <summary>
    /// Unified A* pathfinder on a 2D tile grid. Uses Manhattan distance as heuristic.
    /// If the goal tile is impassable, the nearest walkable neighbour within radius 4 is used.
    /// Returns an empty list if no path is found within <paramref name="maxNodes"/> expansions.
    /// </summary>
    protected static List<TilePos> AStarTilePath(
        int width, int height,
        Func<int, int, bool> isWalkable,
        TilePos from, TilePos to,
        int maxNodes = int.MaxValue)
    {
        // Redirect impassable goal to nearest walkable neighbour
        if (!isWalkable(to.X, to.Y))
        {
            TilePos? adj = null;
            for (int r = 1; r <= 4 && adj == null; r++)
                for (int dy = -r; dy <= r && adj == null; dy++)
                    for (int dx = -r; dx <= r && adj == null; dx++)
                        if ((Math.Abs(dx) == r || Math.Abs(dy) == r) && isWalkable(to.X + dx, to.Y + dy))
                            adj = new TilePos(to.X + dx, to.Y + dy);
            if (adj == null) return [];
            to = adj.Value;
        }

        if (from == to) return [];

        var gCost = new Dictionary<TilePos, int>();
        var prev = new Dictionary<TilePos, TilePos>();
        var open = new PriorityQueue<TilePos, int>();

        gCost[from] = 0;
        prev[from] = from;
        open.Enqueue(from, Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y));

        ReadOnlySpan<(int dx, int dy)> dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        while (open.Count > 0 && prev.Count < maxNodes)
        {
            var cur = open.Dequeue();
            if (cur == to) break;

            int curG = gCost[cur];
            foreach (var (dx, dy) in dirs)
            {
                var nb = new TilePos(cur.X + dx, cur.Y + dy);
                if ((uint)nb.X >= (uint)width || (uint)nb.Y >= (uint)height) continue;
                if (!isWalkable(nb.X, nb.Y)) continue;
                if (prev.ContainsKey(nb)) continue;
                int nbG = curG + 1;
                gCost[nb] = nbG;
                prev[nb] = cur;
                open.Enqueue(nb, nbG + Math.Abs(nb.X - to.X) + Math.Abs(nb.Y - to.Y));
            }
        }

        if (!prev.ContainsKey(to)) return [];

        var path = new List<TilePos>();
        var step = to;
        while (step != from)
        {
            path.Add(step);
            step = prev[step];
        }
        path.Reverse();
        return path;
    }
}
