namespace SpaceExplorationGame.Generation;

/// <summary>
/// Shared validation rules for planet surface terrain types.
/// Keeps walkability/landing/spawn checks consistent across systems.
/// </summary>
public static class SurfaceTerrainRules
{
    public static bool IsVoid(TerrainType terrain) => terrain == TerrainType.Void;

    /// <summary>Tiles the player/NPC cannot traverse on the surface.</summary>
    public static bool IsBlockedForTraversal(TerrainType terrain) =>
        terrain is TerrainType.Water or TerrainType.Lava or TerrainType.Void or TerrainType.Settlement;

    /// <summary>Tiles valid for movement, landing, and generic surface targeting.</summary>
    public static bool IsTraversable(TerrainType terrain) => !IsBlockedForTraversal(terrain);

    /// <summary>
    /// Tiles that should be replaced when forcing a walkable area.
    /// Keeps Void untouched to preserve world boundaries.
    /// </summary>
    public static bool IsReplaceableForWalkableArea(TerrainType terrain) =>
        terrain is TerrainType.Water or TerrainType.Lava;
}
