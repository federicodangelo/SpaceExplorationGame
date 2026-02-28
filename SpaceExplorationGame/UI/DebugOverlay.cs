using SpaceExplorationGame.Core;
using SpaceExplorationGame.Platform;
using SpaceExplorationGame.Simulation;

namespace SpaceExplorationGame.UI;

/// <summary>
/// Semi-transparent overlay that shows timing and debug information.
/// Toggled via the "1" key; all timing is collected by <see cref="Game"/>.
/// </summary>
public sealed class DebugOverlay
{
    // ── Layout constants ────────────────────────────────────────────
    private const float PanelX = 8f;
    private const float PanelY = 8f;
    private const float PaddingX = 10f;
    private const float PaddingY = 6f;
    private const float TextScale = 1.5f;
    private const float LineHeight = 14f; // GlyphHeight(8) * TextScale(1.5) ≈ 12, plus 2 px gap
    private const float IndentWidth = 12f;

    private static readonly Color4 BgColor = new(0, 0, 0, 180);
    private static readonly Color4 HeaderColor = new(255, 220, 80, 255);
    private static readonly Color4 TimingColor = new(180, 255, 180, 255);
    private static readonly Color4 ChildTimingColor = new(140, 220, 140, 255);
    private static readonly Color4 InfoColor = new(180, 200, 255, 255);
    private static readonly Color4 SeparatorColor = new(100, 100, 120, 255);

    /// <summary>Render the debug overlay on top of everything.</summary>
    public void Render(SpriteRenderer renderer, GameState? state, SimulationCoordinator coordinator,
        DebugTimer gameTimer, double frameTotalMs)
    {
        // First pass: compute all lines to determine panel height
        var lines = new List<(string text, Color4 color, int indent)>();

        // Header
        lines.Add(("DEBUG OVERLAY  [1] to close", HeaderColor, 0));
        lines.Add(("", SeparatorColor, 0));

        // Frame totals
        lines.Add(($"Frame Total: {frameTotalMs,7:F2} ms", TimingColor, 0));
        AddTimingLines(lines, gameTimer.Entries, 1);
        lines.Add(("", SeparatorColor, 0));

        // State debug info
        if (state is IDebugInfoProvider stateProvider)
        {
            lines.Add(($"-- State: {state.Type} --", HeaderColor, 0));

            var timings = stateProvider.GetDebugTimings();
            if (timings is { Count: > 0 })
                AddTimingLines(lines, timings, 1);

            var info = stateProvider.GetDebugInfo();
            if (info is { Count: > 0 })
            {
                foreach (var line in info)
                    lines.Add((line, InfoColor, 1));
            }

            lines.Add(("", SeparatorColor, 0));
        }

        // Simulation debug info
        foreach (var sim in coordinator.Simulations)
        {
            if (sim is not IDebugInfoProvider simProvider) continue;

            string simName = sim.GetType().Name;
            float? remaining = coordinator.GetRemainingAliveTime(sim);
            string lifetime = remaining.HasValue ? $"  TTL: {remaining.Value:F0}s" : "";
            lines.Add(($"-- Sim: {simName}{lifetime} --", HeaderColor, 0));

            var timings = simProvider.GetDebugTimings();
            if (timings is { Count: > 0 })
                AddTimingLines(lines, timings, 1);

            var info = simProvider.GetDebugInfo();
            if (info is { Count: > 0 })
            {
                foreach (var line in info)
                    lines.Add((line, InfoColor, 1));
            }

            lines.Add(("", SeparatorColor, 0));
        }

        // Calculate panel size
        float panelW = 720f;
        float panelH = PaddingY * 2 + lines.Count * LineHeight;

        // Draw background
        renderer.DrawRectScreen(PanelX, PanelY, panelW, panelH, BgColor);

        // Draw lines
        float y = PanelY + PaddingY;
        foreach (var (text, color, indent) in lines)
        {
            if (text.Length > 0)
            {
                float x = PanelX + PaddingX + indent * IndentWidth;
                renderer.DrawTextScreen(x, y, text, color, TextScale);
            }
            y += LineHeight;
        }
    }

    private static void AddTimingLines(List<(string text, Color4 color, int indent)> lines,
        IReadOnlyList<DebugTimingEntry> entries, int indent)
    {
        foreach (var entry in entries)
        {
            var color = indent == 1 ? TimingColor : ChildTimingColor;
            lines.Add(($"{entry.Name}: {entry.ElapsedMs,7:F2} ms", color, indent));
        }
    }
}
