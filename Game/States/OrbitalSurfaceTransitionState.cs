using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Core.Config;

namespace SpaceExplorationGame.States;

/// <summary>
/// Bidirectional orbital/surface transition.
/// Handles both landing (solar-system to surface) and takeoff (surface to solar-system)
/// using the same animation timeline in forward/reverse.
/// </summary>
public class OrbitalSurfaceTransitionState : GameState
{
    private enum TransitionMode
    {
        Landing,
        Takeoff
    }

    private readonly TransitionMode _mode;

    public override GameStateType Type => _mode == TransitionMode.Landing
        ? GameStateType.PlanetSurface
        : GameStateType.SolarSystem;

    private readonly StarSystemData _starSystem;
    private readonly PlanetData _planetOrMoon;
    private readonly int _tileX;
    private readonly int _tileY;
    private readonly Vector2 _shipWorldStart;
    private readonly Vector2 _targetBodyWorldStart;
    private readonly Vector2 _solarCameraStart;
    private readonly float _solarZoomStart;
    private readonly float _shipRotationStart;

    private PlanetSurfaceData _surfaceData = null!;
    private nint _terrainTexture;
    private Camera _terrainBlendCamera = new Camera(1, 1, 0.0001f, 100000f);
    private float _elapsed;
    private bool _landingSfxPlayed;

    private Vector2 _shipScreenStart;
    private Vector2 _planetScreenStart;
    private float _planetRadiusStartPx;

    private readonly List<StarParticle> _stars = [];
    private readonly Random _rng = new();

    private const float AlignDuration = 0.85f;
    private const float DescentDuration = 2.85f;
    private const float TouchdownDuration = 1.35f;
    public const float TotalDuration = AlignDuration + DescentDuration + TouchdownDuration;

    private float ScreenW;
    private float ScreenH;
    private float CX;
    private float CY;

    private readonly record struct StarParticle(float X, float Y, byte Brightness);

    public OrbitalSurfaceTransitionState(
        StarSystemData starSystem,
        PlanetData planetOrMoon,
        int landingTileX,
        int landingTileY,
        Vector2 shipWorldStart,
        float shipRotationStart,
        Vector2 targetBodyWorldStart,
        Vector2 solarCameraStart,
        float solarZoomStart)
    {
        _mode = TransitionMode.Landing;
        _starSystem = starSystem;
        _planetOrMoon = planetOrMoon;
        _tileX = landingTileX;
        _tileY = landingTileY;
        _shipWorldStart = shipWorldStart;
        _shipRotationStart = shipRotationStart;
        _targetBodyWorldStart = targetBodyWorldStart;
        _solarCameraStart = solarCameraStart;
        _solarZoomStart = MathF.Max(0.01f, solarZoomStart);
    }

    public OrbitalSurfaceTransitionState(
        StarSystemData starSystem,
        PlanetData planetOrMoon,
        PlanetSurfaceData surfaceData,
        int launchTileX,
        int launchTileY)
    {
        _mode = TransitionMode.Takeoff;
        _starSystem = starSystem;
        _planetOrMoon = planetOrMoon;
        _surfaceData = surfaceData;
        _tileX = Math.Clamp(launchTileX, 0, Math.Max(0, surfaceData.Width - 1));
        _tileY = Math.Clamp(launchTileY, 0, Math.Max(0, surfaceData.Height - 1));

        _shipWorldStart = Vector2.Zero;
        _targetBodyWorldStart = Vector2.Zero;
        _solarCameraStart = Vector2.Zero;
        _solarZoomStart = CameraConfig.SolarSystemZoomDefault;
    }

    public override void Enter(Game game)
    {
        _elapsed = 0f;
        _landingSfxPlayed = false;
        ScreenW = game.SpriteRenderer.WindowWidth;
        ScreenH = game.SpriteRenderer.WindowHeight;
        CX = ScreenW * 0.5f;
        CY = ScreenH * 0.5f;

        // Announce the transition so other clients can play departure/arrival effects
        game.Network?.SendTransitionStarted(
            _mode == TransitionMode.Landing ? new Engine.Network.NetPlayerTransition
            {
                From = Engine.Network.NetPlayerLocation.ForSolarSystem(_starSystem.Index),
                To = Engine.Network.NetPlayerLocation.ForMoon(_starSystem.Index, _planetOrMoon.Index, _planetOrMoon.MoonIndex)
            }
            : new Engine.Network.NetPlayerTransition
            {
                From = Engine.Network.NetPlayerLocation.ForMoon(_starSystem.Index, _planetOrMoon.Index, _planetOrMoon.MoonIndex),
                To = Engine.Network.NetPlayerLocation.ForSolarSystem(_starSystem.Index)
            }
        );

        if (_mode == TransitionMode.Landing)
        {
            _surfaceData = game.UniverseGenerator.GeneratePlanetSurface(_starSystem, _planetOrMoon);

            _shipScreenStart = WorldToScreenFromSolarSnapshot(_shipWorldStart);
            _planetScreenStart = WorldToScreenFromSolarSnapshot(_targetBodyWorldStart);
            _planetRadiusStartPx = MathF.Max(_planetOrMoon.Radius * _solarZoomStart, 8f);
        }
        else
        {
            _shipScreenStart = new Vector2(CX, CY);
            _planetScreenStart = new Vector2(CX, CY);
            _planetRadiusStartPx = MathF.Max(_planetOrMoon.Radius * CameraConfig.SolarSystemZoomDefault, 8f);
        }

        _terrainTexture = TerrainRenderer.CreateTerrainTexture(game.Textures, _surfaceData);

        _stars.Clear();
        for (int i = 0; i < 50; i++)
        {
            _stars.Add(new StarParticle(
                X: (float)_rng.NextDouble() * ScreenW,
                Y: (float)_rng.NextDouble() * ScreenH,
                Brightness: (byte)_rng.Next(40, 145)));
        }

        if (_mode == TransitionMode.Landing)
        {
            game.Audio.SetMusicTheme(AudioThemes.PlanetSurface);
        }
        else
        {
            game.Audio.PlaySfx(AudioSfx.Takeoff);
            game.Audio.SetMusicTheme(AudioThemes.SolarSystem);
        }
    }

    public override void Exit(Game game)
    {
        if (_terrainTexture != nint.Zero)
        {
            game.Textures.DestroyTexture(_terrainTexture);
            _terrainTexture = nint.Zero;
        }
    }

    public override void UpdateInput(Game game)
    {
        // No user input during cinematic landing.
    }

    public override void Update(Game game)
    {
        float dt = game.DeltaTime;
        _elapsed += dt;

        if (_mode == TransitionMode.Landing && !_landingSfxPlayed && _elapsed >= AlignDuration * 0.65f)
        {
            game.Audio.PlaySfx(AudioSfx.Landing);
            _landingSfxPlayed = true;
        }

        if (_elapsed >= TotalDuration)
        {
            game.Player.SolarSystemReturnContext = _planetOrMoon.IsMoon
                ? PlayerData.ReturnContext.FromMoon
                : PlayerData.ReturnContext.FromPlanet;
            game.Player.ReturnPlanetIndex = _planetOrMoon.IsMoon ? -1 : _planetOrMoon.Index;
            game.Player.ReturnMoonPlanetIndex = _planetOrMoon.IsMoon ? _planetOrMoon.Index : -1;
            game.Player.ReturnMoonIndex = _planetOrMoon.IsMoon ? _planetOrMoon.MoonIndex : -1;

            if (_mode == TransitionMode.Landing)
            {
                game.ChangeState(new PlanetSurfaceState(
                    _starSystem,
                    _planetOrMoon,
                    _tileX,
                    _tileY,
                    preGeneratedSurfaceData: _surfaceData));
            }
            else
            {
                game.Player.InVehicle = false;
                game.Player.ClearSavedSurfacePositions();
                game.ChangeState(new SolarSystemState(_starSystem));
            }
            return;
        }
    }

    public override void RenderGame(Game game)
    {
        var renderer = game.SpriteRenderer;

        renderer.DrawRectScreen(0, 0, ScreenW, ScreenH, new Color4(3, 4, 10, 255));

        foreach (var s in _stars)
            renderer.DrawRectScreen(s.X, s.Y, 1.4f, 1.4f, new Color3(s.Brightness, s.Brightness, s.Brightness));

        float animElapsed = _mode == TransitionMode.Landing ? _elapsed : (TotalDuration - _elapsed);
        float p = Math.Clamp(animElapsed / TotalDuration, 0f, 1f);
        float descentP = Math.Clamp((animElapsed - AlignDuration) / DescentDuration, 0f, 1f);
        float touchdownP = Math.Clamp((animElapsed - AlignDuration - DescentDuration) / TouchdownDuration, 0f, 1f);

        float travelP = EaseInOut01(MathF.Max(0f, (p - 0.08f) / 0.86f));

        float planetX = Lerp(_planetScreenStart.X, CX, travelP);
        float planetY = Lerp(_planetScreenStart.Y, CY, travelP);
        float planetRadius = Lerp(_planetRadiusStartPx, MathF.Max(ScreenW, ScreenH) * 1.08f, EaseInOut01(descentP));

        float terrainBlend = EaseInOut01(Math.Clamp((descentP - 0.24f) / 0.76f, 0f, 1f));
        game.PlanetRenderer.RenderBodyScreen(renderer,
            planetX, planetY, planetRadius,
            _planetOrMoon.Color, _planetOrMoon.Type, _planetOrMoon.IsMoon, _planetOrMoon.IsMoon ? _planetOrMoon.MoonIndex : _planetOrMoon.Index,
            (float)game.GlobalTime, alphaMultiplier: 1f - terrainBlend * terrainBlend);

        Vector2 landingScreenTarget = new(CX, CY + 4f);
        if (_terrainTexture != nint.Zero && terrainBlend > 0f)
        {
            landingScreenTarget = DrawTerrainLandingBlend(game, planetX, planetY, planetRadius, terrainBlend, descentP);
        }

        float shipApproachP = EaseInOut01(Math.Clamp((animElapsed - 0.15f) / (AlignDuration + DescentDuration * 0.9f), 0f, 1f));
        Vector2 shipCruiseTarget = new(CX, CY - 36f);
        Vector2 shipFinalTarget = landingScreenTarget + new Vector2(0f, -18f);
        float lockToTileP = EaseInOut01(Math.Clamp((descentP - 0.62f) / 0.38f, 0f, 1f));
        Vector2 shipTarget = Vector2.Lerp(shipCruiseTarget, shipFinalTarget, lockToTileP);
        float shipX = Lerp(_shipScreenStart.X, shipTarget.X, shipApproachP);
        float shipBaseY = Lerp(_shipScreenStart.Y, shipTarget.Y, shipApproachP);
        float descendOffset = EaseInOut01(touchdownP) * 18f;
        float shipY = shipBaseY + descendOffset;

        // Landing: lerp from the in-space rotation to 0° (horizontal/landed) over the whole descent.
        // Takeoff: ship is already at 0° and stays there (no rotation needed).
        float shipRotation = _mode == TransitionMode.Landing
            ? MathHelper.LerpRotation(_shipRotationStart, 0f, p * 4.0f) // faster easing for rotation so it settles sooner than the position
            : 0f;

        // Lerp the rendered ship scale so it seamlessly matches the camera zoom in the
        // source and destination states.  p==0 corresponds to the solar-system view
        // (zoom = _solarZoomStart) and p==1 corresponds to the planet-surface view
        // (zoom = PlanetSurfaceZoomDefault).  Using `p` (the forward-direction progress)
        // works for both landing (p 0→1) and takeoff (p 1→0).
        float shipZoom = Lerp(_solarZoomStart, CameraConfig.PlanetSurfaceZoomDefault, p);

        game.SpaceshipRenderer.RenderScreen(renderer,
            shipX, shipY, shipRotation, game.Player.CurrentShipType.Id, game.Player.CurrentShipType.SpriteSize, shipZoom);

        if (touchdownP > 0f)
        {
            float ringR = 18f + 36f * touchdownP;
            byte a = (byte)(140 * (1f - touchdownP));
            renderer.DrawSolidRingScreen(landingScreenTarget.X, landingScreenTarget.Y, ringR * 0.6f, ringR,
                new Color4(200, 170, 120, a), 40);
        }
    }

    public override void RenderHud(Game game) { }

    private Vector2 WorldToScreenFromSolarSnapshot(Vector2 world)
    {
        Vector2 delta = (world - _solarCameraStart) * _solarZoomStart;
        return new Vector2(CX + delta.X, CY + delta.Y);
    }

    private Vector2 DrawTerrainLandingBlend(Game game, float planetX, float planetY, float planetRadius,
        float terrainBlend, float descentP)
    {
        float ts = WindowConfig.TileSize;
        float mapW = _surfaceData.Width;
        float mapH = _surfaceData.Height;
        float mapSize = MathF.Min(mapW, mapH);

        // --- Camera zoom ---
        // Start: planet disc diameter covers the terrain circle (circular texture inscribed in mapSize tiles).
        // End:   gameplay zoom, matching the first frame of PlanetSurfaceState exactly.
        float startZoom = (planetRadius * 2f * 0.95f) / (mapSize * ts);
        float endZoom = CameraConfig.PlanetSurfaceZoomDefault;
        float zoom = Lerp(startZoom, endZoom, terrainBlend);

        // --- Camera position (world space) ---
        // Pan from the map centre toward the landing tile as we zoom in.
        float panP = EaseInOut01(Math.Clamp((descentP - 0.08f) / 0.92f, 0f, 1f));
        Vector2 camPos = Vector2.Lerp(
            new Vector2(mapW * 0.5f * ts, mapH * 0.5f * ts),
            new Vector2(_tileX * ts, _tileY * ts),
            panP);

        // --- Viewport offset ---
        // Start: the camera position (map centre) appears at (planetX, planetY).
        // End:   it appears at screen centre (offset = 0).
        float vpOffX = Lerp(planetX - CX, 0f, terrainBlend);
        float vpOffY = Lerp(planetY - CY, 0f, terrainBlend);

        // Configure the camera — atmosphere and settlements use it directly.
        _terrainBlendCamera.Update((int)ScreenW, (int)ScreenH);
        _terrainBlendCamera.Zoom = zoom;
        _terrainBlendCamera.Position = camPos;
        _terrainBlendCamera.ViewportOffsetX = vpOffX;
        _terrainBlendCamera.ViewportOffsetY = vpOffY;

        // Draw the full terrain texture using camera-derived screen rect.
        Vector2 texTopLeftScreen = _terrainBlendCamera.WorldToScreen(Vector2.Zero);
        float dstW = mapW * ts * zoom;
        float dstH = mapH * ts * zoom;
        byte a = (byte)Math.Clamp((int)(terrainBlend * 255), 0, 255);
        game.SpriteRenderer.DrawTextureScreen(_terrainTexture,
            new Rect(0, 0, mapW, mapH),
            new Rect(texTopLeftScreen.X, texTopLeftScreen.Y, dstW, dstH), a);

        PlanetSurfaceRenderer.RenderAtmosphere(game.SpriteRenderer, _terrainBlendCamera,
            _surfaceData, _planetOrMoon.Type, game.GlobalTime, alphaScale: terrainBlend);

        float markerP = EaseInOut01(Math.Clamp((descentP - 0.22f) / 0.78f, 0f, 1f)) * terrainBlend;
        byte markerAlpha = (byte)Math.Clamp((int)(markerP * 255f), 0, 255);
        if (markerAlpha > 0)
        {
            SettlementRenderer.RenderProjected(game.SpriteRenderer, _surfaceData,
                (float worldCenterX, float worldCenterY, float worldW, float worldH) =>
                {
                    Vector2 topLeft = _terrainBlendCamera.WorldToScreen(
                        new Vector2(worldCenterX - worldW * 0.5f, worldCenterY - worldH * 0.5f));
                    float screenW = MathF.Max(1f, worldW * zoom);
                    float screenH = MathF.Max(1f, worldH * zoom);
                    return new Rect(topLeft.X, topLeft.Y, screenW, screenH);
                },
                (Vector2 worldPos) => _terrainBlendCamera.WorldToScreen(worldPos),
                markerAlpha);
        }

        // Return the screen position of the landing tile for ship / touchdown ring placement.
        return _terrainBlendCamera.WorldToScreen(new Vector2(_tileX * ts, _tileY * ts));
    }

    private static float Lerp(float a, float b, float t) => float.Lerp(a, b, Math.Clamp(t, 0f, 1f));

    private static float EaseInOut01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
