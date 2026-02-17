using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Map.Base;

namespace SpaceExplorationGame.UI.Overlays.Map;

/// <summary>
/// Map panel showing the galaxy star chart with FTL travel.
/// Supports hover, click-to-select, double-click/Enter to travel.
/// </summary>
public class GalaxyMapPanel : MapPanelBase
{
    // ── State ──
    private List<StarSystemData> _starSystems = [];
    private int _selectedSystemIndex = -1;
    private int _hoveredSystemIndex = -1;
    private List<BackgroundStar> _backgroundStars = [];
    private List<NebulaCloud> _nebulae = [];
    private float _lastClickTime;
    private int _lastClickSystem = -1;

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    public override void Open(Game game)
    {
        _starSystems = game.GalaxyData;
        _lastClickSystem = -1;
        InitGalaxyBackground(game);
    }

    public override void Close(Game game)
    {
        _nebulae.Clear();
        _backgroundStars.Clear();
    }

    public override void SetupCamera(Game game)
    {
        Camera.ZoomMin = GameConfig.GalaxyMapZoomMin;
        Camera.ZoomMax = GameConfig.GalaxyMapZoomMax;
        _selectedSystemIndex = -1;
        _hoveredSystemIndex = -1;

        if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < _starSystems.Count)
        {
            _selectedSystemIndex = game.Player.CurrentStarSystemIndex;
            Camera.Position = _starSystems[_selectedSystemIndex].GalaxyPosition;
        }
        else
        {
            Camera.Position = new Vector2(GameConfig.GalaxyWidth * GameConfig.TileSize / 2f,
                                          GameConfig.GalaxyHeight * GameConfig.TileSize / 2f);
        }
        Camera.Zoom = GameConfig.GalaxyMapZoomDefault;
        Camera.ClampZoom();
    }

    // ─────────────────────────────────────────────────────────────
    //  INPUT
    // ─────────────────────────────────────────────────────────────

    public override bool UpdateInput(Game game)
    {
        var input = game.Input;
        Vector2 currentMouse = new(input.MouseX, input.MouseY);

        HandleZoomAndPan(input, currentMouse);

        // Hover
        _hoveredSystemIndex = -1;
        if (IsMouseInMap(currentMouse))
        {
            float bestDist = float.MaxValue;
            for (int i = 0; i < _starSystems.Count; i++)
            {
                var screenPos = Camera.WorldToScreen(_starSystems[i].GalaxyPosition);
                float distSq = (currentMouse - screenPos).LengthSquared();
                float hitR = MathF.Max(_starSystems[i].StarRadius * 2f * Camera.Zoom, 20f);
                if (distSq < hitR * hitR && distSq < bestDist) { bestDist = distSq; _hoveredSystemIndex = i; }
            }
        }

        if (input.IsMouseReleased(1))
        {
            if (!IsPanning && _hoveredSystemIndex >= 0)
            {
                float now = (float)game.GlobalTime;
                if (_hoveredSystemIndex == _lastClickSystem && (now - _lastClickTime) < DoubleClickTime)
                {
                    _selectedSystemIndex = _hoveredSystemIndex;
                    TravelToSelected(game);
                    _lastClickSystem = -1;
                }
                else
                {
                    _selectedSystemIndex = _hoveredSystemIndex;
                    _lastClickTime = now;
                    _lastClickSystem = _hoveredSystemIndex;
                }
            }
            else if (!IsPanning) _lastClickSystem = -1;
            IsPanning = false;
        }

        if (input.IsKeyPressed(SDL.Scancode.Return) && _selectedSystemIndex >= 0)
            TravelToSelected(game);

        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  GALAXY HELPERS
    // ─────────────────────────────────────────────────────────────

    private void InitGalaxyBackground(Game game)
    {
        var bgRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xDEADBEEF);
        _backgroundStars.Clear();
        for (int i = 0; i < 500; i++)
            _backgroundStars.Add(new BackgroundStar(
                bgRng.NextFloat(0, GameConfig.GalaxyWidth * GameConfig.TileSize),
                bgRng.NextFloat(0, GameConfig.GalaxyHeight * GameConfig.TileSize),
                (byte)bgRng.NextInt(30, 120)));

        var nebRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xFACEFEED);
        _nebulae.Clear();
        for (int i = 0; i < 8; i++)
        {
            byte[] choices = [(byte)nebRng.NextInt(20, 60), (byte)nebRng.NextInt(10, 40), (byte)nebRng.NextInt(30, 70)];
            int ci = nebRng.NextInt(0, 3);
            _nebulae.Add(new NebulaCloud(
                nebRng.NextFloat(0, GameConfig.GalaxyWidth * GameConfig.TileSize),
                nebRng.NextFloat(0, GameConfig.GalaxyHeight * GameConfig.TileSize),
                nebRng.NextFloat(200, 600),
                new Color3(ci == 0 ? choices[0] : (byte)10, ci == 1 ? choices[1] : (byte)10, ci == 2 ? choices[2] : (byte)15)));
        }
    }

    private float GetSystemDistance(int indexA, int indexB)
    {
        if (indexA < 0 || indexB < 0 || indexA >= _starSystems.Count || indexB >= _starSystems.Count)
            return float.MaxValue;
        return (_starSystems[indexA].GalaxyPosition - _starSystems[indexB].GalaxyPosition).Length();
    }

    private float GetFuelCost(int fromIndex, int toIndex) =>
        GetSystemDistance(fromIndex, toIndex) * GameConfig.FuelPerDistanceUnit;

    private float GetFtlRange(Game game)
    {
        var stats = game.Player.GetCombinedStats();
        return stats.FtlRange > 0 ? stats.FtlRange : GameConfig.FtlMaxRange;
    }

    private bool IsSystemReachable(Game game, int targetIndex)
    {
        int current = game.Player.CurrentStarSystemIndex;
        if (current == targetIndex) return true;
        float distance = GetSystemDistance(current, targetIndex);
        float fuelCost = distance * GameConfig.FuelPerDistanceUnit;
        return distance <= GetFtlRange(game) && game.Player.ShipFuel >= fuelCost;
    }

    private bool IsInFtlRange(Game game, int fromIndex, int targetIndex) =>
        GetSystemDistance(fromIndex, targetIndex) <= GetFtlRange(game);

    private void TravelToSelected(Game game)
    {
        if (_selectedSystemIndex < 0) return;
        int current = game.Player.CurrentStarSystemIndex;
        if (_selectedSystemIndex == current)
        {
            OnRequestClose?.Invoke(game);
        }
        else if (IsSystemReachable(game, _selectedSystemIndex))
        {
            float fuelCost = GetFuelCost(current, _selectedSystemIndex);
            game.Player.TrySpendFuel(fuelCost);
            game.Player.CurrentStarSystemIndex = _selectedSystemIndex;
            var targetSystem = _starSystems[_selectedSystemIndex];
            _nebulae.Clear();
            _backgroundStars.Clear();
            OnRequestClose?.Invoke(game);
            game.ChangeState(new FTLTransitionState(targetSystem));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  RENDERING
    // ─────────────────────────────────────────────────────────────

    public override void RenderContent(Game game, SpriteRenderer renderer)
    {
        var camera = Camera;
        int currentSys = game.Player.CurrentStarSystemIndex;

        // Background stars
        foreach (var (x, y, brightness) in _backgroundStars)
        {
            var screenPos = camera.WorldToScreen(new Vector2(x, y));
            renderer.DrawRectScreen(screenPos.X, screenPos.Y,
                Math.Max(1, camera.Zoom), Math.Max(1, camera.Zoom),
                new Color3(brightness, brightness, brightness));
        }

        // Nebula clouds
        foreach (var (nx, ny, nr, nColor) in _nebulae)
        {
            renderer.DrawFilledCircle(camera, new Vector2(nx, ny), nr, nColor.WithAlpha(20));
            renderer.DrawFilledCircle(camera, new Vector2(nx + nr * 0.3f, ny - nr * 0.2f), nr * 0.7f, nColor.WithAlpha(15));
            renderer.DrawFilledCircle(camera, new Vector2(nx - nr * 0.4f, ny + nr * 0.3f), nr * 0.5f, nColor.WithAlpha(10));
        }

        // FTL range circle
        if (currentSys >= 0 && currentSys < _starSystems.Count)
        {
            var playerPos = _starSystems[currentSys].GalaxyPosition;
            float ftlRange = GetFtlRange(game);
            renderer.DrawCircle(camera, playerPos, ftlRange, new Color4(40, 80, 40, 200), 64);
            float fuelRange = game.Player.ShipFuel / GameConfig.FuelPerDistanceUnit;
            if (fuelRange < ftlRange)
                renderer.DrawCircle(camera, playerPos, fuelRange, new Color4(80, 160, 200, 80));
        }

        // Star systems
        for (int i = 0; i < _starSystems.Count; i++)
        {
            var sys = _starSystems[i];
            bool isSelected = i == _selectedSystemIndex;
            bool isHovered = i == _hoveredSystemIndex;
            bool isCurrentSystem = i == currentSys;
            bool inRange = currentSys >= 0 && (isCurrentSystem || IsInFtlRange(game, currentSys, i));
            bool reachable = currentSys >= 0 && IsSystemReachable(game, i);

            float radius = sys.StarRadius;
            float displayRadius = radius * 2f;

            byte alpha = 255;
            if (!inRange) alpha = 80;
            else if (!reachable && !isCurrentSystem) alpha = 160;

            // Star glow
            renderer.DrawFilledCircle(camera, sys.GalaxyPosition, displayRadius + 4,
                new Color4(sys.StarColor.R, sys.StarColor.G, sys.StarColor.B, (byte)(alpha / 4)));
            // Star body
            renderer.DrawFilledCircle(camera, sys.GalaxyPosition, displayRadius,
                new Color4(sys.StarColor.R, sys.StarColor.G, sys.StarColor.B, alpha));

            if (inRange && !reachable && !isCurrentSystem)
                renderer.DrawFilledCircle(camera, sys.GalaxyPosition, radius * 0.5f, new Color4(255, 40, 40, 60));

            if (isHovered || isSelected)
            {
                byte ringG = reachable || isCurrentSystem ? (byte)255 : (byte)80;
                byte ringB = reachable || isCurrentSystem ? (byte)255 : (byte)80;
                renderer.DrawCircle(camera, sys.GalaxyPosition, displayRadius + 6, new Color3(255, ringG, ringB));
            }

            float textScale = Math.Max(1f, camera.Zoom);
            byte labelBright = (byte)(inRange ? 200 : 80);
            renderer.DrawText(camera, sys.GalaxyPosition + new Vector2(0, radius + 12),
                sys.Name, new Color3(labelBright, labelBright, labelBright), textScale);
        }

        // Mission markers
        float pulse = (float)(0.5 + 0.5 * Math.Sin(game.GlobalTime * 3.0));
        byte missionAlpha = (byte)(140 + (int)(pulse * 115));
        foreach (var mission in game.Player.ActiveMissions)
        {
            if (mission.Status != MissionStatus.Completed &&
                mission.Target.HasSystem && mission.Target.SystemIndex < _starSystems.Count)
            {
                var targetSys = _starSystems[mission.Target.SystemIndex];
                var mc = mission.TypeColor;
                float mr = targetSys.StarRadius * 2f;
                renderer.DrawCircle(camera, targetSys.GalaxyPosition, mr + 8,
                    new Color4(mc.R, mc.G, mc.B, missionAlpha));
                renderer.DrawCircle(camera, targetSys.GalaxyPosition, mr + 11,
                    new Color4(mc.R, mc.G, mc.B, (byte)(missionAlpha / 3)));
                DrawMissionDiamond(renderer, camera, targetSys.GalaxyPosition, mr, mc, missionAlpha, mission.TypeLabel);
            }

            if (mission.Status == MissionStatus.Completed &&
                mission.TurnIn.HasSystem && mission.TurnIn.SystemIndex < _starSystems.Count)
            {
                var turnInSys = _starSystems[mission.TurnIn.SystemIndex];
                float mr = turnInSys.StarRadius * 2f;
                renderer.DrawCircle(camera, turnInSys.GalaxyPosition, mr + 8,
                    new Color4(100, 255, 100, missionAlpha));
                renderer.DrawCircle(camera, turnInSys.GalaxyPosition, mr + 11,
                    new Color4(100, 255, 100, (byte)(missionAlpha / 3)));
                DrawMissionDiamond(renderer, camera, turnInSys.GalaxyPosition, mr,
                    new Color3(100, 255, 100), missionAlpha, "TURN IN");
            }
        }

        // Player marker
        if (currentSys >= 0 && currentSys < _starSystems.Count)
        {
            var playerSys = _starSystems[currentSys];
            renderer.DrawCircle(camera, playerSys.GalaxyPosition, playerSys.StarRadius * 2f + 10, new Color3(0, 255, 100));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  INFO PANEL
    // ─────────────────────────────────────────────────────────────

    public override void RenderInfoPanel(Game game, SpriteRenderer renderer)
    {
        RenderInfoPanelHeader(renderer, "NAVIGATION DATA");

        float cx = IpX + 12;
        float cy = IpY + 40;

        renderer.DrawTextScreen(cx, cy, "SYSTEMS", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(cx, cy + 16, _starSystems.Count.ToString(), new Color3(200, 220, 255), 1.8f);
        renderer.DrawRectScreen(cx, cy + 42, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        // Fuel
        renderer.DrawTextScreen(cx, cy + 52, "FUEL", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(cx, cy + 68, $"{game.Player.ShipFuel:F1} / {game.Player.ShipMaxFuel:F0}", new Color3(100, 200, 255), 1.8f);
        float fuelBarW = InfoPanelW - 24;
        renderer.DrawRectScreen(cx, cy + 94, fuelBarW, 10, new Color3(40, 40, 40));
        float fuelPct = game.Player.ShipMaxFuel > 0 ? game.Player.ShipFuel / game.Player.ShipMaxFuel : 0;
        renderer.DrawRectScreen(cx, cy + 94, fuelBarW * fuelPct, 10, new Color3(100, 200, 255));

        if (_selectedSystemIndex >= 0 && _selectedSystemIndex != game.Player.CurrentStarSystemIndex)
        {
            float jumpDist = GetSystemDistance(game.Player.CurrentStarSystemIndex, _selectedSystemIndex);
            float jumpCost = jumpDist * GameConfig.FuelPerDistanceUnit;
            float costPct = game.Player.ShipMaxFuel > 0 ? jumpCost / game.Player.ShipMaxFuel : 0;
            float remainPct = fuelPct - costPct;
            if (remainPct < 0) remainPct = 0;
            bool canAffordJump = game.Player.ShipFuel >= jumpCost;
            var costColor = canAffordJump ? new Color4(255, 160, 40, 200) : new Color4(255, 60, 60, 200);
            float costStartX = cx + fuelBarW * remainPct;
            float costW = fuelBarW * fuelPct - fuelBarW * remainPct;
            if (costW > 0)
                renderer.DrawRectScreen(costStartX, cy + 94, costW, 10, costColor);
        }

        renderer.DrawRectScreen(cx, cy + 114, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        // Selected system info
        float selY = cy + 124;
        if (_selectedSystemIndex >= 0)
        {
            var sys = _starSystems[_selectedSystemIndex];
            bool isCurrentSystem = _selectedSystemIndex == game.Player.CurrentStarSystemIndex;
            float distance = isCurrentSystem ? 0 : GetSystemDistance(game.Player.CurrentStarSystemIndex, _selectedSystemIndex);
            float fuelCost = distance * GameConfig.FuelPerDistanceUnit;
            bool inRange = isCurrentSystem || distance <= GetFtlRange(game);
            bool canAfford = isCurrentSystem || game.Player.ShipFuel >= fuelCost;

            renderer.DrawTextScreen(cx, selY, "SELECTED", new Color3(100, 120, 160), 1.3f);
            renderer.DrawTextScreen(cx, selY + 16, sys.Name.ToUpper(), new Color3(200, 220, 255), 1.8f);
            renderer.DrawRectScreen(cx, selY + 42, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));

            renderer.DrawTextScreen(cx, selY + 52, $"CLASS {sys.StarClass} STAR", new Color3(200, 200, 200), 1.5f);
            renderer.DrawTextScreen(cx, selY + 72, $"PLANETS: {sys.PlanetCount}", new Color3(200, 200, 200), 1.5f);
            renderer.DrawTextScreen(cx, selY + 92,
                $"STATION: {(sys.HasSpaceStation ? "YES" : "NO")}",
                sys.HasSpaceStation ? new Color3(100, 255, 200) : new Color3(120, 120, 120), 1.5f);

            string dangerText = $"DANGER: {new string('*', sys.DangerLevel)}{new string('.', 5 - sys.DangerLevel)}";
            byte dangerR = sys.DangerLevel <= 2 ? (byte)100 : sys.DangerLevel <= 3 ? (byte)255 : (byte)255;
            byte dangerG = sys.DangerLevel <= 2 ? (byte)255 : sys.DangerLevel <= 3 ? (byte)200 : (byte)80;
            byte dangerB = sys.DangerLevel <= 2 ? (byte)100 : sys.DangerLevel <= 3 ? (byte)50 : (byte)80;
            renderer.DrawTextScreen(cx, selY + 112, dangerText, new Color3(dangerR, dangerG, dangerB), 1.5f);

            var missionsHere = game.Player.ActiveMissions.Where(m =>
                m.Target.IsSystem(_selectedSystemIndex) ||
                (m.Status == MissionStatus.Completed && m.TurnIn.IsSystem(_selectedSystemIndex))).ToList();

            float missionY = selY + 136;
            if (missionsHere.Count > 0)
            {
                renderer.DrawRectScreen(cx, missionY - 4, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
                foreach (var m in missionsHere)
                {
                    var mc = m.TypeColor;
                    string statusTag = m.Status == MissionStatus.Completed ? " [DONE]" : "";
                    renderer.DrawTextScreen(cx, missionY, $"[!] {m.TypeLabel}{statusTag}", new Color3(mc.R, mc.G, mc.B), 1.3f);
                    missionY += 16;
                }
                missionY += 4;
            }

            renderer.DrawRectScreen(cx, missionY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
            missionY += 10;

            if (isCurrentSystem)
            {
                renderer.DrawTextScreen(cx, missionY, "YOU ARE HERE", new Color3(100, 255, 200), 1.5f);
                renderer.DrawTextScreen(cx, missionY + 20, "[ENTER] CLOSE MAP", new Color3(100, 255, 100), 1.5f);
            }
            else
            {
                renderer.DrawTextScreen(cx, missionY, $"DISTANCE: {distance:F0}", new Color3(200, 200, 200), 1.5f);
                byte fuelR = canAfford ? (byte)100 : (byte)255;
                byte fuelG = canAfford ? (byte)200 : (byte)80;
                byte fuelB = canAfford ? (byte)255 : (byte)80;
                renderer.DrawTextScreen(cx, missionY + 20, $"FUEL COST: {fuelCost:F1}", new Color3(fuelR, fuelG, fuelB), 1.5f);

                if (!inRange)
                    renderer.DrawTextScreen(cx, missionY + 40, "OUT OF FTL RANGE", new Color3(255, 80, 80), 1.5f);
                else if (!canAfford)
                    renderer.DrawTextScreen(cx, missionY + 40, "NOT ENOUGH FUEL", new Color3(255, 80, 80), 1.5f);
                else
                    renderer.DrawTextScreen(cx, missionY + 40, "[ENTER] TRAVEL", new Color3(100, 255, 100), 1.5f);
            }
        }
        else
        {
            renderer.DrawTextScreen(cx, selY, "NO SYSTEM SELECTED", new Color3(100, 120, 160), 1.5f);
            renderer.DrawTextScreen(cx, selY + 20, "CLICK A STAR TO SELECT", new Color3(140, 140, 160), 1.3f);
        }

        // Controls
        float ctrlY = IpY + IpH - 110;
        renderer.DrawRectScreen(cx, ctrlY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        renderer.DrawTextScreen(cx, ctrlY + 8, "WASD/ARROWS/DRAG: PAN", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 24, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 40, "CLICK: SELECT SYSTEM", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 56, "DBLCLICK/ENTER: TRAVEL", new Color3(100, 255, 100), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 72, "M: SOLAR SYSTEM  ESC: CLOSE", new Color3(255, 150, 150), 1.3f);
    }
}
