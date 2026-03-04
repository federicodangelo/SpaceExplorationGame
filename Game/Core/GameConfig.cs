namespace SpaceExplorationGame.Core;

/// <summary>
/// Central game configuration. All tunable constants live here.
/// </summary>
public static class GameConfig
{
    // Window
    static public int DefaultWindowWidth = 1920;
    static public int DefaultWindowHeight = 1080;
    public const string WindowTitle = "Space Exploration Game";

    // Debug
    public static bool Debug = false;

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

    public const float GalaxyMapZoomMin = 0.005f;
    public const float GalaxyMapZoomMax = 6.0f;
    public const float GalaxyMapZoomDefault = 0.1f;

    // Galaxy
    public const int GalaxyWidth = 2000;   // in tiles
    public const int GalaxyHeight = 2000;
    public const int MinStarSystems = 80;
    public const int MaxStarSystems = 80;

    // Solar System
    public const int SolarSystemWidth = 1000;  // in tiles
    public const int SolarSystemHeight = 1000;
    public const int MinPlanets = 2;
    public const int MaxPlanets = 10;
    public const float ShipBrakeMultiplier = 0.95f;

    // Planet Surface
    public const int PlanetSurfaceWidth = 256;  // in tiles
    public const int PlanetSurfaceHeight = 256;

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

    // Avatar
    public const float AvatarBaseWalkSpeed = 200f;   // pixels/sec

    public const float AvatarBaseMaxHealth = 100f;

    // ── Combat ──────────────────────────────────────────────────
    // Projectiles
    public const float ProjectileRadius = 4f;          // collision radius

    // Enemy ships
    public const int MinEnemiesPerSystem = 0;
    public const int MaxEnemiesPerSystem = 20;
    public const int MinTradersPerSystem = 10;
    public const int MaxTradersPerSystem = 40;
    public const int MinPatrolsPerSystem = 0;
    public const int MaxPatrolsPerSystem = 20;
    public const float EnemyDetectRange = 500f;        // range to notice targets
    public const float EnemyEngageDistance = 200f;      // preferred combat distance

    // Shields
    public const float BaseShieldRegenRate = 5f;        // shield points per second
    public const float ShieldRegenDelay = 3f;           // seconds after hit before regen

    // Danger levels
    public const int MinDangerLevel = 1;
    public const int MaxDangerLevel = 5;

    // ── Dynamic NPC Spawning ────────────────────────────────────
    public const float NpcWarpDuration = 1.5f;          // warp-in/out animation length in seconds
    public const float NpcSpawnCheckInterval = 15f;      // seconds between spawn budget checks
    public const float NpcPirateRespawnDelay = 45f;      // seconds before a killed pirate is replaced
    public const float NpcTraderRespawnDelay = 20f;      // seconds before a killed trader is replaced
    public const float NpcPatrolRespawnDelay = 30f;      // seconds before a killed patrol is replaced
    public const float NpcInitialSpawnFraction = 0.6f;   // fraction of target spawned instantly on entry

    // Death penalty
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
    public const float SurfaceRockMinSize = 18f;          // visual size (smallest)
    public const float SurfaceRockMaxSize = 24f;          // visual size (largest)
    public const float SurfaceRockMinHp = 15f;
    public const float SurfaceRockMaxHp = 40f;
    public const int SurfaceRockMinResource = 1;          // min resource units per rock
    public const int SurfaceRockMaxResource = 5;           // max resource units per rock

    // ── Dynamic Surface NPC Spawning ─────────────────────────────
    public const float SurfaceNpcLandingDuration = 2.0f;     // landing / takeoff animation length
    public const float SurfaceNpcSpawnCheckInterval = 20f;   // seconds between surface spawn checks
    public const float SurfaceNpcEnemyRespawnDelay = 60f;    // seconds before a killed enemy is replaced
    public const float SurfaceNpcCargoRespawnDelay = 30f;    // seconds before a killed cargo is replaced
    public const float SurfaceNpcPatrolRespawnDelay = 40f;   // seconds before a killed patrol is replaced
    public const float SurfaceNpcInitialSpawnFraction = 0.6f;// fraction spawned instantly on landing
    public const int SurfaceNpcMinEnemies = 10;
    public const int SurfaceNpcMaxEnemies = 20;
    public const int SurfaceNpcMinCargo = 5;
    public const int SurfaceNpcMaxCargo = 10;
    public const int SurfaceNpcMinPatrols = 5;
    public const int SurfaceNpcMaxPatrols = 10;
    public const float SurfaceNpcShipSize = 64f;             // landed ship sprite size
    public const float SurfaceNpcInactivityTimeout = 60f;  // seconds of idle wandering before NPC departs
    public const float SurfaceNpcBoardingSpeed = 80f;      // walk speed toward ship when boarding

    // ── Audio ───────────────────────────────────────────────────
    public const float AudioMasterVolume = 0.5f;
    public const float AudioMusicVolume = 0.4f;
    public const float AudioSfxVolume = 0.7f;
    public const float CombatMusicDelay = 5f;            // seconds of combat music after last damage
}
