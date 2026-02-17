using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Base;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>The two view modes the overlay can show.</summary>
public enum MapViewMode { SolarSystem, Galaxy }

/// <summary>Type of object hovered/selected in the solar system map.</summary>
public enum SolarMapObjectType { None, Star, Planet, Moon, Station }

/// <summary>Identifies a clickable object in the solar system map.</summary>
public readonly record struct SolarMapSelection(
    SolarMapObjectType Type, int PlanetIndex = -1, int MoonIndex = -1, int StationIndex = -1);

/// <summary>
/// Full-screen overlay that shows a solar system map (default) or the galaxy star chart.
/// The player can toggle between modes with M, browse objects, and set navigation targets.
/// Opened with M key from SolarSystemState.
/// </summary>
public class GalaxyMapOverlay : OverlayBase
{
    // ── Current mode ──
    private MapViewMode _viewMode = MapViewMode.SolarSystem;

    // ── Galaxy mode state ──
    private List<StarSystemData> _starSystems = [];
    private int _selectedSystemIndex = -1;
    private int _hoveredSystemIndex = -1;
    private List<BackgroundStar> _backgroundStars = [];
    private bool _isPanning;
    private Vector2 _lastMouseScreen;
    private float _lastClickTime;
    private int _lastClickSystem = -1;
    private const float DoubleClickTime = 0.4f;
    private List<NebulaCloud> _nebulae = [];

    // ── Solar system mode state ──
    private StarSystemData? _currentStarSystem;
    private List<PlanetData> _planets = [];
    private List<SpaceStationData> _stations = [];
    private SolarMapSelection _hoveredObject = new(SolarMapObjectType.None);
    private SolarMapSelection _selectedObject = new(SolarMapObjectType.None);
    private SolarMapSelection _lastClickObject = new(SolarMapObjectType.None);
    private float _lastSolarClickTime;

    // ── Shared camera (reconfigured per mode) ──
    private readonly Camera _camera = new(GameConfig.WindowWidth, GameConfig.WindowHeight,
        GameConfig.GalaxyMapZoomMin, GameConfig.GalaxyMapZoomMax);

    // Solar system map zoom limits
    private const float SolarMapZoomMin = 0.02f;
    private const float SolarMapZoomMax = 0.5f;
    private const float SolarMapZoomDefault = 0.03f;

    // Layout
    private const float MapWidth = 800f;
    private const float MapHeight = 700f;
    private const float MapPad = 12f;
    private const float MapHeaderH = 30f;
    private const float InfoPanelWidth = 280f;
    private const float InfoPanelGap = 20f;
    private const float TabHeight = 28f;

    private float _mapX, _mapY;
    private float _frameX, _frameY, _frameW, _frameH;
    private float _ipX, _ipY, _ipH;

    // ─────────────────────────────────────────────────────────────
    //  OPEN / CLOSE
    // ─────────────────────────────────────────────────────────────

    /// <summary>Open the overlay in any mode. Default = SolarSystem.</summary>
    public void Open(Game game, MapViewMode initialMode = MapViewMode.SolarSystem)
    {
        IsOpen = true;
        _starSystems = game.GalaxyData;
        _isPanning = false;
        _lastClickSystem = -1;

        ComputeLayout();

        // Solar system data
        if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < _starSystems.Count)
        {
            _currentStarSystem = _starSystems[game.Player.CurrentStarSystemIndex];
            var rng = game.Seeds.GetStarSystemRandom(_currentStarSystem.Index);
            var (planets, _, stations) = SolarSystemGenerator.Generate(rng, _currentStarSystem);
            _planets = planets;
            _stations = stations;
        }

        // Galaxy background
        InitGalaxyBackground(game);

        // Setup mode
        _viewMode = initialMode;
        SetupCameraForMode(game);

        // Clear solar system selection
        _hoveredObject = new(SolarMapObjectType.None);
        _selectedObject = new(SolarMapObjectType.None);
    }

    private void ComputeLayout()
    {
        _frameW = MapWidth + MapPad * 2;
        _frameH = MapHeight + MapPad * 2 + MapHeaderH;
        float totalW = _frameW + InfoPanelGap + InfoPanelWidth;
        _frameX = (GameConfig.WindowWidth - totalW) / 2f;
        _frameY = (GameConfig.WindowHeight - _frameH) / 2f;
        _mapX = _frameX + MapPad;
        _mapY = _frameY + MapPad + MapHeaderH;
        _ipX = _frameX + _frameW + InfoPanelGap;
        _ipY = _frameY;
        _ipH = _frameH;
    }

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

    private void SetupCameraForMode(Game game)
    {
        _camera.ViewportWidth = (int)MapWidth;
        _camera.ViewportHeight = (int)MapHeight;
        _camera.ViewportOffsetX = _mapX;
        _camera.ViewportOffsetY = _mapY;

        if (_viewMode == MapViewMode.Galaxy)
        {
            _camera.ZoomMin = GameConfig.GalaxyMapZoomMin;
            _camera.ZoomMax = GameConfig.GalaxyMapZoomMax;
            _selectedSystemIndex = -1;
            _hoveredSystemIndex = -1;

            if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < _starSystems.Count)
            {
                _selectedSystemIndex = game.Player.CurrentStarSystemIndex;
                _camera.Position = _starSystems[_selectedSystemIndex].GalaxyPosition;
            }
            else
            {
                _camera.Position = new Vector2(GameConfig.GalaxyWidth * GameConfig.TileSize / 2f,
                                               GameConfig.GalaxyHeight * GameConfig.TileSize / 2f);
            }
            _camera.Zoom = GameConfig.GalaxyMapZoomDefault;
        }
        else // SolarSystem
        {
            _camera.ZoomMin = SolarMapZoomMin;
            _camera.ZoomMax = SolarMapZoomMax;
            float centerX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
            float centerY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
            _camera.Position = new Vector2(centerX, centerY);
            _camera.Zoom = SolarMapZoomDefault;
        }
        _camera.ClampZoom();
    }

    /// <summary>Close the overlay.</summary>
    public void Close(Game game)
    {
        _nebulae.Clear();
        _backgroundStars.Clear();
        _planets.Clear();
        _stations.Clear();
        base.Close();
    }

    // ─────────────────────────────────────────────────────────────
    //  GALAXY HELPERS (unchanged logic)
    // ─────────────────────────────────────────────────────────────

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
            Close(game);
        }
        else if (IsSystemReachable(game, _selectedSystemIndex))
        {
            float fuelCost = GetFuelCost(current, _selectedSystemIndex);
            game.Player.TrySpendFuel(fuelCost);
            game.Player.CurrentStarSystemIndex = _selectedSystemIndex;
            var targetSystem = _starSystems[_selectedSystemIndex];
            _nebulae.Clear();
            _backgroundStars.Clear();
            IsOpen = false;
            game.ChangeState(new SolarSystemState(targetSystem));
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  SOLAR SYSTEM HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>Compute the world position of a planet at the given time.</summary>
    private Vector2 GetPlanetWorldPos(PlanetData planet, float time)
    {
        float cx = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float cy = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        float angle = planet.StartAngle + planet.OrbitSpeed * time;
        return new Vector2(cx + MathF.Cos(angle) * planet.OrbitRadius,
                           cy + MathF.Sin(angle) * planet.OrbitRadius);
    }

    /// <summary>Compute the world position of a moon at the given time.</summary>
    private Vector2 GetMoonWorldPos(PlanetData planet, MoonData moon, float time)
    {
        var parentPos = GetPlanetWorldPos(planet, time);
        float angle = moon.StartAngle + moon.OrbitSpeed * time;
        return parentPos + new Vector2(MathF.Cos(angle) * moon.OrbitRadius,
                                       MathF.Sin(angle) * moon.OrbitRadius);
    }

    /// <summary>Compute the world position of a station at the given time.</summary>
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

    /// <summary>Check if a solar map selection matches the current nav target.</summary>
    private bool IsCurrentNavTarget(PlayerData player, SolarMapSelection sel)
    {
        return sel.Type switch
        {
            SolarMapObjectType.Star => player.NavTargetType == NavigationTargetType.Star,
            SolarMapObjectType.Planet => player.NavTargetType == NavigationTargetType.Planet
                                        && player.NavTargetPlanetIndex == sel.PlanetIndex,
            SolarMapObjectType.Moon => player.NavTargetType == NavigationTargetType.Moon
                                      && player.NavTargetPlanetIndex == sel.PlanetIndex
                                      && player.NavTargetMoonIndex == sel.MoonIndex,
            SolarMapObjectType.Station => player.NavTargetType == NavigationTargetType.Station
                                         && player.NavTargetStationIndex == sel.StationIndex,
            _ => false
        };
    }

    /// <summary>Set the selected object as the player's nav target (or clear if already set).</summary>
    private void ToggleNavTarget(PlayerData player)
    {
        if (_selectedObject.Type == SolarMapObjectType.None) return;

        // If already targeting this, clear
        if (IsCurrentNavTarget(player, _selectedObject))
        {
            player.ClearNavigationTarget();
            return;
        }

        switch (_selectedObject.Type)
        {
            case SolarMapObjectType.Star when _currentStarSystem != null:
                player.SetNavTargetStar(_currentStarSystem.Name, new Color3(255, 220, 80));
                break;
            case SolarMapObjectType.Planet when _selectedObject.PlanetIndex >= 0 && _selectedObject.PlanetIndex < _planets.Count:
                var planet = _planets[_selectedObject.PlanetIndex];
                player.SetNavTargetPlanet(_selectedObject.PlanetIndex, planet.Name, planet.Color);
                break;
            case SolarMapObjectType.Moon when _selectedObject.PlanetIndex >= 0 && _selectedObject.PlanetIndex < _planets.Count
                                           && _selectedObject.MoonIndex >= 0
                                           && _selectedObject.MoonIndex < _planets[_selectedObject.PlanetIndex].Moons.Count:
                var moon = _planets[_selectedObject.PlanetIndex].Moons[_selectedObject.MoonIndex];
                player.SetNavTargetMoon(_selectedObject.PlanetIndex, _selectedObject.MoonIndex,
                    moon.Name, moon.Color);
                break;
            case SolarMapObjectType.Station when _selectedObject.StationIndex >= 0 && _selectedObject.StationIndex < _stations.Count:
                var station = _stations[_selectedObject.StationIndex];
                player.SetNavTargetStation(_selectedObject.StationIndex, station.Name, new Color3(100, 200, 255));
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  INPUT
    // ─────────────────────────────────────────────────────────────

    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;
        var input = game.Input;

        // Escape closes
        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close(game);
            return true;
        }

        // M toggles between modes
        if (input.IsKeyPressed(SDL.Scancode.M))
        {
            _viewMode = _viewMode == MapViewMode.SolarSystem ? MapViewMode.Galaxy : MapViewMode.SolarSystem;
            SetupCameraForMode(game);
            return true;
        }

        // Tab also toggles (alternative key)
        if (input.IsKeyPressed(SDL.Scancode.Tab))
        {
            _viewMode = _viewMode == MapViewMode.SolarSystem ? MapViewMode.Galaxy : MapViewMode.SolarSystem;
            SetupCameraForMode(game);
            return true;
        }

        // Check if mouse clicked on tab buttons
        Vector2 currentMouse = new(input.MouseX, input.MouseY);
        if (input.IsMouseReleased(1))
        {
            // Solar System tab
            float tabSolarX = _frameX;
            float tabGalaxyX = _frameX + _frameW / 2f;
            float tabY = _frameY;
            if (currentMouse.Y >= tabY && currentMouse.Y <= tabY + MapHeaderH)
            {
                if (currentMouse.X >= tabSolarX && currentMouse.X < tabGalaxyX && _viewMode != MapViewMode.SolarSystem)
                {
                    _viewMode = MapViewMode.SolarSystem;
                    SetupCameraForMode(game);
                    return true;
                }
                else if (currentMouse.X >= tabGalaxyX && currentMouse.X < tabSolarX + _frameW && _viewMode != MapViewMode.Galaxy)
                {
                    _viewMode = MapViewMode.Galaxy;
                    SetupCameraForMode(game);
                    return true;
                }
            }
        }

        if (_viewMode == MapViewMode.Galaxy)
            return UpdateGalaxyInput(game, input, currentMouse);
        else
            return UpdateSolarSystemInput(game, input, currentMouse);
    }

    private bool UpdateGalaxyInput(Game game, InputManager input, Vector2 currentMouse)
    {
        // Zoom
        if (input.MouseWheelY != 0)
        {
            var worldBeforeZoom = _camera.ScreenToWorld(currentMouse);
            _camera.Zoom *= 1f + input.MouseWheelY * GameConfig.CameraZoomFactor;
            _camera.ClampZoom();
            var worldAfterZoom = _camera.ScreenToWorld(currentMouse);
            _camera.Position += worldBeforeZoom - worldAfterZoom;
        }

        // Panning
        if (input.IsMousePressed(1)) { _lastMouseScreen = currentMouse; _isPanning = false; }
        if (input.IsMouseDown(1))
        {
            Vector2 delta = currentMouse - _lastMouseScreen;
            if (delta.LengthSquared() > 4f)
            {
                _isPanning = true;
                _camera.Position -= delta / _camera.Zoom;
                _lastMouseScreen = currentMouse;
            }
        }

        // Hover
        _hoveredSystemIndex = -1;
        bool mouseInMap = currentMouse.X >= _mapX && currentMouse.X < _mapX + MapWidth &&
                          currentMouse.Y >= _mapY && currentMouse.Y < _mapY + MapHeight;
        if (mouseInMap)
        {
            float bestDist = float.MaxValue;
            for (int i = 0; i < _starSystems.Count; i++)
            {
                var screenPos = _camera.WorldToScreen(_starSystems[i].GalaxyPosition);
                float distSq = (currentMouse - screenPos).LengthSquared();
                float hitR = MathF.Max(_starSystems[i].StarRadius * 2f * _camera.Zoom, 20f);
                if (distSq < hitR * hitR && distSq < bestDist) { bestDist = distSq; _hoveredSystemIndex = i; }
            }
        }

        if (input.IsMouseReleased(1))
        {
            if (!_isPanning && _hoveredSystemIndex >= 0)
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
            else if (!_isPanning) _lastClickSystem = -1;
            _isPanning = false;
        }

        if (input.IsKeyPressed(SDL.Scancode.Return) && _selectedSystemIndex >= 0)
            TravelToSelected(game);

        return true;
    }

    private bool UpdateSolarSystemInput(Game game, InputManager input, Vector2 currentMouse)
    {
        float time = (float)game.GlobalTime;

        // Zoom
        if (input.MouseWheelY != 0)
        {
            var worldBeforeZoom = _camera.ScreenToWorld(currentMouse);
            _camera.Zoom *= 1f + input.MouseWheelY * GameConfig.CameraZoomFactor;
            _camera.ClampZoom();
            var worldAfterZoom = _camera.ScreenToWorld(currentMouse);
            _camera.Position += worldBeforeZoom - worldAfterZoom;
        }

        // Panning
        if (input.IsMousePressed(1)) { _lastMouseScreen = currentMouse; _isPanning = false; }
        if (input.IsMouseDown(1))
        {
            Vector2 delta = currentMouse - _lastMouseScreen;
            if (delta.LengthSquared() > 4f)
            {
                _isPanning = true;
                _camera.Position -= delta / _camera.Zoom;
                _lastMouseScreen = currentMouse;
            }
        }

        // Hover detection
        _hoveredObject = new(SolarMapObjectType.None);
        bool mouseInMap = currentMouse.X >= _mapX && currentMouse.X < _mapX + MapWidth &&
                          currentMouse.Y >= _mapY && currentMouse.Y < _mapY + MapHeight;
        if (mouseInMap)
        {
            float bestDist = float.MaxValue;

            // Star
            float cx = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
            float cy = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
            var starScreen = _camera.WorldToScreen(new Vector2(cx, cy));
            float starHit = MathF.Max((_currentStarSystem?.StarRadius ?? 10f) * 2f * _camera.Zoom, 15f);
            float starDist = (currentMouse - starScreen).LengthSquared();
            if (starDist < starHit * starHit && starDist < bestDist)
            {
                bestDist = starDist;
                _hoveredObject = new(SolarMapObjectType.Star);
            }

            // Planets and moons
            for (int i = 0; i < _planets.Count; i++)
            {
                var pPos = GetPlanetWorldPos(_planets[i], time);
                var pScreen = _camera.WorldToScreen(pPos);
                float pHitR = MathF.Max(_planets[i].Radius * _camera.Zoom, 12f);
                float pDist = (currentMouse - pScreen).LengthSquared();
                if (pDist < pHitR * pHitR && pDist < bestDist)
                {
                    bestDist = pDist;
                    _hoveredObject = new(SolarMapObjectType.Planet, PlanetIndex: i);
                }

                for (int j = 0; j < _planets[i].Moons.Count; j++)
                {
                    var mPos = GetMoonWorldPos(_planets[i], _planets[i].Moons[j], time);
                    var mScreen = _camera.WorldToScreen(mPos);
                    float mHitR = MathF.Max(_planets[i].Moons[j].Radius * _camera.Zoom, 10f);
                    float mDist = (currentMouse - mScreen).LengthSquared();
                    if (mDist < mHitR * mHitR && mDist < bestDist)
                    {
                        bestDist = mDist;
                        _hoveredObject = new(SolarMapObjectType.Moon, PlanetIndex: i, MoonIndex: j);
                    }
                }
            }

            // Stations
            for (int i = 0; i < _stations.Count; i++)
            {
                var sPos = GetStationWorldPos(_stations[i], time);
                var sScreen = _camera.WorldToScreen(sPos);
                float sHitR = MathF.Max(8f * _camera.Zoom, 10f);
                float sDist = (currentMouse - sScreen).LengthSquared();
                if (sDist < sHitR * sHitR && sDist < bestDist)
                {
                    bestDist = sDist;
                    _hoveredObject = new(SolarMapObjectType.Station, StationIndex: i);
                }
            }
        }

        // Click to select / double-click to set target and close
        if (input.IsMouseReleased(1) && !_isPanning)
        {
            if (_hoveredObject.Type != SolarMapObjectType.None)
            {
                float now = (float)game.GlobalTime;
                if (_hoveredObject == _lastClickObject && (now - _lastSolarClickTime) < DoubleClickTime)
                {
                    // Double-click: set as target and close
                    _selectedObject = _hoveredObject;
                    if (!IsCurrentNavTarget(game.Player, _selectedObject))
                        ToggleNavTarget(game.Player);
                    Close(game);
                    return true;
                }
                _selectedObject = _hoveredObject;
                _lastClickObject = _hoveredObject;
                _lastSolarClickTime = now;
            }
            else
            {
                _lastClickObject = new(SolarMapObjectType.None);
            }
            _isPanning = false;
        }
        else if (input.IsMouseReleased(1))
            _isPanning = false;

        // T or Enter to toggle nav target
        if ((input.IsKeyPressed(SDL.Scancode.T) || input.IsKeyPressed(SDL.Scancode.Return))
            && _selectedObject.Type != SolarMapObjectType.None)
        {
            ToggleNavTarget(game.Player);
        }

        return true;
    }

    public override void Update(Game game, float dt)
    {
        if (!IsOpen) return;
        var input = game.Input;
        float camSpeed = 500f / _camera.Zoom;
        if (input.IsKeyDown(SDL.Scancode.W) || input.IsKeyDown(SDL.Scancode.Up))
            _camera.Position -= new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.S) || input.IsKeyDown(SDL.Scancode.Down))
            _camera.Position += new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.A) || input.IsKeyDown(SDL.Scancode.Left))
            _camera.Position -= new Vector2(camSpeed * dt, 0);
        if (input.IsKeyDown(SDL.Scancode.D) || input.IsKeyDown(SDL.Scancode.Right))
            _camera.Position += new Vector2(camSpeed * dt, 0);
    }

    // ─────────────────────────────────────────────────────────────
    //  RENDER
    // ─────────────────────────────────────────────────────────────

    public override void Render(Game game)
    {
        if (!IsOpen) return;
        var renderer = game.SpriteRenderer;

        // Dark background
        renderer.DrawRectScreen(0, 0, GameConfig.WindowWidth, GameConfig.WindowHeight, new Color4(0, 0, 0, 180));

        // Frame with tab header
        DrawFrame(renderer, _frameX, _frameY, _frameW, _frameH, 230);
        RenderTabHeader(renderer);

        // Inner border
        renderer.DrawRectScreen(_mapX - 1, _mapY - 1, MapWidth + 2, MapHeight + 2, new Color4(50, 65, 110, 180));

        // Map content (clipped)
        renderer.SetClipRect(_mapX, _mapY, MapWidth, MapHeight);
        if (_viewMode == MapViewMode.Galaxy)
            RenderGalaxyContent(game, renderer);
        else
            RenderSolarSystemContent(game, renderer);
        renderer.ClearClipRect();

        // Info panel
        DrawFrame(renderer, _ipX, _ipY, InfoPanelWidth, _ipH, 220);
        if (_viewMode == MapViewMode.Galaxy)
            RenderGalaxyInfoPanel(game, renderer);
        else
            RenderSolarSystemInfoPanel(game, renderer);
    }

    private void RenderTabHeader(SpriteRenderer renderer)
    {
        float halfW = _frameW / 2f;

        // Tab background
        renderer.DrawRectScreen(_frameX, _frameY, _frameW, MapHeaderH, new Color4(20, 25, 50, 240));
        renderer.DrawRectScreen(_frameX, _frameY + MapHeaderH - 1, _frameW, 1, new Color4(60, 80, 140, 200));

        // Solar System tab
        bool solarActive = _viewMode == MapViewMode.SolarSystem;
        var solarBg = solarActive ? new Color4(40, 55, 100, 240) : new Color4(20, 25, 50, 200);
        var solarText = solarActive ? new Color3(200, 220, 255) : new Color3(100, 110, 140);
        renderer.DrawRectScreen(_frameX, _frameY, halfW, MapHeaderH - 1, solarBg);
        string solarLabel = "SOLAR SYSTEM [M]";
        float solarLabelW = renderer.MeasureText(solarLabel, 1.6f);
        renderer.DrawTextScreen(_frameX + halfW / 2f - solarLabelW / 2f, _frameY + 6, solarLabel, solarText, 1.6f);

        // Galaxy tab
        bool galaxyActive = _viewMode == MapViewMode.Galaxy;
        var galaxyBg = galaxyActive ? new Color4(40, 55, 100, 240) : new Color4(20, 25, 50, 200);
        var galaxyText = galaxyActive ? new Color3(200, 220, 255) : new Color3(100, 110, 140);
        renderer.DrawRectScreen(_frameX + halfW, _frameY, halfW, MapHeaderH - 1, galaxyBg);
        string galaxyLabel = "STAR CHART [M]";
        float galaxyLabelW = renderer.MeasureText(galaxyLabel, 1.6f);
        renderer.DrawTextScreen(_frameX + halfW + halfW / 2f - galaxyLabelW / 2f, _frameY + 6, galaxyLabel, galaxyText, 1.6f);

        // Divider between tabs
        renderer.DrawRectScreen(_frameX + halfW - 1, _frameY + 4, 1, MapHeaderH - 8, new Color4(60, 80, 140, 150));
    }

    // ─────────────────────────────────────────────────────────────
    //  SOLAR SYSTEM MAP RENDERING
    // ─────────────────────────────────────────────────────────────

    private void RenderSolarSystemContent(Game game, SpriteRenderer renderer)
    {
        var camera = _camera;
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
        bool isStarTarget = game.Player.NavTargetType == NavigationTargetType.Star;
        if (isStarHovered || isStarSelected)
            renderer.DrawCircle(camera, starCenter, starDisplay + 6, new Color3(255, 255, 200));
        if (isStarTarget)
            DrawTargetBrackets(renderer, camera, starCenter, starDisplay + 10, game);

        // Star label
        renderer.DrawText(camera, starCenter + new Vector2(0, starDisplay + 8),
            _currentStarSystem?.Name ?? "STAR", new Color3(255, 220, 80), Math.Max(1f, camera.Zoom * 18f));

        // Planets
        for (int i = 0; i < _planets.Count; i++)
        {
            var planet = _planets[i];
            var pPos = GetPlanetWorldPos(planet, time);
            float pRadius = Math.Max(planet.Radius, 4f);

            renderer.DrawFilledCircle(camera, pPos, pRadius, planet.Color.WithAlpha(220));

            bool isPHovered = _hoveredObject.Type == SolarMapObjectType.Planet && _hoveredObject.PlanetIndex == i;
            bool isPSelected = _selectedObject.Type == SolarMapObjectType.Planet && _selectedObject.PlanetIndex == i;
            bool isPTarget = game.Player.NavTargetType == NavigationTargetType.Planet && game.Player.NavTargetPlanetIndex == i;

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
            for (int j = 0; j < planet.Moons.Count; j++)
            {
                var moon = planet.Moons[j];
                var mPos = GetMoonWorldPos(planet, moon, time);
                float mRadius = Math.Max(moon.Radius, 2f);

                renderer.DrawFilledCircle(camera, mPos, mRadius, moon.Color.WithAlpha(180));

                bool isMHovered = _hoveredObject is { Type: SolarMapObjectType.Moon } && _hoveredObject.PlanetIndex == i && _hoveredObject.MoonIndex == j;
                bool isMSelected = _selectedObject is { Type: SolarMapObjectType.Moon } && _selectedObject.PlanetIndex == i && _selectedObject.MoonIndex == j;
                bool isMTarget = game.Player.NavTargetType == NavigationTargetType.Moon
                                 && game.Player.NavTargetPlanetIndex == i && game.Player.NavTargetMoonIndex == j;

                if (isMHovered || isMSelected)
                    renderer.DrawCircle(camera, mPos, mRadius + 3, new Color3(200, 200, 220));
                if (isMTarget)
                    DrawTargetBrackets(renderer, camera, mPos, mRadius + 6, game);

                renderer.DrawText(camera, mPos + new Vector2(0, mRadius + 4),
                    moon.Name, new Color3(160, 160, 180), Math.Max(1f, camera.Zoom * 10f));
            }
        }

        // Stations
        for (int i = 0; i < _stations.Count; i++)
        {
            var station = _stations[i];
            var sPos = GetStationWorldPos(station, time);

            // Station diamond shape
            float ds = Math.Max(6f, 3f / camera.Zoom);
            renderer.DrawFilledCircle(camera, sPos, ds * 0.6f, new Color4(100, 200, 255, 220));

            bool isSHovered = _hoveredObject.Type == SolarMapObjectType.Station && _hoveredObject.StationIndex == i;
            bool isSSelected = _selectedObject.Type == SolarMapObjectType.Station && _selectedObject.StationIndex == i;
            bool isSTarget = game.Player.NavTargetType == NavigationTargetType.Station && game.Player.NavTargetStationIndex == i;

            if (isSHovered || isSSelected)
                renderer.DrawCircle(camera, sPos, ds + 4, new Color3(100, 200, 255));
            if (isSTarget)
                DrawTargetBrackets(renderer, camera, sPos, ds + 8, game);

            renderer.DrawText(camera, sPos + new Vector2(0, ds + 4),
                station.Name, new Color3(100, 200, 255), Math.Max(1f, camera.Zoom * 12f));
        }

        // Mission markers on planets/stations
        if (_currentStarSystem != null)
        {
            float mPulse = (float)(0.5 + 0.5 * Math.Sin(game.GlobalTime * 3.0));
            byte mAlpha = (byte)(140 + (int)(mPulse * 115));
            foreach (var mission in game.Player.ActiveMissions)
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
                        // System-level mission (patrol, delivery to station) - mark all stations
                        for (int si = 0; si < _stations.Count; si++)
                        {
                            var sPos2 = GetStationWorldPos(_stations[si], time);
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
                    // Turn-in is always at a station
                    for (int si = 0; si < _stations.Count; si++)
                    {
                        var sPos2 = GetStationWorldPos(_stations[si], time);
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
    }

    /// <summary>Draw animated targeting brackets around an object.</summary>
    private static void DrawTargetBrackets(SpriteRenderer renderer, Camera camera,
        Vector2 worldPos, float radius, Game game)
    {
        float pulse = (float)(0.7 + 0.3 * Math.Sin(game.GlobalTime * 3.0));
        float r = radius + 2f * pulse;
        var color = new Color4(255, 200, 50, (byte)(150 + (int)(pulse * 105)));
        renderer.DrawCircle(camera, worldPos, r, color);
        renderer.DrawCircle(camera, worldPos, r + 1.5f, new Color4(255, 200, 50, (byte)(60 + (int)(pulse * 60))));
    }

    // ─────────────────────────────────────────────────────────────
    //  SOLAR SYSTEM INFO PANEL
    // ─────────────────────────────────────────────────────────────

    private void RenderSolarSystemInfoPanel(Game game, SpriteRenderer renderer)
    {
        // Header
        renderer.DrawRectScreen(_ipX, _ipY, InfoPanelWidth, 30, new Color4(30, 40, 70, 240));
        renderer.DrawRectScreen(_ipX, _ipY + 29, InfoPanelWidth, 1, new Color4(60, 80, 140, 200));
        string header = "SYSTEM DATA";
        float headerW = renderer.MeasureText(header, 1.8f);
        renderer.DrawTextScreen(_ipX + InfoPanelWidth / 2f - headerW / 2f, _ipY + 6, header, new Color3(140, 170, 220), 1.8f);

        float px = _ipX + 12;
        float py = _ipY + 40;

        // System summary
        if (_currentStarSystem != null)
        {
            renderer.DrawTextScreen(px, py, _currentStarSystem.Name.ToUpper(), new Color3(255, 220, 80), 1.8f);
            py += 24;
            renderer.DrawTextScreen(px, py, $"CLASS {_currentStarSystem.StarClass} STAR", new Color3(180, 180, 200), 1.3f);
            py += 16;
            renderer.DrawTextScreen(px, py, $"PLANETS: {_planets.Count}  STATIONS: {_stations.Count}", new Color3(180, 180, 200), 1.3f);
            py += 20;
        }

        renderer.DrawRectScreen(px, py, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));
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
        py = _ipY + _ipH - 160;
        renderer.DrawRectScreen(px, py, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));
        py += 8;
        renderer.DrawTextScreen(px, py, "NAV TARGET", new Color3(100, 120, 160), 1.3f);
        py += 18;
        if (game.Player.HasNavigationTarget)
        {
            renderer.DrawTextScreen(px, py, game.Player.NavTargetName.ToUpper(),
                game.Player.NavTargetColor, 1.8f);
            py += 22;
            renderer.DrawTextScreen(px, py, $"TYPE: {game.Player.NavTargetType.ToString().ToUpper()}", new Color3(180, 180, 200), 1.3f);
        }
        else
        {
            renderer.DrawTextScreen(px, py, "NONE", new Color3(80, 80, 100), 1.5f);
        }

        // Controls
        float ctrlY = _ipY + _ipH - 80;
        renderer.DrawRectScreen(px, ctrlY, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));
        renderer.DrawTextScreen(px, ctrlY + 8, "WASD/DRAG: PAN", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(px, ctrlY + 24, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(px, ctrlY + 40, "T/ENTER: SET TARGET", new Color3(255, 200, 100), 1.3f);
        renderer.DrawTextScreen(px, ctrlY + 56, "M: STAR CHART  ESC: CLOSE", new Color3(255, 150, 150), 1.3f);
    }

    private void RenderSelectedObjectInfo(Game game, SpriteRenderer renderer, float px, float py)
    {
        float time = (float)game.GlobalTime;
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
                RenderTargetButton(renderer, px, py, isTarget);
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
                RenderTargetButton(renderer, px, py, isTarget);
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
                RenderTargetButton(renderer, px, py, isTarget);
                break;

            case SolarMapObjectType.Station when sel.StationIndex >= 0 && sel.StationIndex < _stations.Count:
                var station = _stations[sel.StationIndex];
                renderer.DrawTextScreen(px, py, "SELECTED: STATION", new Color3(100, 120, 160), 1.3f);
                py += 20;
                renderer.DrawTextScreen(px, py, station.Name.ToUpper() + targetTag,
                    isTarget ? new Color3(255, 200, 50) : new Color3(100, 200, 255), 1.8f);
                py += 26;
                string orbitLabel = station.OrbitParentPlanetIndex >= 0 && station.OrbitParentPlanetIndex < _planets.Count
                    ? $"ORBITS: {_planets[station.OrbitParentPlanetIndex].Name.ToUpper()}"
                    : "ORBITS: STAR";
                renderer.DrawTextScreen(px, py, orbitLabel, new Color3(200, 200, 200), 1.5f);
                py += 20;
                renderer.DrawTextScreen(px, py, "DOCK: FLY NEAR & PRESS E",
                    new Color3(100, 200, 255), 1.5f);
                py += 28;
                RenderTargetButton(renderer, px, py, isTarget);
                break;
        }
    }

    private void RenderTargetButton(SpriteRenderer renderer, float px, float py, bool isTarget)
    {
        string btnText = isTarget ? "[T] CLEAR TARGET" : "[T] SET AS TARGET";
        var btnColor = isTarget ? new Color3(255, 100, 100) : new Color3(255, 200, 100);
        renderer.DrawRectScreen(px, py, InfoPanelWidth - 24, 20, new Color4(40, 50, 80, 180));
        float btnW = renderer.MeasureText(btnText, 1.5f);
        renderer.DrawTextScreen(px + (InfoPanelWidth - 24) / 2f - btnW / 2f, py + 2, btnText, btnColor, 1.5f);
    }

    // ─────────────────────────────────────────────────────────────
    //  GALAXY MAP RENDERING (existing, refactored into methods)
    // ─────────────────────────────────────────────────────────────

    private void RenderGalaxyContent(Game game, SpriteRenderer renderer)
    {
        var camera = _camera;

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
        int currentSys = game.Player.CurrentStarSystemIndex;
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

    private void RenderGalaxyInfoPanel(Game game, SpriteRenderer renderer)
    {
        // Header
        renderer.DrawRectScreen(_ipX, _ipY, InfoPanelWidth, 30, new Color4(30, 40, 70, 240));
        renderer.DrawRectScreen(_ipX, _ipY + 29, InfoPanelWidth, 1, new Color4(60, 80, 140, 200));
        string navLabel = "NAVIGATION DATA";
        float navLabelW = renderer.MeasureText(navLabel, 1.8f);
        renderer.DrawTextScreen(_ipX + InfoPanelWidth / 2f - navLabelW / 2f, _ipY + 6, navLabel, new Color3(140, 170, 220), 1.8f);

        float cx = _ipX + 12;
        float cy = _ipY + 40;

        renderer.DrawTextScreen(cx, cy, "SYSTEMS", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(cx, cy + 16, _starSystems.Count.ToString(), new Color3(200, 220, 255), 1.8f);
        renderer.DrawRectScreen(cx, cy + 42, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));

        // Fuel
        renderer.DrawTextScreen(cx, cy + 52, "FUEL", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(cx, cy + 68, $"{game.Player.ShipFuel:F1} / {game.Player.ShipMaxFuel:F0}", new Color3(100, 200, 255), 1.8f);
        float fuelBarW = InfoPanelWidth - 24;
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

        renderer.DrawRectScreen(cx, cy + 114, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));

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
            renderer.DrawRectScreen(cx, selY + 42, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));

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
                renderer.DrawRectScreen(cx, missionY - 4, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));
                foreach (var m in missionsHere)
                {
                    var mc = m.TypeColor;
                    string statusTag = m.Status == MissionStatus.Completed ? " [DONE]" : "";
                    renderer.DrawTextScreen(cx, missionY, $"[!] {m.TypeLabel}{statusTag}", new Color3(mc.R, mc.G, mc.B), 1.3f);
                    missionY += 16;
                }
                missionY += 4;
            }

            renderer.DrawRectScreen(cx, missionY, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));
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
        float ctrlY = _ipY + _ipH - 110;
        renderer.DrawRectScreen(cx, ctrlY, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));
        renderer.DrawTextScreen(cx, ctrlY + 8, "WASD/ARROWS/DRAG: PAN", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 24, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 40, "CLICK: SELECT SYSTEM", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 56, "DBLCLICK/ENTER: TRAVEL", new Color3(100, 255, 100), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 72, "M: SOLAR SYSTEM  ESC: CLOSE", new Color3(255, 150, 150), 1.3f);
    }

    private static void DrawMissionDiamond(SpriteRenderer renderer, Camera camera,
        Vector2 objectPos, float objectRadius, Color3 color, byte alpha, string? label = null)
    {
        var iconPos = objectPos + new Vector2(0, -(objectRadius + 16));
        float diamondSize = 6f;
        var screenIcon = camera.WorldToScreen(iconPos);
        if (screenIcon.X >= -20 && screenIcon.X < GameConfig.WindowWidth + 20 &&
            screenIcon.Y >= -20 && screenIcon.Y < GameConfig.WindowHeight + 20)
        {
            float ds = diamondSize * Math.Max(1f, camera.Zoom * 0.5f);
            var c = new Color4(color.R, color.G, color.B, alpha);
            // Filled diamond
            renderer.DrawLineScreen(screenIcon.X, screenIcon.Y - ds, screenIcon.X + ds, screenIcon.Y, c);
            renderer.DrawLineScreen(screenIcon.X + ds, screenIcon.Y, screenIcon.X, screenIcon.Y + ds, c);
            renderer.DrawLineScreen(screenIcon.X, screenIcon.Y + ds, screenIcon.X - ds, screenIcon.Y, c);
            renderer.DrawLineScreen(screenIcon.X - ds, screenIcon.Y, screenIcon.X, screenIcon.Y - ds, c);
            // "!" inside diamond
            renderer.DrawTextScreen(screenIcon.X - 3, screenIcon.Y - ds + 2, "!",
                new Color3(color.R, color.G, color.B), 1.5f);
            // Label above diamond
            if (label != null)
            {
                float labelW = renderer.MeasureText(label, 1.2f);
                renderer.DrawTextScreen(screenIcon.X - labelW / 2f, screenIcon.Y - ds - 14, label,
                    new Color3(color.R, color.G, color.B), 1.2f);
            }
        }
    }
}
