using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.UI.Overlays.Map.Base;

namespace SpaceExplorationGame.UI.Overlays.Map;

/// <summary>Type of object hovered/selected in the solar system map.</summary>
public enum SolarMapObjectType { None, Star, Planet, Moon, SpaceStation }

/// <summary>Identifies a clickable object in the solar system map.</summary>
public readonly record struct SolarMapSelection(
    SolarMapObjectType Type, int PlanetIndex = -1, int MoonIndex = -1, int SpaceStationIndex = -1);

/// <summary>
/// Map panel showing the current solar system with interactive planets, moons, and stations.
/// Supports hover, click-to-select, double-click-to-target, and navigation target management.
/// </summary>
public class SolarSystemMapPanel : MapPanelBase
{
    // Zoom limits
    private const float ZoomMin = 0.02f;
    private const float ZoomMax = 0.5f;
    private const float ZoomDefault = 0.03f;

    // ── State ──
    private StarSystemData? _currentStarSystem;
    private List<PlanetData> _planets = [];
    private List<SpaceStationData> _spaceStations = [];
    private SolarMapSelection _hoveredObject = new(SolarMapObjectType.None);
    private SolarMapSelection _selectedObject = new(SolarMapObjectType.None);

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    public override void Open(Game game)
    {
        var starSystems = game.GalaxyData;
        if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < starSystems.Count)
        {
            _currentStarSystem = starSystems[game.Player.CurrentStarSystemIndex];
            var content = game.WorldGenerator.GenerateSolarSystem(game.Seeds, _currentStarSystem);
            _planets = content.Planets;
            _spaceStations = content.SpaceStations;
        }

        _hoveredObject = new(SolarMapObjectType.None);
        _selectedObject = new(SolarMapObjectType.None);
    }

    public override void Close(Game game)
    {
    }

    public override void SetupCamera(Game game)
    {
        Camera.ZoomMin = ZoomMin;
        Camera.ZoomMax = ZoomMax;
        float centerX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float centerY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Camera.Position = new Vector2(centerX, centerY);
        Camera.Zoom = ZoomDefault;
        Camera.ClampZoom();
    }

    // ─────────────────────────────────────────────────────────────
    //  INPUT
    // ─────────────────────────────────────────────────────────────

    public override bool UpdateInput(Game game)
    {
        var input = game.Input;
        Vector2 currentMouse = new(input.MouseX, input.MouseY);
        bool usingGamepad = input.ActiveInputMethod == InputMethod.Gamepad;
        Vector2 selectionPoint = usingGamepad ? GetMapScreenCenter() : currentMouse;
        float time = (float)game.GlobalTime;

        HandleZoomAndPan(input, currentMouse);
        HandleGamepadTriggerZoom(input, game.DeltaTime);

        // Hover detection
        _hoveredObject = new(SolarMapObjectType.None);
        if (usingGamepad || IsMouseInMap(currentMouse))
        {
            float bestDist = float.MaxValue;

            // Star
            float cx = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
            float cy = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
            var starScreen = Camera.WorldToScreen(new Vector2(cx, cy));
            float starHit = MathF.Max((_currentStarSystem?.StarRadius ?? 10f) * 2f * Camera.Zoom, 15f);
            float starDist = (selectionPoint - starScreen).LengthSquared();
            if (starDist < starHit * starHit && starDist < bestDist)
            {
                bestDist = starDist;
                _hoveredObject = new(SolarMapObjectType.Star);
            }

            // Planets and moons
            foreach (var planet in _planets)
            {
                var pPos = GetPlanetWorldPos(planet, time);
                var pScreen = Camera.WorldToScreen(pPos);
                float pHitR = MathF.Max(planet.Radius * Camera.Zoom, 12f);
                float pDist = (selectionPoint - pScreen).LengthSquared();
                if (pDist < pHitR * pHitR && pDist < bestDist)
                {
                    bestDist = pDist;
                    _hoveredObject = new(SolarMapObjectType.Planet, PlanetIndex: planet.Index);
                }

                foreach (var moon in planet.Moons)
                {
                    var mPos = GetMoonWorldPos(planet, moon, time);
                    var mScreen = Camera.WorldToScreen(mPos);
                    float mHitR = MathF.Max(moon.Radius * Camera.Zoom, 10f);
                    float mDist = (selectionPoint - mScreen).LengthSquared();
                    if (mDist < mHitR * mHitR && mDist < bestDist)
                    {
                        bestDist = mDist;
                        _hoveredObject = new(SolarMapObjectType.Moon, PlanetIndex: planet.Index, MoonIndex: moon.Index);
                    }
                }
            }

            // Stations
            foreach (var spaceStation in _spaceStations)
            {
                var sPos = GetStationWorldPos(spaceStation, time);
                var sScreen = Camera.WorldToScreen(sPos);
                float sHitR = MathF.Max(8f * Camera.Zoom, 10f);
                float sDist = (selectionPoint - sScreen).LengthSquared();
                if (sDist < sHitR * sHitR && sDist < bestDist)
                {
                    bestDist = sDist;
                    _hoveredObject = new(SolarMapObjectType.SpaceStation, SpaceStationIndex: spaceStation.Index);
                }
            }
        }

        // Click to select / click same object again to set target and close
        if (input.IsMouseReleased(1) && !IsPanning)
        {
            if (_hoveredObject.Type != SolarMapObjectType.None)
            {
                if (_hoveredObject == _selectedObject)
                {
                    SetNavTarget(game.Player);
                    OnRequestClose?.Invoke(game);
                    return true;
                }
                _selectedObject = _hoveredObject;
            }
            IsPanning = false;
        }
        else if (input.IsMouseReleased(1))
            IsPanning = false;

        if (usingGamepad && input.IsActionPressed(InputAction.MenuConfirm)
            && _hoveredObject.Type != SolarMapObjectType.None)
        {
            if (_hoveredObject == _selectedObject)
            {
                SetNavTarget(game.Player);
                OnRequestClose?.Invoke(game);
                return true;
            }

            _selectedObject = _hoveredObject;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  SOLAR SYSTEM HELPERS
    // ─────────────────────────────────────────────────────────────

    private Vector2 GetPlanetWorldPos(PlanetData planet, float time)
    {
        float cx = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float cy = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        float angle = planet.StartAngle + planet.OrbitSpeed * time;
        return new Vector2(cx + MathF.Cos(angle) * planet.OrbitRadius,
                           cy + MathF.Sin(angle) * planet.OrbitRadius);
    }

    private Vector2 GetMoonWorldPos(PlanetData planet, MoonData moon, float time)
    {
        var parentPos = GetPlanetWorldPos(planet, time);
        float angle = moon.StartAngle + moon.OrbitSpeed * time;
        return parentPos + new Vector2(MathF.Cos(angle) * moon.OrbitRadius,
                                       MathF.Sin(angle) * moon.OrbitRadius);
    }

    private Vector2 GetStationWorldPos(SpaceStationData station, float time)
    {
        float cx = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float cy = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 parentPos;
        if (station.OrbitParentPlanetIndex >= 0 && station.OrbitParentPlanetIndex < _planets.Count)
            parentPos = GetPlanetWorldPos(_planets[station.OrbitParentPlanetIndex], time);
        else
            parentPos = new Vector2(cx, cy);
        float angle = station.StartAngle + station.OrbitSpeed * time;
        return parentPos + new Vector2(MathF.Cos(angle) * station.OrbitRadius,
                                       MathF.Sin(angle) * station.OrbitRadius);
    }

    private static bool IsCurrentNavTarget(PlayerData player, SolarMapSelection sel)
    {
        return sel.Type switch
        {
            SolarMapObjectType.Star => player.Navigation.Type == NavigationTargetType.Star,
            SolarMapObjectType.Planet => player.Navigation.Type == NavigationTargetType.Planet
                                        && player.Navigation.PlanetIndex == sel.PlanetIndex,
            SolarMapObjectType.Moon => player.Navigation.Type == NavigationTargetType.Moon
                                      && player.Navigation.PlanetIndex == sel.PlanetIndex
                                      && player.Navigation.MoonIndex == sel.MoonIndex,
            SolarMapObjectType.SpaceStation => player.Navigation.Type == NavigationTargetType.SpaceStation
                                         && player.Navigation.SpaceStationIndex == sel.SpaceStationIndex,
            _ => false
        };
    }

    private void SetNavTarget(PlayerData player)
    {
        if (_selectedObject.Type == SolarMapObjectType.None) return;

        switch (_selectedObject.Type)
        {
            case SolarMapObjectType.Star when _currentStarSystem != null:
                player.Navigation.SetStar(_currentStarSystem.Name, new Color3(255, 220, 80));
                break;
            case SolarMapObjectType.Planet when _selectedObject.PlanetIndex >= 0 && _selectedObject.PlanetIndex < _planets.Count:
                var planet = _planets[_selectedObject.PlanetIndex];
                player.Navigation.SetPlanet(_selectedObject.PlanetIndex, planet.Name, planet.Color);
                break;
            case SolarMapObjectType.Moon when _selectedObject.PlanetIndex >= 0 && _selectedObject.PlanetIndex < _planets.Count
                                           && _selectedObject.MoonIndex >= 0
                                           && _selectedObject.MoonIndex < _planets[_selectedObject.PlanetIndex].Moons.Count:
                var moon = _planets[_selectedObject.PlanetIndex].Moons[_selectedObject.MoonIndex];
                player.Navigation.SetMoon(_selectedObject.PlanetIndex, _selectedObject.MoonIndex,
                    moon.Name, moon.Color);
                break;
            case SolarMapObjectType.SpaceStation when _selectedObject.SpaceStationIndex >= 0 && _selectedObject.SpaceStationIndex < _spaceStations.Count:
                var spaceStation = _spaceStations[_selectedObject.SpaceStationIndex];
                player.Navigation.SetStation(_selectedObject.SpaceStationIndex, spaceStation.Name, new Color3(100, 200, 255));
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  RENDERING
    // ─────────────────────────────────────────────────────────────

    public override void RenderContent(Game game, SpriteRenderer renderer)
    {
        var camera = Camera;
        float time = (float)game.GlobalTime;
        float cx = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float cy = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        var starCenter = new Vector2(cx, cy);
        float starRadius = _currentStarSystem?.StarRadius ?? 20f;

        // Orbit rings
        foreach (var planet in _planets)
            renderer.DrawCircle(camera, starCenter, planet.OrbitRadius, new Color4(30, 40, 60, 120), 64);

        // Star
        float starDisplay = starRadius * 2f;
        renderer.DrawFilledCircle(camera, starCenter, starDisplay, new Color4(255, 220, 80, 200));
        renderer.DrawCircle(camera, starCenter, starDisplay + 2, new Color4(255, 240, 150, 80));

        bool isStarHovered = _hoveredObject.Type == SolarMapObjectType.Star;
        bool isStarSelected = _selectedObject.Type == SolarMapObjectType.Star;
        bool isStarTarget = game.Player.Navigation.Type == NavigationTargetType.Star;
        if (isStarHovered || isStarSelected)
            renderer.DrawCircle(camera, starCenter, starDisplay + 6, new Color3(255, 255, 200));
        if (isStarTarget)
            DrawTargetBrackets(renderer, camera, starCenter, starDisplay + 10, game);

        // Star label
        renderer.DrawText(camera, starCenter + new Vector2(0, starDisplay + 8),
            _currentStarSystem?.Name ?? "STAR", new Color3(255, 220, 80), Math.Max(1f, camera.Zoom * 18f));

        // Planets
        foreach (var planet in _planets)
        {
            var pPos = GetPlanetWorldPos(planet, time);
            float pRadius = Math.Max(planet.Radius, 4f);

            renderer.DrawFilledCircle(camera, pPos, pRadius, planet.Color.WithAlpha(220));

            bool isPHovered = _hoveredObject.Type == SolarMapObjectType.Planet && _hoveredObject.PlanetIndex == planet.Index;
            bool isPSelected = _selectedObject.Type == SolarMapObjectType.Planet && _selectedObject.PlanetIndex == planet.Index;
            bool isPTarget = game.Player.Navigation.Type == NavigationTargetType.Planet && game.Player.Navigation.PlanetIndex == planet.Index;

            if (isPHovered || isPSelected)
                renderer.DrawCircle(camera, pPos, pRadius + 4, new Color3(255, 255, 255));
            if (isPTarget)
                DrawTargetBrackets(renderer, camera, pPos, pRadius + 8, game);

            // Planet label
            renderer.DrawText(camera, pPos + new Vector2(0, pRadius + 6),
                planet.Name, planet.Color, Math.Max(1f, camera.Zoom * 14f));

            // Moon orbit rings
            foreach (var moon in planet.Moons)
                renderer.DrawCircle(camera, pPos, moon.OrbitRadius, new Color4(40, 45, 65, 80), 32);

            // Moons
            foreach (var moon in planet.Moons)
            {
                var mPos = GetMoonWorldPos(planet, moon, time);
                float mRadius = Math.Max(moon.Radius, 2f);

                renderer.DrawFilledCircle(camera, mPos, mRadius, moon.Color.WithAlpha(180));

                bool isMHovered = _hoveredObject is { Type: SolarMapObjectType.Moon } && _hoveredObject.PlanetIndex == planet.Index && _hoveredObject.MoonIndex == moon.Index;
                bool isMSelected = _selectedObject is { Type: SolarMapObjectType.Moon } && _selectedObject.PlanetIndex == planet.Index && _selectedObject.MoonIndex == moon.Index;
                bool isMTarget = game.Player.Navigation.Type == NavigationTargetType.Moon
                                 && game.Player.Navigation.PlanetIndex == planet.Index && game.Player.Navigation.MoonIndex == moon.Index;

                if (isMHovered || isMSelected)
                    renderer.DrawCircle(camera, mPos, mRadius + 3, new Color3(200, 200, 220));
                if (isMTarget)
                    DrawTargetBrackets(renderer, camera, mPos, mRadius + 6, game);

                renderer.DrawText(camera, mPos + new Vector2(0, mRadius + 4),
                    moon.Name, new Color3(160, 160, 180), Math.Max(1f, camera.Zoom * 10f));
            }
        }

        // Stations
        foreach (var spaceStation in _spaceStations)
        {
            var sPos = GetStationWorldPos(spaceStation, time);

            float ds = Math.Max(6f, 3f / camera.Zoom);
            renderer.DrawFilledCircle(camera, sPos, ds * 0.6f, new Color4(100, 200, 255, 220));

            bool isSHovered = _hoveredObject.Type == SolarMapObjectType.SpaceStation && _hoveredObject.SpaceStationIndex == spaceStation.Index;
            bool isSSelected = _selectedObject.Type == SolarMapObjectType.SpaceStation && _selectedObject.SpaceStationIndex == spaceStation.Index;
            bool isSTarget = game.Player.Navigation.Type == NavigationTargetType.SpaceStation && game.Player.Navigation.SpaceStationIndex == spaceStation.Index;

            if (isSHovered || isSSelected)
                renderer.DrawCircle(camera, sPos, ds + 4, new Color3(100, 200, 255));
            if (isSTarget)
                DrawTargetBrackets(renderer, camera, sPos, ds + 8, game);

            renderer.DrawText(camera, sPos + new Vector2(0, ds + 4),
                spaceStation.Name, new Color3(100, 200, 255), Math.Max(1f, camera.Zoom * 12f));
        }

        // Mission markers on planets/stations
        if (_currentStarSystem != null)
        {
            float mPulse = (float)(0.5 + 0.5 * Math.Sin(game.GlobalTime * 3.0));
            byte mAlpha = (byte)(140 + (int)(mPulse * 115));
            foreach (var mission in game.Player.Missions.Active)
            {
                if (mission.Status != MissionStatus.Completed &&
                    mission.Target.HasSystem && mission.Target.SystemIndex == _currentStarSystem.Index)
                {
                    var mc = mission.TypeColor;
                    if (mission.Target.HasPlanet && mission.Target.PlanetIndex >= 0 && mission.Target.PlanetIndex < _planets.Count)
                    {
                        var planet = _planets[mission.Target.PlanetIndex];
                        var pPos = GetPlanetWorldPos(planet, time);
                        float pR = Math.Max(planet.Radius, 4f);
                        renderer.DrawCircle(camera, pPos, pR + 6, new Color4(mc.R, mc.G, mc.B, mAlpha));
                        renderer.DrawCircle(camera, pPos, pR + 9, new Color4(mc.R, mc.G, mc.B, (byte)(mAlpha / 3)));
                        DrawMissionDiamond(renderer, camera, pPos, pR, mc, mAlpha, mission.TypeLabel);
                    }
                    else
                    {
                        foreach (var spaceStation in _spaceStations)
                        {
                            var sPos2 = GetStationWorldPos(spaceStation, time);
                            float sR = Math.Max(6f, 3f / camera.Zoom);
                            renderer.DrawCircle(camera, sPos2, sR + 6, new Color4(mc.R, mc.G, mc.B, mAlpha));
                            renderer.DrawCircle(camera, sPos2, sR + 9, new Color4(mc.R, mc.G, mc.B, (byte)(mAlpha / 3)));
                            DrawMissionDiamond(renderer, camera, sPos2, sR, mc, mAlpha, mission.TypeLabel);
                        }
                    }
                }

                if (mission.Status == MissionStatus.Completed &&
                    mission.TurnIn.HasSystem && mission.TurnIn.SystemIndex == _currentStarSystem.Index)
                {
                    foreach (var spaceStation in _spaceStations)
                    {
                        var sPos2 = GetStationWorldPos(spaceStation, time);
                        float sR = Math.Max(6f, 3f / camera.Zoom);
                        renderer.DrawCircle(camera, sPos2, sR + 6, new Color4(100, 255, 100, mAlpha));
                        renderer.DrawCircle(camera, sPos2, sR + 9, new Color4(100, 255, 100, (byte)(mAlpha / 3)));
                        DrawMissionDiamond(renderer, camera, sPos2, sR,
                            new Color3(100, 255, 100), mAlpha, "TURN IN");
                    }
                }
            }
        }

        // Player ship marker
        var shipPos = game.Player.ShipWorldPosition;
        renderer.DrawFilledCircle(camera, shipPos, Math.Max(4f, 2f / camera.Zoom), new Color4(0, 255, 100, 230));
        renderer.DrawCircle(camera, shipPos, Math.Max(7f, 3.5f / camera.Zoom), new Color3(0, 255, 100));
        renderer.DrawText(camera, shipPos + new Vector2(0, Math.Max(8f, 4f / camera.Zoom)),
            "YOU", new Color3(0, 255, 100), Math.Max(1f, camera.Zoom * 14f));

        if (game.Input.ActiveInputMethod == InputMethod.Gamepad)
            RenderCenterSelectionReticle(renderer, new Color4(255, 230, 120, 220));
    }

    // ─────────────────────────────────────────────────────────────
    //  INFO PANEL
    // ─────────────────────────────────────────────────────────────

    public override void RenderInfoPanel(Game game, SpriteRenderer renderer)
    {
        RenderInfoPanelHeader(renderer, "SYSTEM DATA");

        float px = IpX + 12;
        float py = IpY + 40;

        // System summary
        if (_currentStarSystem != null)
        {
            renderer.DrawTextScreen(px, py, _currentStarSystem.Name.ToUpper(), new Color3(255, 220, 80), 1.8f);
            py += 24;
            renderer.DrawTextScreen(px, py, $"CLASS {_currentStarSystem.StarClass} STAR", new Color3(180, 180, 200), 1.3f);
            py += 16;
            renderer.DrawTextScreen(px, py, $"PLANETS: {_planets.Count}  SPACE STATIONS: {_spaceStations.Count}", new Color3(180, 180, 200), 1.3f);
            py += 20;
        }

        renderer.DrawRectScreen(px, py, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        py += 8;

        // Selected object details
        if (_selectedObject.Type != SolarMapObjectType.None)
        {
            RenderSelectedObjectInfo(game, renderer, px, py);
        }
        else
        {
            renderer.DrawTextScreen(px, py, "NO OBJECT SELECTED", new Color3(100, 120, 160), 1.5f);
            py += 20;
            renderer.DrawTextScreen(px, py, "CLICK AN OBJECT", new Color3(140, 140, 160), 1.3f);
            py += 16;
            renderer.DrawTextScreen(px, py, "TO VIEW DETAILS", new Color3(140, 140, 160), 1.3f);
        }

        // Nav target display
        py = IpY + IpH - 160;
        renderer.DrawRectScreen(px, py, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        py += 8;
        renderer.DrawTextScreen(px, py, "NAV TARGET", new Color3(100, 120, 160), 1.3f);
        py += 18;
        if (game.Player.Navigation.HasTarget)
        {
            renderer.DrawTextScreen(px, py, game.Player.Navigation.Name.ToUpper(),
                game.Player.Navigation.Color, 1.8f);
            py += 22;
            renderer.DrawTextScreen(px, py, $"TYPE: {game.Player.Navigation.Type.ToString().ToUpper()}", new Color3(180, 180, 200), 1.3f);
        }
        else
        {
            renderer.DrawTextScreen(px, py, "NONE", new Color3(80, 80, 100), 1.5f);
        }

        // Controls
        float ctrlY = IpY + IpH - 80;
        renderer.DrawRectScreen(px, ctrlY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        if (game.Input.ActiveInputMethod == InputMethod.Gamepad)
        {
            string panText = "LEFT STICK: PAN";
            renderer.DrawTextScreen(px, ctrlY + 8, panText, new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlY + 24, "LT/RT: ZOOM", new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlY + 40,
                $"{game.Input.GetActionHelpText(InputAction.MenuConfirm)}: SELECT  /  SAME OBJECT: SET TARGET + CLOSE",
                new Color3(255, 200, 100), 1.3f);
            renderer.DrawTextScreen(px, ctrlY + 56,
                $"{game.Input.GetActionHelpText(InputAction.MapPreviousView)}/{game.Input.GetActionHelpText(InputAction.MapNextView)}: SWITCH MAP  {game.Input.GetActionHelpText(InputAction.MenuBack)}: CLOSE",
                new Color3(255, 150, 150), 1.3f);
        }
        else
        {
            string panText =
                $"{game.Input.GetActionHelpText(InputAction.MoveUp)}/{game.Input.GetActionHelpText(InputAction.MoveDown)}/{game.Input.GetActionHelpText(InputAction.MoveLeft)}/{game.Input.GetActionHelpText(InputAction.MoveRight)}/{game.Input.GetMouseButtonHelpText(SDL.ButtonLeft)}-DRAG: PAN";
            renderer.DrawTextScreen(px, ctrlY + 8, panText, new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlY + 24, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlY + 40,
                $"{game.Input.GetMouseButtonHelpText(SDL.ButtonLeft)}: SELECT  /  SAME OBJECT: SET TARGET + CLOSE",
                new Color3(255, 200, 100), 1.3f);
            renderer.DrawTextScreen(px, ctrlY + 56,
                $"{game.Input.GetActionHelpText(InputAction.ToggleMap)}: STAR CHART  {game.Input.GetActionHelpText(InputAction.MenuBack)}: CLOSE",
                new Color3(255, 150, 150), 1.3f);
        }
    }

    private void RenderSelectedObjectInfo(Game game, SpriteRenderer renderer, float px, float py)
    {
        var sel = _selectedObject;
        bool isTarget = IsCurrentNavTarget(game.Player, sel);
        string targetTag = isTarget ? "  [TARGET]" : "";

        switch (sel.Type)
        {
            case SolarMapObjectType.Star when _currentStarSystem != null:
                renderer.DrawTextScreen(px, py, "SELECTED: STAR", new Color3(100, 120, 160), 1.3f);
                py += 20;
                renderer.DrawTextScreen(px, py, _currentStarSystem.Name.ToUpper() + targetTag,
                    isTarget ? new Color3(255, 200, 50) : new Color3(255, 220, 80), 1.8f);
                py += 26;
                renderer.DrawTextScreen(px, py, $"CLASS: {_currentStarSystem.StarClass}",
                    new Color3(200, 200, 200), 1.5f);
                py += 20;
                renderer.DrawTextScreen(px, py, $"RADIUS: {_currentStarSystem.StarRadius:F0}",
                    new Color3(200, 200, 200), 1.5f);
                py += 28;
                RenderTargetButton(game, renderer, px, py, isTarget);
                break;

            case SolarMapObjectType.Planet when sel.PlanetIndex >= 0 && sel.PlanetIndex < _planets.Count:
                var planet = _planets[sel.PlanetIndex];
                renderer.DrawTextScreen(px, py, "SELECTED: PLANET", new Color3(100, 120, 160), 1.3f);
                py += 20;
                renderer.DrawTextScreen(px, py, planet.Name.ToUpper() + targetTag,
                    isTarget ? new Color3(255, 200, 50) : planet.Color, 1.8f);
                py += 26;
                renderer.DrawTextScreen(px, py, $"TYPE: {planet.Type.ToString().ToUpper()}",
                    new Color3(200, 200, 200), 1.5f);
                py += 20;
                renderer.DrawTextScreen(px, py, $"MOONS: {planet.MoonCount}",
                    new Color3(200, 200, 200), 1.5f);
                py += 20;
                renderer.DrawTextScreen(px, py, $"RINGS: {(planet.HasRings ? "YES" : "NO")}",
                    new Color3(200, 200, 200), 1.5f);
                py += 20;
                if (planet.HasSolidSurface)
                {
                    renderer.DrawTextScreen(px, py, "LANDABLE: YES",
                        new Color3(100, 255, 100), 1.5f);
                    py += 20;
                    renderer.DrawTextScreen(px, py,
                        $"SETTLEMENTS: {(planet.HasSettlement ? "YES" : "NO")}",
                        planet.HasSettlement ? new Color3(255, 220, 100) : new Color3(120, 120, 120), 1.5f);
                }
                else
                {
                    renderer.DrawTextScreen(px, py, "LANDABLE: NO (GAS GIANT)",
                        new Color3(255, 80, 80), 1.5f);
                }
                py += 28;
                RenderTargetButton(game, renderer, px, py, isTarget);
                break;

            case SolarMapObjectType.Moon when sel.PlanetIndex >= 0 && sel.PlanetIndex < _planets.Count
                                          && sel.MoonIndex >= 0 && sel.MoonIndex < _planets[sel.PlanetIndex].Moons.Count:
                var moonPlanet = _planets[sel.PlanetIndex];
                var moon = moonPlanet.Moons[sel.MoonIndex];
                renderer.DrawTextScreen(px, py, "SELECTED: MOON", new Color3(100, 120, 160), 1.3f);
                py += 20;
                renderer.DrawTextScreen(px, py, moon.Name.ToUpper() + targetTag,
                    isTarget ? new Color3(255, 200, 50) : new Color3(180, 180, 210), 1.8f);
                py += 26;
                renderer.DrawTextScreen(px, py, $"TYPE: {moon.Type.ToString().ToUpper()}",
                    new Color3(200, 200, 200), 1.5f);
                py += 20;
                renderer.DrawTextScreen(px, py, $"ORBITS: {moonPlanet.Name.ToUpper()}",
                    new Color3(180, 180, 200), 1.5f);
                py += 20;
                renderer.DrawTextScreen(px, py, "LANDABLE: YES",
                    new Color3(100, 255, 100), 1.5f);
                py += 28;
                RenderTargetButton(game, renderer, px, py, isTarget);
                break;

            case SolarMapObjectType.SpaceStation when sel.SpaceStationIndex >= 0 && sel.SpaceStationIndex < _spaceStations.Count:
                var spaceStation = _spaceStations[sel.SpaceStationIndex];
                renderer.DrawTextScreen(px, py, "SELECTED: SPACE STATION", new Color3(100, 120, 160), 1.3f);
                py += 20;
                renderer.DrawTextScreen(px, py, spaceStation.Name.ToUpper() + targetTag,
                    isTarget ? new Color3(255, 200, 50) : new Color3(100, 200, 255), 1.8f);
                py += 26;
                string orbitLabel = spaceStation.OrbitParentPlanetIndex >= 0 && spaceStation.OrbitParentPlanetIndex < _planets.Count
                    ? $"ORBITS: {_planets[spaceStation.OrbitParentPlanetIndex].Name.ToUpper()}"
                    : "ORBITS: STAR";
                renderer.DrawTextScreen(px, py, orbitLabel, new Color3(200, 200, 200), 1.5f);
                py += 20;
                renderer.DrawTextScreen(px, py,
                    $"DOCK: FLY NEAR & PRESS {game.Input.GetActionHelpText(InputAction.Interact).ToUpper()}",
                    new Color3(100, 200, 255), 1.5f);
                py += 28;
                RenderTargetButton(game, renderer, px, py, isTarget);
                break;
        }
    }

    private void RenderTargetButton(Game game, SpriteRenderer renderer, float px, float py, bool isTarget)
    {
        string confirmText = game.Input.ActiveInputMethod == InputMethod.Gamepad
            ? game.Input.GetActionHelpText(InputAction.MenuConfirm).ToUpper()
            : game.Input.GetMouseButtonHelpText(SDL.ButtonLeft).ToUpper();
        string btnText = isTarget
            ? $"[{confirmText}] SAME OBJECT: TARGET LOCKED"
            : $"[{confirmText}] SAME OBJECT: SET AS TARGET + CLOSE";
        var btnColor = isTarget ? new Color3(255, 200, 100) : new Color3(255, 200, 100);
        renderer.DrawRectScreen(px, py, InfoPanelW - 24, 20, new Color4(40, 50, 80, 180));
        float btnW = renderer.MeasureText(btnText, 1.5f);
        renderer.DrawTextScreen(px + (InfoPanelW - 24) / 2f - btnW / 2f, py + 2, btnText, btnColor, 1.5f);
    }
}
