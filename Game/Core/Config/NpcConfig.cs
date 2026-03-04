namespace SpaceExplorationGame.Core.Config;

public static class NpcConfig
{
    // ── Space NPC Counts ────────────────────────────────────────
    public const int MinEnemiesPerSystem = 0;
    public const int MaxEnemiesPerSystem = 20;
    public const int MinTradersPerSystem = 10;
    public const int MaxTradersPerSystem = 40;
    public const int MinPatrolsPerSystem = 0;
    public const int MaxPatrolsPerSystem = 20;
    public const float EnemyDetectRange = 500f;        // range to notice targets
    public const float EnemyEngageDistance = 200f;     // preferred combat distance

    // ── Dynamic NPC Spawning ────────────────────────────────────
    public const float NpcWarpDuration = 1.5f;          // warp-in/out animation length in seconds
    public const float NpcSpawnCheckInterval = 15f;     // seconds between spawn budget checks
    public const float NpcPirateRespawnDelay = 45f;     // seconds before a killed pirate is replaced
    public const float NpcTraderRespawnDelay = 20f;     // seconds before a killed trader is replaced
    public const float NpcPatrolRespawnDelay = 30f;     // seconds before a killed patrol is replaced
    public const float NpcInitialSpawnFraction = 0.6f;  // fraction of target spawned instantly on entry

    // ── Dynamic Surface NPC Spawning ─────────────────────────────
    public const float SurfaceNpcLandingDuration = 2.0f;      // landing / takeoff animation length
    public const float SurfaceNpcSpawnCheckInterval = 20f;    // seconds between surface spawn checks
    public const float SurfaceNpcEnemyRespawnDelay = 60f;     // seconds before a killed enemy is replaced
    public const float SurfaceNpcCargoRespawnDelay = 30f;     // seconds before a killed cargo is replaced
    public const float SurfaceNpcPatrolRespawnDelay = 40f;    // seconds before a killed patrol is replaced
    public const float SurfaceNpcInitialSpawnFraction = 0.6f; // fraction spawned instantly on landing
    public const int SurfaceNpcMinEnemies = 15;
    public const int SurfaceNpcMaxEnemies = 30;
    public const int SurfaceNpcMinCargo = 10;
    public const int SurfaceNpcMaxCargo = 20;
    public const int SurfaceNpcMinPatrols = 10;
    public const int SurfaceNpcMaxPatrols = 20;
    public const float SurfaceNpcShipSize = 64f;              // landed ship sprite size
    public const float SurfaceNpcInactivityTimeout = 60f;     // seconds of idle wandering before NPC departs
    public const float SurfaceNpcBoardingSpeed = 80f;         // walk speed toward ship when boarding
}
