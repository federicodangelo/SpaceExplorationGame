using SDL3;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Creates and manages procedural pixel art textures for the game.
/// All sprites are generated programmatically as SDL textures.
/// </summary>
public class TextureManager : IDisposable
{
    private readonly nint _renderer;
    private readonly Dictionary<string, nint> _textures = [];

    // Commonly used texture keys
    public const string ShipSolar = "ship_solar";
    public const string ShipFlame = "ship_flame";
    public const string AvatarDown = "avatar_down";
    public const string ShipLanded = "ship_landed";
    public const string Station = "station";
    public const string Asteroid = "asteroid";
    public const string Vehicle = "vehicle";

    public TextureManager(nint renderer)
    {
        _renderer = renderer;
        GenerateAllTextures();
    }

    public nint GetTexture(string key) =>
        _textures.TryGetValue(key, out var tex) ? tex : nint.Zero;

    /// <summary>Create a planet texture with shading and surface detail.</summary>
    public nint CreatePlanetTexture(int size, byte r, byte g, byte b, uint detailSeed)
    {
        var pixels = new byte[size * size * 4]; // RGBA
        float center = size / 2f;
        float radius = size / 2f - 1;
        var rng = new Generation.SeededRandom(detailSeed);

        // Generate some surface noise
        var noise = new float[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                noise[x, y] = rng.NextFloat(-0.15f, 0.15f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                int idx = (y * size + x) * 4;

                if (dist <= radius)
                {
                    // Sphere shading: light from top-left
                    float nx = dx / radius;
                    float ny = dy / radius;
                    float nz = MathF.Sqrt(MathF.Max(0, 1f - nx * nx - ny * ny));

                    // Diffuse lighting (light from top-left-front)
                    float lightX = -0.4f, lightY = -0.5f, lightZ = 0.7f;
                    float len = MathF.Sqrt(lightX * lightX + lightY * lightY + lightZ * lightZ);
                    lightX /= len; lightY /= len; lightZ /= len;
                    float diffuse = MathF.Max(0, nx * lightX + ny * lightY + nz * lightZ);

                    // Ambient + diffuse
                    float brightness = 0.25f + 0.75f * diffuse;

                    // Surface noise variation
                    float n = noise[x, y];

                    float fr = Math.Clamp(r * brightness + r * n, 0, 255);
                    float fg = Math.Clamp(g * brightness + g * n, 0, 255);
                    float fb = Math.Clamp(b * brightness + b * n, 0, 255);

                    // Edge darkening (atmosphere effect)
                    float edge = 1f - MathF.Pow(dist / radius, 4);
                    fr *= edge + (1 - edge) * 0.3f;
                    fg *= edge + (1 - edge) * 0.3f;
                    fb *= edge + (1 - edge) * 0.3f;

                    // Specular highlight
                    float specular = MathF.Pow(MathF.Max(0, nz * lightZ + nx * lightX + ny * lightY), 16);
                    fr = Math.Min(255, fr + 60 * specular);
                    fg = Math.Min(255, fg + 60 * specular);
                    fb = Math.Min(255, fb + 60 * specular);

                    pixels[idx + 0] = (byte)fr;  // R
                    pixels[idx + 1] = (byte)fg;  // G
                    pixels[idx + 2] = (byte)fb;  // B
                    pixels[idx + 3] = 255;        // A
                }
                else if (dist <= radius + 1)
                {
                    // Anti-alias edge
                    float alpha = Math.Clamp(radius + 1 - dist, 0, 1);
                    pixels[idx + 0] = (byte)(r * 0.3f);
                    pixels[idx + 1] = (byte)(g * 0.3f);
                    pixels[idx + 2] = (byte)(b * 0.3f);
                    pixels[idx + 3] = (byte)(alpha * 120);
                }
                else
                {
                    pixels[idx + 0] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = 0;
                }
            }
        }

        return CreateTextureFromPixels(pixels, size, size);
    }

    /// <summary>Create a star texture with glow gradient.</summary>
    public nint CreateStarTexture(int size, byte r, byte g, byte b)
    {
        var pixels = new byte[size * size * 4];
        float center = size / 2f;
        float coreRadius = size * 0.2f;
        float glowRadius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                int idx = (y * size + x) * 4;

                if (dist <= coreRadius)
                {
                    // Bright core (white to star color)
                    float t = dist / coreRadius;
                    pixels[idx + 0] = (byte)(255 - (255 - r) * t * 0.5f);
                    pixels[idx + 1] = (byte)(255 - (255 - g) * t * 0.5f);
                    pixels[idx + 2] = (byte)(255 - (255 - b) * t * 0.5f);
                    pixels[idx + 3] = 255;
                }
                else if (dist <= glowRadius)
                {
                    // Glow falloff
                    float t = (dist - coreRadius) / (glowRadius - coreRadius);
                    float intensity = MathF.Pow(1f - t, 2.5f);
                    pixels[idx + 0] = (byte)(r * intensity);
                    pixels[idx + 1] = (byte)(g * intensity);
                    pixels[idx + 2] = (byte)(b * intensity);
                    pixels[idx + 3] = (byte)(255 * intensity);
                }
                else
                {
                    pixels[idx + 0] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = 0;
                }
            }
        }

        return CreateTextureFromPixels(pixels, size, size);
    }

    private void GenerateAllTextures()
    {
        // Player ship (top-down view, 32x32)
        _textures[ShipSolar] = GenerateShipTexture();
        _textures[ShipFlame] = GenerateFlameTexture();
        _textures[AvatarDown] = GenerateAvatarTexture();
        _textures[ShipLanded] = GenerateLandedShipTexture();
        _textures[Station] = GenerateStationTexture();
        _textures[Asteroid] = GenerateAsteroidTexture();
        _textures[Vehicle] = GenerateVehicleTexture();
    }

    private nint GenerateShipTexture()
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];

        // Ship pointing right (0 degrees = right)
        // Sleek triangular design
        int[] shipRows = [
            // y, xStart, xEnd  (centered at 16,16)
        ];

        // Build ship shape programmatically - pointed right
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 16; // center
                int cy = y - 16;

                // Ship body: triangle pointing right
                // Nose at x=14, tail at x=-12, wingspan +-8 at tail
                float halfWidth = 8f * (1f - (cx + 12f) / 26f); // wider at tail
                if (cx >= -12 && cx <= 14 && Math.Abs(cy) <= halfWidth)
                {
                    // Color gradient: nose is brighter
                    float t = (cx + 12f) / 26f; // 0=tail, 1=nose
                    byte sr = (byte)(80 + 100 * t);
                    byte sg = (byte)(200 + 55 * t);
                    byte sb = (byte)(80 + 100 * t);

                    // Cockpit highlight
                    if (cx > 8 && Math.Abs(cy) < 2)
                    {
                        sr = 180; sg = 220; sb = 255;
                    }
                    // Wing edges
                    else if (Math.Abs(cy) > halfWidth - 1.5f)
                    {
                        sr = (byte)(sr * 0.6f);
                        sg = (byte)(sg * 0.6f);
                        sb = (byte)(sb * 0.6f);
                    }
                    // Center stripe
                    else if (Math.Abs(cy) < 1.5f && cx < 6)
                    {
                        sr = (byte)Math.Min(255, sr + 30);
                        sg = (byte)Math.Min(255, sg + 30);
                        sb = (byte)Math.Min(255, sb + 30);
                    }

                    pixels[idx + 0] = sr;
                    pixels[idx + 1] = sg;
                    pixels[idx + 2] = sb;
                    pixels[idx + 3] = 255;
                }
                // Engine pods at tail
                else if (cx >= -14 && cx <= -10 && Math.Abs(cy) >= 4 && Math.Abs(cy) <= 7)
                {
                    pixels[idx + 0] = 60;
                    pixels[idx + 1] = 70;
                    pixels[idx + 2] = 90;
                    pixels[idx + 3] = 255;
                }
                // Engine glow
                else if (cx >= -14 && cx <= -12 && Math.Abs(cy) >= 2 && Math.Abs(cy) <= 4)
                {
                    pixels[idx + 0] = 100;
                    pixels[idx + 1] = 150;
                    pixels[idx + 2] = 200;
                    pixels[idx + 3] = 180;
                }
            }
        }

        return CreateTextureFromPixels(pixels, size, size);
    }

    private nint GenerateFlameTexture()
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];

        // Engine flame centered in texture, pointing LEFT
        // When drawn offset behind the ship this will trail behind
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 16;
                int cy = y - 16;

                // Main flame cone: wide at right (engine), tapers left (exhaust tip)
                if (cx >= -14 && cx <= 8)
                {
                    float flameLen = (8f - cx) / 22f; // 0 at engine, 1 at tip
                    float maxY = 6f * (1f - flameLen * 0.6f); // narrows toward tip

                    if (Math.Abs(cy) <= maxY)
                    {
                        float intensity = 1f - flameLen * 0.7f;
                        float coreT = 1f - Math.Abs(cy) / maxY;

                        // Bright core: white-yellow center, orange-red edges
                        byte fr = (byte)(255 * intensity);
                        byte fg = (byte)(220 * intensity * coreT + 80 * intensity * (1 - coreT));
                        byte fb = (byte)(120 * intensity * coreT * coreT);
                        byte fa = (byte)(240 * intensity * (0.5f + 0.5f * coreT));

                        pixels[idx + 0] = fr;
                        pixels[idx + 1] = fg;
                        pixels[idx + 2] = fb;
                        pixels[idx + 3] = fa;
                    }
                }
            }
        }

        return CreateTextureFromPixels(pixels, size, size);
    }

    private nint GenerateAvatarTexture()
    {
        const int size = 16;
        var pixels = new byte[size * size * 4];

        // Tiny humanoid sprite
        // Row by row definition (centered at 8,8)
        SetPixelBlock(pixels, size, 6, 1, 4, 3, 200, 180, 150, 255);   // Head
        SetPixelBlock(pixels, size, 6, 4, 4, 1, 60, 180, 100, 255);    // Neck
        SetPixelBlock(pixels, size, 5, 5, 6, 4, 60, 180, 100, 255);    // Torso (green suit)
        SetPixelBlock(pixels, size, 3, 6, 2, 3, 60, 180, 100, 255);    // Left arm
        SetPixelBlock(pixels, size, 11, 6, 2, 3, 60, 180, 100, 255);   // Right arm
        SetPixelBlock(pixels, size, 6, 9, 2, 4, 50, 50, 140, 255);     // Left leg
        SetPixelBlock(pixels, size, 8, 9, 2, 4, 50, 50, 140, 255);     // Right leg
        SetPixelBlock(pixels, size, 5, 13, 3, 1, 80, 60, 40, 255);     // Left boot
        SetPixelBlock(pixels, size, 8, 13, 3, 1, 80, 60, 40, 255);     // Right boot
        // Visor
        SetPixelBlock(pixels, size, 7, 2, 2, 1, 100, 180, 255, 255);

        return CreateTextureFromPixels(pixels, size, size);
    }

    private nint GenerateLandedShipTexture()
    {
        const int size = 24;
        var pixels = new byte[size * size * 4];

        // Top-down landed ship (smaller, resting)
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 12;
                int cy = y - 12;

                // Body: oval shape
                float ex = cx / 10f;
                float ey = cy / 6f;
                if (ex * ex + ey * ey <= 1f)
                {
                    // Ship hull
                    float t = MathF.Sqrt(ex * ex + ey * ey);
                    byte sr = (byte)(120 + 40 * (1 - t));
                    byte sg = (byte)(130 + 40 * (1 - t));
                    byte sb = (byte)(160 + 40 * (1 - t));

                    // Cockpit
                    if (cx > 3 && Math.Abs(cy) < 2)
                    {
                        sr = 140; sg = 200; sb = 255;
                    }

                    pixels[idx + 0] = sr;
                    pixels[idx + 1] = sg;
                    pixels[idx + 2] = sb;
                    pixels[idx + 3] = 255;
                }
                // Landing struts
                else if (Math.Abs(cx) < 8 && Math.Abs(cy) >= 6 && Math.Abs(cy) <= 8 && (Math.Abs(cx) == 4 || Math.Abs(cx) == 7))
                {
                    pixels[idx + 0] = 80;
                    pixels[idx + 1] = 80;
                    pixels[idx + 2] = 80;
                    pixels[idx + 3] = 200;
                }
            }
        }

        return CreateTextureFromPixels(pixels, size, size);
    }

    private nint GenerateStationTexture()
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

                // Central hub (circle)
                float dist = MathF.Sqrt(cx * cx + cy * cy);
                if (dist <= 5)
                {
                    pixels[idx + 0] = 180;
                    pixels[idx + 1] = 180;
                    pixels[idx + 2] = 220;
                    pixels[idx + 3] = 255;
                }
                // Outer ring
                else if (dist >= 9 && dist <= 12)
                {
                    pixels[idx + 0] = 150;
                    pixels[idx + 1] = 150;
                    pixels[idx + 2] = 200;
                    pixels[idx + 3] = 255;
                }
                // Solar panel arms (cross shape)
                else if ((Math.Abs(cx) <= 1 && Math.Abs(cy) <= 14) || (Math.Abs(cy) <= 1 && Math.Abs(cx) <= 14))
                {
                    if (dist > 5 && dist < 9)
                    {
                        // Struts
                        pixels[idx + 0] = 100;
                        pixels[idx + 1] = 100;
                        pixels[idx + 2] = 130;
                        pixels[idx + 3] = 255;
                    }
                    else if (dist >= 12)
                    {
                        // Panel areas
                        pixels[idx + 0] = 60;
                        pixels[idx + 1] = 80;
                        pixels[idx + 2] = 180;
                        pixels[idx + 3] = 255;
                    }
                }
                // Solar panels (rectangles at cross ends)
                else if (Math.Abs(cx) <= 3 && Math.Abs(cy) >= 12 && Math.Abs(cy) <= 15)
                {
                    pixels[idx + 0] = 50;
                    pixels[idx + 1] = 70;
                    pixels[idx + 2] = 160;
                    pixels[idx + 3] = 255;
                }
                else if (Math.Abs(cy) <= 3 && Math.Abs(cx) >= 12 && Math.Abs(cx) <= 15)
                {
                    pixels[idx + 0] = 50;
                    pixels[idx + 1] = 70;
                    pixels[idx + 2] = 160;
                    pixels[idx + 3] = 255;
                }
                // Docking ring indicators
                if (dist >= 11.5f && dist <= 12.5f)
                {
                    float angle = MathF.Atan2(cy, cx);
                    if ((int)(angle * 8 / MathF.PI) % 2 == 0)
                    {
                        pixels[idx + 0] = 255;
                        pixels[idx + 1] = 200;
                        pixels[idx + 2] = 100;
                        pixels[idx + 3] = 255;
                    }
                }
            }
        }

        return CreateTextureFromPixels(pixels, size, size);
    }

    private nint GenerateAsteroidTexture()
    {
        const int size = 12;
        var pixels = new byte[size * size * 4];

        // Irregular rocky blob
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 6;
                int cy = y - 6;
                float dist = MathF.Sqrt(cx * cx + cy * cy);

                // Irregular radius
                float angle = MathF.Atan2(cy, cx);
                float r = 4f + MathF.Sin(angle * 3) * 1f + MathF.Cos(angle * 5) * 0.5f;

                if (dist <= r)
                {
                    float shade = 0.5f + 0.5f * (1f - dist / r);
                    // Gray-brown rock with variation
                    float vary = MathF.Sin(x * 2.5f + y * 1.7f) * 0.15f;
                    pixels[idx + 0] = (byte)(140 * (shade + vary));
                    pixels[idx + 1] = (byte)(120 * (shade + vary));
                    pixels[idx + 2] = (byte)(100 * (shade + vary));
                    pixels[idx + 3] = 255;
                }
            }
        }

        return CreateTextureFromPixels(pixels, size, size);
    }

    private nint GenerateVehicleTexture()
    {
        const int size = 20;
        var pixels = new byte[size * size * 4];

        // Top-down 4-wheel rover with roll cage
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 10;
                int cy = y - 10;

                // Main body (rounded rectangle)
                if (Math.Abs(cx) <= 5 && Math.Abs(cy) <= 7)
                {
                    // Body color: warm gray-orange
                    float shade = 1f - Math.Abs(cy) / 10f * 0.2f;
                    pixels[idx + 0] = (byte)(180 * shade);
                    pixels[idx + 1] = (byte)(140 * shade);
                    pixels[idx + 2] = (byte)(80 * shade);
                    pixels[idx + 3] = 255;

                    // Cockpit windshield (top)
                    if (cy <= -3 && Math.Abs(cx) <= 3)
                    {
                        pixels[idx + 0] = 100;
                        pixels[idx + 1] = 180;
                        pixels[idx + 2] = 230;
                        pixels[idx + 3] = 255;
                    }
                    // Roll cage bars
                    else if (Math.Abs(cx) == 5 || (cy == 0 && Math.Abs(cx) <= 5))
                    {
                        pixels[idx + 0] = 100;
                        pixels[idx + 1] = 100;
                        pixels[idx + 2] = 110;
                        pixels[idx + 3] = 255;
                    }
                }
                // Wheels (4 corners)
                else if (Math.Abs(cx) >= 5 && Math.Abs(cx) <= 8 &&
                         (Math.Abs(cy - 5) <= 2 || Math.Abs(cy + 5) <= 2))
                {
                    pixels[idx + 0] = 50;
                    pixels[idx + 1] = 50;
                    pixels[idx + 2] = 50;
                    pixels[idx + 3] = 255;

                    // Wheel tread highlight
                    if (Math.Abs(cx) == 6 || Math.Abs(cx) == 7)
                    {
                        pixels[idx + 0] = 70;
                        pixels[idx + 1] = 70;
                        pixels[idx + 2] = 70;
                    }
                }
                // Headlights (front)
                else if (cy == -8 && (Math.Abs(cx) == 3 || Math.Abs(cx) == 4))
                {
                    pixels[idx + 0] = 255;
                    pixels[idx + 1] = 255;
                    pixels[idx + 2] = 200;
                    pixels[idx + 3] = 255;
                }
                // Tail lights (rear)
                else if (cy == 8 && (Math.Abs(cx) == 3 || Math.Abs(cx) == 4))
                {
                    pixels[idx + 0] = 255;
                    pixels[idx + 1] = 50;
                    pixels[idx + 2] = 50;
                    pixels[idx + 3] = 255;
                }
            }
        }

        return CreateTextureFromPixels(pixels, size, size);
    }

    private nint CreateTextureFromPixels(byte[] pixels, int width, int height)
    {
        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                var surface = SDL.CreateSurfaceFrom(width, height,
                    SDL.PixelFormat.ABGR8888, (nint)ptr, width * 4);

                if (surface == nint.Zero)
                    throw new Exception($"Failed to create surface: {SDL.GetError()}");

                var texture = SDL.CreateTextureFromSurface(_renderer, surface);
                SDL.DestroySurface(surface);

                if (texture == nint.Zero)
                    throw new Exception($"Failed to create texture: {SDL.GetError()}");

                // Enable alpha blending on the texture
                SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);

                return texture;
            }
        }
    }

    private static void SetPixelBlock(byte[] pixels, int stride, int x, int y, int w, int h,
        byte r, byte g, byte b, byte a)
    {
        for (int py = y; py < y + h; py++)
        {
            for (int px = x; px < x + w; px++)
            {
                if (px >= 0 && px < stride && py >= 0 && py < stride)
                {
                    int idx = (py * stride + px) * 4;
                    pixels[idx + 0] = r;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = b;
                    pixels[idx + 3] = a;
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (var tex in _textures.Values)
        {
            SDL.DestroyTexture(tex);
        }
        _textures.Clear();
        GC.SuppressFinalize(this);
    }
}
