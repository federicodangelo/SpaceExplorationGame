namespace SpaceExplorationGame.Core;

/// <summary>
/// Types of mineable resources found in asteroids.
/// </summary>
public enum ResourceType
{
    Iron,
    Nickel,
    Gold,
    Platinum,
    Crystal,
    Ice
}

/// <summary>
/// Describes a resource type's properties: name, color, base value per unit.
/// </summary>
public static class ResourceCatalog
{
    public record ResourceInfo(
        ResourceType Type,
        string Name,
        int ValuePerUnit,    // credits per unit when sold
        byte R, byte G, byte B  // display color
    );

    public static readonly ResourceInfo[] AllResources =
    [
        new(ResourceType.Iron,     "Iron",     5,   180, 140, 100),
        new(ResourceType.Nickel,   "Nickel",   8,   160, 170, 170),
        new(ResourceType.Gold,     "Gold",     20,  255, 220, 80),
        new(ResourceType.Platinum, "Platinum", 35,  210, 220, 240),
        new(ResourceType.Crystal,  "Crystal",  50,  120, 200, 255),
        new(ResourceType.Ice,      "Ice",      3,   200, 230, 255),
    ];

    public static ResourceInfo Get(ResourceType type) =>
        AllResources[(int)type];
}

/// <summary>
/// Data about an individual asteroid that can be mined.
/// </summary>
public class MineableAsteroid
{
    public float BaseAngle { get; set; }
    public float Radius { get; set; }      // orbit radius from center
    public float Speed { get; set; }       // orbit speed
    public float Size { get; set; }        // visual size
    public float MaxHp { get; set; }       // starting HP
    public float Hp { get; set; }          // current HP
    public ResourceType Resource { get; set; }
    public int ResourceAmount { get; set; } // units to drop on depletion
    public bool Depleted { get; set; }
}

/// <summary>
/// A stack of a single resource type in the player's cargo.
/// </summary>
public record CargoItem(ResourceType Resource, int Amount);
