using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Per-layer atmosphere tint colours for a planet type.
/// <para>
/// <see cref="Inner"/> / <see cref="Mid"/> / <see cref="Outer"/> are canonical RGBA values
/// calibrated for the in-game <see cref="PlanetRenderer"/> atmospheric shell.
/// Callers that render at smaller scale (maps, UI panels) should scale the alpha down to taste.
/// </para>
/// </summary>
public readonly record struct AtmosphereLayerColors(
    Color4 Inner,
    Color4 Mid,
    Color4 Outer,
    /// <summary>True for planet types that sport a visible atmospheric shell during in-game rendering.</summary>
    bool HasInGameAtmosphere)
{
    /// <summary>Sentinel returned for planet types with no defined atmosphere colours.</summary>
    public static readonly AtmosphereLayerColors None = default;

    /// <summary>True when no atmosphere colours are defined (all channels are zero).</summary>
    public bool IsEmpty => Inner.A == 0 && Outer.A == 0;
}

/// <summary>
/// Canonical atmosphere halo colours for each <see cref="PlanetType"/>.
/// <para>
/// All rendering code that draws planet atmosphere glows (in-game <see cref="PlanetRenderer"/>,
/// solar-system map, landing panel, and surface-map panel) should use this class to ensure a
/// consistent visual style across contexts.
/// </para>
/// </summary>
public static class PlanetAtmosphereColors
{
    // ── Lookup table ────────────────────────────────────────────────────────
    //  Alpha values are calibrated for the detail-level of the in-game
    //  atmospheric shell.  Map / UI callers scale them down as needed.

    /// <summary>Returns the canonical atmosphere colours for <paramref name="type"/>.</summary>
    public static AtmosphereLayerColors Get(PlanetType type) => type switch
    {
        // Worlds with thick, visible atmospheres – rendered both in-game and on maps
        PlanetType.Terrestrial => new(
            Inner: new Color4(170, 215, 255, 80),
            Mid: new Color4(120, 180, 245, 48),
            Outer: new Color4(90, 145, 220, 24),
            HasInGameAtmosphere: true),

        PlanetType.Ocean => new(
            Inner: new Color4(150, 210, 255, 86),
            Mid: new Color4(110, 175, 245, 54),
            Outer: new Color4(85, 140, 220, 28),
            HasInGameAtmosphere: true),

        PlanetType.Frozen => new(
            Inner: new Color4(200, 235, 255, 70),
            Mid: new Color4(160, 210, 245, 45),
            Outer: new Color4(120, 180, 225, 24),
            HasInGameAtmosphere: true),

        PlanetType.GasGiant => new(
            Inner: new Color4(235, 215, 170, 48),
            Mid: new Color4(220, 190, 135, 34),
            Outer: new Color4(190, 150, 95, 22),
            HasInGameAtmosphere: true),

        PlanetType.IceGiant => new(
            Inner: new Color4(170, 220, 255, 52),
            Mid: new Color4(130, 185, 245, 36),
            Outer: new Color4(95, 150, 220, 22),
            HasInGameAtmosphere: true),

        // Worlds with thin / dusty exospheres – shown only as subtle map halos
        PlanetType.Desert => new(
            Inner: new Color4(220, 180, 100, 52),
            Mid: new Color4(205, 160, 85, 34),
            Outer: new Color4(190, 140, 70, 20),
            HasInGameAtmosphere: false),

        PlanetType.Volcanic => new(
            Inner: new Color4(255, 140, 60, 65),
            Mid: new Color4(240, 110, 45, 42),
            Outer: new Color4(220, 80, 30, 26),
            HasInGameAtmosphere: false),

        // Rocky worlds with only the faintest wisp of glow
        PlanetType.Rocky => new(
            Inner: new Color4(160, 155, 150, 32),
            Mid: new Color4(140, 135, 130, 20),
            Outer: new Color4(120, 115, 110, 12),
            HasInGameAtmosphere: false),

        _ => AtmosphereLayerColors.None,
    };
}
