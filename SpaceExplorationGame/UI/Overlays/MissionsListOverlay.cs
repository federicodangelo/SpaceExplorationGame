using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay opened from the in-game menu that shows all active missions.
/// Allows the player to switch the tracked mission or abandon missions.
/// </summary>
public class MissionsListOverlay : OverlayBase
{
    private int _selectedIndex;
    private string? _statusMessage;
    private float _statusTimer;

    public void Open(Game game)
    {
        _selectedIndex = 0;
        _statusMessage = null;
        _statusTimer = 0;

        // Pre-select the currently tracked mission if any
        var tracked = game.Player.GetTrackedMission();
        if (tracked != null)
        {
            int idx = game.Player.ActiveMissions.IndexOf(tracked);
            if (idx >= 0) _selectedIndex = idx;
        }

        IsOpen = true;
    }

    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close();
            return true;
        }

        int count = game.Player.ActiveMissions.Count;

        // Navigation
        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            _selectedIndex = count > 0 ? (_selectedIndex - 1 + count) % count : 0;
        else if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            _selectedIndex = count > 0 ? (_selectedIndex + 1) % count : 0;

        // Track selected mission (Enter / E)
        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
        {
            if (_selectedIndex >= 0 && _selectedIndex < count)
            {
                game.Player.TrackedMissionIndex = _selectedIndex;
                var m = game.Player.ActiveMissions[_selectedIndex];
                _statusMessage = $"TRACKING: [{m.TypeLabel}] {m.Title}";
                _statusTimer = 2f;
            }
        }

        // Abandon mission (X)
        if (input.IsKeyPressed(SDL.Scancode.X))
        {
            if (_selectedIndex >= 0 && _selectedIndex < count)
            {
                var mission = game.Player.ActiveMissions[_selectedIndex];
                game.Player.AbandonMission(mission);
                count = game.Player.ActiveMissions.Count;
                _selectedIndex = Math.Min(_selectedIndex, count - 1);
                if (_selectedIndex < 0) _selectedIndex = 0;
                _statusMessage = "MISSION ABANDONED";
                _statusTimer = 2f;
            }
        }

        return true;
    }

    public override void Update(Game game, float dt)
    {
        if (!IsOpen) return;
        if (_statusTimer > 0)
        {
            _statusTimer -= dt;
            if (_statusTimer <= 0) _statusMessage = null;
        }
    }

    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Semi-transparent background
        renderer.DrawRectScreen(0, 0, w, h, new Color4(0, 0, 0, 160));

        var active = game.Player.ActiveMissions;
        float itemH = 80;
        float panelW = 620;
        float panelH = Math.Max(220, 90 + active.Count * itemH + 60);
        float panelX = w / 2f - panelW / 2f;
        float panelY = h / 2f - panelH / 2f;

        // Panel border + background
        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, new Color4(60, 60, 100, 200));
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, new Color4(15, 15, 35, 245));

        // Title
        string title = "ACTIVE MISSIONS";
        float titleScale = 2.5f;
        float titleW = renderer.MeasureText(title, titleScale);
        renderer.DrawTextScreen(w / 2f - titleW / 2f, panelY + 12, title, new Color3(100, 180, 255), titleScale);

        // Mission count
        string countText = $"{active.Count}/{PlayerData.MaxActiveMissions}";
        float countW = renderer.MeasureText(countText, 1.5f);
        renderer.DrawTextScreen(panelX + panelW - countW - 15, panelY + 17, countText, new Color3(180, 180, 200), 1.5f);

        renderer.DrawLineScreen(panelX + 15, panelY + 42, panelX + panelW - 15, panelY + 42, new Color3(60, 60, 100));

        // Mission list
        float listY = panelY + 50;

        if (active.Count == 0)
        {
            string empty = "NO ACTIVE MISSIONS";
            float emptyW = renderer.MeasureText(empty, 2f);
            renderer.DrawTextScreen(w / 2f - emptyW / 2f, listY + 20, empty, new Color3(120, 120, 140), 2f);

            string hint = "Accept missions from station mission boards.";
            float hintW = renderer.MeasureText(hint, 1.5f);
            renderer.DrawTextScreen(w / 2f - hintW / 2f, listY + 48, hint, new Color3(90, 90, 110), 1.5f);
        }
        else
        {
            int trackedIdx = game.Player.TrackedMissionIndex;
            // If no explicit tracked index, determine which one GetTrackedMission returns
            if (trackedIdx < 0)
            {
                var tracked = game.Player.GetTrackedMission();
                if (tracked != null) trackedIdx = active.IndexOf(tracked);
            }

            for (int i = 0; i < active.Count; i++)
            {
                float y = listY + i * itemH;
                var m = active[i];
                bool selected = i == _selectedIndex;
                bool isTracked = i == trackedIdx;
                bool completed = m.Status == MissionStatus.Completed;

                // Selection highlight
                if (selected)
                    renderer.DrawRectScreen(panelX + 5, y, panelW - 10, itemH - 4,
                        completed ? new Color3(30, 50, 30) : new Color3(35, 40, 65));

                // Tracked indicator
                if (isTracked)
                {
                    renderer.DrawTextScreen(panelX + 10, y + 5, ">>>", new Color3(100, 255, 200), 1.5f);
                }

                float labelX = panelX + 40;

                // Status badge
                string statusTag = completed ? "[COMPLETE]" : "[ACTIVE]";
                var statusColor = completed ? new Color3(100, 255, 100) : new Color3(255, 200, 80);
                renderer.DrawTextScreen(labelX, y + 5, statusTag, statusColor, 1.5f);

                // Type badge
                float afterStatus = labelX + renderer.MeasureText(statusTag, 1.5f) + 8;
                renderer.DrawTextScreen(afterStatus, y + 5, $"[{m.TypeLabel}]", m.TypeColor, 1.5f);

                // Title
                renderer.DrawTextScreen(labelX, y + 24, m.Title,
                    selected ? new Color3(255, 255, 220) : new Color3(200, 200, 200), 2f);

                // Progress
                renderer.DrawTextScreen(labelX + 10, y + 46, m.ProgressText,
                    completed ? new Color3(100, 255, 100) : new Color3(180, 180, 200), 1.5f);

                // Reward
                renderer.DrawTextScreen(panelX + panelW - 200, y + 46,
                    $"REWARD: {m.CreditReward} CR", new Color3(255, 220, 80), 1.5f);
            }
        }

        // Status message
        if (_statusMessage != null)
        {
            float msgScale = 2f;
            float msgW = renderer.MeasureText(_statusMessage, msgScale);
            float msgY = panelY + panelH - 55;
            renderer.DrawRectScreen(w / 2f - msgW / 2f - 10, msgY, msgW + 20, 25, new Color4(0, 60, 0, 220));
            renderer.DrawTextScreen(w / 2f - msgW / 2f, msgY + 3, _statusMessage, new Color3(100, 255, 100), msgScale);
        }

        // Controls hint
        float ctrlY = panelY + panelH - 25;
        string controls = active.Count > 0
            ? "W/S: SELECT  ENTER: TRACK  X: ABANDON  ESC: BACK"
            : "ESC: BACK";
        float ctrlW = renderer.MeasureText(controls, 1.5f);
        renderer.DrawTextScreen(w / 2f - ctrlW / 2f, ctrlY, controls, new Color3(100, 100, 130), 1.5f);
    }
}
