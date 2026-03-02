using System.Numerics;

namespace Engine.Core;

// ── Engine-level types used by platform interfaces ───────────────

/// <summary>An RGB color with byte components.</summary>
public readonly record struct Color3(byte R, byte G, byte B)
{
    /// <summary>Creates a <see cref="Color4"/> from this color with the specified alpha.</summary>
    public Color4 WithAlpha(byte a) => new(R, G, B, a);
}

/// <summary>An RGBA color with byte components.</summary>
public readonly record struct Color4(byte R, byte G, byte B, byte A)
{
    /// <summary>Implicit conversion from <see cref="Color3"/> to <see cref="Color4"/> with full opacity (A = 255).</summary>
    public static implicit operator Color4(Color3 c) => new(c.R, c.G, c.B, 255);

    /// <summary>Returns the RGB portion of this color.</summary>
    public Color3 Rgb => new(R, G, B);

    public Color4 WithAlpha(byte a) => new(R, G, B, a);
}

/// <summary>A float axis-aligned rectangle (position + size).</summary>
public readonly record struct Rect(float X, float Y, float W, float H);

/// <summary>Visible world-space bounds (top-left and bottom-right corners).</summary>
public readonly record struct VisibleBounds(Vector2 TopLeft, Vector2 BottomRight);
