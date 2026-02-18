using SDL3;
using SpaceExplorationGame.Core;
using System.Numerics;

namespace SpaceExplorationGame.Rendering.Base;

/// <summary>
/// Handles rendering sprites, colored rectangles, and basic shapes using SDL3.
/// </summary>
public class SpriteRenderer : IDisposable
{
    private readonly nint _renderer;
    private readonly List<nint> _textures = [];
    private readonly FontRenderer _fontRenderer;

    public SpriteRenderer(nint renderer)
    {
        _renderer = renderer;
        // Enable alpha blending so draw calls with a < 255 are translucent
        SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);
        _fontRenderer = new FontRenderer(renderer);
    }

    /// <summary>Set a clip rectangle — all subsequent draw calls are confined to this area.</summary>
    public void SetClipRect(float x, float y, float w, float h)
    {
        var rect = new SDL.Rect { X = (int)x, Y = (int)y, W = (int)w, H = (int)h };
        SDL.SetRenderClipRect(_renderer, in rect);
    }

    /// <summary>Clear the clip rectangle so draw calls cover the full window again.</summary>
    public void ClearClipRect()
    {
        SDL.SetRenderClipRect(_renderer, nint.Zero);
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
    public void DrawRect(Camera camera, Vector2 worldPos, int width, int height, Color4 color)
    {
        var screenPos = camera.WorldToScreen(worldPos);
        var scaledW = width * camera.Zoom;
        var scaledH = height * camera.Zoom;

        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);
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
    public void DrawRectScreen(float x, float y, float w, float h, Color4 color)
    {
        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);
        var rect = new SDL.FRect { X = x, Y = y, W = w, H = h };
        SDL.RenderFillRect(_renderer, in rect);
    }

    /// <summary>Draw a circle outline in world space (using line segments).</summary>
    public void DrawCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color, int segments = 32)
    {
        var center = camera.WorldToScreen(worldCenter);
        var radius = worldRadius * camera.Zoom;

        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);

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
    public void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color)
    {
        var center = camera.WorldToScreen(worldCenter);
        var radius = worldRadius * camera.Zoom;

        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);

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
    public void DrawLine(Camera camera, Vector2 worldStart, Vector2 worldEnd, Color4 color)
    {
        var start = camera.WorldToScreen(worldStart);
        var end = camera.WorldToScreen(worldEnd);
        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);
        SDL.RenderLine(_renderer, start.X, start.Y, end.X, end.Y);
    }

    /// <summary>Draw a line directly in screen space.</summary>
    public void DrawLineScreen(float x1, float y1, float x2, float y2, Color4 color)
    {
        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);
        SDL.RenderLine(_renderer, x1, y1, x2, y2);
    }

    /// <summary>Draw text in world space (delegates to FontRenderer).</summary>
    public void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f)
        => _fontRenderer.DrawText(camera, worldPos, text, color, scale);

    /// <summary>Draw text in screen space (delegates to FontRenderer).</summary>
    public void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f)
        => _fontRenderer.DrawTextScreen(x, y, text, color, scale);

    /// <summary>Measure the width of text in screen pixels.</summary>
    public float MeasureText(string text, float scale = 1f)
        => _fontRenderer.MeasureText(text, scale);

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
    public void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 color)
    {
        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);
        for (int y = (int)(-radius); y <= (int)radius; y++)
        {
            float x = MathF.Sqrt(radius * radius - y * y);
            SDL.RenderLine(_renderer, cx - x, cy + y, cx + x, cy + y);
        }
    }

    public void Dispose()
    {
        _fontRenderer.Dispose();
        foreach (var tex in _textures)
        {
            SDL.DestroyTexture(tex);
        }
        _textures.Clear();
        GC.SuppressFinalize(this);
    }
}
