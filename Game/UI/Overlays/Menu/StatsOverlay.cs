using SpaceExplorationGame.Core;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay displaying lifetime player statistics: combat, economy, and exploration.
/// Accessible from the in-game menu.
/// </summary>
public class StatsOverlay : PanelOverlayBase
{
    protected override string Title => "STATISTICS";
    protected override Color3 TitleColor => new(220, 200, 100);
    protected override float PanelWidth => 520;
    protected override float PanelHeight => 530;
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            return input == null ? "" : $"{input.GetActionHelpText(InputAction.MenuBack)}: BACK";
        }
    }

    protected override void RenderPanelContent(Game game, ISpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        var stats = game.Player.Stats;
        float x = panelX + 20;
        float rX = panelX + panelW - 20; // right-align anchor
        float y = contentY + 5;
        float lineH = 22f;
        float sectionGap = 12f;

        // ── Combat ──
        DrawSectionHeader(renderer, x, ref y, "COMBAT");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Kills", $"{stats.TotalKills}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Deaths", $"{stats.Deaths}");
        if (stats.TotalKills > 0 && stats.Deaths > 0)
            DrawStatLine(renderer, x, rX, ref y, lineH, "K/D Ratio", $"{(float)stats.TotalKills / stats.Deaths:F1}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Damage Dealt", $"{stats.TotalDamageDealt:F0}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Damage Received", $"{stats.TotalDamageReceived:F0}");

        // Kills by faction
        if (stats.KillsByFaction.Count > 0)
        {
            foreach (var (faction, count) in stats.KillsByFaction)
                DrawStatLine(renderer, x + 15, rX, ref y, lineH, $"{faction} Kills", $"{count}", dimLabel: true);
        }

        y += sectionGap;

        // ── Economy ──
        DrawSectionHeader(renderer, x, ref y, "ECONOMY");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Credits Earned", $"{stats.TotalCreditsEarned:N0}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Credits Spent", $"{stats.TotalCreditsSpent:N0}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Resources Mined", $"{stats.TotalResourcesMined}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Parts Found", $"{stats.PartsFound}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Missions Completed", $"{game.Player.Missions.Completed}");

        y += sectionGap;

        // ── Exploration ──
        DrawSectionHeader(renderer, x, ref y, "EXPLORATION");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Systems Visited", $"{stats.SystemsVisited.Count}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Planets Landed", $"{stats.PlanetsLanded}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Stations Docked", $"{stats.SpaceStationsVisited}");
        DrawStatLine(renderer, x, rX, ref y, lineH, "Play Time", FormatPlayTime(stats.PlayTimeSeconds));
    }

    private static void DrawSectionHeader(ISpriteRenderer renderer, float x, ref float y, string title)
    {
        renderer.DrawTextScreen(x, y, title, new Color3(180, 200, 255), 1.8f);
        y += 26;
    }

    private static void DrawStatLine(ISpriteRenderer renderer, float x, float rX, ref float y, float lineH,
        string label, string value, bool dimLabel = false)
    {
        var labelColor = dimLabel ? new Color3(110, 110, 130) : new Color3(150, 150, 170);
        renderer.DrawTextScreen(x, y, label, labelColor, 1.4f);

        float valueW = renderer.MeasureText(value, 1.5f);
        renderer.DrawTextScreen(rX - valueW, y, value, new Color3(220, 220, 240), 1.5f);
        y += lineH;
    }

    private static string FormatPlayTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
            : $"{ts.Minutes}m {ts.Seconds:D2}s";
    }
}
