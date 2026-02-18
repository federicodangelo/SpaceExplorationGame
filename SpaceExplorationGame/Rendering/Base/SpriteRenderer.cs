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

    // ── Font atlas ──────────────────────────────────────────────────────
    private nint _fontAtlas;
    private int _atlasWidth;
    private int _atlasHeight;
    // Maps each defined character to its column index in the atlas row.
    private readonly Dictionary<char, int> _glyphIndex = [];
    // Pre-computed normalized UV coordinates per glyph: (u0, v0, u1, v1)
    private readonly Dictionary<char, (float U0, float V0, float U1, float V1)> _glyphUV = [];
    // Reusable buffers for SDL.RenderGeometry batching (avoids per-call allocs).
    private SDL.Vertex[] _vertexBuf = new SDL.Vertex[256]; // grows as needed
    private int[] _indexBuf = new int[384];                  // grows as needed

    public SpriteRenderer(nint renderer)
    {
        _renderer = renderer;
        // Enable alpha blending so draw calls with a < 255 are translucent
        SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);
        BuildFontAtlas();
    }

    // ── Font atlas construction ─────────────────────────────────────────
    /// <summary>Builds a single texture containing every defined glyph (white on transparent).</summary>
    private void BuildFontAtlas()
    {
        var glyphs = MiniBitmapFont.GetAllGlyphs();
        int count = glyphs.Count;
        if (count == 0) return;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;

        // Layout: one row, all glyphs side by side (with 1px padding to avoid bleeding)
        int cellW = gw + 1;
        _atlasWidth = cellW * count;
        _atlasHeight = gh;

        var pixels = new byte[_atlasWidth * _atlasHeight * 4];

        int col = 0;
        foreach (var (ch, data) in glyphs)
        {
            _glyphIndex[ch] = col;

            int baseX = col * cellW;
            for (int py = 0; py < gh; py++)
            {
                for (int px = 0; px < gw; px++)
                {
                    if (data[py * gw + px])
                    {
                        int idx = ((py * _atlasWidth) + baseX + px) * 4;
                        pixels[idx + 0] = 255; // R
                        pixels[idx + 1] = 255; // G
                        pixels[idx + 2] = 255; // B
                        pixels[idx + 3] = 255; // A
                    }
                }
            }
            col++;
        }

        // Create SDL texture from pixel data
        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                var surface = SDL.CreateSurfaceFrom(_atlasWidth, _atlasHeight,
                    SDL.PixelFormat.ABGR8888, (nint)ptr, _atlasWidth * 4);
                _fontAtlas = SDL.CreateTextureFromSurface(_renderer, surface);
                SDL.DestroySurface(surface);
            }
        }

        SDL.SetTextureBlendMode(_fontAtlas, SDL.BlendMode.Blend);
        SDL.SetTextureScaleMode(_fontAtlas, SDL.ScaleMode.Nearest);

        // Pre-compute normalized UVs for each glyph
        float invW = 1f / _atlasWidth;
        float invH = 1f / _atlasHeight;
        foreach (var (ch, idx) in _glyphIndex)
        {
            float u0 = (idx * cellW) * invW;
            float v0 = 0f;
            float u1 = (idx * cellW + gw) * invW;
            float v1 = gh * invH;
            _glyphUV[ch] = (u0, v0, u1, v1);
        }
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

    /// <summary>Draw text as simple pixel blocks (very basic bitmap font).</summary>
    public void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f)
    {
        var screenPos = camera.WorldToScreen(worldPos);
        DrawTextScreen(screenPos.X, screenPos.Y, text, color, scale);
    }

    /// <summary>Draw text in screen space using a font atlas and a single batched draw call.</summary>
    public void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f)
    {
        if (_fontAtlas == nint.Zero || text.Length == 0) return;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;
        float charWidth = (gw + 1) * scale; // +1 matches the original 6px advance
        float charHeight = gh * scale;

        // Count visible (non-space) characters that have glyphs
        int visibleCount = 0;
        foreach (char c in text)
        {
            if (c != ' ' && _glyphUV.ContainsKey(c))
                visibleCount++;
        }

        if (visibleCount == 0) return;

        // Ensure buffers are large enough (4 verts + 6 indices per quad)
        int requiredVerts = visibleCount * 4;
        int requiredIndices = visibleCount * 6;
        if (_vertexBuf.Length < requiredVerts)
            _vertexBuf = new SDL.Vertex[requiredVerts];
        if (_indexBuf.Length < requiredIndices)
            _indexBuf = new int[requiredIndices];

        // Vertex color (SDL.Vertex uses FColor: 0-1 floats)
        var fcolor = new SDL.FColor
        {
            R = color.R / 255f,
            G = color.G / 255f,
            B = color.B / 255f,
            A = color.A / 255f
        };

        float cursorX = x;
        int vi = 0; // vertex write index
        int ii = 0; // index write index

        foreach (char c in text)
        {
            if (c == ' ')
            {
                cursorX += charWidth;
                continue;
            }

            if (!_glyphUV.TryGetValue(c, out var uv))
            {
                cursorX += charWidth;
                continue;
            }

            int baseVertex = vi;

            // Top-left
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = cursorX, Y = y },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U0, Y = uv.V0 }
            };
            // Top-right
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = cursorX + gw * scale, Y = y },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U1, Y = uv.V0 }
            };
            // Bottom-right
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = cursorX + gw * scale, Y = y + charHeight },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U1, Y = uv.V1 }
            };
            // Bottom-left
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = cursorX, Y = y + charHeight },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U0, Y = uv.V1 }
            };

            // Two triangles: 0-1-2, 0-2-3
            _indexBuf[ii++] = baseVertex;
            _indexBuf[ii++] = baseVertex + 1;
            _indexBuf[ii++] = baseVertex + 2;
            _indexBuf[ii++] = baseVertex;
            _indexBuf[ii++] = baseVertex + 2;
            _indexBuf[ii++] = baseVertex + 3;

            cursorX += charWidth;
        }

        // Single draw call for the entire string
        SDL.RenderGeometry(_renderer, _fontAtlas, _vertexBuf, vi, _indexBuf, ii);
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
        if (_fontAtlas != nint.Zero)
        {
            SDL.DestroyTexture(_fontAtlas);
            _fontAtlas = nint.Zero;
        }
        foreach (var tex in _textures)
        {
            SDL.DestroyTexture(tex);
        }
        _textures.Clear();
        GC.SuppressFinalize(this);
    }
}
