using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders weather particle effects on-screen based on the planet biome.
/// </summary>
/// <remarks>
/// Particles live in a virtual tiling grid offset by the camera so they
/// scroll with the world (parallax factor ~0.35) instead of sticking to the screen.
/// </remarks>
public static class WeatherRenderer
{
    public static void Render(ISpriteRenderer renderer,
        int screenW, int screenH, PlanetData? planet, double globalTime,
        float cameraX, float cameraY)
    {
        if (planet == null) return;
        var biome = planet.Type;

        // Camera parallax: particles scroll slower than the world, giving depth.
        const float parallax = 0.35f;
        float camOx = cameraX * parallax;
        float camOy = cameraY * parallax;

        // Helper: wrap a value into [0, range) handling negatives.
        static float Wrap(float v, float range)
        {
            float m = v % range;
            return m < 0 ? m + range : m;
        }

        int particleCount;
        switch (biome)
        {
            case PlanetType.Terrestrial:
            case PlanetType.Ocean:
                // Rain — falls downward with slight wind drift
                particleCount = 80;
                for (int i = 0; i < particleCount; i++)
                {
                    int hash = i * 374761 + i * 17 + 12345;
                    float baseX = (hash & 0xFFFF) % screenW;
                    float speed = 350f + (hash >> 16 & 0xFF);
                    float windDrift = (float)(globalTime * 15.0 + i * 5.3);
                    float px = Wrap(baseX + windDrift - camOx, screenW);
                    float py = Wrap((float)(i * 73.7 + globalTime * speed) - camOy, screenH + 20) - 10;
                    byte alpha = (byte)(80 + (hash >> 8 & 0x3F));
                    renderer.DrawRectScreen(px, py, 1, 8,
                        new Color4(160, 180, 220, alpha));
                }
                break;

            case PlanetType.Desert:
                // Dust / sand particles drifting horizontally
                particleCount = 45;
                for (int i = 0; i < particleCount; i++)
                {
                    int hash = i * 668265 + i * 23 + 54321;
                    float baseY = (hash & 0xFFFF) % screenH;
                    float speed = 80f + (hash >> 16 & 0x7F);
                    float drift = (float)Math.Sin(globalTime * 0.5 + i * 1.1) * 8f;
                    float px = Wrap((float)(i * 97.3 + globalTime * speed) - camOx, screenW + 40) - 20;
                    float py = Wrap(baseY + drift - camOy, screenH);
                    byte alpha = (byte)(60 + (hash >> 8 & 0x3F));
                    renderer.DrawRectScreen(px, py, 4, 2,
                        new Color4(200, 180, 140, alpha));
                }
                break;

            case PlanetType.Frozen:
                // Snow — falls slowly with sinusoidal horizontal drift
                particleCount = 70;
                for (int i = 0; i < particleCount; i++)
                {
                    int hash = i * 472882 + i * 13 + 67890;
                    float baseX = (hash & 0xFFFF) % screenW;
                    float drift = (float)Math.Sin(globalTime * 0.8 + i * 0.7) * 40f;
                    float speed = 40f + (hash >> 16 & 0x3F);
                    float px = Wrap(baseX + drift - camOx, screenW);
                    float py = Wrap((float)(i * 53.1 + globalTime * speed) - camOy, screenH + 10) - 5;
                    byte alpha = (byte)(90 + (hash >> 8 & 0x3F));
                    int sz = (hash >> 12 & 1) == 0 ? 2 : 3;
                    renderer.DrawRectScreen(px, py, sz, sz,
                        new Color4(220, 230, 240, alpha));
                }
                break;

            case PlanetType.Volcanic:
                // Floating ash / embers — rise upward
                particleCount = 40;
                for (int i = 0; i < particleCount; i++)
                {
                    int hash = i * 338947 + i * 11 + 33333;
                    float baseX = (hash & 0xFFFF) % screenW;
                    float drift = (float)Math.Sin(globalTime * 1.2 + i * 1.3) * 25f;
                    float speed = 30f + (hash >> 16 & 0x3F);
                    float px = Wrap(baseX + drift - camOx, screenW);
                    float py = Wrap((float)(screenH - (i * 61.7 + globalTime * speed)) + camOy, screenH + 10);
                    bool isEmber = (hash >> 12 & 3) == 0;
                    var color = isEmber
                        ? new Color4(255, 130, 40, 100)
                        : new Color4(120, 100, 80, 70);
                    renderer.DrawRectScreen(px, py, 2, 2, color);
                }
                break;
        }
    }
}
