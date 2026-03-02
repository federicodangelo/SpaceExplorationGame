using System.Numerics;
using Engine.Core;

namespace Engine.Platform;

/// <summary>
/// Abstraction for text rendering using bitmap fonts.
/// </summary>
public interface IFontRenderer : IDisposable
{
    void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f);
    void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f);
    float MeasureText(string text, float scale = 1f);
}
