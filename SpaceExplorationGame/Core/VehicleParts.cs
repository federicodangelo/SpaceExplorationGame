namespace SpaceExplorationGame.Core;

/// <summary>
/// Vehicle equipment slot types.
/// </summary>
public enum VehicleSlotType
{
    Engine,
    Chassis,
    Lights
}

/// <summary>
/// A vehicle part that can be installed in a slot.
/// </summary>
public record VehiclePart(
    string Id,
    string Name,
    VehicleSlotType Slot,
    int Tier,          // 1 = basic, 2 = improved, 3 = advanced
    int BuyCost,
    int SellValue,
    string Description,
    VehiclePartStats Stats
) : ICustomizablePart;

/// <summary>
/// Stat modifiers provided by a vehicle part.
/// All values are additive to the vehicle's base stats.
/// </summary>
public record VehiclePartStats(
    float Acceleration = 0f,       // added to vehicle acceleration
    float MaxSpeed = 0f,           // added to vehicle max speed
    float RotationSpeed = 0f,      // added to vehicle rotation speed
    float Friction = 0f,           // added to friction (higher = less drag = more slide)
    float Visibility = 0f          // future: headlight range
);

/// <summary>
/// Catalog of all available vehicle parts.
/// </summary>
public static class VehiclePartCatalog
{
    public static readonly VehiclePart[] AllParts =
    [
        // ── Engines ──────────────────────────────────────────────
        new("veng_basic",    "Standard Motor",     VehicleSlotType.Engine, 1, 0,   25,
            "Reliable electric motor. Gets you moving.",
            new VehiclePartStats(Acceleration: 300f, MaxSpeed: 600f)),

        new("veng_improved", "Turbo Motor",        VehicleSlotType.Engine, 2, 250, 125,
            "High-torque motor with better acceleration.",
            new VehiclePartStats(Acceleration: 420f, MaxSpeed: 750f)),

        new("veng_advanced", "Fusion Drive",       VehicleSlotType.Engine, 3, 650, 325,
            "Compact fusion powerplant. Extreme performance.",
            new VehiclePartStats(Acceleration: 550f, MaxSpeed: 950f)),

        // ── Chassis ──────────────────────────────────────────────
        new("vchas_basic",    "Steel Frame",       VehicleSlotType.Chassis, 1, 0,   20,
            "Heavy but sturdy. Standard handling.",
            new VehiclePartStats(RotationSpeed: 150f, Friction: 0f)),

        new("vchas_improved", "Alloy Frame",       VehicleSlotType.Chassis, 2, 220, 110,
            "Lighter alloy improves agility.",
            new VehiclePartStats(RotationSpeed: 180f, Friction: 0.005f)),

        new("vchas_advanced", "Carbon Composite",  VehicleSlotType.Chassis, 3, 550, 275,
            "Ultra-light chassis. Razor-sharp handling.",
            new VehiclePartStats(RotationSpeed: 220f, Friction: 0.01f)),

        // ── Lights ───────────────────────────────────────────────
        new("vlight_basic",    "Halogen Lamps",    VehicleSlotType.Lights, 1, 0,   10,
            "Basic headlights. Limited range.",
            new VehiclePartStats(Visibility: 100f)),

        new("vlight_improved", "LED Array",        VehicleSlotType.Lights, 2, 120, 60,
            "Bright LED array. Good visibility.",
            new VehiclePartStats(Visibility: 200f)),

        new("vlight_advanced", "Floodlight System", VehicleSlotType.Lights, 3, 300, 150,
            "Full-spectrum floodlights. See everything.",
            new VehiclePartStats(Visibility: 350f)),
    ];

    /// <summary>Find a part by its ID.</summary>
    public static VehiclePart? GetById(string id) =>
        Array.Find(AllParts, p => p.Id == id);

    /// <summary>Get all parts that fit a given slot type.</summary>
    public static VehiclePart[] GetPartsForSlot(VehicleSlotType slot) =>
        Array.FindAll(AllParts, p => p.Slot == slot);

    /// <summary>Get the default starter loadout.</summary>
    public static Dictionary<VehicleSlotType, VehiclePart> GetStarterLoadout() => new()
    {
        [VehicleSlotType.Engine]  = GetById("veng_basic")!,
        [VehicleSlotType.Chassis] = GetById("vchas_basic")!,
        [VehicleSlotType.Lights]  = GetById("vlight_basic")!,
    };

    /// <summary>Slot display names.</summary>
    public static string GetSlotName(VehicleSlotType slot) => slot switch
    {
        VehicleSlotType.Engine  => "ENGINE",
        VehicleSlotType.Chassis => "CHASSIS",
        VehicleSlotType.Lights  => "LIGHTS",
        _ => slot.ToString().ToUpper()
    };
}
