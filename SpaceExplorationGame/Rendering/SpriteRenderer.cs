using SDL3;
using SpaceExplorationGame.Core;
using System.Numerics;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Handles rendering sprites, colored rectangles, and basic shapes using SDL3.
/// </summary>
public class SpriteRenderer : IDisposable
{
    private readonly nint _renderer;
    private readonly List<nint> _textures = [];

    public SpriteRenderer(nint renderer)
    {
        _renderer = renderer;
    }

    /// <summary>Load a texture from file and return its index.</summary>
    public int LoadTexture(string path)
    {
        var surface = SDL.LoadBMP(path);
        if (surface == nint.Zero)
        {
            throw new Exception($"Failed to load image: {path} - {SDL.GetError()}");
        }

        var texture = SDL.CreateTextureFromSurface(_renderer, surface);
        SDL.DestroySurface(surface);

        if (texture == nint.Zero)
        {
            throw new Exception($"Failed to create texture: {SDL.GetError()}");
        }

        _textures.Add(texture);
        return _textures.Count - 1;
    }

    /// <summary>Draw a filled rectangle in world space (transformed by camera).</summary>
    public void DrawRect(Camera camera, Vector2 worldPos, int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var screenPos = camera.WorldToScreen(worldPos);
        var scaledW = width * camera.Zoom;
        var scaledH = height * camera.Zoom;

        SDL.SetRenderDrawColor(_renderer, r, g, b, a);
        var rect = new SDL.FRect
        {
            X = screenPos.X - scaledW / 2f,
            Y = screenPos.Y - scaledH / 2f,
            W = scaledW,
            H = scaledH
        };
        SDL.RenderFillRect(_renderer, in rect);
    }

    /// <summary>Draw a filled rectangle directly in screen space.</summary>
    public void DrawRectScreen(float x, float y, float w, float h, byte r, byte g, byte b, byte a = 255)
    {
        SDL.SetRenderDrawColor(_renderer, r, g, b, a);
        var rect = new SDL.FRect { X = x, Y = y, W = w, H = h };
        SDL.RenderFillRect(_renderer, in rect);
    }

    /// <summary>Draw a circle outline in world space (using line segments).</summary>
    public void DrawCircle(Camera camera, Vector2 worldCenter, float worldRadius, byte r, byte g, byte b, byte a = 255, int segments = 32)
    {
        var center = camera.WorldToScreen(worldCenter);
        var radius = worldRadius * camera.Zoom;

        SDL.SetRenderDrawColor(_renderer, r, g, b, a);

        float angleStep = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a1 = angleStep * i;
            float a2 = angleStep * (i + 1);
            SDL.RenderLine(_renderer,
                center.X + MathF.Cos(a1) * radius,
                center.Y + MathF.Sin(a1) * radius,
                center.X + MathF.Cos(a2) * radius,
                center.Y + MathF.Sin(a2) * radius);
        }
    }

    /// <summary>Draw a filled circle in world space.</summary>
    public void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius, byte r, byte g, byte b, byte a = 255)
    {
        var center = camera.WorldToScreen(worldCenter);
        var radius = worldRadius * camera.Zoom;

        SDL.SetRenderDrawColor(_renderer, r, g, b, a);

        // Simple scanline fill
        for (int y = (int)(-radius); y <= (int)radius; y++)
        {
            float x = MathF.Sqrt(radius * radius - y * y);
            SDL.RenderLine(_renderer,
                center.X - x, center.Y + y,
                center.X + x, center.Y + y);
        }
    }

    /// <summary>Draw a line in world space.</summary>
    public void DrawLine(Camera camera, Vector2 worldStart, Vector2 worldEnd, byte r, byte g, byte b, byte a = 255)
    {
        var start = camera.WorldToScreen(worldStart);
        var end = camera.WorldToScreen(worldEnd);
        SDL.SetRenderDrawColor(_renderer, r, g, b, a);
        SDL.RenderLine(_renderer, start.X, start.Y, end.X, end.Y);
    }

    /// <summary>Draw a line directly in screen space.</summary>
    public void DrawLineScreen(float x1, float y1, float x2, float y2, byte r, byte g, byte b, byte a = 255)
    {
        SDL.SetRenderDrawColor(_renderer, r, g, b, a);
        SDL.RenderLine(_renderer, x1, y1, x2, y2);
    }

    /// <summary>Draw text as simple pixel blocks (very basic bitmap font).</summary>
    public void DrawText(Camera camera, Vector2 worldPos, string text, byte r, byte g, byte b, float scale = 1f)
    {
        var screenPos = camera.WorldToScreen(worldPos);
        DrawTextScreen(screenPos.X, screenPos.Y, text, r, g, b, scale);
    }

    /// <summary>Draw text in screen space using a minimal built-in pixel font.</summary>
    public void DrawTextScreen(float x, float y, string text, byte r, byte g, byte b, float scale = 1f)
    {
        SDL.SetRenderDrawColor(_renderer, r, g, b, 255);
        float cursorX = x;
        float charWidth = 6 * scale;
        float charHeight = 8 * scale;

        foreach (char c in text)
        {
            if (c == ' ')
            {
                cursorX += charWidth;
                continue;
            }

            var pixels = MiniBitmapFont.GetChar(c);
            if (pixels != null)
            {
                for (int py = 0; py < 8; py++)
                {
                    for (int px = 0; px < 5; px++)
                    {
                        if (pixels[py * 5 + px])
                        {
                            var rect = new SDL.FRect
                            {
                                X = cursorX + px * scale,
                                Y = y + py * scale,
                                W = scale,
                                H = scale
                            };
                            SDL.RenderFillRect(_renderer, in rect);
                        }
                    }
                }
            }

            cursorX += charWidth;
        }
    }

    /// <summary>Measure the width of text in screen pixels.</summary>
    public float MeasureText(string text, float scale = 1f)
    {
        return text.Length * 6f * scale;
    }

    /// <summary>Draw a texture in world space, centered on the position, with rotation.</summary>
    public void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height, float rotationDeg = 0f, byte alpha = 255)
    {
        if (texture == nint.Zero) return;
        var screenPos = camera.WorldToScreen(worldPos);
        float scaledW = width * camera.Zoom;
        float scaledH = height * camera.Zoom;

        var dstRect = new SDL.FRect
        {
            X = screenPos.X - scaledW / 2f,
            Y = screenPos.Y - scaledH / 2f,
            W = scaledW,
            H = scaledH
        };

        if (alpha < 255)
            SDL.SetTextureAlphaMod(texture, alpha);

        if (rotationDeg != 0f)
        {
            var center = new SDL.FPoint { X = scaledW / 2f, Y = scaledH / 2f };
            SDL.RenderTextureRotated(_renderer, texture, nint.Zero, in dstRect, rotationDeg, in center, SDL.FlipMode.None);
        }
        else
        {
            SDL.RenderTexture(_renderer, texture, nint.Zero, in dstRect);
        }

        if (alpha < 255)
            SDL.SetTextureAlphaMod(texture, 255);
    }

    /// <summary>Draw a texture directly in screen space, centered on the position.</summary>
    public void DrawTextureScreen(nint texture, float x, float y, float w, float h, float rotationDeg = 0f, byte alpha = 255)
    {
        if (texture == nint.Zero) return;

        var dstRect = new SDL.FRect
        {
            X = x - w / 2f,
            Y = y - h / 2f,
            W = w,
            H = h
        };

        if (alpha < 255)
            SDL.SetTextureAlphaMod(texture, alpha);

        if (rotationDeg != 0f)
        {
            var center = new SDL.FPoint { X = w / 2f, Y = h / 2f };
            SDL.RenderTextureRotated(_renderer, texture, nint.Zero, in dstRect, rotationDeg, in center, SDL.FlipMode.None);
        }
        else
        {
            SDL.RenderTexture(_renderer, texture, nint.Zero, in dstRect);
        }

        if (alpha < 255)
            SDL.SetTextureAlphaMod(texture, 255);
    }

    /// <summary>Draw a filled circle in screen space.</summary>
    public void DrawFilledCircleScreen(float cx, float cy, float radius, byte r, byte g, byte b, byte a = 255)
    {
        SDL.SetRenderDrawColor(_renderer, r, g, b, a);
        for (int y = (int)(-radius); y <= (int)radius; y++)
        {
            float x = MathF.Sqrt(radius * radius - y * y);
            SDL.RenderLine(_renderer, cx - x, cy + y, cx + x, cy + y);
        }
    }

    public void Dispose()
    {
        foreach (var tex in _textures)
        {
            SDL.DestroyTexture(tex);
        }
        _textures.Clear();
        GC.SuppressFinalize(this);
    }
}
