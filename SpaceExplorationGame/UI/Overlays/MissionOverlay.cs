using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay for the Mission Board interaction.
/// Shows two tabs: Available missions (accept) and Active missions (turn in / abandon).
/// Used by both SpaceStationOverlay (docked menu) and InteriorState (walkable).
/// </summary>
public class MissionOverlay : OverlayBase
{
    private enum Tab { Available, Active }

    private Tab _currentTab = Tab.Available;
    private int _selectedIndex;
    private List<Mission> _availableMissions = [];
    private string? _statusMessage;
    private float _statusTimer;

    // Context for generating missions
    private StarSystemData? _currentSystem;
    private ulong _boardSeed;

    /// <summary>Open the mission board with context for generating available missions.</summary>
    public void Open(Game game, StarSystemData currentSystem, ulong boardSeed)
    {
        _currentSystem = currentSystem;
        _boardSeed = boardSeed;

        // Generate available missions for this board
        var galaxySystems = GalaxyGenerator.Generate(game.Seeds.GetGalaxyRandom());
        var allMissions = MissionGenerator.GenerateBoardMissions(
            game.Seeds, boardSeed, currentSystem, galaxySystems);

        // Filter out missions already claimed by the player
        _availableMissions = allMissions
            .Where(m => !game.Player.ClaimedMissionIds.Contains(m.Id))
            .ToList();

        _currentTab = game.Player.HasCompletedMissions ? Tab.Active : Tab.Available;
        _selectedIndex = 0;
        _statusMessage = null;
        _statusTimer = 0;
        IsOpen = true;
    }

    /// <summary>Legacy Open() for backwards compatibility (no mission generation context).</summary>
    public void Open()
    {
        // Fallback: open with empty available missions (active tab only)
        _availableMissions = [];
        _currentTab = Tab.Active;
        _selectedIndex = 0;
        _statusMessage = null;
        _statusTimer = 0;
        IsOpen = true;
    }

    private int CurrentListCount(Game game) => _currentTab switch
    {
        Tab.Available => _availableMissions.Count,
        Tab.Active => game.Player.ActiveMissions.Count,
        _ => 0
    };

    /// <summary>Process input for the mission overlay. Returns true if the overlay is active.</summary>
    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close();
            return true;
        }

        // Tab switching with left/right
        if (input.IsKeyPressed(SDL.Scancode.Left) || input.IsKeyPressed(SDL.Scancode.A))
        {
            _currentTab = Tab.Available;
            _selectedIndex = 0;
        }
        else if (input.IsKeyPressed(SDL.Scancode.Right) || input.IsKeyPressed(SDL.Scancode.D))
        {
            _currentTab = Tab.Active;
            _selectedIndex = 0;
        }

        int count = CurrentListCount(game);

        // Navigation
        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
        {
            _selectedIndex = count > 0 ? (_selectedIndex - 1 + count) % count : 0;
        }
        else if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
        {
            _selectedIndex = count > 0 ? (_selectedIndex + 1) % count : 0;
        }

        // Confirm action
        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
        {
            if (_currentTab == Tab.Available && _selectedIndex < _availableMissions.Count)
            {
                var mission = _availableMissions[_selectedIndex];
                if (game.Player.ActiveMissions.Count >= PlayerData.MaxActiveMissions)
                {
                    _statusMessage = $"MAX {PlayerData.MaxActiveMissions} ACTIVE MISSIONS!";
                    _statusTimer = 2.5f;
                }
                else
                {
                    game.Player.AcceptMission(mission);
                    _availableMissions.RemoveAt(_selectedIndex);
                    _selectedIndex = Math.Min(_selectedIndex, _availableMissions.Count - 1);
                    if (_selectedIndex < 0) _selectedIndex = 0;
                    _statusMessage = "MISSION ACCEPTED!";
                    _statusTimer = 2f;
                }
            }
            else if (_currentTab == Tab.Active && _selectedIndex < game.Player.ActiveMissions.Count)
            {
                var mission = game.Player.ActiveMissions[_selectedIndex];
                if (mission.Status == MissionStatus.Completed)
                {
                    int reward = game.Player.TurnInMission(mission);
                    _selectedIndex = Math.Min(_selectedIndex, game.Player.ActiveMissions.Count - 1);
                    if (_selectedIndex < 0) _selectedIndex = 0;
                    _statusMessage = $"MISSION COMPLETE! +{reward} CREDITS";
                    _statusTimer = 2.5f;
                }
            }
        }

        // Abandon active mission with X key
        if (input.IsKeyPressed(SDL.Scancode.X))
        {
            if (_currentTab == Tab.Active && _selectedIndex < game.Player.ActiveMissions.Count)
            {
                var mission = game.Player.ActiveMissions[_selectedIndex];
                game.Player.AbandonMission(mission);
                _selectedIndex = Math.Min(_selectedIndex, game.Player.ActiveMissions.Count - 1);
                if (_selectedIndex < 0) _selectedIndex = 0;
                _statusMessage = "MISSION ABANDONED";
                _statusTimer = 2f;
            }
        }

        return true;
    }

    /// <summary>Update timers.</summary>
    public override void Update(Game game, float dt)
    {
        if (!IsOpen) return;
        if (_statusTimer > 0)
        {
            _statusTimer -= dt;
            if (_statusTimer <= 0) _statusMessage = null;
        }
    }

    /// <summary>Render the mission overlay.</summary>
    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Semi-transparent background
        renderer.DrawRectScreen(0, 0, w, h, new Color4(0, 0, 0, 150));

        float panelW = 650;
        float panelH = 580;
        float panelX = w / 2f - panelW / 2f;
        float panelY = h / 2f - panelH / 2f;

        // Panel border + background
        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, new Color4(60, 60, 100, 200));
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, new Color4(15, 15, 35, 245));

        // Title
        renderer.DrawTextScreen(panelX + 15, panelY + 10, "MISSION BOARD", new Color3(100, 180, 255), 2.5f);

        // Mission count display
        int activeCount = game.Player.ActiveMissions.Count;
        int completedCount = game.Player.ActiveMissions.Count(m => m.Status == MissionStatus.Completed);
        string countText = $"ACTIVE: {activeCount}/{PlayerData.MaxActiveMissions}";
        if (completedCount > 0) countText += $"  READY: {completedCount}";
        renderer.DrawTextScreen(panelX + panelW - 300, panelY + 17, countText, new Color3(180, 180, 200), 1.5f);

        renderer.DrawLineScreen(panelX + 15, panelY + 42, panelX + panelW - 15, panelY + 42, new Color3(60, 60, 100));

        // Tab bar
        float tabY = panelY + 48;
        float tabW = panelW / 2f - 20;

        // Available tab
        bool availSel = _currentTab == Tab.Available;
        renderer.DrawRectScreen(panelX + 10, tabY, tabW, 28,
            availSel ? new Color3(40, 50, 80) : new Color3(20, 20, 40));
        renderer.DrawTextScreen(panelX + 15, tabY + 5,
            $"< AVAILABLE ({_availableMissions.Count}) >",
            availSel ? new Color3(100, 255, 200) : new Color3(100, 100, 130), 2f);

        // Active tab
        bool activeSel = _currentTab == Tab.Active;
        renderer.DrawRectScreen(panelX + 10 + tabW + 20, tabY, tabW, 28,
            activeSel ? new Color3(40, 50, 80) : new Color3(20, 20, 40));
        string activeLabel = completedCount > 0
            ? $"< ACTIVE ({activeCount}) [{completedCount} READY] >"
            : $"< ACTIVE ({activeCount}) >";
        renderer.DrawTextScreen(panelX + 15 + tabW + 20, tabY + 5,
            activeLabel,
            activeSel ? new Color3(100, 255, 200) : new Color3(100, 100, 130), 2f);

        renderer.DrawLineScreen(panelX + 15, tabY + 32, panelX + panelW - 15, tabY + 32, new Color3(60, 60, 100));

        // Mission list
        float listY = tabY + 40;
        float listH = panelH - (listY - panelY) - 60; // leave room for controls

        if (_currentTab == Tab.Available)
            RenderAvailableMissions(renderer, panelX, listY, panelW, listH);
        else
            RenderActiveMissions(renderer, panelX, listY, panelW, listH, game);

        // Status message
        if (_statusMessage != null)
        {
            float msgW = renderer.MeasureText(_statusMessage, 2f);
            renderer.DrawRectScreen(panelX + panelW / 2f - msgW / 2f - 10, panelY + panelH - 55,
                msgW + 20, 25, new Color4(0, 60, 0, 220));
            renderer.DrawTextScreen(panelX + panelW / 2f - msgW / 2f, panelY + panelH - 52,
                _statusMessage, new Color3(100, 255, 100), 2f);
        }

        // Controls hint
        float ctrlY = panelY + panelH - 25;
        string controls = _currentTab == Tab.Available
            ? "A/D: TABS  W/S: SELECT  ENTER: ACCEPT  ESC: CLOSE"
            : "A/D: TABS  W/S: SELECT  ENTER: TURN IN  X: ABANDON  ESC: CLOSE";
        renderer.DrawTextScreen(panelX + 10, ctrlY, controls, new Color3(100, 100, 130), 1.5f);
    }

    private void RenderAvailableMissions(SpriteRenderer renderer, float panelX, float startY, float panelW, float listH)
    {
        if (_availableMissions.Count == 0)
        {
            renderer.DrawTextScreen(panelX + 20, startY + 20, "NO MISSIONS AVAILABLE", new Color3(120, 120, 140), 2f);
            renderer.DrawTextScreen(panelX + 20, startY + 45, "Check other stations for missions.", new Color3(90, 90, 110), 1.5f);
            return;
        }

        float itemH = 80;
        for (int i = 0; i < _availableMissions.Count; i++)
        {
            float y = startY + i * itemH;
            if (y + itemH > startY + listH) break; // clip

            var m = _availableMissions[i];
            bool selected = i == _selectedIndex;

            if (selected)
                renderer.DrawRectScreen(panelX + 5, y, panelW - 10, itemH - 4, new Color3(35, 40, 65));

            // Type badge
            renderer.DrawTextScreen(panelX + 15, y + 5, $"[{m.TypeLabel}]", m.TypeColor, 1.5f);

            // Title
            float titleX = panelX + 15 + renderer.MeasureText($"[{m.TypeLabel}]", 1.5f) + 10;
            renderer.DrawTextScreen(titleX, y + 5, m.Title,
                selected ? new Color3(255, 255, 220) : new Color3(200, 200, 200), 2f);

            // Description
            renderer.DrawTextScreen(panelX + 25, y + 28, m.Description, new Color3(140, 140, 160), 1.5f);

            // Reward
            renderer.DrawTextScreen(panelX + 25, y + 48,
                $"REWARD: {m.CreditReward} CREDITS", new Color3(255, 220, 80), 1.5f);

            // Target info
            if (m.TargetSystemName.Length > 0)
            {
                string targetInfo = m.Type switch
                {
                    MissionType.Exploration => $"TARGET: {m.TargetPlanetName?.ToUpper()} IN {m.TargetSystemName.ToUpper()}",
                    _ => $"TARGET: {m.TargetSystemName.ToUpper()}"
                };
                renderer.DrawTextScreen(panelX + 25, y + 63, targetInfo, new Color3(120, 160, 200), 1.2f);
            }
        }
    }

    private void RenderActiveMissions(SpriteRenderer renderer, float panelX, float startY, float panelW, float listH, Game game)
    {
        var active = game.Player.ActiveMissions;
        if (active.Count == 0)
        {
            renderer.DrawTextScreen(panelX + 20, startY + 20, "NO ACTIVE MISSIONS", new Color3(120, 120, 140), 2f);
            renderer.DrawTextScreen(panelX + 20, startY + 45, "Accept missions from the Available tab.", new Color3(90, 90, 110), 1.5f);
            return;
        }

        float itemH = 80;
        for (int i = 0; i < active.Count; i++)
        {
            float y = startY + i * itemH;
            if (y + itemH > startY + listH) break; // clip

            var m = active[i];
            bool selected = i == _selectedIndex;
            bool completed = m.Status == MissionStatus.Completed;

            if (selected)
                renderer.DrawRectScreen(panelX + 5, y, panelW - 10, itemH - 4,
                    completed ? new Color3(30, 50, 30) : new Color3(35, 40, 65));

            // Status badge
            string statusTag = completed ? "[COMPLETE]" : "[IN PROGRESS]";
            var statusColor = completed ? new Color3(100, 255, 100) : new Color3(255, 200, 80);
            renderer.DrawTextScreen(panelX + 15, y + 5, statusTag, statusColor, 1.5f);

            // Type badge
            float afterStatus = panelX + 15 + renderer.MeasureText(statusTag, 1.5f) + 8;
            renderer.DrawTextScreen(afterStatus, y + 5, $"[{m.TypeLabel}]", m.TypeColor, 1.5f);

            // Title
            renderer.DrawTextScreen(panelX + 15, y + 24, m.Title,
                selected ? new Color3(255, 255, 220) : new Color3(200, 200, 200), 2f);

            // Progress
            renderer.DrawTextScreen(panelX + 25, y + 46, m.ProgressText,
                completed ? new Color3(100, 255, 100) : new Color3(180, 180, 200), 1.5f);

            // Reward
            renderer.DrawTextScreen(panelX + panelW - 200, y + 46,
                $"REWARD: {m.CreditReward} CR", new Color3(255, 220, 80), 1.5f);

            // Turn-in hint for completed missions
            if (completed && selected)
            {
                renderer.DrawTextScreen(panelX + 25, y + 62,
                    "PRESS ENTER TO TURN IN", new Color3(100, 255, 100), 1.2f);
            }
        }
    }
}
