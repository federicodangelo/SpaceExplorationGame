using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.States;

/// <summary>
/// Deluxe orbital-to-surface landing transition.
/// Blends from the current solar-system view into the generated terrain around the selected landing tile,
/// then hands off directly to <see cref="PlanetSurfaceState"/> with matching landing coordinates.
/// </summary>
public class LandingTransitionState : GameState
{
    public override GameStateType Type => GameStateType.PlanetSurface;

    private readonly StarSystemData _starSystem;
    private readonly PlanetData _planet;
    private readonly int _landingTileX;
    private readonly int _landingTileY;
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

    private readonly record struct StarParticle(float X, float Y, float Speed, byte Brightness);

    public LandingTransitionState(
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
        _starSystem = starSystem;
        _planet = planet;
        _landingTileX = landingTileX;
        _landingTileY = landingTileY;
        _shipWorldStart = shipWorldStart;
        _targetBodyWorldStart = targetBodyWorldStart;
        _solarCameraStart = solarCameraStart;
        _solarZoomStart = MathF.Max(0.01f, solarZoomStart);
        _isMoon = isMoon;
        _moonPlanetIndex = moonPlanetIndex;
        _moonIndex = moonIndex;
    }

    public override void Enter(Game game)
    {
        _elapsed = 0f;
        _landingSfxPlayed = false;

        _surfaceData = game.WorldGenerator.GeneratePlanetSurface(game.Seeds, _starSystem, _planet);
        _terrainTexture = CreateTerrainTexture(game, _surfaceData);

        _shipScreenStart = WorldToScreenFromSolarSnapshot(_shipWorldStart);
        _planetScreenStart = WorldToScreenFromSolarSnapshot(_targetBodyWorldStart);
        _planetRadiusStartPx = MathF.Max(_planet.Radius * _solarZoomStart, 8f);

        _stars.Clear();
        for (int i = 0; i < 300; i++)
        {
            _stars.Add(new StarParticle(
                X: (float)_rng.NextDouble() * ScreenW,
                Y: (float)_rng.NextDouble() * ScreenH,
                Speed: 12f + (float)_rng.NextDouble() * 45f,
                Brightness: (byte)_rng.Next(40, 145)));
        }

        game.Audio.SetMusicTheme(MusicTheme.PlanetSurface);
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

        if (!_landingSfxPlayed && _elapsed >= AlignDuration * 0.65f)
        {
            game.Audio.PlaySfx(SfxType.Landing);
            _landingSfxPlayed = true;
        }

        if (_elapsed >= TotalDuration)
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
                _landingTileX,
                _landingTileY,
                preGeneratedSurfaceData: _surfaceData,
                skipIntroLandingAnimation: true));
            return;
        }

        float starBoost = 1f + 2.2f * EaseInOut01(MathF.Min(1f, _elapsed / (AlignDuration + DescentDuration)));
        for (int i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            float nx = s.X - s.Speed * starBoost * dt;
            if (nx < -2f) nx = ScreenW + 2f;
            _stars[i] = s with { X = nx };
        }
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;

        renderer.DrawRectScreen(0, 0, ScreenW, ScreenH, new Color4(3, 4, 10, 255));

        foreach (var s in _stars)
            renderer.DrawRectScreen(s.X, s.Y, 1.4f, 1.4f, new Color3(s.Brightness, s.Brightness, s.Brightness));

        float p = Math.Clamp(_elapsed / TotalDuration, 0f, 1f);
        float descentP = Math.Clamp((_elapsed - AlignDuration) / DescentDuration, 0f, 1f);
        float touchdownP = Math.Clamp((_elapsed - AlignDuration - DescentDuration) / TouchdownDuration, 0f, 1f);

        float travelP = EaseInOut01(MathF.Max(0f, (p - 0.08f) / 0.86f));

        float planetX = Lerp(_planetScreenStart.X, CX, travelP);
        float planetY = Lerp(_planetScreenStart.Y, CY, travelP);
        float planetRadius = Lerp(_planetRadiusStartPx, MathF.Max(ScreenW, ScreenH) * 1.08f, EaseInOut01(descentP));

        var inner = new Color4(
            (byte)Math.Clamp(_planet.Color.R + 35, 0, 255),
            (byte)Math.Clamp(_planet.Color.G + 35, 0, 255),
            (byte)Math.Clamp(_planet.Color.B + 35, 0, 255),
            255);
        var outer = new Color4(
            (byte)Math.Clamp((int)(_planet.Color.R * 0.68f), 0, 255),
            (byte)Math.Clamp((int)(_planet.Color.G * 0.68f), 0, 255),
            (byte)Math.Clamp((int)(_planet.Color.B * 0.68f), 0, 255),
            255);

        renderer.DrawFilledCircleScreen(planetX, planetY, planetRadius, inner, outer, planetRadius * 0.2f, 64);

        float cloudAlpha = 0.45f * (1f - touchdownP);
        DrawAtmosphere(renderer, planetX, planetY, planetRadius, cloudAlpha);

        float terrainBlend = EaseInOut01(Math.Clamp((descentP - 0.24f) / 0.76f, 0f, 1f));
        Vector2 landingScreenTarget = new(CX, CY + 4f);
        if (_terrainTexture != nint.Zero && terrainBlend > 0f)
        {
            landingScreenTarget = DrawTerrainLandingBlend(game, planetX, planetY, planetRadius, terrainBlend, descentP);
        }

        float shipApproachP = EaseInOut01(Math.Clamp((_elapsed - 0.15f) / (AlignDuration + DescentDuration * 0.9f), 0f, 1f));
        Vector2 shipCruiseTarget = new(CX, CY - 36f);
        Vector2 shipFinalTarget = landingScreenTarget + new Vector2(0f, -18f);
        float lockToTileP = EaseInOut01(Math.Clamp((descentP - 0.62f) / 0.38f, 0f, 1f));
        Vector2 shipTarget = Vector2.Lerp(shipCruiseTarget, shipFinalTarget, lockToTileP);
        float shipX = Lerp(_shipScreenStart.X, shipTarget.X, shipApproachP);
        float shipBaseY = Lerp(_shipScreenStart.Y, shipTarget.Y, shipApproachP);
        float descendOffset = EaseInOut01(touchdownP) * 18f;
        float shipY = shipBaseY + descendOffset;

        game.SpaceshipRenderer.RenderFlyingScreen(renderer,
            shipX, shipY, 90f, game.Player.CurrentShipType.Id, game.Player.CurrentShipType.SpriteSize);

        if (touchdownP > 0f)
        {
            float ringR = 18f + 36f * touchdownP;
            byte a = (byte)(140 * (1f - touchdownP));
            renderer.DrawSolidRingScreen(landingScreenTarget.X, landingScreenTarget.Y, ringR * 0.6f, ringR,
                new Color4(200, 170, 120, a), 40);
        }

        float vignetteA = Math.Clamp(0.35f + terrainBlend * 0.3f, 0f, 0.65f);
        byte vignetteByte = (byte)(vignetteA * 255f);
        renderer.DrawRectScreen(0, 0, ScreenW, 18, new Color4(0, 0, 0, vignetteByte));
        renderer.DrawRectScreen(0, ScreenH - 18, ScreenW, 18, new Color4(0, 0, 0, vignetteByte));

        string status = touchdownP > 0.01f ? "TOUCHDOWN" : descentP > 0f ? "DESCENT" : "APPROACH";
        float labelW = renderer.MeasureText(status, 1.8f);
        renderer.DrawTextScreen(CX - labelW / 2f, 20f, status, new Color3(180, 205, 255), 1.8f);
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

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
                var variationColor = TileMapRenderer.GetColorVariation(color, x, y, 800f);
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

        return game.Textures.CreateTextureFromPixels(pixels, w, h, SDL.ScaleMode.Nearest);
    }

    private Vector2 DrawTerrainLandingBlend(Game game, float planetX, float planetY, float planetRadius,
        float terrainBlend, float descentP)
    {
        float mapW = _surfaceData.Width;
        float mapH = _surfaceData.Height;

        // Camera pan in texture-space: world center -> selected landing tile.
        float panP = EaseInOut01(Math.Clamp((descentP - 0.08f) / 0.92f, 0f, 1f));
        float centerX = Lerp(mapW * 0.5f, _landingTileX, panP);
        float centerY = Lerp(mapH * 0.5f, _landingTileY, panP);

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

        var srcRect = new SDL.FRect { X = srcX, Y = srcY, W = srcW, H = srcH };
        var dstRect = new SDL.FRect
        {
            X = dstCenterX - dstW * 0.5f,
            Y = dstCenterY - dstH * 0.5f,
            W = dstW,
            H = dstH
        };

        byte a = (byte)Math.Clamp((int)(terrainBlend * 255), 0, 255);
        SDL.SetTextureAlphaMod(_terrainTexture, a);
        SDL.RenderTexture(game.Renderer, _terrainTexture, in srcRect, in dstRect);
        SDL.SetTextureAlphaMod(_terrainTexture, 255);

        float targetU = (_landingTileX - srcX) / srcW;
        float targetV = (_landingTileY - srcY) / srcH;
        targetU = Math.Clamp(targetU, 0f, 1f);
        targetV = Math.Clamp(targetV, 0f, 1f);
        return new Vector2(dstRect.X + targetU * dstRect.W, dstRect.Y + targetV * dstRect.H);
    }

    private static void DrawAtmosphere(SpriteRenderer renderer, float cx, float cy, float radius, float alpha)
    {
        if (alpha <= 0f) return;

        byte a1 = (byte)(120f * alpha);
        byte a2 = (byte)(70f * alpha);
        byte a3 = (byte)(40f * alpha);

        renderer.DrawSolidRingScreen(cx, cy, radius * 0.98f, radius * 1.05f, new Color4(180, 220, 255, a1), 56);
        renderer.DrawSolidRingScreen(cx, cy, radius * 1.05f, radius * 1.11f, new Color4(140, 190, 255, a2), 56);
        renderer.DrawSolidRingScreen(cx, cy, radius * 1.11f, radius * 1.17f, new Color4(110, 150, 230, a3), 56);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    private static float EaseInOut01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
