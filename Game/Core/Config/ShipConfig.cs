namespace SpaceExplorationGame.Core.Config;

public static class ShipConfig
{
    // Ship movement
    public const float ShipBrakeMultiplier = 0.95f;

    // FTL Travel
    public const float FuelPerDistanceUnit = 0.002f; // fuel cost per world-pixel of distance
    public const float FtlMaxRange = 25000f;         // max FTL jump range in world-pixels
    public const float StationRefuelAmount = 50f;    // fuel restored when docking at a station

    // Shields
    public const float BaseShieldRegenRate = 5f;     // shield points per second
    public const float ShieldRegenDelay = 3f;        // seconds after hit before regen
}
