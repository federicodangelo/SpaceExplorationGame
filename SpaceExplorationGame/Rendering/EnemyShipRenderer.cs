using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders NPC ships (pirates, traders, patrols) with faction-colored procedural textures.
/// Owns textures per faction. IDisposable.
/// </summary>
public class EnemyShipRenderer : IDisposable
{
    private readonly nint _pirateTexture;
    private readonly nint _traderTexture;
    private readonly nint _patrolTexture;
    private readonly nint _flameTexture;
    private readonly TextureManager _textures;

    public EnemyShipRenderer(TextureManager textures)
    {
        _textures = textures;
        _pirateTexture = GeneratePirateTexture(textures);
        _traderTexture = GenerateTraderTexture(textures);
        _patrolTexture = GeneratePatrolTexture(textures);
        _flameTexture = GenerateFlameTexture(textures);
    }

    /// <summary>Render an NPC ship at a world position with rotation.</summary>
    public void Render(SpriteRenderer renderer, Camera camera, Vector2 position, float rotation,
        Faction faction, int size)
    {
        var texture = faction switch
        {
            Faction.Pirate => _pirateTexture,
            Faction.Trader => _traderTexture,
            Faction.Patrol => _patrolTexture,
            _ => _pirateTexture
        };

        renderer.DrawTexture(camera, texture, position, size, size, rotation);
    }

    /// <summary>Render a health bar above an NPC ship.</summary>
    public void RenderHealthBar(SpriteRenderer renderer, Camera camera, Vector2 position,
        float hullPercent, float shieldPercent, float maxShield, int shipSize)
    {
        float barWidth = shipSize * 1.2f;
        float barHeight = 3f;
        var barPos = position - new Vector2(barWidth / 2f, shipSize / 2f + 8f);

        // Hull bar (red/green)
        var screenPos = camera.WorldToScreen(barPos);
        float zoom = camera.Zoom;
        float w = barWidth * zoom;
        float h = barHeight * zoom;

        // Background
        renderer.DrawRectScreen(screenPos.X, screenPos.Y, w, h, new Color4(40, 40, 40, 180));
        // Hull fill
        byte hullR = hullPercent > 0.5f ? (byte)(255 * (1 - hullPercent) * 2) : (byte)255;
        byte hullG = hullPercent > 0.5f ? (byte)255 : (byte)(255 * hullPercent * 2);
        renderer.DrawRectScreen(screenPos.X, screenPos.Y, w * hullPercent, h, new Color4(hullR, hullG, 0, 200));

        // Shield bar (if has shields)
        if (maxShield > 0)
        {
            float shieldY = screenPos.Y - h - 1;
            renderer.DrawRectScreen(screenPos.X, shieldY, w, h, new Color4(40, 40, 60, 180));
            renderer.DrawRectScreen(screenPos.X, shieldY, w * shieldPercent, h, new Color4(80, 160, 255, 200));
        }
    }

    // ── Texture Generation ──────────────────────────────────────

    /// <summary>Pirate ship: aggressive angular shape, red/dark colors.</summary>
    private static nint GeneratePirateTexture(TextureManager textures)
    {
        int size = 28;
        var pixels = new byte[size * size * 4];

        // Dark angular hull
        int cx = size / 2, cy = size / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;

                // Arrow/wedge shape pointing right
                float nx = dx / (size * 0.45f);
                float ny = dy / (size * 0.35f);

                // Tapered hull: wider at back, narrow at front
                float widthAtX = 1.0f - nx * 0.6f;
                if (nx < -0.8f) widthAtX *= 0.6f; // engine section narrows

                if (MathF.Abs(ny) < widthAtX && nx > -0.9f && nx < 0.95f)
                {
                    float shade = 0.6f + 0.4f * (1f - MathF.Abs(ny) / widthAtX);

                    // Wings — darker
                    if (MathF.Abs(ny) > widthAtX * 0.6f)
                    {
                        TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1,
                            new Color4((byte)(120 * shade), (byte)(30 * shade), (byte)(30 * shade), 255));
                    }
                    // Cockpit
                    else if (nx > 0.5f && MathF.Abs(ny) < 0.2f)
                    {
                        TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1,
                            new Color4(255, (byte)(80 * shade), (byte)(40 * shade), 255));
                    }
                    // Hull
                    else
                    {
                        TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1,
                            new Color4((byte)(160 * shade), (byte)(50 * shade), (byte)(50 * shade), 255));
                    }
                }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    /// <summary>Trader ship: bulky rounded shape, gold/brown colors.</summary>
    private static nint GenerateTraderTexture(TextureManager textures)
    {
        int size = 32;
        var pixels = new byte[size * size * 4];

        int cx = size / 2, cy = size / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float nx = dx / (size * 0.4f);
                float ny = dy / (size * 0.35f);

                // Rounded rectangular hull
                float shape = MathF.Abs(nx) * 0.7f + MathF.Abs(ny);
                if (shape < 1.0f && nx > -0.9f && nx < 0.7f)
                {
                    float shade = 0.6f + 0.4f * (1f - shape);

                    // Cargo section (wide middle)
                    if (nx > -0.5f && nx < 0.3f && MathF.Abs(ny) < 0.7f)
                    {
                        TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1,
                            new Color4((byte)(200 * shade), (byte)(160 * shade), (byte)(80 * shade), 255));
                    }
                    // Cockpit
                    else if (nx > 0.4f && MathF.Abs(ny) < 0.3f)
                    {
                        TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1,
                            new Color4((byte)(100 * shade), (byte)(200 * shade), (byte)(220 * shade), 255));
                    }
                    // Hull frame
                    else
                    {
                        TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1,
                            new Color4((byte)(160 * shade), (byte)(130 * shade), (byte)(70 * shade), 255));
                    }
                }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    /// <summary>Patrol ship: sleek military shape, blue/white colors.</summary>
    private static nint GeneratePatrolTexture(TextureManager textures)
    {
        int size = 30;
        var pixels = new byte[size * size * 4];

        int cx = size / 2, cy = size / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float nx = dx / (size * 0.45f);
                float ny = dy / (size * 0.4f);

                // Sleek pointed shape
                float widthAtX = 1.0f - MathF.Max(0, nx) * 0.8f;
                if (nx < -0.6f) widthAtX *= 0.8f;

                if (MathF.Abs(ny) < widthAtX && nx > -0.85f && nx < 0.95f)
                {
                    float shade = 0.6f + 0.4f * (1f - MathF.Abs(ny) / widthAtX);

                    // Wings
                    if (MathF.Abs(ny) > widthAtX * 0.5f)
                    {
                        TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1,
                            new Color4((byte)(60 * shade), (byte)(100 * shade), (byte)(200 * shade), 255));
                    }
                    // Cockpit
                    else if (nx > 0.5f && MathF.Abs(ny) < 0.2f)
                    {
                        TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1,
                            new Color4((byte)(200 * shade), (byte)(220 * shade), (byte)(255 * shade), 255));
                    }
                    // Hull
                    else
                    {
                        TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1,
                            new Color4((byte)(80 * shade), (byte)(140 * shade), (byte)(220 * shade), 255));
                    }
                }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    /// <summary>Small engine flame for NPC ships.</summary>
    private static nint GenerateFlameTexture(TextureManager textures)
    {
        int size = 16;
        var pixels = new byte[size * size * 4];

        int cx = size / 2, cy = size / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float dist = MathF.Sqrt(dx * dx + dy * dy) / (size * 0.4f);
                if (dist < 1f)
                {
                    float intensity = 1f - dist;
                    byte r = (byte)(255 * intensity);
                    byte g = (byte)(180 * intensity);
                    byte b = (byte)(40 * intensity * intensity);
                    byte a = (byte)(200 * intensity);
                    TextureManager.SetPixelBlock(pixels, size, x, y, 1, 1, new Color4(r, g, b, a));
                }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    public void Dispose()
    {
        _textures.DestroyTexture(_pirateTexture);
        _textures.DestroyTexture(_traderTexture);
        _textures.DestroyTexture(_patrolTexture);
        _textures.DestroyTexture(_flameTexture);
        GC.SuppressFinalize(this);
    }
}
