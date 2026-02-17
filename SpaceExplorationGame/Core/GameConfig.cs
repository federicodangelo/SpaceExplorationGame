namespace SpaceExplorationGame.Core;

/// <summary>
/// Central game configuration. All tunable constants live here.
/// </summary>
public static class GameConfig
{
    // Window
    public const int WindowWidth = 1920;
    public const int WindowHeight = 1080;
    public const string WindowTitle = "Space Exploration Game";

    // Tiles
    public const int TileSize = 32;

    // Timing
    public const float TargetFps = 60f;
    public const float FixedTimeStep = 1f / TargetFps;
    public const int MaxFrameSkip = 5;

    // Camera
    public const float CameraZoomFactor = 0.15f;  // multiplicative zoom per scroll step

    // Zoom limits per context
    public const float SolarSystemZoomMin = 0.5f;
    public const float SolarSystemZoomMax = 1.0f;
    public const float SolarSystemZoomDefault = 1.0f;
    
    public const float InteriorZoomMin = 1.0f;
    public const float InteriorZoomMax = 1.5f;
    public const float InteriorZoomDefault = 1.5f;

    public const float PlanetSurfaceZoomMin = 1.0f;
    public const float PlanetSurfaceZoomMax = 1.5f;
    public const float PlanetSurfaceZoomDefault = 1.5f;

    public const float GalaxyMapZoomMin = 0.05f;
    public const float GalaxyMapZoomMax = 6.0f;
    public const float GalaxyMapZoomDefault = 0.1f;

    // Galaxy
    public const int GalaxyWidth = 2000;   // in tiles
    public const int GalaxyHeight = 2000;
    public const int MinStarSystems = 40;
    public const int MaxStarSystems = 80;

    // Solar System
    public const int SolarSystemWidth = 1000;  // in tiles
    public const int SolarSystemHeight = 1000;
    public const int MinPlanets = 2;
    public const int MaxPlanets = 10;

    // Planet Surface
    public const int PlanetSurfaceWidth = 256;  // in tiles
    public const int PlanetSurfaceHeight = 256;

    // Player Ship
    public const float ShipAcceleration = 200f;     // pixels/sec^2
    public const float ShipMaxSpeed = 400f;          // pixels/sec
    public const float ShipRotationSpeed = 180f;     // degrees/sec

    // FTL Travel
    public const float FuelPerDistanceUnit = 0.002f; // fuel cost per world-pixel of distance
    public const float FtlMaxRange = 25000f;         // max FTL jump range in world-pixels
    public const float StationRefuelAmount = 50f;    // fuel restored when docking at a station

    // Planet Vehicle
    public const float VehicleAcceleration = 300f;    // pixels/sec^2
    public const float VehicleMaxSpeed = 600f;        // pixels/sec (3x avatar)
    public const float VehicleRotationSpeed = 150f;   // degrees/sec
    public const float VehicleFriction = 0.98f;       // per-frame velocity damping
    public const float VehicleBrakeMultiplier = 0.92f; // brake damping per frame
    public const float VehicleMountRadius = 35f;      // distance to mount/dismount

    // ── Combat ──────────────────────────────────────────────────
    // Projectiles
    public const float ProjectileSpeed = 600f;         // pixels/sec (player projectile)
    public const float ProjectileLifetime = 2.0f;      // seconds before despawn
    public const float ProjectileRadius = 4f;          // collision radius
    public const float PlayerFireRate = 0.25f;         // seconds between player shots

    // Enemy ships
    public const int MinEnemiesPerSystem = 0;
    public const int MaxEnemiesPerSystem = 20;
    public const int MinTradersPerSystem = 10;
    public const int MaxTradersPerSystem = 40;
    public const int MinPatrolsPerSystem = 0;
    public const int MaxPatrolsPerSystem = 20;
    public const float EnemyDetectRange = 300f;        // range to notice targets
    public const float EnemyWeaponRange = 300f;        // range to start firing
    public const float EnemyEngageDistance = 200f;      // preferred combat distance
    public const float EnemyFleeHealthPercent = 0.2f;   // flee below 20% hull
    public const float EnemyProjectileSpeed = 300f;     // slightly slower than player
    public const float EnemyFireRate = 1.4f;            // seconds between shots
    public const float TraderSpeed = 150f * 3.0f;              // trader max speed
    public const float PirateSpeed = 300f * 3.0f;              // pirate max speed
    public const float PatrolSpeed = 250f * 3.0f;              // patrol max speed

    // Shields
    public const float BaseShieldRegenRate = 5f;        // shield points per second
    public const float ShieldRegenDelay = 3f;           // seconds after hit before regen

    // Danger levels
    public const int MinDangerLevel = 1;
    public const int MaxDangerLevel = 5;

    // Death penalty
    public const float DeathHullPercent = 0.5f;         // respawn with 50% hull
    public const float DeathCargoLossPercent = 0.25f;    // lose 25% of cargo
    public const float DeathCreditsLossPercent = 0.10f;  // lose 10% of credits

    // Loot
    public const int BaseLootCredits = 50;              // base credits per kill
    public const float ResourceDropChance = 0.5f;       // 50% chance to drop resources
    public const float PartDropChance = 0.10f;          // 10% chance to drop a part

    // ── Surface Combat ──────────────────────────────────────────
    public const float AvatarProjectileSpeed = 400f;     // avatar gun projectile speed
    public const float AvatarProjectileLifetime = 1.5f;  // seconds before despawn
    public const float AvatarFireRate = 0.35f;           // seconds between avatar shots
    public const float BaseAvatarWeaponDamage = 10f;     // base avatar weapon damage

    public const int MinFaunaPerPlanet = 3;
    public const int MaxFaunaPerPlanet = 10;
    public const int MinBanditsPerPlanet = 0;
    public const int MaxBanditsPerPlanet = 4;

    public const float FaunaSpeed = 80f;                // fauna walking speed
    public const float FaunaDetectRange = 120f;         // fauna aggro range
    public const float FaunaAttackRange = 25f;          // fauna melee range (contact damage via fast projectile)
    public const float FaunaAttackRate = 1.2f;          // seconds between fauna attacks
    public const float FaunaBaseDamage = 8f;
    public const float FaunaBaseHull = 30f;

    public const float BanditSpeed = 100f;              // bandit walking speed
    public const float BanditDetectRange = 160f;        // bandit aggro range
    public const float BanditAttackRange = 140f;        // bandit shooting range
    public const float BanditFireRate = 1.0f;           // seconds between bandit shots
    public const float BanditProjectileSpeed = 250f;
    public const float BanditBaseDamage = 6f;
    public const float BanditBaseHull = 50f;

    public const int SurfaceLootCreditsMin = 10;
    public const int SurfaceLootCreditsMax = 40;

    // ── Surface Mining Rocks ────────────────────────────────────
    public const int MinRocksPerPlanet = 8;
    public const int MaxRocksPerPlanet = 20;
    public const float SurfaceRockMinSize = 10f;          // visual size (smallest)
    public const float SurfaceRockMaxSize = 18f;          // visual size (largest)
    public const float SurfaceRockMinHp = 15f;
    public const float SurfaceRockMaxHp = 40f;
    public const int SurfaceRockMinResource = 1;          // min resource units per rock
    public const int SurfaceRockMaxResource = 5;           // max resource units per rock
}
