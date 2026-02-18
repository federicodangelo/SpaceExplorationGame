using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders space stations. Owns the singleton station texture.
/// </summary>
public class StationRenderer : IDisposable
{
    const int TextureSize = 256;
    // Scale factor for all distances (original was 32x32)
    const float Scale = TextureSize / 32.0f;

    const int NumLightsOuterRing = 8;
    const double BlinkPeriod = 2.0; // seconds

    static Color3 BlinkColor1 = new Color3(255, 40, 40); // red
    static Color3 BlinkColor2 = new Color3(40, 255, 40); // green

    const float OuterRingRadius = 90.0f;
    const float OuterRingLightRadius = 3f;
    const float CenterLightRadius = 8f;


    private readonly TextureManager _textures;
    private nint _texture;
    private readonly Dictionary<Color3, nint> _radialFalloffTextures = new();

    public StationRenderer(TextureManager textures)
    {
        _textures = textures;
        _texture = GenerateStationTexture(textures);
        // No need to pre-generate, will be generated per color on demand
    }

    /// <summary>Renders all stations with a slowly rotating texture.</summary>
    public void RenderStations(SpriteRenderer renderer, Camera camera,
        World ecsWorld, List<Entity> stationEntities, double globalTime)
    {
        for (int i = 0; i < stationEntities.Count; i++)
        {
            var stTransform = ecsWorld.Get<Transform>(stationEntities[i]);

            RenderStation(renderer, camera, stTransform.Position, globalTime);
        }
    }

    public void RenderStation(SpriteRenderer renderer, Camera camera, Vector2 position, double globalTime)
    {
        float stRotation = (float)(globalTime * 10) % 360f;
        renderer.DrawTexture(camera, _texture, position, 280, 280, stRotation);

        // Blinking lights overlay (on the outer ring)
        double blinkPhase = globalTime % BlinkPeriod;

        for (int l = 0; l < NumLightsOuterRing; l++)
        {
            float angle = (float)(l * MathF.PI * 2f / NumLightsOuterRing);
            // Rotate with station
            float totalAngle = angle + stRotation * MathF.PI / 180f;
            Vector2 offset = new Vector2(MathF.Cos(totalAngle), MathF.Sin(totalAngle)) * OuterRingRadius;
            Vector2 lightPos = position + offset;

            // Alternate blinking color for each light
            bool blinkState = (l % 2 == 0) ? (blinkPhase < BlinkPeriod / 2) : (blinkPhase >= BlinkPeriod / 2);
            var color = blinkState ? BlinkColor1 : BlinkColor2;
            DrawLightGlow(renderer, camera, lightPos, OuterRingLightRadius * 4, color); // Draw glow
            DrawLight(renderer, camera, lightPos, OuterRingLightRadius, color);
        }

        // Center light
        {
            Vector2 lightPos = position;
            bool blinkState = blinkPhase >= BlinkPeriod / 2;
            var color = blinkState ? BlinkColor1 : BlinkColor2; //Inverted colors for inner ring
            DrawLightGlow(renderer, camera, lightPos, CenterLightRadius * 2.5f, color); // Draw glow
            DrawLight(renderer, camera, lightPos, CenterLightRadius, color);
        }
    }

    // Helper function to draw a single light
    // Draws a radial falloff texture as a glow behind the light
    private void DrawLightGlow(SpriteRenderer renderer, Camera camera, Vector2 position, float radius, Color3 color)
    {
        int size = (int)(radius * 2f);
        nint texture = GetOrCreateRadialFalloffTexture(color, size);
        if (texture != nint.Zero)
        {
            renderer.DrawTexture(camera, texture, position, size, size, 0f);
        }
    }

    private nint GetOrCreateRadialFalloffTexture(Color3 color, int size)
    {
        // Use only RGB for key, alpha is handled by draw call
        if (_radialFalloffTextures.TryGetValue(color, out var tex))
            return tex;
        var newTex = GenerateRadialFalloffTexture(_textures, size, color);
        _radialFalloffTextures[color] = newTex;
        return newTex;
    }

    private void DrawLight(SpriteRenderer renderer, Camera camera, Vector2 position, float radius, Color4 color)
    {
        renderer.DrawFilledCircle(camera, position, radius, color);
    }
    
    private static nint GenerateStationTexture(TextureManager textures) 
    {
        var pixels = new byte[TextureSize * TextureSize * 4];

        int center = TextureSize / 2;

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                int idx = (y * TextureSize + x) * 4;
                int cx = x - center;
                int cy = y - center;

                // Central hub (circle)
                float dist = MathF.Sqrt(cx * cx + cy * cy);
                if (dist <= 5 * Scale)
                {
                    pixels[idx + 0] = 180;
                    pixels[idx + 1] = 180;
                    pixels[idx + 2] = 220;
                    pixels[idx + 3] = 255;
                }
                // Outer ring
                else if (dist >= 9 * Scale && dist <= 12 * Scale)
                {
                    pixels[idx + 0] = 150;
                    pixels[idx + 1] = 150;
                    pixels[idx + 2] = 200;
                    pixels[idx + 3] = 255;
                }
                // Solar panel arms (cross shape)
                else if ((Math.Abs(cx) <= 1 * Scale && Math.Abs(cy) <= 14 * Scale) || (Math.Abs(cy) <= 1 * Scale && Math.Abs(cx) <= 14 * Scale))
                {
                    if (dist > 5 * Scale && dist < 9 * Scale)
                    {
                        // Struts
                        pixels[idx + 0] = 100;
                        pixels[idx + 1] = 100;
                        pixels[idx + 2] = 130;
                        pixels[idx + 3] = 255;
                    }
                    else if (dist >= 12 * Scale)
                    {
                        // Panel areas
                        pixels[idx + 0] = 60;
                        pixels[idx + 1] = 80;
                        pixels[idx + 2] = 180;
                        pixels[idx + 3] = 255;
                    }
                }
                // Solar panels (rectangles at cross ends)
                else if (Math.Abs(cx) <= 3 * Scale && Math.Abs(cy) >= 12 * Scale && Math.Abs(cy) <= 15 * Scale)
                {
                    pixels[idx + 0] = 50;
                    pixels[idx + 1] = 70;
                    pixels[idx + 2] = 160;
                    pixels[idx + 3] = 255;
                }
                else if (Math.Abs(cy) <= 3 * Scale && Math.Abs(cx) >= 12 * Scale && Math.Abs(cx) <= 15 * Scale)
                {
                    pixels[idx + 0] = 50;
                    pixels[idx + 1] = 70;
                    pixels[idx + 2] = 160;
                    pixels[idx + 3] = 255;
                }
                // Docking ring indicators
                if (dist >= 11.5f * Scale && dist <= 12.5f * Scale)
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

        return textures.CreateTextureFromPixels(pixels, TextureSize, TextureSize);
    }

    // Generates a radial falloff (glow) texture with smooth alpha
    private static nint GenerateRadialFalloffTexture(TextureManager textures, int size, Color3 color)
    {
        var pixels = new byte[size * size * 4];
        int center = size / 2;
        float maxDist = center;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int dx = x - center;
                int dy = y - center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float falloff = 1f - Math.Clamp(dist / maxDist, 0f, 1f);
                // Use a smoothstep for a soft edge
                float alpha = falloff * falloff * (3f - 2f * falloff);
                pixels[idx + 0] = color.R;
                pixels[idx + 1] = color.G;
                pixels[idx + 2] = color.B;
                pixels[idx + 3] = (byte)(alpha * 255);
            }
        }
        return textures.CreateTextureFromPixels(pixels, size, size);
    }
    
    public void Dispose()
    {
        _textures.DestroyTexture(_texture);
        foreach (var tex in _radialFalloffTextures.Values)
        {
            if (tex != nint.Zero)
                _textures.DestroyTexture(tex);
        }
        _radialFalloffTextures.Clear();
        _texture = nint.Zero;
        GC.SuppressFinalize(this);
    }
}
