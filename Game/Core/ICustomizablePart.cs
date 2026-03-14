namespace SpaceExplorationGame.Core;

/// <summary>Item rarity for color-coded UI and drop weighting.</summary>
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Legendary
}

/// <summary>
/// Common interface for all customizable equipment parts (ship, avatar, vehicle).
/// </summary>
public interface ICustomizablePart
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    int Tier { get; }
    int BuyCost { get; }
    int SellValue { get; }
    Rarity Rarity { get; }
}
