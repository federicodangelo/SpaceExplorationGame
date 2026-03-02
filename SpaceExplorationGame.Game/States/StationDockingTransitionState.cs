using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.States;

/// <summary>
/// Bidirectional docking/undocking cinematic for space stations.
/// Docking  (solar-system → interior): ship approaches the station, then the interior is
///   revealed through an expanding portal as the ship enters the docking bay.
/// Undocking (interior → solar-system): the portal collapses, the exterior reappears, and
///   the ship launches away — the full animation in reverse.
/// </summary>
public class StationDockingTransitionState : GameState
{
    private enum TransitionMode { Docking, Undocking }

    private readonly TransitionMode _mode;

    public override GameStateType Type => _mode == TransitionMode.Docking
        ? GameStateType.SolarSystem
        : GameStateType.Interior;

    // ── Data ──────────────────────────────────────────────────────────
    private readonly StarSystemData _starSystem;
    private readonly SpaceStationData _spaceStation;
    private InteriorData _interiorData = null!;

    // Docking-mode: solar snapshot for ship/station screen-position math
    private readonly Vector2 _shipWorldStart;
    private readonly Vector2 _stationWorldPos;
    private readonly Vector2 _solarCameraStart;
    private readonly float _solarZoomStart;

    // ── Cameras ───────────────────────────────────────────────────────
    /// <summary>Used to place the station exterior at screen-centre during the cinematic.</summary>
    private readonly Camera _stationCamera = new(GameConfig.WindowWidth, GameConfig.WindowHeight, 0.01f, 100f);
    /// <summary>Interior camera — zooms from wide-out to default over the Entry phase.</summary>
    private readonly Camera _interiorCamera = new(GameConfig.WindowWidth, GameConfig.WindowHeight,
        GameConfig.InteriorZoomMin, GameConfig.InteriorZoomMax);

    // ── Phase timing ─────────────────────────────────────────────────
    private const float ApproachDuration = 0.8f;   // stars + station + ship flies in
    private const float EntryDuration = 1.6f;   // portal opens, interior revealed
    private const float TouchdownDuration = 0.8f;   // full interior, ring pulse
    private static float TotalDuration => ApproachDuration + EntryDuration + TouchdownDuration;

    // ── State ─────────────────────────────────────────────────────────
    private float _elapsed;
    private bool _sfxPlayed;

    // ── Screen-space start for ship ───────────────────────────────────
    private Vector2 _shipScreenStart;   // solar-system approach start (docking) or off-screen exit (undocking)

    // ── Background stars ─────────────────────────────────────────────
    private readonly List<StarParticle> _stars = [];
    private readonly Random _rng = new();

    private static readonly float ScreenW = GameConfig.WindowWidth;
    private static readonly float ScreenH = GameConfig.WindowHeight;
    private static readonly float CX = ScreenW * 0.5f;
    private static readonly float CY = ScreenH * 0.5f;

    private readonly record struct StarParticle(float X, float Y, byte Brightness);

    // ══════════════════════════════════════════════════════════════════
    // Constructors
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Docking: player is arriving from the solar system.</summary>
    public StationDockingTransitionState(
        StarSystemData starSystem,
        SpaceStationData spaceStation,
        Vector2 shipWorldStart,
        Vector2 stationWorldPos,
        Vector2 solarCameraStart,
        float solarZoomStart)
    {
        _mode = TransitionMode.Docking;
        _starSystem = starSystem;
        _spaceStation = spaceStation;
        _shipWorldStart = shipWorldStart;
        _stationWorldPos = stationWorldPos;
        _solarCameraStart = solarCameraStart;
        _solarZoomStart = MathF.Max(0.01f, solarZoomStart);
    }

    /// <summary>Undocking: player is launching from the interior.</summary>
    public StationDockingTransitionState(
        StarSystemData starSystem,
        SpaceStationData spaceStation,
        InteriorData interiorData,
        Vector2 stationWorldPos)
    {
        _mode = TransitionMode.Undocking;
        _starSystem = starSystem;
        _spaceStation = spaceStation;
        _interiorData = interiorData;
        _stationWorldPos = stationWorldPos;

        // Ship ends up hovering at screen centre (over the station) after launching
        _shipScreenStart = new Vector2(CX, CY);

        _shipWorldStart = Vector2.Zero;
        _solarCameraStart = Vector2.Zero;
        _solarZoomStart = GameConfig.SolarSystemZoomDefault;
    }

    // ══════════════════════════════════════════════════════════════════
    // GameState lifecycle
    // ══════════════════════════════════════════════════════════════════

    public override void Enter(Game game)
    {
        _elapsed = 0f;
        _sfxPlayed = false;

        if (_mode == TransitionMode.Docking)
        {
            // Generate the interior so we can render it during the cinematic
            _interiorData = game.UniverseGenerator.GenerateStationInterior(_starSystem, _spaceStation);

            // Ship starts at its solar-system screen position
            _shipScreenStart = WorldToScreenFromSolarSnapshot(_shipWorldStart);

            game.Audio.SetMusicTheme(AudioThemes.Interior);
        }
        else
        {
            game.Audio.PlaySfx(AudioSfx.Takeoff);
            game.Audio.SetMusicTheme(AudioThemes.SolarSystem);
        }

        // Station camera: keep station centred on screen
        _stationCamera.Position = _stationWorldPos;
        _stationCamera.Zoom = 1f;
        _stationCamera.ClampZoom();

        // Interior camera: centred on the landing pad, starts zoomed out
        PositionInteriorCamera(_mode == TransitionMode.Docking
            ? GameConfig.InteriorZoomDefault * 0.3f
            : GameConfig.InteriorZoomDefault);

        // Background stars
        _stars.Clear();
        for (int i = 0; i < 60; i++)
        {
            _stars.Add(new StarParticle(
                X: (float)_rng.NextDouble() * ScreenW,
                Y: (float)_rng.NextDouble() * ScreenH,
                Brightness: (byte)_rng.Next(40, 145)));
        }
    }

    public override void Exit(Game game) { }

    public override void UpdateInput(Game game) { }   // no input during cinematic

    public override void Update(Game game)
    {
        float dt = game.DeltaTime;
        _elapsed += dt;

        // Play landing SFX mid-approach on docking
        if (_mode == TransitionMode.Docking && !_sfxPlayed &&
            _elapsed >= ApproachDuration * 0.65f)
        {
            game.Audio.PlaySfx(AudioSfx.Landing);
            _sfxPlayed = true;
        }

        if (_elapsed >= TotalDuration)
        {
            if (_mode == TransitionMode.Docking)
            {
                // Hand off to the interactive interior state.
                // InteriorState will regenerate the same deterministic interior via the coordinator.
                game.ChangeState(new InteriorState(
                    InteriorOrigin.SpaceStation, _starSystem,
                    spaceStation: _spaceStation, startInShip: false));
            }
            else
            {
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromSpaceStation;
                game.Player.ReturnSpaceStationIndex = _spaceStation.Index;
                game.ChangeState(new SolarSystemState(_starSystem));
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Rendering
    // ══════════════════════════════════════════════════════════════════

    public override void RenderGame(Game game)
    {
        var renderer = game.SpriteRenderer;

        // animElapsed drives both modes: forward for Docking, backward for Undocking
        float animElapsed = _mode == TransitionMode.Docking ? _elapsed : (TotalDuration - _elapsed);

        float approachP = Math.Clamp(animElapsed / ApproachDuration, 0f, 1f);
        float entryP = Math.Clamp((animElapsed - ApproachDuration) / EntryDuration, 0f, 1f);
        float touchdownP = Math.Clamp((animElapsed - ApproachDuration - EntryDuration) / TouchdownDuration, 0f, 1f);

        // ── 1. Deep-space background ──────────────────────────────────
        renderer.DrawRectScreen(0, 0, ScreenW, ScreenH, new Color4(3, 4, 10, 255));
        foreach (var s in _stars)
            renderer.DrawRectScreen(s.X, s.Y, 1.4f, 1.4f, new Color3(s.Brightness, s.Brightness, s.Brightness));

        // ── 2. Interior (appears during Entry phase, full during Touchdown) ──
        if (entryP > 0f || touchdownP > 0f)
        {
            // Zoom the interior camera from zoomed-out to default as we enter
            float zoomEased = EaseInOut01(entryP + touchdownP);
            PositionInteriorCamera(Lerp(GameConfig.InteriorZoomDefault * 0.3f, GameConfig.InteriorZoomDefault, zoomEased));

            if (touchdownP > 0f)
            {
                // Full screen — no clip rect needed
                InteriorRenderer.RenderWorld(renderer, _interiorCamera, _interiorData, game.GlobalTime, null);
                RenderLandedShip(renderer);
            }
            else
            {
                // Expanding circular portal: radius grows from 0 to cover the whole screen
                float maxR = MathF.Sqrt(ScreenW * ScreenW + ScreenH * ScreenH) * 0.5f + 20f;
                float r = EaseInOut01(entryP) * maxR;
                float clipX = MathF.Max(0f, CX - r);
                float clipY = MathF.Max(0f, CY - r);
                float clipW = MathF.Min(ScreenW, r * 2f);
                float clipH = MathF.Min(ScreenH, r * 2f);

                renderer.SetClipRect(clipX, clipY, clipW, clipH);
                InteriorRenderer.RenderWorld(renderer, _interiorCamera, _interiorData, game.GlobalTime, null);
                RenderLandedShip(renderer);
                renderer.ClearClipRect();
            }
        }

        // ── 3. Space Station exterior (visible during Approach; fades in then out) ──
        if (touchdownP <= 0f)
        {
            float stationAlpha = entryP > 0f ? (1f - EaseInOut01(entryP)) : 1f;
            if (stationAlpha > 0.01f)
            {
                // Station grows as ship approaches — zoom ramps from 1× to 6× over Approach+Entry
                float zoomP = EaseInOut01(Math.Clamp(animElapsed / (ApproachDuration + EntryDuration), 0f, 1f));
                _stationCamera.Zoom = Lerp(1f, 6f, zoomP);
                _stationCamera.ClampZoom();

                game.SpaceStationRenderer.RenderSpaceStation(renderer, _stationCamera, _stationWorldPos, game.GlobalTime, stationAlpha);
            }
        }

        // ── 4. Flying ship (all phases) ───────────────────────────────
        // Pad screen position (where the ship is heading / just left from)
        Vector2 padScreen = PadScreenPosition();

        // Ship travels from solar start → screen centre → landing pad
        float totalApproachSpan = ApproachDuration + EntryDuration * 0.5f;
        float shipP = EaseInOut01(Math.Clamp(animElapsed / totalApproachSpan, 0f, 1f));
        float shipX = Lerp(_shipScreenStart.X, padScreen.X, shipP);
        float shipY = Lerp(_shipScreenStart.Y, padScreen.Y, shipP);

        // Touchdown settle — small downward nudge when landing
        float settleY = EaseInOut01(touchdownP) * 10f;
        if (_mode == TransitionMode.Docking) shipY += settleY;

        // Ship rotation: faces down (90°) throughout, rotates to face up (0°) while launching
        float rotation = 90f;
        if (_mode == TransitionMode.Undocking)
        {
            float modeP = Math.Clamp(_elapsed / TotalDuration, 0f, 1f);
            rotation = Lerp(90f, 0f, EaseInOut01(Math.Clamp((modeP - 0.65f) / 0.35f, 0f, 1f)));
        }

        game.SpaceshipRenderer.RenderFlyingScreen(renderer,
            shipX, shipY, rotation,
            game.Player.CurrentShipType.Id, game.Player.CurrentShipType.SpriteSize);

        // ── 5. Touchdown ring pulse ───────────────────────────────────
        if (touchdownP > 0f && _interiorData.LandingPadTilePos.HasValue)
        {
            float ringR = 18f + 36f * touchdownP;
            byte ringA = (byte)(140 * (1f - touchdownP));
            renderer.DrawSolidRingScreen(padScreen.X, padScreen.Y,
                ringR * 0.6f, ringR, new Color4(200, 170, 120, ringA), 40);
        }
    }

    public override void RenderHud(Game game) { }

    // ══════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════

    private void PositionInteriorCamera(float zoom)
    {
        if (_interiorData?.LandingPadTilePos.HasValue == true)
        {
            float padWorldX = (_interiorData.LandingPadTilePos.Value.X + 0.5f) * GameConfig.TileSize;
            float padWorldY = (_interiorData.LandingPadTilePos.Value.Y + 0.5f) * GameConfig.TileSize;
            _interiorCamera.Position = new Vector2(padWorldX, padWorldY);
        }
        _interiorCamera.Zoom = zoom;
        _interiorCamera.ClampZoom();
    }

    private void RenderLandedShip(ISpriteRenderer renderer)
    {
        if (!_interiorData.LandingPadTilePos.HasValue) return;
        float padWorldX = (_interiorData.LandingPadTilePos.Value.X + 0.5f) * GameConfig.TileSize;
        float padWorldY = (_interiorData.LandingPadTilePos.Value.Y + 0.5f) * GameConfig.TileSize;
        // Note: during the Entry phase the ship is still flying (outside), so we skip the landed
        // sprite — it will only be shown in the full interior (handled by the caller already inside
        // the clip-rect block).  We keep it here for the touchdown full-screen phase.
        // The flying ship overlay will cover it near the pad anyway.
    }

    /// <summary>Screen position of the landing pad using the current interior camera.</summary>
    private Vector2 PadScreenPosition()
    {
        if (_interiorData?.LandingPadTilePos.HasValue == true)
        {
            float padWorldX = (_interiorData.LandingPadTilePos.Value.X + 0.5f) * GameConfig.TileSize;
            float padWorldY = (_interiorData.LandingPadTilePos.Value.Y + 0.5f) * GameConfig.TileSize;
            return _interiorCamera.WorldToScreen(new Vector2(padWorldX, padWorldY));
        }
        return new Vector2(CX, CY);
    }

    private Vector2 WorldToScreenFromSolarSnapshot(Vector2 world)
    {
        Vector2 delta = (world - _solarCameraStart) * _solarZoomStart;
        return new Vector2(CX + delta.X, CY + delta.Y);
    }

    private static float Lerp(float a, float b, float t) => float.Lerp(a, b, Math.Clamp(t, 0f, 1f));

    private static float EaseInOut01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
