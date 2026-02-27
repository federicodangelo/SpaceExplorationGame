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
    private readonly TextureManager _textures;

    // Cached circle texture (RGBA) used for drawing filled circles up to 256x256
    private nint _cachedCircleTexture = nint.Zero;
    private const int CachedCircleSize = 64; // max texture size (pixels)

    public SpriteRenderer(nint renderer, TextureManager textures)
    {
        _renderer = renderer;
        _textures = textures;
        // Enable alpha blending so draw calls with a < 255 are translucent
        SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);
        _fontRenderer = new FontRenderer(renderer, textures);
        _cachedCircleTexture = CreateCachedCircleTexture();
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

    /// <summary>Draw a solid ring (annulus) in world space.</summary>
    public void DrawSolidRing(Camera camera, Vector2 worldCenter, float innerRadius, float outerRadius,
        Color4 color, int segments = 48)
    {
        var center = camera.WorldToScreen(worldCenter);
        float inner = innerRadius * camera.Zoom;
        float outer = outerRadius * camera.Zoom;
        DrawSolidRingScreen(center.X, center.Y, inner, outer, color, segments);
    }

    /// <summary>
    /// Draw a filled circle in world space with a radial gradient.
    /// Color remains <paramref name="innerColor"/> from center to <paramref name="transitionStartRadius"/>,
    /// then transitions to <paramref name="outerColor"/> at <paramref name="worldRadius"/>.
    /// </summary>
    public void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius,
        Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32)
    {
        var center = camera.WorldToScreen(worldCenter);
        var radius = worldRadius * camera.Zoom;
        var transitionRadius = transitionStartRadius * camera.Zoom;

        DrawFilledCircleScreen(center.X, center.Y, radius, innerColor, outerColor, transitionRadius, segments);
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

    /// <summary>Draw a texture in world space with a color tint (RGBA).</summary>
    public void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height, Color4 color, float rotationDeg = 0f)
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

        SDL.SetTextureColorMod(texture, color.R, color.G, color.B);
        SDL.SetTextureAlphaMod(texture, color.A);

        if (rotationDeg != 0f)
        {
            var center = new SDL.FPoint { X = scaledW / 2f, Y = scaledH / 2f };
            SDL.RenderTextureRotated(_renderer, texture, nint.Zero, in dstRect, rotationDeg, in center, SDL.FlipMode.None);
        }
        else
        {
            SDL.RenderTexture(_renderer, texture, nint.Zero, in dstRect);
        }

        // Reset mods
        SDL.SetTextureColorMod(texture, 255, 255, 255);
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

    /// <summary>Draw a texture in screen space with a color tint (RGBA).</summary>
    public void DrawTextureScreen(nint texture, float x, float y, float w, float h, Color4 color, float rotationDeg = 0f)
    {
        if (texture == nint.Zero) return;

        var dstRect = new SDL.FRect
        {
            X = x - w / 2f,
            Y = y - h / 2f,
            W = w,
            H = h
        };

        SDL.SetTextureColorMod(texture, color.R, color.G, color.B);
        SDL.SetTextureAlphaMod(texture, color.A);

        if (rotationDeg != 0f)
        {
            var center = new SDL.FPoint { X = w / 2f, Y = h / 2f };
            SDL.RenderTextureRotated(_renderer, texture, nint.Zero, in dstRect, rotationDeg, in center, SDL.FlipMode.None);
        }
        else
        {
            SDL.RenderTexture(_renderer, texture, nint.Zero, in dstRect);
        }

        SDL.SetTextureColorMod(texture, 255, 255, 255);
        SDL.SetTextureAlphaMod(texture, 255);
    }

    /// <summary>Render pre-built colored geometry (no texture) in a single batched draw call.</summary>
    public void DrawGeometryScreen(SDL.Vertex[] vertices, int numVertices, int[] indices, int numIndices, nint? texture = null)
    {
        SDL.RenderGeometry(_renderer, texture ?? nint.Zero, vertices, numVertices, indices, numIndices);
    }

    public void DrawTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
    {
        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);
        SDL.RenderLine(_renderer, x1, y1, x2, y2);
        SDL.RenderLine(_renderer, x2, y2, x3, y3);
        SDL.RenderLine(_renderer, x3, y3, x1, y1);
    }

    // Reusable buffers for triangle rendering (avoids per-call allocs).
    private static SDL.Vertex[] _triBuf = new SDL.Vertex[3];
    private static readonly int[] TriIndices = [0, 1, 2];

    public void DrawFilledTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
    {
        var fcolor = new SDL.FColor
        {
            R = color.R / 255f,
            G = color.G / 255f,
            B = color.B / 255f,
            A = color.A / 255f
        };

        _triBuf[0] = new SDL.Vertex { Position = new SDL.FPoint { X = x1, Y = y1 }, Color = fcolor };
        _triBuf[1] = new SDL.Vertex { Position = new SDL.FPoint { X = x2, Y = y2 }, Color = fcolor };
        _triBuf[2] = new SDL.Vertex { Position = new SDL.FPoint { X = x3, Y = y3 }, Color = fcolor };

        DrawGeometryScreen(_triBuf, 3, TriIndices, 3);
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

    /// <summary>Ensure shared buffers are large enough for the given vertex/index counts.</summary>
    private static void EnsureBuffers(int requiredVerts, int requiredIndices)
    {
        if (_vertexBuf.Length < requiredVerts)
            _vertexBuf = new SDL.Vertex[requiredVerts];
        if (_indexBuf.Length < requiredIndices)
            _indexBuf = new int[requiredIndices];
    }

    public void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32)
    {
        if (segments < 3) segments = 3;

        float diameter = radius * 2f;

        // If the requested circle fits inside the cached texture, draw it using a textured quad
        if (diameter <= CachedCircleSize)
        {
            DrawTextureScreen(_cachedCircleTexture, cx, cy, diameter, diameter, color);
            return;
        }

        // Fallback: prepare vertices for a triangle fan
        SDL.FColor fcolor2 = new SDL.FColor
        {
            R = color.R / 255.0f,
            G = color.G / 255.0f,
            B = color.B / 255.0f,
            A = color.A / 255.0f
        };

        int requiredVerts = segments + 2;
        int requiredIndices = (segments + 1) * 3;
        EnsureBuffers(requiredVerts, requiredIndices);
        var v = _vertexBuf;
        var id = _indexBuf;

        v[0] = new SDL.Vertex
        {
            Position = new SDL.FPoint() { X = cx, Y = cy },
            Color = fcolor2,
        };

        float angleStep = MathF.PI * 2f / segments;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * angleStep;
            float x = cx + MathF.Cos(angle) * radius;
            float y = cy + MathF.Sin(angle) * radius;
            v[i + 1] = new SDL.Vertex
            {
                Position = new SDL.FPoint() { X = x, Y = y },
                Color = fcolor2,
            };
        }

        for (int i = 0; i < segments; i++)
        {
            id[i * 3 + 0] = 0;
            id[i * 3 + 1] = i + 1;
            id[i * 3 + 2] = i + 2;
        }

        DrawGeometryScreen(v, requiredVerts, id, requiredIndices);
    }

    /// <summary>Draw a solid ring (annulus) in screen space.</summary>
    public void DrawSolidRingScreen(float cx, float cy, float innerRadius, float outerRadius,
        Color4 color, int segments = 48)
    {
        if (segments < 3) segments = 3;
        if (outerRadius <= 0f) return;

        float inner = MathF.Max(0f, MathF.Min(innerRadius, outerRadius));
        if (inner <= 0f)
        {
            DrawFilledCircleScreen(cx, cy, outerRadius, color, segments);
            return;
        }

        int ringVerts = segments + 1;
        int totalVerts = ringVerts * 2;
        int totalIndices = segments * 6;

        EnsureBuffers(totalVerts, totalIndices);

        SDL.FColor fcolor = new SDL.FColor
        {
            R = color.R / 255f,
            G = color.G / 255f,
            B = color.B / 255f,
            A = color.A / 255f
        };

        float step = MathF.PI * 2f / segments;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * step;
            float cs = MathF.Cos(angle);
            float sn = MathF.Sin(angle);

            int innerIdx = i;
            int outerIdx = ringVerts + i;

            _vertexBuf[innerIdx] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = cx + cs * inner, Y = cy + sn * inner },
                Color = fcolor,
            };

            _vertexBuf[outerIdx] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = cx + cs * outerRadius, Y = cy + sn * outerRadius },
                Color = fcolor,
            };
        }

        int w = 0;
        for (int i = 0; i < segments; i++)
        {
            int i0 = i;
            int i1 = i + 1;
            int o0 = ringVerts + i;
            int o1 = ringVerts + i + 1;

            _indexBuf[w++] = i0;
            _indexBuf[w++] = o0;
            _indexBuf[w++] = i1;

            _indexBuf[w++] = i1;
            _indexBuf[w++] = o0;
            _indexBuf[w++] = o1;
        }

        DrawGeometryScreen(_vertexBuf, totalVerts, _indexBuf, totalIndices);
    }

    /// <summary>
    /// Draw a filled circle in screen space with a radial gradient.
    /// Color remains <paramref name="innerColor"/> from center to <paramref name="transitionStartRadius"/>,
    /// then transitions to <paramref name="outerColor"/> at <paramref name="radius"/>.
    /// </summary>
    public void DrawFilledCircleScreen(float cx, float cy, float radius,
        Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32)
    {
        if (radius <= 0f) return;
        if (segments < 3) segments = 3;

        float tRadius = Math.Clamp(transitionStartRadius, 0f, radius);

        if (tRadius >= radius ||
            (innerColor.R == outerColor.R && innerColor.G == outerColor.G &&
             innerColor.B == outerColor.B && innerColor.A == outerColor.A))
        {
            DrawFilledCircleScreen(cx, cy, radius, innerColor, segments);
            return;
        }

        // Special case: gradient from center directly to outer edge.
        if (tRadius <= 0f)
        {
            int requiredVerts = segments + 2;
            int requiredIndices = segments * 3;
            EnsureBuffers(requiredVerts, requiredIndices);

            SDL.FColor innerF = new SDL.FColor
            {
                R = innerColor.R / 255f,
                G = innerColor.G / 255f,
                B = innerColor.B / 255f,
                A = innerColor.A / 255f
            };
            SDL.FColor outerF = new SDL.FColor
            {
                R = outerColor.R / 255f,
                G = outerColor.G / 255f,
                B = outerColor.B / 255f,
                A = outerColor.A / 255f
            };

            _vertexBuf[0] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = cx, Y = cy },
                Color = innerF,
            };

            float angleStep = MathF.PI * 2f / segments;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * angleStep;
                float x = cx + MathF.Cos(angle) * radius;
                float y = cy + MathF.Sin(angle) * radius;
                _vertexBuf[i + 1] = new SDL.Vertex
                {
                    Position = new SDL.FPoint { X = x, Y = y },
                    Color = outerF,
                };
            }

            for (int i = 0; i < segments; i++)
            {
                _indexBuf[i * 3 + 0] = 0;
                _indexBuf[i * 3 + 1] = i + 1;
                _indexBuf[i * 3 + 2] = i + 2;
            }

            DrawGeometryScreen(_vertexBuf, requiredVerts, _indexBuf, requiredIndices);
            return;
        }

        // General case: inner solid disk + gradient annulus.
        int ringVerts = segments + 1;
        int totalVerts = 1 + ringVerts + ringVerts;
        int innerIndices = segments * 3;
        int annulusIndices = segments * 6;
        int totalIndices = innerIndices + annulusIndices;

        EnsureBuffers(totalVerts, totalIndices);

        SDL.FColor inner = new SDL.FColor
        {
            R = innerColor.R / 255f,
            G = innerColor.G / 255f,
            B = innerColor.B / 255f,
            A = innerColor.A / 255f
        };
        SDL.FColor outer = new SDL.FColor
        {
            R = outerColor.R / 255f,
            G = outerColor.G / 255f,
            B = outerColor.B / 255f,
            A = outerColor.A / 255f
        };

        _vertexBuf[0] = new SDL.Vertex
        {
            Position = new SDL.FPoint { X = cx, Y = cy },
            Color = inner,
        };

        float step = MathF.PI * 2f / segments;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * step;
            float cs = MathF.Cos(angle);
            float sn = MathF.Sin(angle);

            int innerRingIndex = 1 + i;
            int outerRingIndex = 1 + ringVerts + i;

            _vertexBuf[innerRingIndex] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = cx + cs * tRadius, Y = cy + sn * tRadius },
                Color = inner,
            };
            _vertexBuf[outerRingIndex] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = cx + cs * radius, Y = cy + sn * radius },
                Color = outer,
            };
        }

        int w = 0;
        // Inner fan
        for (int i = 0; i < segments; i++)
        {
            _indexBuf[w++] = 0;
            _indexBuf[w++] = 1 + i;
            _indexBuf[w++] = 1 + i + 1;
        }

        // Gradient annulus (two triangles per segment)
        int outerBase = 1 + ringVerts;
        for (int i = 0; i < segments; i++)
        {
            int i0 = 1 + i;
            int i1 = 1 + i + 1;
            int o0 = outerBase + i;
            int o1 = outerBase + i + 1;

            _indexBuf[w++] = i0;
            _indexBuf[w++] = o0;
            _indexBuf[w++] = i1;

            _indexBuf[w++] = i1;
            _indexBuf[w++] = o0;
            _indexBuf[w++] = o1;
        }

        DrawGeometryScreen(_vertexBuf, totalVerts, _indexBuf, totalIndices);
    }

    public void Dispose()
    {
        _textures.DestroyTexture(_cachedCircleTexture);
        _cachedCircleTexture = nint.Zero;

        _fontRenderer.Dispose();
        GC.SuppressFinalize(this);
    }

    private nint CreateCachedCircleTexture()
    {
        int w = CachedCircleSize;
        int h = CachedCircleSize;
        byte[] pixels = new byte[w * h * 4];
        float cx = w / 2f;
        float cy = h / 2f;
        float r = w / 2f;
        float r2 = r * r;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float dist2 = dx * dx + dy * dy;
                int idx = (y * w + x) * 4;
                if (dist2 <= r2)
                {
                    pixels[idx + 0] = 255; // R
                    pixels[idx + 1] = 255; // G
                    pixels[idx + 2] = 255; // B
                    pixels[idx + 3] = 255; // A
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

        // Use TextureManager helper to create a texture from pixel data.
        return _textures.CreateTextureFromPixels(pixels, w, h, SDL.ScaleMode.Nearest);
    }
}
