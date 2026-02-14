namespace SpaceExplorationGame.Core;

/// <summary>
/// Avatar equipment slot types.
/// </summary>
public enum AvatarSlotType
{
    Suit,
    Helmet,
    Boots
}

/// <summary>
/// An avatar part that can be installed in a slot.
/// </summary>
public record AvatarPart(
    string Id,
    string Name,
    AvatarSlotType Slot,
    int Tier,          // 1 = basic, 2 = improved, 3 = advanced
    int BuyCost,
    int SellValue,
    string Description,
    AvatarPartStats Stats
) : ICustomizablePart;

/// <summary>
/// Stat modifiers provided by an avatar part.
/// All values are additive to the avatar's base stats.
/// </summary>
public record AvatarPartStats(
    float WalkSpeed = 0f,          // added to base walk speed
    float OxygenCapacity = 0f,     // future: time before suffocation
    float TerrainPenalty = 0f      // reduction in terrain slowdown (0.1 = 10% less penalty)
);

/// <summary>
/// Catalog of all available avatar parts.
/// </summary>
public static class AvatarPartCatalog
{
    public static readonly AvatarPart[] AllParts =
    [
        // ── Suits ────────────────────────────────────────────────
        new("suit_basic",    "Standard Suit",     AvatarSlotType.Suit, 1, 0,   20,
            "Basic EVA suit. Adequate protection.",
            new AvatarPartStats(WalkSpeed: 0f)),

        new("suit_improved", "Reinforced Suit",   AvatarSlotType.Suit, 2, 200, 100,
            "Armored suit with mobility enhancements.",
            new AvatarPartStats(WalkSpeed: 30f)),

        new("suit_advanced", "Nano-Fiber Suit",   AvatarSlotType.Suit, 3, 500, 250,
            "Cutting-edge suit. Lightweight and fast.",
            new AvatarPartStats(WalkSpeed: 70f)),

        // ── Helmets ──────────────────────────────────────────────
        new("helmet_basic",    "Standard Visor",   AvatarSlotType.Helmet, 1, 0,   15,
            "Basic helmet with minimal HUD.",
            new AvatarPartStats(OxygenCapacity: 100f)),

        new("helmet_improved", "Enhanced Visor",   AvatarSlotType.Helmet, 2, 180, 90,
            "Improved optics and extended O2 supply.",
            new AvatarPartStats(OxygenCapacity: 180f)),

        new("helmet_advanced", "Tactical Helmet",  AvatarSlotType.Helmet, 3, 450, 225,
            "Full tactical HUD with max O2 capacity.",
            new AvatarPartStats(OxygenCapacity: 300f)),

        // ── Boots ────────────────────────────────────────────────
        new("boots_basic",    "Standard Boots",    AvatarSlotType.Boots, 1, 0,   15,
            "Basic footwear. Functional.",
            new AvatarPartStats(TerrainPenalty: 0f)),

        new("boots_improved", "Trekker Boots",     AvatarSlotType.Boots, 2, 150, 75,
            "Rugged boots for rough terrain.",
            new AvatarPartStats(TerrainPenalty: 0.15f, WalkSpeed: 15f)),

        new("boots_advanced", "Grav-Boots",        AvatarSlotType.Boots, 3, 400, 200,
            "Anti-grav soles. Glide over any surface.",
            new AvatarPartStats(TerrainPenalty: 0.35f, WalkSpeed: 40f)),
    ];

    /// <summary>Find a part by its ID.</summary>
    public static AvatarPart? GetById(string id) =>
        Array.Find(AllParts, p => p.Id == id);

    /// <summary>Get all parts that fit a given slot type.</summary>
    public static AvatarPart[] GetPartsForSlot(AvatarSlotType slot) =>
        Array.FindAll(AllParts, p => p.Slot == slot);

    /// <summary>Get the default starter loadout.</summary>
    public static Dictionary<AvatarSlotType, AvatarPart> GetStarterLoadout() => new()
    {
        [AvatarSlotType.Suit]   = GetById("suit_basic")!,
        [AvatarSlotType.Helmet] = GetById("helmet_basic")!,
        [AvatarSlotType.Boots]  = GetById("boots_basic")!,
    };

    /// <summary>Slot display names.</summary>
    public static string GetSlotName(AvatarSlotType slot) => slot switch
    {
        AvatarSlotType.Suit   => "SUIT",
        AvatarSlotType.Helmet => "HELMET",
        AvatarSlotType.Boots  => "BOOTS",
        _ => slot.ToString().ToUpper()
    };
}
