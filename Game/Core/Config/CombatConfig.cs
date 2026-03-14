namespace SpaceExplorationGame.Core.Config;

public static class CombatConfig
{
    // Projectiles
    public const float ProjectileRadius = 4f;          // collision radius

    // Death penalty
    public const float DeathCargoLossPercent = 0.25f;  // lose 25% of cargo
    public const float DeathCreditsLossPercent = 0.10f; // lose 10% of credits

    // Loot
    public const int BaseLootCredits = 50;             // base credits per kill
    public const float ResourceDropChance = 0.5f;      // 50% chance to drop resources
    public const float PartDropChance = 0.10f;         // 10% chance to drop a part

    // ── Ship Energy System ─────────────────────────────────────
    public const float BaseShipEnergy = 100f;
    public const float BaseShipEnergyRegen = 15f;       // energy per second
    public const float BeamDamagePerSecond = 12f;       // beam weapon DPS
    public const float BeamMaxRange = 280f;             // beam max length
    public const float BeamWidth = 6f;                  // beam visual width
    public const float TrackingTurnRate = 200f;         // degrees/sec for homing missiles
    public const int SpreadProjectileCount = 4;         // shots per spread fire
    public const float SpreadArcDegrees = 30f;          // total fan arc

    // ── Surface Combat ──────────────────────────────────────────
    public const float AvatarProjectileSpeed = 400f;    // avatar gun projectile speed
    public const float AvatarProjectileLifetime = 0.75f; // seconds before despawn
    public const float AvatarFireRate = 0.35f;          // seconds between avatar shots
    public const float BaseAvatarWeaponDamage = 10f;    // base avatar weapon damage

    // Avatar ammo
    public const int SidearmMaxAmmo = -1;               // infinite
    public const int PulseRifleMaxAmmo = 60;
    public const int PlasmaCannonMaxAmmo = 20;
    public const int AvatarSpreadCount = 3;             // shots per spread fire on surface
    public const float AvatarSpreadArc = 25f;           // degrees

    // Dodge roll
    public const float DodgeRollDuration = 0.2f;        // seconds
    public const float DodgeRollSpeed = 600f;            // world units/sec during roll
    public const float DodgeRollCooldown = 0.8f;         // seconds between rolls

    // Surface NPC combat stats (used by all surface factions — pirates reuse bandit stats)
    public const float BanditSpeed = 100f;              // NPC walking speed
    public const float BanditDetectRange = 250f;        // NPC aggro range
    public const float BanditAttackRange = 140f;        // NPC shooting range
    public const float BanditFireRate = 1.0f;           // seconds between NPC shots
    public const float BanditProjectileSpeed = 250f;
    public const float BanditBaseDamage = 6f;
    public const float BanditBaseHull = 50f;

    public const int SurfaceLootCreditsMin = 10;
    public const int SurfaceLootCreditsMax = 40;

    // ── Surface Cover Obstacles ─────────────────────────────────
    public const int MinCoverPerPlanet = 50;
    public const int MaxCoverPerPlanet = 150;
    public const float CoverMinSize = 14f;
    public const float CoverMaxSize = 20f;
    public const float CoverMinHp = 30f;
    public const float CoverMaxHp = 80f;

    // ── Surface Mining Rocks ────────────────────────────────────
    public const int MinRocksPerPlanet = 8;
    public const int MaxRocksPerPlanet = 20;
    public const float SurfaceRockMinSize = 18f;         // visual size (smallest)
    public const float SurfaceRockMaxSize = 24f;         // visual size (largest)
    public const float SurfaceRockMinHp = 15f;
    public const float SurfaceRockMaxHp = 40f;
    public const int SurfaceRockMinResource = 1;         // min resource units per rock
    public const int SurfaceRockMaxResource = 5;         // max resource units per rock
}
