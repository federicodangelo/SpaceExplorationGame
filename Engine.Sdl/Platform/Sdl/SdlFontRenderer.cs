using SDL3;
using Engine.Core;
using Engine.Rendering.Base;
using System.Numerics;

namespace Engine.Platform.Sdl;

/// <summary>
/// Renders text using pre-built multi-scale font atlas textures.
/// Each atlas contains every glyph rasterized at a specific scale for crisp pixel-perfect rendering.
/// Text is drawn in a single batched SDL.RenderGeometry call per string.
/// </summary>
public class SdlFontRenderer : BaseFontRenderer
{
    private readonly nint _renderer;

    // Reusable buffers for SDL.RenderGeometry batching (avoids per-call allocs).
    private SDL.Vertex[] _vertexBuf = new SDL.Vertex[256]; // grows as needed
    private int[] _indexBuf = new int[384];                  // grows as needed

    public SdlFontRenderer(nint renderer, SdlTextureManager textures)
        : base(textures)
    {
        _renderer = renderer;
    }

    /// <summary>Draw text in screen space using a pre-scaled font atlas and a single batched draw call.</summary>
    public override void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f)
    {
        if (_fontAtlases.Length == 0 || text.Length == 0) return;

        ref var atlas = ref PickAtlas(scale);
        var glyphUV = atlas.GlyphUV;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;
        float charAdvance = (gw + 1) * scale; // 6px logical advance scaled
        float drawW = gw * scale;
        float drawH = gh * scale;

        // Count visible (non-space) characters that have glyphs
        int visibleCount = 0;
        foreach (char c in text)
        {
            if (c != ' ' && glyphUV.ContainsKey(c))
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

        // Snap starting position to integer pixels for crisp rendering
        float cursorX = MathF.Round(x);
        float snappedY = MathF.Round(y);
        int snappedDrawW = (int)MathF.Round(drawW);
        int snappedDrawH = (int)MathF.Round(drawH);
        int snappedAdvance = (int)MathF.Round(charAdvance);
        int vi = 0; // vertex write index
        int ii = 0; // index write index

        foreach (char c in text)
        {
            if (c == ' ')
            {
                cursorX += snappedAdvance;
                continue;
            }

            if (!glyphUV.TryGetValue(c, out var uv))
            {
                cursorX += snappedAdvance;
                continue;
            }

            int baseVertex = vi;
            float rx = MathF.Round(cursorX);

            // Top-left
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = rx, Y = snappedY },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U0, Y = uv.V0 }
            };
            // Top-right
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = rx + snappedDrawW, Y = snappedY },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U1, Y = uv.V0 }
            };
            // Bottom-right
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = rx + snappedDrawW, Y = snappedY + snappedDrawH },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U1, Y = uv.V1 }
            };
            // Bottom-left
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = rx, Y = snappedY + snappedDrawH },
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

            cursorX += snappedAdvance;
        }

        // Single draw call for the entire string
        SDL.RenderGeometry(_renderer, atlas.Texture, _vertexBuf, vi, _indexBuf, ii);
    }

}
