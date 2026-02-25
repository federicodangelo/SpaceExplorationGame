namespace SpaceExplorationGame.Core;

/// <summary>
/// A ship type template that defines the hull's characteristics and available slots.
/// </summary>
public record ShipType(
    string Id,
    string Name,
    string Description,
    ShipSlotType[] AvailableSlots,
    int SpriteSize,       // pixels (32 = small, 40 = medium, 48 = large)
    float Weight,         // multiplier on acceleration/maxSpeed (1.0 = baseline)
    float BaseHull,       // base hull points before armor parts
    float BaseFuel,       // base fuel capacity before FTL drive parts
    int BuyCost,          // credits to purchase (0 = starter ship, free)
    int SellValue,        // credits received when trading in
    float BaseCargo = 50f // base cargo capacity before utility part bonuses
);

/// <summary>
/// Catalog of available ship types.
/// </summary>
public static class ShipTypeCatalog
{
    public static readonly ShipType Scout = new(
        "scout", "Scout", "Light reconnaissance craft. Fast and nimble but limited.",
        [ShipSlotType.Engine, ShipSlotType.Shield, ShipSlotType.FtlDrive, ShipSlotType.Utility, ShipSlotType.Weapon1],
        SpriteSize: 32, Weight: 1.0f, BaseHull: 80f, BaseFuel: 80f,
        BuyCost: 0, SellValue: 200, BaseCargo: 40f
    );

    public static readonly ShipType Fighter = new(
        "fighter", "Fighter", "Combat vessel with dual weapon mounts. Tough but short-range.",
        [ShipSlotType.Engine, ShipSlotType.Armor, ShipSlotType.Shield, ShipSlotType.Weapon1, ShipSlotType.Weapon2],
        SpriteSize: 32, Weight: 1.1f, BaseHull: 120f, BaseFuel: 60f,
        BuyCost: 1500, SellValue: 750, BaseCargo: 30f
    );

    public static readonly ShipType Freighter = new(
        "freighter", "Freighter", "Heavy hauler with extra cargo space. Slow but durable.",
        [ShipSlotType.Engine, ShipSlotType.Armor, ShipSlotType.FtlDrive, ShipSlotType.Utility, ShipSlotType.Utility2, ShipSlotType.Weapon1],
        SpriteSize: 48, Weight: 1.4f, BaseHull: 200f, BaseFuel: 160f,
        BuyCost: 3000, SellValue: 1500, BaseCargo: 120f
    );

    public static readonly ShipType Explorer = new(
        "explorer", "Explorer", "Versatile deep-space vessel. All slots, balanced performance.",
        [ShipSlotType.Engine, ShipSlotType.Armor, ShipSlotType.Shield, ShipSlotType.FtlDrive,
         ShipSlotType.Weapon1, ShipSlotType.Weapon2, ShipSlotType.Utility],
        SpriteSize: 40, Weight: 1.2f, BaseHull: 150f, BaseFuel: 140f,
        BuyCost: 5000, SellValue: 2500, BaseCargo: 80f
    );

    public static readonly ShipType[] AllTypes = [Scout, Fighter, Freighter, Explorer];

    public static ShipType? GetById(string id) =>
        Array.Find(AllTypes, t => t.Id == id);

    /// <summary>The player starts with a Scout.</summary>
    public static ShipType StarterShip => Scout;
}

/// <summary>
/// Ship equipment slot types.
/// </summary>
public enum ShipSlotType
{
    Engine,
    Armor,
    Shield,
    FtlDrive,
    Weapon1,
    Weapon2,
    Utility,
    Utility2
}

/// <summary>
/// A ship part that can be installed in a slot.
/// </summary>
public record ShipPart(
    string Id,
    string Name,
    ShipSlotType Slot,
    int Tier,          // 1 = basic, 2 = improved, 3 = advanced
    int BuyCost,
    int SellValue,
    string Description,
    ShipPartStats Stats
) : ICustomizablePart;

/// <summary>
/// Stat modifiers provided by a ship part.
/// All values are additive to the ship's base stats.
/// </summary>
public record ShipPartStats(
    float Acceleration = 0f,
    float MaxSpeed = 0f,
    float RotationSpeed = 0f,
    float MaxHull = 0f,
    float MaxFuel = 0f,
    float FtlRange = 0f,
    float ShieldStrength = 0f,   // future: damage absorption
    float WeaponDamage = 0f,     // weapon damage / mining beam DPS
    float WeaponFireRate = 0f,   // seconds between shots
    float WeaponRange = 0f,      // weapon effective range
    float ProjectileSpeed = 0f,  // weapon projectile speed
    float FuelEfficiency = 0f,   // multiplier reduction on fuel cost (0.1 = 10% less fuel)
    float CargoCapacity = 0f     // bonus cargo capacity (added to ship base)
);

/// <summary>
/// Catalog of all available ship parts.
/// </summary>
public static class ShipPartCatalog
{
    public static readonly ShipPart[] AllParts =
    [
        // ── Engines ──────────────────────────────────────────────
        new("engine_basic",    "Basic Thruster",     ShipSlotType.Engine, 1, 0,   25,
            "Standard issue thruster. Gets the job done.",
            new ShipPartStats(Acceleration: 200f, MaxSpeed: 800f, RotationSpeed: 180f)),

        new("engine_improved", "Ion Drive",          ShipSlotType.Engine, 2, 300, 150,
            "Efficient ion propulsion with better thrust.",
            new ShipPartStats(Acceleration: 280f, MaxSpeed: 1000f, RotationSpeed: 200f)),

        new("engine_advanced", "Plasma Thruster",    ShipSlotType.Engine, 3, 800, 400,
            "High-performance plasma drive. Fast and agile.",
            new ShipPartStats(Acceleration: 380f, MaxSpeed: 1300f, RotationSpeed: 240f)),

        // ── Armor ────────────────────────────────────────────────
        new("armor_basic",     "Light Plating",      ShipSlotType.Armor,  1, 0,   20,
            "Thin hull plates. Minimal protection.",
            new ShipPartStats(MaxHull: 100f)),

        new("armor_improved",  "Composite Armor",    ShipSlotType.Armor,  2, 250, 125,
            "Layered composite provides solid protection.",
            new ShipPartStats(MaxHull: 180f)),

        new("armor_advanced",  "Nano-Weave Hull",    ShipSlotType.Armor,  3, 700, 350,
            "Self-repairing nano-material hull plating.",
            new ShipPartStats(MaxHull: 300f)),

        // ── Shields ──────────────────────────────────────────────
        new("shield_basic",    "Deflector Screen",   ShipSlotType.Shield, 1, 0,   30,
            "Basic energy shield. Absorbs light damage.",
            new ShipPartStats(ShieldStrength: 30f)),

        new("shield_improved", "Barrier Generator",  ShipSlotType.Shield, 2, 350, 175,
            "Multi-layer barrier for moderate protection.",
            new ShipPartStats(ShieldStrength: 70f)),

        new("shield_advanced", "Fortress Shield",    ShipSlotType.Shield, 3, 900, 450,
            "Military-grade shield generator.",
            new ShipPartStats(ShieldStrength: 130f)),

        // ── FTL Drives ───────────────────────────────────────────
        new("ftl_basic",       "Warp Coil Mk I",    ShipSlotType.FtlDrive, 1, 0,   40,
            "Entry-level FTL drive. Short range jumps.",
            new ShipPartStats(FtlRange: 2500f * 5.0f, MaxFuel: 100f)),

        new("ftl_improved",    "Warp Coil Mk II",   ShipSlotType.FtlDrive, 2, 400, 200,
            "Extended range with better fuel capacity.",
            new ShipPartStats(FtlRange: 4000f * 5.0f, MaxFuel: 160f)),

        new("ftl_advanced",    "Hyperdrive",         ShipSlotType.FtlDrive, 3, 1200, 600,
            "Top-tier FTL system. Galaxy-spanning range.",
            new ShipPartStats(FtlRange: 6000f * 5.0f, MaxFuel: 250f)),

        // ── Weapons ──────────────────────────────────────────────
        new("weapon_none",     "(Empty)",            ShipSlotType.Weapon1, 0, 0, 0,
            "No weapon installed.",
            new ShipPartStats()),

        new("weapon_laser",    "Pulse Laser",        ShipSlotType.Weapon1, 1, 0,   35,
            "Basic energy weapon. Low damage, no ammo.",
            new ShipPartStats(WeaponDamage: 5f, WeaponFireRate: 0.5f, WeaponRange: 320f, ProjectileSpeed: 600f)),

        new("weapon_cannon",   "Kinetic Cannon",     ShipSlotType.Weapon1, 2, 350, 175,
            "Ballistic rounds with solid punch.",
            new ShipPartStats(WeaponDamage: 7f, WeaponFireRate: 0.75f, WeaponRange: 300f, ProjectileSpeed: 520f)),

        new("weapon_missile",  "Missile Launcher",   ShipSlotType.Weapon1, 3, 750, 375,
            "Guided missiles. High damage, slow fire rate.",
            new ShipPartStats(WeaponDamage: 10f, WeaponFireRate: 1.0f, WeaponRange: 420f, ProjectileSpeed: 420f)),

        // ── Utility ──────────────────────────────────────────────
        new("util_none",       "(Empty)",            ShipSlotType.Utility, 0, 0, 0,
            "No utility module installed.",
            new ShipPartStats()),

        new("util_efficiency", "Fuel Optimizer",     ShipSlotType.Utility, 2, 300, 150,
            "Reduces fuel consumption by 20%.",
            new ShipPartStats(FuelEfficiency: 0.2f)),

        new("util_booster",    "Afterburner",        ShipSlotType.Utility, 3, 600, 300,
            "Emergency speed boost. +100 max speed.",
            new ShipPartStats(MaxSpeed: 100f, Acceleration: 50f)),

        // ── Cargo ────────────────────────────────────────────────
        new("util_cargo_small", "Cargo Pod",          ShipSlotType.Utility, 1, 200, 100,
            "Small cargo expansion. +30 capacity.",
            new ShipPartStats(CargoCapacity: 30f)),

        new("util_cargo_large", "Cargo Bay",          ShipSlotType.Utility, 2, 500, 250,
            "Large cargo expansion. +80 capacity.",
            new ShipPartStats(CargoCapacity: 80f)),
    ];

    /// <summary>Find a part by its ID.</summary>
    public static ShipPart? GetById(string id) =>
        Array.Find(AllParts, p => p.Id == id);

    /// <summary>Get all parts that fit a given slot type.</summary>
    public static ShipPart[] GetPartsForSlot(ShipSlotType slot) =>
        slot switch
        {
            // Weapon slots can use any weapon part
            ShipSlotType.Weapon1 or ShipSlotType.Weapon2 =>
                Array.FindAll(AllParts, p => p.Slot is ShipSlotType.Weapon1 or ShipSlotType.Weapon2),
            ShipSlotType.Utility or ShipSlotType.Utility2 =>
                Array.FindAll(AllParts, p => p.Slot is ShipSlotType.Utility or ShipSlotType.Utility2),
            _ => Array.FindAll(AllParts, p => p.Slot == slot)
        };

    /// <summary>Get the default starter loadout for a given ship type.</summary>
    public static Dictionary<ShipSlotType, ShipPart> GetStarterLoadout(ShipType shipType)
    {
        var loadout = new Dictionary<ShipSlotType, ShipPart>();
        foreach (var slot in shipType.AvailableSlots)
        {
            loadout[slot] = slot switch
            {
                ShipSlotType.Engine => GetById("engine_basic")!,
                ShipSlotType.Armor => GetById("armor_basic")!,
                ShipSlotType.Shield => GetById("shield_basic")!,
                ShipSlotType.FtlDrive => GetById("ftl_basic")!,
                ShipSlotType.Weapon1 => GetById("weapon_laser")!,
                ShipSlotType.Weapon2 => GetById("weapon_none")!,
                ShipSlotType.Utility or ShipSlotType.Utility2 => GetById("util_none")!,
                _ => GetById("util_none")!,
            };
        }
        return loadout;
    }

    /// <summary>Slot display names.</summary>
    public static string GetSlotName(ShipSlotType slot) => slot switch
    {
        ShipSlotType.Engine => "ENGINE",
        ShipSlotType.Armor => "ARMOR",
        ShipSlotType.Shield => "SHIELD",
        ShipSlotType.FtlDrive => "FTL DRIVE",
        ShipSlotType.Weapon1 => "WEAPON 1",
        ShipSlotType.Weapon2 => "WEAPON 2",
        ShipSlotType.Utility => "UTILITY",
        ShipSlotType.Utility2 => "UTILITY 2",
        _ => slot.ToString().ToUpper()
    };
}
