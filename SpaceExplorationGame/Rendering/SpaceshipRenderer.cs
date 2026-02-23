using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders the player's spaceship in all contexts (solar system flight and planet surface).
/// Owns all ship textures (per-type solar/landed sprites and flame) so future customisation
/// (hull type, weapon mounts, visual upgrades) can be handled in one place.
/// </summary>
public class SpaceshipRenderer : IDisposable
{
    private readonly TextureManager _textures;
    private readonly Dictionary<string, nint> _solarTextures = [];
    private readonly Dictionary<string, nint> _landedTextures = [];

    public SpaceshipRenderer(TextureManager textures)
    {
        _textures = textures;
        // Solar (in-flight) textures per ship type
        _solarTextures["scout"] = GenerateScoutTexture(textures);
        _solarTextures["fighter"] = GenerateFighterTexture(textures);
        _solarTextures["freighter"] = GenerateFreighterTexture(textures);
        _solarTextures["explorer"] = GenerateExplorerTexture(textures);

        // Landed textures per ship type
        _landedTextures["scout"] = GenerateLandedShipTexture(textures, 24, 80, 200, 80, 10f, 6f);
        _landedTextures["fighter"] = GenerateLandedShipTexture(textures, 24, 200, 80, 80, 8f, 7f);
        _landedTextures["freighter"] = GenerateLandedShipTexture(textures, 32, 180, 160, 80, 14f, 10f);
        _landedTextures["explorer"] = GenerateLandedShipTexture(textures, 28, 80, 140, 220, 12f, 8f);
    }

    /// <summary>Gets the in-flight texture for a ship type.</summary>
    public nint GetSolarTexture(string shipTypeId) =>
        _solarTextures.TryGetValue(shipTypeId, out var tex) ? tex : nint.Zero;

    /// <summary>Gets the landed texture for a ship type.</summary>
    public nint GetLandedTexture(string shipTypeId) =>
        _landedTextures.TryGetValue(shipTypeId, out var tex) ? tex : nint.Zero;

    /// <summary>Renders the ship in flight with optional engine flame effect.</summary>
    public void RenderFlying(SpriteRenderer renderer, Camera camera,
        Vector2 position, float rotation, string shipTypeId, int spriteSize)
    {
        var shipTexture = GetSolarTexture(shipTypeId);

        // Ship sprite (rotated to match heading)
        renderer.DrawTexture(camera, shipTexture, position, spriteSize, spriteSize, rotation);
    }

    /// <summary>Renders the landed ship on a planet surface with a label.</summary>
    public void RenderLanded(SpriteRenderer renderer, Camera camera,
        Vector2 position, string shipTypeId, int spriteSize)
    {
        var shipTexture = GetLandedTexture(shipTypeId);
        int landedSize = (int)(spriteSize * 1.5f);
        renderer.DrawTexture(camera, shipTexture, position, landedSize, landedSize);
        renderer.DrawText(camera, position + new Vector2(-12, 14), "SHIP", new Color3(180, 180, 200));
    }

    // ──── Texture generation ────────────────────────────────────────

    private static nint GenerateScoutTexture(TextureManager textures)
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 16;
                int cy = y - 16;

                float halfWidth = 8f * (1f - (cx + 12f) / 26f);
                if (cx >= -12 && cx <= 14 && Math.Abs(cy) <= halfWidth)
                {
                    float t = (cx + 12f) / 26f;
                    byte sr = (byte)(80 + 100 * t);
                    byte sg = (byte)(200 + 55 * t);
                    byte sb = (byte)(80 + 100 * t);

                    if (cx > 8 && Math.Abs(cy) < 2)
                    { sr = 180; sg = 220; sb = 255; }
                    else if (Math.Abs(cy) > halfWidth - 1.5f)
                    { sr = (byte)(sr * 0.6f); sg = (byte)(sg * 0.6f); sb = (byte)(sb * 0.6f); }
                    else if (Math.Abs(cy) < 1.5f && cx < 6)
                    { sr = (byte)Math.Min(255, sr + 30); sg = (byte)Math.Min(255, sg + 30); sb = (byte)Math.Min(255, sb + 30); }

                    pixels[idx + 0] = sr; pixels[idx + 1] = sg; pixels[idx + 2] = sb; pixels[idx + 3] = 255;
                }
                else if (cx >= -14 && cx <= -10 && Math.Abs(cy) >= 4 && Math.Abs(cy) <= 7)
                { pixels[idx + 0] = 60; pixels[idx + 1] = 70; pixels[idx + 2] = 90; pixels[idx + 3] = 255; }
                else if (cx >= -14 && cx <= -12 && Math.Abs(cy) >= 2 && Math.Abs(cy) <= 4)
                { pixels[idx + 0] = 100; pixels[idx + 1] = 150; pixels[idx + 2] = 200; pixels[idx + 3] = 180; }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    private static nint GenerateFighterTexture(TextureManager textures)
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 16;
                int cy = y - 16;

                float fuselageHalf = 5f * (1f - (cx + 10f) / 24f);
                if (cx >= -10 && cx <= 14 && Math.Abs(cy) <= fuselageHalf && fuselageHalf > 0)
                {
                    float t = (cx + 10f) / 23f;
                    byte sr = (byte)(180 + 60 * t);
                    byte sg = (byte)(60 + 40 * t);
                    byte sb = (byte)(60 + 30 * t);

                    if (cx > 7 && Math.Abs(cy) < 2)
                    { sr = 200; sg = 220; sb = 255; }

                    pixels[idx + 0] = sr; pixels[idx + 1] = sg; pixels[idx + 2] = sb; pixels[idx + 3] = 255;
                }
                else if (cx >= -8 && cx <= 4)
                {
                    float wingFront = -8f + (Math.Abs(cy) - 4f) * 0.8f;
                    float wingBack = 4f - (Math.Abs(cy) - 4f) * 1.2f;
                    if (Math.Abs(cy) >= 4 && Math.Abs(cy) <= 12 && cx >= wingFront && cx <= wingBack)
                    {
                        byte sr = 160, sg = 50, sb = 50;
                        if (Math.Abs(cy) >= 11)
                        { sr = 200; sg = 60; sb = 60; }
                        if (Math.Abs(cy) >= 10 && cx >= -2 && cx <= 1)
                        { sr = 255; sg = 200; sb = 80; }

                        pixels[idx + 0] = sr; pixels[idx + 1] = sg; pixels[idx + 2] = sb; pixels[idx + 3] = 255;
                    }
                }

                if (cx >= -14 && cx <= -10 && Math.Abs(cy) >= 5 && Math.Abs(cy) <= 8)
                { pixels[idx + 0] = 80; pixels[idx + 1] = 40; pixels[idx + 2] = 40; pixels[idx + 3] = 255; }
                if (cx >= -14 && cx <= -12 && Math.Abs(cy) >= 3 && Math.Abs(cy) <= 5)
                { pixels[idx + 0] = 255; pixels[idx + 1] = 120; pixels[idx + 2] = 60; pixels[idx + 3] = 180; }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    private static nint GenerateFreighterTexture(TextureManager textures)
    {
        const int size = 48;
        var pixels = new byte[size * size * 4];
        int half = size / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - half;
                int cy = y - half;

                float hw = 18f;
                float hh = 10f;
                float rx = MathF.Abs(cx) / hw;
                float ry = MathF.Abs(cy) / hh;
                float rr = rx * rx + ry * ry;

                if (cx >= -18 && cx <= 16 && rr <= 1.1f)
                {
                    float t = (cx + 18f) / 34f;
                    byte sr = (byte)(140 + 40 * t);
                    byte sg = (byte)(130 + 30 * t);
                    byte sb = (byte)(60 + 20 * t);

                    if (cx > 10 && Math.Abs(cy) < 3)
                    { sr = 150; sg = 200; sb = 240; }
                    else if (cx >= -12 && cx <= 6 && (Math.Abs(cy) == 4 || Math.Abs(cy) == 7))
                    { sr = (byte)(sr * 0.7f); sg = (byte)(sg * 0.7f); sb = (byte)(sb * 0.7f); }
                    else if (MathF.Abs(cy) > hh - 2)
                    { sr = (byte)(sr * 0.75f); sg = (byte)(sg * 0.75f); sb = (byte)(sb * 0.75f); }

                    pixels[idx + 0] = sr; pixels[idx + 1] = sg; pixels[idx + 2] = sb; pixels[idx + 3] = 255;
                }

                if (cx >= -10 && cx <= 5 && Math.Abs(cy) >= 11 && Math.Abs(cy) <= 14)
                {
                    pixels[idx + 0] = 120; pixels[idx + 1] = 110; pixels[idx + 2] = 60;
                    pixels[idx + 3] = 255;
                }

                if (cx >= -22 && cx <= -18 && Math.Abs(cy) <= 12)
                { pixels[idx + 0] = 100; pixels[idx + 1] = 90; pixels[idx + 2] = 50; pixels[idx + 3] = 255; }
                if (cx >= -22 && cx <= -20 && Math.Abs(cy) <= 8)
                { pixels[idx + 0] = 255; pixels[idx + 1] = 180; pixels[idx + 2] = 60; pixels[idx + 3] = 180; }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    private static nint GenerateExplorerTexture(TextureManager textures)
    {
        const int size = 40;
        var pixels = new byte[size * size * 4];
        int half = size / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - half;
                int cy = y - half;

                float ex = cx / 16f;
                float ey = cy / 8f;
                if (ex * ex + ey * ey <= 1f && cx >= -14)
                {
                    float t = (cx + 14f) / 30f;
                    byte sr = (byte)(60 + 60 * t);
                    byte sg = (byte)(120 + 40 * t);
                    byte sb = (byte)(180 + 60 * t);

                    if (cx > 10 && Math.Abs(cy) < 2)
                    { sr = 180; sg = 230; sb = 255; }
                    else if (Math.Abs(cy) < 1.5f && cx >= -8 && cx <= 8)
                    { sr = (byte)Math.Min(255, sr + 40); sg = (byte)Math.Min(255, sg + 40); sb = (byte)Math.Min(255, sb + 40); }
                    else if (MathF.Abs(ey) > 0.8f)
                    { sr = (byte)(sr * 0.7f); sg = (byte)(sg * 0.7f); sb = (byte)(sb * 0.7f); }

                    pixels[idx + 0] = sr; pixels[idx + 1] = sg; pixels[idx + 2] = sb; pixels[idx + 3] = 255;
                }

                if (cx >= -6 && cx <= 6)
                {
                    float finCenter = cy > 0 ? 10f : -10f;
                    float finDist = MathF.Abs(cy - finCenter);
                    if (finDist <= 3f)
                    {
                        float finAlpha = 1f - finDist / 3f;
                        pixels[idx + 0] = (byte)(80 * finAlpha); pixels[idx + 1] = (byte)(160 * finAlpha);
                        pixels[idx + 2] = (byte)(220 * finAlpha); pixels[idx + 3] = (byte)(220 * finAlpha);
                    }
                }

                if (cx >= -18 && cx <= -14 && Math.Abs(cy) >= 5 && Math.Abs(cy) <= 8)
                { pixels[idx + 0] = 50; pixels[idx + 1] = 80; pixels[idx + 2] = 130; pixels[idx + 3] = 255; }
                if (cx >= -18 && cx <= -16 && Math.Abs(cy) >= 3 && Math.Abs(cy) <= 5)
                { pixels[idx + 0] = 80; pixels[idx + 1] = 180; pixels[idx + 2] = 255; pixels[idx + 3] = 180; }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    private static nint GenerateLandedShipTexture(TextureManager textures, int size, byte hullR, byte hullG, byte hullB, float hullHalfX, float hullHalfY)
    {
        var pixels = new byte[size * size * 4];
        int half = size / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - half;
                int cy = y - half;

                float ex = cx / hullHalfX;
                float ey = cy / hullHalfY;
                if (ex * ex + ey * ey <= 1f)
                {
                    float t = MathF.Sqrt(ex * ex + ey * ey);
                    byte sr = (byte)(hullR + 40 * (1 - t));
                    byte sg = (byte)(hullG + 40 * (1 - t));
                    byte sb = (byte)(hullB + 40 * (1 - t));

                    if (cx > hullHalfX * 0.4f && Math.Abs(cy) < 2)
                    { sr = 140; sg = 200; sb = 255; }

                    pixels[idx + 0] = sr; pixels[idx + 1] = sg; pixels[idx + 2] = sb; pixels[idx + 3] = 255;
                }
                else if (Math.Abs(cx) < hullHalfX * 0.8f && Math.Abs(cy) >= hullHalfY && Math.Abs(cy) <= hullHalfY + 2
                    && (Math.Abs(cx) == (int)(hullHalfX * 0.4f) || Math.Abs(cx) == (int)(hullHalfX * 0.7f)))
                {
                    pixels[idx + 0] = 80; pixels[idx + 1] = 80; pixels[idx + 2] = 80; pixels[idx + 3] = 200;
                }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    public void Dispose()
    {
        foreach (var tex in _solarTextures.Values)
            _textures.DestroyTexture(tex);
        _solarTextures.Clear();

        foreach (var tex in _landedTextures.Values)
            _textures.DestroyTexture(tex);
        _landedTextures.Clear();

        GC.SuppressFinalize(this);
    }
}
