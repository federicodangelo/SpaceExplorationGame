using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay opened from the in-game menu that shows all active missions.
/// Allows the player to switch the tracked mission or abandon missions.
/// </summary>
public class MissionsListOverlay : ListPanelOverlay
{
    private Game _game = null!;

    protected override string Title => "ACTIVE MISSIONS";
    protected override Color3 TitleColor => new(100, 180, 255);
    protected override float PanelWidth => 620;
    protected override float PanelHeight
    {
        get
        {
            int count = _game?.Player.ActiveMissions.Count ?? 0;
            return Math.Max(220, 90 + count * ItemHeight + 60);
        }
    }
    protected override string? ControlsHint
    {
        get
        {
            int count = _game?.Player.ActiveMissions.Count ?? 0;
            return count > 0 ? "W/S: SELECT  ENTER: TRACK  X: ABANDON  ESC: BACK" : "ESC: BACK";
        }
    }

    protected override int ItemCount => _game?.Player.ActiveMissions.Count ?? 0;
    protected override float ItemHeight => 80f;
    protected override float ListOffsetY => 0f;

    public void Open(Game game)
    {
        _game = game;
        base.Open();

        // Pre-select the currently tracked mission if any
        var tracked = game.Player.GetTrackedMission();
        if (tracked != null)
        {
            int idx = game.Player.ActiveMissions.IndexOf(tracked);
            if (idx >= 0) SelectedIndex = idx;
        }
    }

    protected override void OnItemConfirmed(Game game, int index)
    {
        if (index < 0 || index >= game.Player.ActiveMissions.Count) return;

        game.Player.TrackedMissionIndex = index;
        var m = game.Player.ActiveMissions[index];
        SetStatus($"TRACKING: [{m.TypeLabel}] {m.Title}", 2f);
    }

    protected override void OnItemSecondary(Game game, int index)
    {
        if (index < 0 || index >= game.Player.ActiveMissions.Count) return;

        var mission = game.Player.ActiveMissions[index];
        game.Player.AbandonMission(mission);
        ClampSelection();
        SetStatus("MISSION ABANDONED", 2f);
    }

    protected override void RenderPanelContent(Game game, SpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        var active = game.Player.ActiveMissions;

        // Mission count
        string countText = $"{active.Count}/{PlayerData.MaxActiveMissions}";
        float countW = renderer.MeasureText(countText, 1.5f);
        renderer.DrawTextScreen(panelX + panelW - countW - 15, PanelY + 17, countText,
            new Color3(180, 180, 200), 1.5f);

        if (active.Count == 0)
        {
            string empty = "NO ACTIVE MISSIONS";
            float emptyW = renderer.MeasureText(empty, 2f);
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - emptyW / 2f, contentY + 20, empty,
                new Color3(120, 120, 140), 2f);

            string hint = "Accept missions from station mission boards.";
            float hintW = renderer.MeasureText(hint, 1.5f);
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - hintW / 2f, contentY + 48, hint,
                new Color3(90, 90, 110), 1.5f);
            return;
        }

        int trackedIdx = game.Player.TrackedMissionIndex;
        if (trackedIdx < 0)
        {
            var tracked = game.Player.GetTrackedMission();
            if (tracked != null) trackedIdx = active.IndexOf(tracked);
        }

        float listY = contentY;
        for (int i = 0; i < active.Count; i++)
        {
            float y = listY + i * ItemHeight;
            var m = active[i];
            bool selected = i == SelectedIndex;
            bool isTracked = i == trackedIdx;
            bool completed = m.Status == MissionStatus.Completed;

            if (selected)
                renderer.DrawRectScreen(panelX + 5, y, panelW - 10, ItemHeight - 4,
                    completed ? new Color3(30, 50, 30) : new Color3(35, 40, 65));

            if (isTracked)
                renderer.DrawTextScreen(panelX + 10, y + 5, ">>>", new Color3(100, 255, 200), 1.5f);

            float labelX = panelX + 40;

            string statusTag = completed ? "[COMPLETE]" : "[ACTIVE]";
            var statusColor = completed ? new Color3(100, 255, 100) : new Color3(255, 200, 80);
            renderer.DrawTextScreen(labelX, y + 5, statusTag, statusColor, 1.5f);

            float afterStatus = labelX + renderer.MeasureText(statusTag, 1.5f) + 8;
            renderer.DrawTextScreen(afterStatus, y + 5, $"[{m.TypeLabel}]", m.TypeColor, 1.5f);

            renderer.DrawTextScreen(labelX, y + 24, m.Title,
                selected ? new Color3(255, 255, 220) : new Color3(200, 200, 200), 2f);

            renderer.DrawTextScreen(labelX + 10, y + 46, m.ProgressText,
                completed ? new Color3(100, 255, 100) : new Color3(180, 180, 200), 1.5f);

            renderer.DrawTextScreen(panelX + panelW - 200, y + 46,
                $"REWARD: {m.CreditReward} CR", new Color3(255, 220, 80), 1.5f);
        }
    }
}
