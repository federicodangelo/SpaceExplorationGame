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

    // ── Surface Combat ──────────────────────────────────────────
    public const float AvatarProjectileSpeed = 400f;    // avatar gun projectile speed
    public const float AvatarProjectileLifetime = 1.5f; // seconds before despawn
    public const float AvatarFireRate = 0.35f;          // seconds between avatar shots
    public const float BaseAvatarWeaponDamage = 10f;    // base avatar weapon damage

    // Surface NPC combat stats (used by all surface factions — pirates reuse bandit stats)
    public const float BanditSpeed = 100f;              // NPC walking speed
    public const float BanditDetectRange = 160f;        // NPC aggro range
    public const float BanditAttackRange = 140f;        // NPC shooting range
    public const float BanditFireRate = 1.0f;           // seconds between NPC shots
    public const float BanditProjectileSpeed = 250f;
    public const float BanditBaseDamage = 6f;
    public const float BanditBaseHull = 50f;

    public const int SurfaceLootCreditsMin = 10;
    public const int SurfaceLootCreditsMax = 40;

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
