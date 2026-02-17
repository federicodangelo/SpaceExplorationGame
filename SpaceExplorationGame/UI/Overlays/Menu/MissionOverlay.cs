using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay for the Mission Board interaction.
/// Shows two tabs: Available missions (accept) and Active missions (turn in / abandon).
/// Used by both SpaceStationOverlay (docked menu) and InteriorState (walkable).
/// </summary>
public class MissionOverlay : ListPanelOverlay
{
    private enum Tab { Available, Active }

    private Tab _currentTab = Tab.Available;
    private List<Mission> _availableMissions = [];

    // Context for generating missions
    private StarSystemData? _currentSystem;
    private ulong _boardSeed;

    protected override string Title => "MISSION BOARD";
    protected override Color3 TitleColor => new(100, 180, 255);
    protected override float PanelWidth => 650;
    protected override float PanelHeight => 580;
    protected override string? ControlsHint => _currentTab == Tab.Available
        ? "A/D: TABS  W/S: SELECT  ENTER: ACCEPT  ESC: CLOSE"
        : "A/D: TABS  W/S: SELECT  ENTER: TURN IN  X: ABANDON  ESC: CLOSE";

    protected override int ItemCount => _currentTab switch
    {
        Tab.Available => _availableMissions.Count,
        Tab.Active => _game?.Player.ActiveMissions.Count ?? 0,
        _ => 0
    };

    protected override float ItemHeight => 80f;
    protected override float ListOffsetY => 45f; // after tab bar

    private Game? _game;

    /// <summary>Open the mission board with context for generating available missions.</summary>
    public void Open(Game game, StarSystemData currentSystem, ulong boardSeed)
    {
        _game = game;
        _currentSystem = currentSystem;
        _boardSeed = boardSeed;

        // Generate available missions for this board
        var allMissions = MissionGenerator.GenerateBoardMissions(
            game.Seeds, boardSeed, currentSystem, game.GalaxyData);

        // Filter out missions already claimed by the player
        _availableMissions = allMissions
            .Where(m => !game.Player.ClaimedMissionIds.Contains(m.Id))
            .ToList();

        _currentTab = game.Player.HasCompletedMissions ? Tab.Active : Tab.Available;

        base.Open();
    }

    /// <summary>Legacy Open() for backwards compatibility (no mission generation context).</summary>
    public override void Open()
    {
        // Fallback: open with empty available missions (active tab only)
        _availableMissions = [];
        _currentTab = Tab.Active;
        base.Open();
    }

    // ── Tab navigation callbacks ──

    protected override void OnNavigateLeft(Game game)
    {
        _currentTab = Tab.Available;
        SelectedIndex = 0;
    }

    protected override void OnNavigateRight(Game game)
    {
        _currentTab = Tab.Active;
        SelectedIndex = 0;
    }

    // ── Item action callbacks ──

    protected override void OnItemConfirmed(Game game, int index)
    {
        if (_currentTab == Tab.Available && index < _availableMissions.Count)
        {
            var mission = _availableMissions[index];
            if (game.Player.ActiveMissions.Count >= PlayerData.MaxActiveMissions)
            {
                SetStatus($"MAX {PlayerData.MaxActiveMissions} ACTIVE MISSIONS!", 2.5f);
            }
            else
            {
                game.Player.AcceptMission(mission);
                _availableMissions.RemoveAt(index);
                ClampSelection();
                SetStatus("MISSION ACCEPTED!", 2f);
            }
        }
        else if (_currentTab == Tab.Active && index < game.Player.ActiveMissions.Count)
        {
            var mission = game.Player.ActiveMissions[index];
            if (mission.Status == MissionStatus.Completed)
            {
                if (_currentSystem != null && mission.TurnIn.IsSystem(_currentSystem.Index))
                {
                    int reward = game.Player.TurnInMission(mission);
                    ClampSelection();
                    SetStatus($"MISSION COMPLETE! +{reward} CREDITS", 2.5f);
                }
                else
                {
                    string sysName = mission.TurnIn.SystemName.ToUpper();
                    SetStatus($"MUST TURN IN AT {sysName}", 2.5f);
                }
            }
        }
    }

    protected override void OnItemSecondary(Game game, int index)
    {
        if (_currentTab == Tab.Active && index < game.Player.ActiveMissions.Count)
        {
            var mission = game.Player.ActiveMissions[index];
            game.Player.AbandonMission(mission);
            ClampSelection();
            SetStatus("MISSION ABANDONED", 2f);
        }
    }

    // ── Rendering ──

    protected override void RenderPanelContent(Game game, SpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        // Mission count display
        int activeCount = game.Player.ActiveMissions.Count;
        int completedCount = game.Player.ActiveMissions.Count(m => m.Status == MissionStatus.Completed);
        string countText = $"ACTIVE: {activeCount}/{PlayerData.MaxActiveMissions}";
        if (completedCount > 0) countText += $"  READY: {completedCount}";
        renderer.DrawTextScreen(panelX + panelW - 300, PanelY + 17, countText,
            new Color3(180, 180, 200), 1.5f);

        // Tab bar
        float tabY = contentY - 7;
        float tabW = panelW / 2f - 20;

        bool availSel = _currentTab == Tab.Available;
        renderer.DrawRectScreen(panelX + 10, tabY, tabW, 28,
            availSel ? new Color3(40, 50, 80) : new Color3(20, 20, 40));
        renderer.DrawTextScreen(panelX + 15, tabY + 5,
            $"< AVAILABLE ({_availableMissions.Count}) >",
            availSel ? new Color3(100, 255, 200) : new Color3(100, 100, 130), 2f);

        bool activeSel = _currentTab == Tab.Active;
        renderer.DrawRectScreen(panelX + 10 + tabW + 20, tabY, tabW, 28,
            activeSel ? new Color3(40, 50, 80) : new Color3(20, 20, 40));
        string activeLabel = completedCount > 0
            ? $"< ACTIVE ({activeCount}) [{completedCount} READY] >"
            : $"< ACTIVE ({activeCount}) >";
        renderer.DrawTextScreen(panelX + 15 + tabW + 20, tabY + 5,
            activeLabel,
            activeSel ? new Color3(100, 255, 200) : new Color3(100, 100, 130), 2f);

        renderer.DrawLineScreen(panelX + 15, tabY + 32, panelX + panelW - 15, tabY + 32,
            new Color3(60, 60, 100));

        // Mission list
        float listY = contentY + ListOffsetY;
        float listH = contentH - ListOffsetY;

        if (_currentTab == Tab.Available)
            RenderAvailableMissions(renderer, panelX, listY, panelW, listH);
        else
            RenderActiveMissions(renderer, panelX, listY, panelW, listH, game);
    }

    private void RenderAvailableMissions(SpriteRenderer renderer, float panelX, float startY,
        float panelW, float listH)
    {
        if (_availableMissions.Count == 0)
        {
            renderer.DrawTextScreen(panelX + 20, startY + 20, "NO MISSIONS AVAILABLE",
                new Color3(120, 120, 140), 2f);
            renderer.DrawTextScreen(panelX + 20, startY + 45, "Check other stations for missions.",
                new Color3(90, 90, 110), 1.5f);
            return;
        }

        for (int i = 0; i < _availableMissions.Count; i++)
        {
            float y = startY + i * ItemHeight;
            if (y + ItemHeight > startY + listH) break;

            var m = _availableMissions[i];
            bool selected = i == SelectedIndex;

            if (selected)
                renderer.DrawRectScreen(panelX + 5, y, panelW - 10, ItemHeight - 4,
                    new Color3(35, 40, 65));

            // Type badge
            renderer.DrawTextScreen(panelX + 15, y + 5, $"[{m.TypeLabel}]", m.TypeColor, 1.5f);

            // Title
            float titleX = panelX + 15 + renderer.MeasureText($"[{m.TypeLabel}]", 1.5f) + 10;
            renderer.DrawTextScreen(titleX, y + 5, m.Title,
                selected ? new Color3(255, 255, 220) : new Color3(200, 200, 200), 2f);

            // Description
            renderer.DrawTextScreen(panelX + 25, y + 28, m.Description,
                new Color3(140, 140, 160), 1.5f);

            // Reward and turn-in location
            renderer.DrawTextScreen(panelX + 25, y + 48,
                $"REWARD: {m.CreditReward} CREDITS", new Color3(255, 220, 80), 1.5f);
            renderer.DrawTextScreen(panelX + panelW - 250, y + 48,
                $"TURN IN: {m.TurnIn.SystemName.ToUpper()}", new Color3(160, 140, 200), 1.2f);

            // Target info
            if (m.Target.HasSystem)
            {
                string targetInfo = m.Target.HasPlanet
                    ? $"TARGET: {m.Target.PlanetName?.ToUpper()} IN {m.Target.SystemName.ToUpper()}"
                    : $"TARGET: {m.Target.SystemName.ToUpper()}";
                renderer.DrawTextScreen(panelX + 25, y + 63, targetInfo,
                    new Color3(120, 160, 200), 1.2f);
            }
        }
    }

    private void RenderActiveMissions(SpriteRenderer renderer, float panelX, float startY,
        float panelW, float listH, Game game)
    {
        var active = game.Player.ActiveMissions;
        if (active.Count == 0)
        {
            renderer.DrawTextScreen(panelX + 20, startY + 20, "NO ACTIVE MISSIONS",
                new Color3(120, 120, 140), 2f);
            renderer.DrawTextScreen(panelX + 20, startY + 45, "Accept missions from the Available tab.",
                new Color3(90, 90, 110), 1.5f);
            return;
        }

        for (int i = 0; i < active.Count; i++)
        {
            float y = startY + i * ItemHeight;
            if (y + ItemHeight > startY + listH) break;

            var m = active[i];
            bool selected = i == SelectedIndex;
            bool completed = m.Status == MissionStatus.Completed;

            if (selected)
                renderer.DrawRectScreen(panelX + 5, y, panelW - 10, ItemHeight - 4,
                    completed ? new Color3(30, 50, 30) : new Color3(35, 40, 65));

            string statusTag = completed ? "[COMPLETE]" : "[IN PROGRESS]";
            var statusColor = completed ? new Color3(100, 255, 100) : new Color3(255, 200, 80);
            renderer.DrawTextScreen(panelX + 15, y + 5, statusTag, statusColor, 1.5f);

            float afterStatus = panelX + 15 + renderer.MeasureText(statusTag, 1.5f) + 8;
            renderer.DrawTextScreen(afterStatus, y + 5, $"[{m.TypeLabel}]", m.TypeColor, 1.5f);

            renderer.DrawTextScreen(panelX + 15, y + 24, m.Title,
                selected ? new Color3(255, 255, 220) : new Color3(200, 200, 200), 2f);

            renderer.DrawTextScreen(panelX + 25, y + 46, m.ProgressText,
                completed ? new Color3(100, 255, 100) : new Color3(180, 180, 200), 1.5f);

            renderer.DrawTextScreen(panelX + panelW - 200, y + 46,
                $"REWARD: {m.CreditReward} CR", new Color3(255, 220, 80), 1.5f);

            if (completed && selected)
            {
                bool canTurnIn = _currentSystem != null && m.TurnIn.IsSystem(_currentSystem.Index);
                if (canTurnIn)
                    renderer.DrawTextScreen(panelX + 25, y + 62,
                        "PRESS ENTER TO TURN IN", new Color3(100, 255, 100), 1.2f);
                else
                    renderer.DrawTextScreen(panelX + 25, y + 62,
                        $"TURN IN AT {m.TurnIn.SystemName.ToUpper()}", new Color3(255, 180, 80), 1.2f);
            }
        }
    }
}
