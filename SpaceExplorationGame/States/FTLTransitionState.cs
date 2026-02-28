using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.States;

/// <summary>
/// Intermediate game state that plays a 2D side-view FTL hyperspace jump animation
/// before transitioning to the target solar system. The player ship is shown center-screen
/// facing right, with horizontal star streaks flying past and energy waves sweeping across.
/// </summary>
public class FTLTransitionState : GameState
{
    public override GameStateType Type => GameStateType.SolarSystem;

    private readonly StarSystemData _sourceSystem;
    private readonly StarSystemData _targetSystem;

    // ── Animation timing ──
    private float _elapsed;
    private const float ChargeDuration = 1.6f;      // Phase 1: charge-up, ship shakes, stars slow
    private const float JumpFlashDuration = 0.15f;   // Phase 2: bright flash as FTL engages
    private const float TravelDuration = 2.5f;       // Phase 3: full-speed star streaks
    private const float ExitDuration = 1.6f;        // Phase 4: decelerate + arrival flash
    private static float TotalDuration => ChargeDuration + JumpFlashDuration + TravelDuration + ExitDuration;

    // ── Horizontal star streaks ──
    private readonly List<StarStreak> _stars = [];
    private const int StarCount = 200;

    // ── Energy waves (vertical lines sweeping left during travel) ──
    private readonly List<float> _wavePositions = [];
    private const int WaveCount = 5;

    // ── Screen dimensions ──
    private static readonly float ScreenW = GameConfig.WindowWidth;
    private static readonly float ScreenH = GameConfig.WindowHeight;
    private static readonly float CX = ScreenW / 2f;
    private static readonly float CY = ScreenH / 2f;

    // ── Ship rendering ──
    private float _shipShakeX;
    private float _shipShakeY;

    // ── RNG ──
    private readonly Random _rng = new();
    private bool _jumpSfxPlayed;

    public FTLTransitionState(StarSystemData sourceSystem, StarSystemData targetSystem)
    {
        _sourceSystem = sourceSystem;
        _targetSystem = targetSystem;
    }

    public override void Enter(Game game)
    {
        _elapsed = 0f;

        // Audio: FTL theme + charge-up SFX
        game.Audio.SetMusicTheme(MusicTheme.FTL, instant: true);
        game.Audio.PlaySfx(SfxType.FtlCharge);

        // Scatter star streaks across the screen
        _stars.Clear();
        for (int i = 0; i < StarCount; i++)
            _stars.Add(CreateStar(fullWidth: true));

        // Space energy waves evenly
        _wavePositions.Clear();
        for (int i = 0; i < WaveCount; i++)
            _wavePositions.Add(ScreenW + i * (ScreenW / WaveCount));

    }

    public override void Exit(Game game)
    {
    }

    public override void UpdateInput(Game game)
    {
        // No input during FTL animation
    }

    public override void Update(Game game)
    {
        float dt = game.DeltaTime;
        _elapsed += dt;

        // Play jump SFX at the flash moment
        if (!_jumpSfxPlayed && _elapsed >= ChargeDuration)
        {
            game.Audio.PlaySfx(SfxType.FtlJump);
            _jumpSfxPlayed = true;
        }

        if (_elapsed >= TotalDuration)
        {
            game.ChangeState(new SolarSystemState(_targetSystem));
            return;
        }

        float speed = GetStarSpeed();

        // Update star streaks — move left across screen
        for (int i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            s.X -= s.Speed * speed * dt;

            // Wrap around when off-screen left
            if (s.X + s.TrailLength < -20)
            {
                s = CreateStar(fullWidth: false);
                s.X = ScreenW + _rng.Next(20, 100);
            }

            // Trail length grows with speed
            s.TrailLength = MathF.Min(s.BaseTrailLength * speed / 2f, ScreenW * 0.6f);
            _stars[i] = s;
        }

        // Ship shake during charge-up
        if (_elapsed < ChargeDuration)
        {
            float intensity = _elapsed / ChargeDuration;
            float shake = intensity * 3f;
            _shipShakeX = (float)(_rng.NextDouble() * 2 - 1) * shake;
            _shipShakeY = (float)(_rng.NextDouble() * 2 - 1) * shake;
        }
        else
        {
            _shipShakeX = 0;
            _shipShakeY = 0;
        }

        // Update energy waves during travel phase
        float travelStart = ChargeDuration + JumpFlashDuration;
        float travelEnd = travelStart + TravelDuration;
        if (_elapsed >= travelStart && _elapsed < travelEnd)
        {
            for (int i = 0; i < _wavePositions.Count; i++)
            {
                _wavePositions[i] -= 1800f * dt;
                if (_wavePositions[i] < -10)
                    _wavePositions[i] += ScreenW + ScreenW / WaveCount;
            }
        }
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;

        // ── Background ──
        byte bgB = (byte)(_elapsed >= ChargeDuration ? 12 : (byte)(_elapsed / ChargeDuration * 12));
        renderer.DrawRectScreen(0, 0, ScreenW, ScreenH, new Color4(0, 0, bgB, 255));

        // ── Source & target stars ──
        RenderSystemStars(game, renderer);

        // ── Star streaks ──
        RenderStars(renderer);

        // ── Energy waves ──
        float travelStart = ChargeDuration + JumpFlashDuration;
        float travelEnd = travelStart + TravelDuration;
        if (_elapsed >= travelStart && _elapsed < travelEnd)
            RenderEnergyWaves(renderer);

        // ── Player ship (center screen, facing right = 0°) ──
        RenderShip(game, renderer);

        // ── Engine glow behind ship ──
        RenderEngineGlow(game, renderer);

        // ── Jump flash ──
        float flashStart = ChargeDuration;
        float flashEnd = flashStart + JumpFlashDuration;
        if (_elapsed >= flashStart && _elapsed < flashEnd)
        {
            float p = (_elapsed - flashStart) / JumpFlashDuration;
            // Quick bright flash that peaks at ~30% then fades
            float intensity = p < 0.3f ? p / 0.3f : 1f - (p - 0.3f) / 0.7f;
            byte a = (byte)(intensity * 220);
            renderer.DrawRectScreen(0, 0, ScreenW, ScreenH, new Color4(180, 210, 255, a));
        }

        // ── Exit flash ──
        float exitStart = ChargeDuration + JumpFlashDuration + TravelDuration;
        if (_elapsed >= exitStart)
        {
            float p = (_elapsed - exitStart) / ExitDuration;
            float intensity = p < 0.25f ? p / 0.25f : 1f - (p - 0.25f) / 0.75f;
            byte a = (byte)(intensity * 200);
            renderer.DrawRectScreen(0, 0, ScreenW, ScreenH, new Color4(200, 220, 255, a));
        }

        // ── HUD text ──
        RenderHudText(renderer);
    }

    // ─────────────────────────────────────────────────────────────
    //  SYSTEM STARS (source & target)
    // ─────────────────────────────────────────────────────────────

    private void RenderSystemStars(Game game, ISpriteRenderer renderer)
    {
        float chargeEnd = ChargeDuration;
        float flashEnd = chargeEnd + JumpFlashDuration;
        float travelEnd = flashEnd + TravelDuration;

        float sourceDisplaySize = MathF.Max(_sourceSystem.StarRadius * 6f, 32f);
        float targetDisplaySize = MathF.Max(_targetSystem.StarRadius * 6f, 32f);

        // ── Source star (starts at left-of-center, scrolls off-screen left) ──
        if (_elapsed < travelEnd)
        {
            float sourceX;
            byte sourceAlpha;

            if (_elapsed < chargeEnd)
            {
                // During charge: star is visible on the far left, slowly drifting left
                float p = _elapsed / chargeEnd;
                sourceX = ScreenW * 0.12f - p * 60f;
                sourceAlpha = (byte)(200 - p * 40);
            }
            else
            {
                // During travel: rapidly scrolls off to the left
                float timeSinceFlash = _elapsed - flashEnd;
                float travelP = MathF.Max(timeSinceFlash / TravelDuration, 0f);
                sourceX = ScreenW * 0.12f - 60f - travelP * ScreenW * 1.5f;
                sourceAlpha = (byte)(160 * MathF.Max(1f - travelP * 3f, 0f));
            }

            if (sourceAlpha > 3 && sourceX > -sourceDisplaySize * 2)
            {
                game.StarRenderer.RenderScreen(renderer,
                    sourceX, CY, sourceDisplaySize, _sourceSystem.StarColor, sourceAlpha,
                    (float)game.GlobalTime);

                // Star name label below
                string srcName = _sourceSystem.Name.ToUpperInvariant();
                float nameW = renderer.MeasureText(srcName, 1f);
                renderer.DrawTextScreen(sourceX - nameW / 2f, CY + sourceDisplaySize * 0.55f,
                    srcName, new Color4(_sourceSystem.StarColor.R, _sourceSystem.StarColor.G,
                    _sourceSystem.StarColor.B, (byte)(sourceAlpha * 0.7f)), 1f);
            }
        }

        // ── Target star (scrolls in from the right during exit) ──
        if (_elapsed >= travelEnd - TravelDuration * 0.15f)
        {
            float targetX;
            byte targetAlpha;

            if (_elapsed < travelEnd)
            {
                // End of travel: star appears at far right edge
                float preArrival = (travelEnd - _elapsed) / (TravelDuration * 0.15f);
                targetX = ScreenW + targetDisplaySize - (1f - preArrival) * ScreenW * 0.3f;
                targetAlpha = (byte)(40 * (1f - preArrival));
            }
            else
            {
                // During exit: star slides in to rest position on the right side
                float p = (_elapsed - travelEnd) / ExitDuration;
                float eased = 1f - (1f - p) * (1f - p); // ease-out quad
                targetX = ScreenW * 0.88f + (1f - eased) * ScreenW * 0.3f;
                targetAlpha = (byte)(40 + eased * 200);
            }

            if (targetAlpha > 3)
            {
                game.StarRenderer.RenderScreen(renderer,
                    targetX, CY, targetDisplaySize, _targetSystem.StarColor, targetAlpha,
                    (float)game.GlobalTime);

                // Star name label below
                string tgtName = _targetSystem.Name.ToUpperInvariant();
                float nameW = renderer.MeasureText(tgtName, 1f);
                byte labelAlpha = (byte)MathF.Min(targetAlpha * 0.7f, 255);
                renderer.DrawTextScreen(targetX - nameW / 2f, CY + targetDisplaySize * 0.55f,
                    tgtName, new Color4(_targetSystem.StarColor.R, _targetSystem.StarColor.G,
                    _targetSystem.StarColor.B, labelAlpha), 1f);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  STAR MANAGEMENT
    // ─────────────────────────────────────────────────────────────

    private StarStreak CreateStar(bool fullWidth)
    {
        float x = fullWidth
            ? (float)(_rng.NextDouble() * ScreenW)
            : ScreenW + (float)(_rng.NextDouble() * 100);
        float y = (float)(_rng.NextDouble() * ScreenH);

        int colorChoice = _rng.Next(5);
        var color = colorChoice switch
        {
            0 => new Color3(180, 200, 255),  // blue-white
            1 => new Color3(100, 160, 255),  // blue
            2 => new Color3(80, 200, 220),   // cyan
            3 => new Color3(140, 140, 160),  // dim gray
            _ => new Color3(220, 220, 240),  // near-white
        };

        return new StarStreak
        {
            X = x,
            Y = y,
            Speed = 200f + (float)_rng.NextDouble() * 300f,
            Color = color,
            Brightness = 0.4f + (float)_rng.NextDouble() * 0.6f,
            BaseTrailLength = 2f + (float)_rng.NextDouble() * 6f,
            TrailLength = 2f,
            Size = _rng.Next(1, 3),
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  RENDERING
    // ─────────────────────────────────────────────────────────────

    private void RenderStars(ISpriteRenderer renderer)
    {
        float speed = GetStarSpeed();

        float exitStart = ChargeDuration + JumpFlashDuration + TravelDuration;
        float exitEnd = exitStart + ExitDuration;

        foreach (var star in _stars)
        {
            float trailLen = star.TrailLength;
            byte alpha = (byte)(star.Brightness * 255);

            // Fade during exit
            if (_elapsed >= exitStart)
            {
                float p = (_elapsed - exitStart) / ExitDuration;
                alpha = (byte)(alpha * MathF.Max(1f - p, 0f));
            }
            if (alpha < 3) continue;

            var c = new Color4(star.Color.R, star.Color.G, star.Color.B, alpha);

            // During travel, draw elongated horizontal streaks
            if (speed > 2f)
            {
                // Main streak line
                renderer.DrawLineScreen(star.X, star.Y, star.X + trailLen, star.Y, c);
                // Second line for thickness on larger stars
                if (star.Size > 1)
                    renderer.DrawLineScreen(star.X, star.Y + 1, star.X + trailLen, star.Y + 1,
                        new Color4(star.Color.R, star.Color.G, star.Color.B, (byte)(alpha * 0.5f)));
            }
            else
            {
                // Normal dot
                renderer.DrawRectScreen(star.X, star.Y, star.Size, star.Size, c);
            }
        }
    }

    private void RenderEnergyWaves(ISpriteRenderer renderer)
    {
        float travelStart = ChargeDuration + JumpFlashDuration;
        float tp = (_elapsed - travelStart) / TravelDuration;
        byte baseAlpha = (byte)(30 + tp * 20);

        foreach (float wx in _wavePositions)
        {
            // Vertical energy line with gradient fade at top/bottom
            for (int y = 0; y < (int)ScreenH; y += 3)
            {
                float edgeFade = 1f - MathF.Abs(y - CY) / CY;
                edgeFade = MathF.Max(edgeFade, 0f);
                byte a = (byte)(baseAlpha * edgeFade);
                if (a < 2) continue;
                renderer.DrawRectScreen(wx, y, 2, 3, new Color4(60, 120, 255, a));
            }
            // Brighter core line
            float coreH = ScreenH * 0.3f;
            renderer.DrawLineScreen(wx + 1, CY - coreH / 2, wx + 1, CY + coreH / 2,
                new Color4(100, 160, 255, (byte)(baseAlpha * 0.6f)));
        }
    }

    private void RenderShip(Game game, ISpriteRenderer renderer)
    {
        var shipType = game.Player.CurrentShipType;
        float size = shipType.SpriteSize * 2f; // render larger for the cutscene
        float sx = CX + _shipShakeX;
        float sy = CY + _shipShakeY;

        game.SpaceshipRenderer.RenderFlyingScreen(renderer, sx, sy, 0f, shipType.Id, (int)size);
    }

    private void RenderEngineGlow(Game game, ISpriteRenderer renderer)
    {
        float shipSize = game.Player.CurrentShipType.SpriteSize * 2f;
        float sx = CX + _shipShakeX;
        float sy = CY + _shipShakeY;

        float speed = GetStarSpeed();
        if (speed < 0.5f) return;

        // Glow intensity scales with speed
        float glowIntensity = MathF.Min(speed / 8f, 1f);

        // Engine exhaust — horizontal lines extending left from the ship
        float exhaustLen = 15f + glowIntensity * 60f;
        float engineX = sx - shipSize * 0.28f;

        // Flicker
        float flicker = 0.8f + 0.2f * MathF.Sin(_elapsed * 30f);
        byte coreAlpha = (byte)(200 * glowIntensity * flicker);
        byte outerAlpha = (byte)(100 * glowIntensity * flicker);

        // Core exhaust (bright orange-yellow)
        renderer.DrawLineScreen(engineX, sy, engineX - exhaustLen, sy,
            new Color4(255, 200, 80, coreAlpha));
        renderer.DrawLineScreen(engineX, sy - 1, engineX - exhaustLen * 0.8f, sy - 1,
            new Color4(255, 160, 40, outerAlpha));
        renderer.DrawLineScreen(engineX, sy + 1, engineX - exhaustLen * 0.8f, sy + 1,
            new Color4(255, 160, 40, outerAlpha));

        // Wider outer glow
        renderer.DrawLineScreen(engineX, sy - 2, engineX - exhaustLen * 0.5f, sy - 2,
            new Color4(255, 120, 20, (byte)(outerAlpha * 0.4f)));
        renderer.DrawLineScreen(engineX, sy + 2, engineX - exhaustLen * 0.5f, sy + 2,
            new Color4(255, 120, 20, (byte)(outerAlpha * 0.4f)));

        // During full travel, add a long blue FTL trail
        float travelStart = ChargeDuration + JumpFlashDuration;
        if (_elapsed >= travelStart && _elapsed < travelStart + TravelDuration)
        {
            float trailLen = 80f + glowIntensity * 200f;
            byte trailAlpha = (byte)(60 * glowIntensity * flicker);
            renderer.DrawLineScreen(engineX, sy, engineX - trailLen, sy,
                new Color4(100, 160, 255, trailAlpha));
            renderer.DrawLineScreen(engineX, sy - 1, engineX - trailLen * 0.7f, sy - 1,
                new Color4(80, 140, 255, (byte)(trailAlpha * 0.5f)));
            renderer.DrawLineScreen(engineX, sy + 1, engineX - trailLen * 0.7f, sy + 1,
                new Color4(80, 140, 255, (byte)(trailAlpha * 0.5f)));
        }
    }

    private void RenderHudText(ISpriteRenderer renderer)
    {
        float chargeEnd = ChargeDuration;
        float flashEnd = chargeEnd + JumpFlashDuration;
        float travelEnd = flashEnd + TravelDuration;

        string text;
        Color4 textColor;

        if (_elapsed < chargeEnd)
        {
            text = "FTL DRIVE CHARGING...";
            float pulse = (MathF.Sin(_elapsed * 8f) + 1f) * 0.5f;
            byte a = (byte)(150 + pulse * 105);
            textColor = new Color4(100, 180, 255, a);
        }
        else if (_elapsed < travelEnd)
        {
            text = "IN HYPERSPACE";
            float pulse = (MathF.Sin(_elapsed * 12f) + 1f) * 0.5f;
            byte a = (byte)(180 + pulse * 75);
            textColor = new Color4(150, 200, 255, a);
        }
        else
        {
            text = $"ARRIVING AT {_targetSystem.Name.ToUpperInvariant()}";
            float p = (_elapsed - travelEnd) / ExitDuration;
            byte a = (byte)(255 * MathF.Max(1f - p, 0f));
            textColor = new Color4(200, 230, 255, a);
        }

        float textScale = 2f;
        float textW = renderer.MeasureText(text, textScale);
        renderer.DrawTextScreen(
            CX - textW / 2f,
            ScreenH - 80,
            text, textColor, textScale);

        // Destination at top
        if (_elapsed < travelEnd)
        {
            string destText = $"DESTINATION: {_targetSystem.Name.ToUpperInvariant()}";
            float destW = renderer.MeasureText(destText, 1.5f);
            byte destAlpha = (byte)(_elapsed < chargeEnd
                ? MathF.Min(_elapsed / chargeEnd * 255, 200)
                : 200);
            renderer.DrawTextScreen(
                CX - destW / 2f, 40,
                destText, new Color4(120, 160, 220, destAlpha), 1.5f);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  SPEED CURVE
    // ─────────────────────────────────────────────────────────────

    private float GetStarSpeed()
    {
        float chargeEnd = ChargeDuration;
        float flashEnd = chargeEnd + JumpFlashDuration;
        float travelEnd = flashEnd + TravelDuration;

        if (_elapsed < chargeEnd)
        {
            // Ramp from 0.5x to 3x
            float p = _elapsed / chargeEnd;
            return 0.5f + p * p * 2.5f;
        }
        else if (_elapsed < flashEnd)
        {
            // Instant jump to full speed
            return 8f;
        }
        else if (_elapsed < travelEnd)
        {
            // Full speed
            return 8f;
        }
        else
        {
            // Decelerate
            float p = (_elapsed - travelEnd) / ExitDuration;
            return 8f * MathF.Max(1f - p * p, 0f);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  TYPES
    // ─────────────────────────────────────────────────────────────

    private struct StarStreak
    {
        public float X;
        public float Y;
        public float Speed;
        public Color3 Color;
        public float Brightness;
        public float BaseTrailLength;
        public float TrailLength;
        public int Size;
    }
}
