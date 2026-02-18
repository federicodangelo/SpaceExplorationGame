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
    private readonly FontRenderer _fontRenderer;

    public SpriteRenderer(nint renderer, TextureManager textures)
    {
        _renderer = renderer;
        // Enable alpha blending so draw calls with a < 255 are translucent
        SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);
        _fontRenderer = new FontRenderer(renderer, textures);
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

        DrawFilledCircleScreen(center.X, center.Y, radius, color);
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

    /// <summary>Render pre-built colored geometry (no texture) in a single batched draw call.</summary>
    public void DrawGeometry(SDL.Vertex[] vertices, int numVertices, int[] indices, int numIndices)
    {
        SDL.RenderGeometry(_renderer, nint.Zero, vertices, numVertices, indices, numIndices);
    }

    /// <summary>Draw a filled circle in screen space.</summary>
    /// <summary>Draw a filled circle in screen space using a triangle fan.</summary>
    /// <param name="cx">Center X</param>
    /// <param name="cy">Center Y</param>
    /// <param name="radius">Radius</param>
    /// <param name="color">Fill color</param>
    /// <param name="segments">Number of segments (vertices), default 32</param>
    // Reusable buffers for batched tile rendering (avoids per-frame allocs).
    private static SDL.Vertex[] _vertexBuf = new SDL.Vertex[1024];
    private static int[] _indexBuf = new int[1536];
    public void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32)
    {
        if (segments < 3) segments = 3;

        SDL.FColor fcolor = new SDL.FColor
        {
            R = color.R / 255.0f,
            G = color.G / 255.0f,
            B = color.B / 255.0f,
            A = color.A / 255.0f
        };

        // Prepare vertices for a triangle fan
        int requiredVerts = segments + 2;
        int requiredIndices = (segments + 1) * 3;
        if (_vertexBuf.Length < requiredVerts)
            _vertexBuf = new SDL.Vertex[requiredVerts];
        if (_indexBuf.Length < requiredIndices)
            _indexBuf = new int[requiredIndices];
        var vertices = _vertexBuf;
        var indices = _indexBuf;

        // Center vertex
        vertices[0] = new SDL.Vertex
        {
            Position = new SDL.FPoint() { X = cx, Y = cy },
            Color = fcolor,
        };

        float angleStep = MathF.PI * 2f / segments;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * angleStep;
            float x = cx + MathF.Cos(angle) * radius;
            float y = cy + MathF.Sin(angle) * radius;
            vertices[i + 1] = new SDL.Vertex
            {
                Position = new SDL.FPoint() { X = x, Y = y },
                Color = fcolor,
            };
        }

        // Indices for triangle fan
        for (int i = 0; i < segments; i++)
        {
            indices[i * 3 + 0] = 0;
            indices[i * 3 + 1] = i + 1;
            indices[i * 3 + 2] = i + 2;
        }

        SDL.RenderGeometry(_renderer, nint.Zero, vertices, requiredVerts, indices, requiredIndices);
    }

    public void Dispose()
    {
        _fontRenderer.Dispose();
        GC.SuppressFinalize(this);
    }
}
