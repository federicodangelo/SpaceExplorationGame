using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using Engine.Platform;

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
    private readonly PlanetData _planet;
    private readonly int _tileX;
    private readonly int _tileY;
    private readonly Vector2 _shipWorldStart;
    private readonly Vector2 _targetBodyWorldStart;
    private readonly Vector2 _solarCameraStart;
    private readonly float _solarZoomStart;
    private readonly bool _isMoon;
    private readonly int _moonPlanetIndex;
    private readonly int _moonIndex;

    private PlanetSurfaceData _surfaceData = null!;
    private nint _terrainTexture;
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
    private static float TotalDuration => AlignDuration + DescentDuration + TouchdownDuration;

    private static readonly float ScreenW = GameConfig.WindowWidth;
    private static readonly float ScreenH = GameConfig.WindowHeight;
    private static readonly float CX = ScreenW * 0.5f;
    private static readonly float CY = ScreenH * 0.5f;

    private readonly record struct StarParticle(float X, float Y, byte Brightness);

    public OrbitalSurfaceTransitionState(
        StarSystemData starSystem,
        PlanetData planet,
        int landingTileX,
        int landingTileY,
        Vector2 shipWorldStart,
        Vector2 targetBodyWorldStart,
        Vector2 solarCameraStart,
        float solarZoomStart,
        bool isMoon,
        int moonPlanetIndex,
        int moonIndex)
    {
        _mode = TransitionMode.Landing;
        _starSystem = starSystem;
        _planet = planet;
        _tileX = landingTileX;
        _tileY = landingTileY;
        _shipWorldStart = shipWorldStart;
        _targetBodyWorldStart = targetBodyWorldStart;
        _solarCameraStart = solarCameraStart;
        _solarZoomStart = MathF.Max(0.01f, solarZoomStart);
        _isMoon = isMoon;
        _moonPlanetIndex = moonPlanetIndex;
        _moonIndex = moonIndex;
    }

    public OrbitalSurfaceTransitionState(
        StarSystemData starSystem,
        PlanetData planet,
        PlanetSurfaceData surfaceData,
        int launchTileX,
        int launchTileY,
        bool isMoon,
        int moonPlanetIndex,
        int moonIndex)
    {
        _mode = TransitionMode.Takeoff;
        _starSystem = starSystem;
        _planet = planet;
        _surfaceData = surfaceData;
        _tileX = Math.Clamp(launchTileX, 0, Math.Max(0, surfaceData.Width - 1));
        _tileY = Math.Clamp(launchTileY, 0, Math.Max(0, surfaceData.Height - 1));

        _shipWorldStart = Vector2.Zero;
        _targetBodyWorldStart = Vector2.Zero;
        _solarCameraStart = Vector2.Zero;
        _solarZoomStart = GameConfig.SolarSystemZoomDefault;

        _isMoon = isMoon;
        _moonPlanetIndex = moonPlanetIndex;
        _moonIndex = moonIndex;

        _shipScreenStart = new Vector2(CX, CY);
        _planetScreenStart = new Vector2(CX, CY);
        _planetRadiusStartPx = MathF.Max(_planet.Radius * GameConfig.SolarSystemZoomDefault, 8f);
    }

    public override void Enter(Game game)
    {
        _elapsed = 0f;
        _landingSfxPlayed = false;

        if (_mode == TransitionMode.Landing)
        {
            _surfaceData = game.UniverseGenerator.GeneratePlanetSurface(_starSystem, _planet);

            _shipScreenStart = WorldToScreenFromSolarSnapshot(_shipWorldStart);
            _planetScreenStart = WorldToScreenFromSolarSnapshot(_targetBodyWorldStart);
            _planetRadiusStartPx = MathF.Max(_planet.Radius * _solarZoomStart, 8f);
        }

        _terrainTexture = CreateTerrainTexture(game, _surfaceData);

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
            if (_mode == TransitionMode.Landing)
            {
                game.Player.SolarSystemReturnContext = _isMoon
                    ? PlayerData.ReturnContext.FromMoon
                    : PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = _isMoon ? -1 : _planet.Index;
                game.Player.ReturnMoonPlanetIndex = _isMoon ? _moonPlanetIndex : -1;
                game.Player.ReturnMoonIndex = _isMoon ? _moonIndex : -1;

                game.ChangeState(new PlanetSurfaceState(
                    _starSystem,
                    _planet,
                    _tileX,
                    _tileY,
                    preGeneratedSurfaceData: _surfaceData));
            }
            else
            {
                game.Player.SolarSystemReturnContext = _isMoon
                    ? PlayerData.ReturnContext.FromMoon
                    : PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = _isMoon ? -1 : _planet.Index;
                game.Player.ReturnMoonPlanetIndex = _isMoon ? _moonPlanetIndex : -1;
                game.Player.ReturnMoonIndex = _isMoon ? _moonIndex : -1;

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
        float modeP = Math.Clamp(_elapsed / TotalDuration, 0f, 1f);
        float p = Math.Clamp(animElapsed / TotalDuration, 0f, 1f);
        float descentP = Math.Clamp((animElapsed - AlignDuration) / DescentDuration, 0f, 1f);
        float touchdownP = Math.Clamp((animElapsed - AlignDuration - DescentDuration) / TouchdownDuration, 0f, 1f);

        float travelP = EaseInOut01(MathF.Max(0f, (p - 0.08f) / 0.86f));

        float planetX = Lerp(_planetScreenStart.X, CX, travelP);
        float planetY = Lerp(_planetScreenStart.Y, CY, travelP);
        float planetRadius = Lerp(_planetRadiusStartPx, MathF.Max(ScreenW, ScreenH) * 1.08f, EaseInOut01(descentP));

        int planetSeed = _isMoon ? (_moonPlanetIndex * 101 + _moonIndex * 17 + 7) : _planet.Index;
        game.PlanetRenderer.RenderBodyScreen(renderer,
            planetX, planetY, planetRadius,
            _planet.Color, _planet.Type, _isMoon, planetSeed,
            (float)game.GlobalTime);

        float terrainBlend = EaseInOut01(Math.Clamp((descentP - 0.24f) / 0.76f, 0f, 1f));
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

        float shipRotation = 90f;
        if (_mode == TransitionMode.Takeoff)
            shipRotation = Lerp(90f, 0f, EaseInOut01(Math.Clamp((modeP - 0.82f) / 0.18f, 0f, 1f)));

        game.SpaceshipRenderer.RenderFlyingScreen(renderer,
            shipX, shipY, shipRotation, game.Player.CurrentShipType.Id, game.Player.CurrentShipType.SpriteSize);

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

    private static nint CreateTerrainTexture(Game game, PlanetSurfaceData surface)
    {
        int w = surface.Width;
        int h = surface.Height;
        var pixels = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var terrain = surface.Tiles[x, y];
                var color = PlanetSurfaceGenerator.GetTerrainColor(terrain);
                var variationColor = ITileMapRenderer.GetColorVariation(color, x, y, 800f);
                int idx = (y * w + x) * 4;
                pixels[idx + 0] = variationColor.R;
                pixels[idx + 1] = variationColor.G;
                pixels[idx + 2] = variationColor.B;
                pixels[idx + 3] = 255;
            }
        }

        // Match planet map terrain texture: settlement overlay + nearest sampling.
        foreach (var s in surface.Settlements)
        {
            for (int sx = s.TileRect.X; sx < s.TileRect.X + s.TileRect.Width && sx < w; sx++)
            {
                for (int sy = s.TileRect.Y; sy < s.TileRect.Y + s.TileRect.Height && sy < h; sy++)
                {
                    int idx = (sy * w + sx) * 4;
                    pixels[idx + 0] = 100;
                    pixels[idx + 1] = 100;
                    pixels[idx + 2] = 120;
                    pixels[idx + 3] = 255;
                }
            }
        }

        return game.Textures.CreateTextureFromPixels(pixels, w, h, TextureScaleMode.Nearest);
    }

    private Vector2 DrawTerrainLandingBlend(Game game, float planetX, float planetY, float planetRadius,
        float terrainBlend, float descentP)
    {
        float mapW = _surfaceData.Width;
        float mapH = _surfaceData.Height;

        // Camera pan in texture-space: world center -> selected landing tile.
        float panP = EaseInOut01(Math.Clamp((descentP - 0.08f) / 0.92f, 0f, 1f));
        float centerX = Lerp(mapW * 0.5f, _tileX, panP);
        float centerY = Lerp(mapH * 0.5f, _tileY, panP);

        // Camera zoom in texture-space: whole planet map -> approx surface gameplay view at default zoom.
        float endViewTilesW = GameConfig.WindowWidth / (GameConfig.TileSize * GameConfig.PlanetSurfaceZoomDefault);
        float endViewTilesH = GameConfig.WindowHeight / (GameConfig.TileSize * GameConfig.PlanetSurfaceZoomDefault);

        float srcW = Lerp(mapW, endViewTilesW, terrainBlend);
        float srcH = Lerp(mapH, endViewTilesH, terrainBlend);

        srcW = Math.Clamp(srcW, 8f, mapW);
        srcH = Math.Clamp(srcH, 8f, mapH);

        float srcX = Math.Clamp(centerX - srcW * 0.5f, 0f, mapW - srcW);
        float srcY = Math.Clamp(centerY - srcH * 0.5f, 0f, mapH - srcH);

        // Screen destination: starts near planet disc, expands to full-screen by touchdown.
        float dstW = Lerp(planetRadius * 2f * 0.95f, ScreenW, terrainBlend);
        float dstH = Lerp(planetRadius * 2f * 0.95f, ScreenH, terrainBlend);
        float dstCenterX = Lerp(planetX, CX, terrainBlend);
        float dstCenterY = Lerp(planetY, CY, terrainBlend);

        float dstX = dstCenterX - dstW * 0.5f;
        float dstY = dstCenterY - dstH * 0.5f;

        byte a = (byte)Math.Clamp((int)(terrainBlend * 255), 0, 255);
        game.SpriteRenderer.DrawTextureScreen(_terrainTexture,
            new Rect(srcX, srcY, srcW, srcH),
            new Rect(dstX, dstY, dstW, dstH), a);

        float markerP = EaseInOut01(Math.Clamp((descentP - 0.22f) / 0.78f, 0f, 1f)) * terrainBlend;
        byte markerAlpha = (byte)Math.Clamp((int)(markerP * 255f), 0, 255);
        if (markerAlpha > 0)
        {
            SettlementRenderer.RenderProjected(game.SpriteRenderer, _surfaceData,
                (float worldCenterX, float worldCenterY, float worldW, float worldH) =>
                {
                    float tileSize = GameConfig.TileSize;
                    float leftTile = (worldCenterX - worldW * 0.5f) / tileSize;
                    float topTile = (worldCenterY - worldH * 0.5f) / tileSize;
                    float rightTile = (worldCenterX + worldW * 0.5f) / tileSize;
                    float bottomTile = (worldCenterY + worldH * 0.5f) / tileSize;

                    float u0 = Math.Clamp((leftTile - srcX) / srcW, 0f, 1f);
                    float v0 = Math.Clamp((topTile - srcY) / srcH, 0f, 1f);
                    float u1 = Math.Clamp((rightTile - srcX) / srcW, 0f, 1f);
                    float v1 = Math.Clamp((bottomTile - srcY) / srcH, 0f, 1f);

                    float screenX = dstX + u0 * dstW;
                    float screenY = dstY + v0 * dstH;
                    float screenW = MathF.Max(1f, (u1 - u0) * dstW);
                    float screenH = MathF.Max(1f, (v1 - v0) * dstH);
                    return new Rect(screenX, screenY, screenW, screenH);
                },
                (Vector2 worldPos) =>
                {
                    float tileX = worldPos.X / GameConfig.TileSize;
                    float tileY = worldPos.Y / GameConfig.TileSize;

                    float u = Math.Clamp((tileX - srcX) / srcW, 0f, 1f);
                    float v = Math.Clamp((tileY - srcY) / srcH, 0f, 1f);
                    return new Vector2(dstX + u * dstW, dstY + v * dstH);
                },
                markerAlpha);
        }

        float targetU = (_tileX - srcX) / srcW;
        float targetV = (_tileY - srcY) / srcH;
        targetU = Math.Clamp(targetU, 0f, 1f);
        targetV = Math.Clamp(targetV, 0f, 1f);
        return new Vector2(dstX + targetU * dstW, dstY + targetV * dstH);
    }

    private static float Lerp(float a, float b, float t) => float.Lerp(a, b, Math.Clamp(t, 0f, 1f));

    private static float EaseInOut01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
