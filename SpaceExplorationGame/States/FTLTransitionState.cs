using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.States;

/// <summary>
/// Intermediate game state that plays a 2D side-view FTL hyperspace jump animation
/// before transitioning to the target solar system. The player ship is shown center-screen
/// facing right, with horizontal star streaks flying past and energy waves sweeping across.
/// </summary>
public class FTLTransitionState : GameState
{
    public override GameStateType Type => GameStateType.SolarSystem;

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

    public FTLTransitionState(StarSystemData targetSystem)
    {
        _targetSystem = targetSystem;
    }

    public override void Enter(Game game)
    {
        _elapsed = 0f;

        // Scatter star streaks across the screen
        _stars.Clear();
        for (int i = 0; i < StarCount; i++)
            _stars.Add(CreateStar(fullWidth: true));

        // Space energy waves evenly
        _wavePositions.Clear();
        for (int i = 0; i < WaveCount; i++)
            _wavePositions.Add(ScreenW + i * (ScreenW / WaveCount));
    }

    public override void Exit(Game game) { }

    public override void UpdateInput(Game game)
    {
        // No input during FTL animation
    }

    public override void Update(Game game, float dt)
    {
        _elapsed += dt;

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

    public override void HandleEvent(Game game, SDL.Event e) { }

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

    private void RenderStars(SpriteRenderer renderer)
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

    private void RenderEnergyWaves(SpriteRenderer renderer)
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

    private void RenderShip(Game game, SpriteRenderer renderer)
    {
        var shipType = game.Player.CurrentShipType;
        var shipTexture = game.SpaceshipRenderer.GetSolarTexture(shipType.Id);
        if (shipTexture == nint.Zero) return;

        float size = shipType.SpriteSize * 2f; // render larger for the cutscene
        float sx = CX + _shipShakeX;
        float sy = CY + _shipShakeY;

        // Ship faces right (rotation = 0°)
        renderer.DrawTextureScreen(shipTexture, sx, sy, size, size, 0f);
    }

    private void RenderEngineGlow(Game game, SpriteRenderer renderer)
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

    private void RenderHudText(SpriteRenderer renderer)
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
