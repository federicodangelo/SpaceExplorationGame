namespace SpaceExplorationGame.Core;

/// <summary>
/// Avatar equipment slot types.
/// </summary>
public enum AvatarSlotType
{
    Suit,
    Helmet,
    Boots,
    Weapon
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
    float TerrainPenalty = 0f,     // reduction in terrain slowdown (0.1 = 10% less penalty)
    float WeaponDamage = 0f,       // bonus weapon damage on planet surface
    float Armor = 0f,              // bonus avatar max health
    WeaponBehavior WeaponBehavior = WeaponBehavior.Standard,
    int MaxAmmo = -1               // max ammo capacity (-1 = infinite)
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

        // ── Weapons ──────────────────────────────────────────────
        new("weapon_basic",    "Sidearm",           AvatarSlotType.Weapon, 1, 0,   20,
            "Standard issue pistol. Infinite ammo.",
            new AvatarPartStats(WeaponDamage: 0f, WeaponBehavior: WeaponBehavior.Standard, MaxAmmo: -1)),

        new("weapon_improved", "Pulse Rifle",       AvatarSlotType.Weapon, 2, 300, 150,
            "Rapid-fire spread weapon. Higher damage.",
            new AvatarPartStats(WeaponDamage: 8f, WeaponBehavior: WeaponBehavior.Spread, MaxAmmo: 60)),

        new("weapon_advanced", "Plasma Cannon",     AvatarSlotType.Weapon, 3, 700, 350,
            "Heavy plasma weapon. Devastating firepower but limited ammo.",
            new AvatarPartStats(WeaponDamage: 20f, WeaponBehavior: WeaponBehavior.Standard, MaxAmmo: 20)),
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
        [AvatarSlotType.Suit] = GetById("suit_basic")!,
        [AvatarSlotType.Helmet] = GetById("helmet_basic")!,
        [AvatarSlotType.Boots] = GetById("boots_basic")!,
        [AvatarSlotType.Weapon] = GetById("weapon_basic")!,
    };

    /// <summary>Slot display names.</summary>
    public static string GetSlotName(AvatarSlotType slot) => slot switch
    {
        AvatarSlotType.Suit => "SUIT",
        AvatarSlotType.Helmet => "HELMET",
        AvatarSlotType.Boots => "BOOTS",
        AvatarSlotType.Weapon => "WEAPON",
        _ => slot.ToString().ToUpper()
    };
}
