
namespace SpaceExplorationGame.Rendering.Base;

/// <summary>
/// Shared rendering colors and constants used across multiple renderers.
/// </summary>
public static class RenderColors
{
    /// <summary>Dark semi-transparent background for health/shield bars.</summary>
    public static readonly Color4 HealthBarBackground = new(40, 40, 40, 180);

    /// <summary>Darker semi-transparent background for shield bars.</summary>
    public static readonly Color4 ShieldBarBackground = new(40, 40, 60, 180);

    /// <summary>Blue fill for shield bars.</summary>
    public static readonly Color4 ShieldBarFill = new(80, 160, 255, 200);

    /// <summary>Warm-white core highlight applied to stars.</summary>
    public static readonly Color4 StarCoreHighlight = new(255, 245, 220, 255);

    /// <summary>Subtle shadow beneath NPCs and surface entities.</summary>
    public static readonly Color4 EntityShadow = new(0, 0, 0, 60);

    /// <summary>
    /// Compute hull bar fill color: green when healthy, red when damaged.
    /// Crossover at 50% HP.
    /// </summary>
    public static Color4 HullFillColor(float hullPercent, byte alpha = 200)
    {
        byte r = hullPercent > 0.5f ? (byte)(255 * (1 - hullPercent) * 2) : (byte)255;
        byte g = hullPercent > 0.5f ? (byte)255 : (byte)(255 * hullPercent * 2);
        return new Color4(r, g, 0, alpha);
    }
}
