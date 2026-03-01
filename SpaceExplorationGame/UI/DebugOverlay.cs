using System.Diagnostics;
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
    private static readonly Color4 MemColor = new(255, 180, 120, 255);

    // ── Memory tracking ─────────────────────────────────────────────
    private readonly Process _process = Process.GetCurrentProcess();
    private long _prevAllocatedBytes;
    private long _allocatedBytesPerFrame;


    /// <summary>Render the debug overlay on top of everything.</summary>
    public void Render(ISpriteRenderer renderer, GameState? state, SimulationCoordinator coordinator,
        DebugTimer gameTimer, double frameTotalMs)
    {
        // First pass: compute all lines to determine panel height
        var lines = new List<(string text, Color4 color, int indent)>();

        // Header
        lines.Add(("DEBUG OVERLAY  [1] to close", HeaderColor, 0));
        lines.Add(("", SeparatorColor, 0));

        // Frame totals
        double fps = frameTotalMs > 0 ? 1000.0 / frameTotalMs : 0.0;
        lines.Add(($"Frame Total: {frameTotalMs,7:F2} ms  ({fps:F1} FPS)", TimingColor, 0));
        AddTimingLines(lines, gameTimer.Entries, 1);
        lines.Add(("", SeparatorColor, 0));

        // Memory / GC info
        UpdateMemoryStats();
        _process.Refresh();
        long managedBytes = GC.GetTotalMemory(false);
        long workingSet = _process.WorkingSet64;
        lines.Add(("-- Memory --", HeaderColor, 0));
        lines.Add(($"Managed Heap : {FormatBytes(managedBytes)}", MemColor, 1));
        lines.Add(($"Working Set  : {FormatBytes(workingSet)}", MemColor, 1));
        lines.Add(($"Alloc/Frame  : {FormatBytes(_allocatedBytesPerFrame)}", MemColor, 1));
        var gcParts = new System.Text.StringBuilder("GC Collections:");
        for (int g = 0; g <= GC.MaxGeneration; g++)
            gcParts.Append($"  Gen{g}={GC.CollectionCount(g)}");
        lines.Add((gcParts.ToString(), MemColor, 1));
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

    private void UpdateMemoryStats()
    {
        long current = GC.GetTotalAllocatedBytes(precise: false);
        _allocatedBytesPerFrame = current - _prevAllocatedBytes;
        _prevAllocatedBytes = current;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
