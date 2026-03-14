using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Core;

// ── Color (extended) ─────────────────────────────────────────────
// Color3, Color4, Rect, and VisibleBounds are defined in EngineTypes.cs

/// <summary>An RGB color paired with a radius value, used for celestial body visuals.</summary>
public readonly record struct ColoredRadius(Color3 Color, float Radius);

// ── 2D positions / rectangles ────────────────────────────────────

/// <summary>An integer 2D position (tile coordinates).</summary>
public readonly record struct TilePos(int X, int Y);

/// <summary>An integer size in tiles.</summary>
public readonly record struct TileSize(int Width, int Height);

/// <summary>An integer axis-aligned rectangle (position + size in tile coordinates).</summary>
public readonly record struct TileRect(int X, int Y, int Width, int Height)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

// ── Camera / view ────────────────────────────────────────────────

/// <summary>A view area defined by an origin and size.</summary>
public readonly record struct ViewArea(Vector2 Origin, Vector2 Size);

// ── Background decorations ───────────────────────────────────────

/// <summary>
/// A pre-computed background star ready for parallax rendering.
/// All visual properties are baked at generation time so the render loop
/// only needs to evaluate the per-frame twinkle sine and a few multiplies.
/// </summary>
public readonly record struct BackgroundStar(
    float X,
    float Y,
    byte BaseBrightness,   // base luminance before twinkle (100-227)
    float TwinklePhase,    // sine phase offset unique to this star
    float TwinkleSpeed,    // sine angular frequency unique to this star
    byte ColorType,        // 0=blue-white 1=warm-yellow 2=orange-red 3=white
    byte Size,             // render size in pixels: 1, 2, or 3
    bool HasGlow           // whether to draw the cross glow
);

/// <summary>A cosmetic nebula cloud with position, radius, and color.</summary>
public readonly record struct NebulaCloud(float X, float Y, float Radius, Color3 Color);

// ── Stat comparison ──────────────────────────────────────────────

/// <summary>A labeled stat difference for equipment comparison UI.</summary>
public readonly record struct StatDiff(string Label, float OldValue, float NewValue)
{
    public float Diff => NewValue - OldValue;
}

// ── Solar system generation ──────────────────────────────────────

/// <summary>Result of generating a solar system's contents.</summary>
public readonly record struct SolarSystemContent(
    List<PlanetData> Planets,
    List<AsteroidBeltData> AsteroidBelts,
    List<SpaceStationData> SpaceStations,
    NpcShipSpawnConfig NpcShipSpawnConfig,
    Vector2 StartingPosition,
    List<DerelictShipData> DerelictShips = default!,
    List<DistressSignalData> DistressSignals = default!);

/// <summary>Data for a derelict ship placed in a solar system.</summary>
public readonly record struct DerelictShipData(
    float OrbitRadius,
    float OrbitSpeed,
    float StartAngle,
    int CreditReward,
    ResourceType ResourceReward,
    int ResourceAmount,
    bool HasRarePart);

/// <summary>Data for a distress signal beacon placed in a solar system.</summary>
public readonly record struct DistressSignalData(
    float Angle,
    float Distance,
    bool IsAmbush,
    int RewardCredits,
    int AmbushPirateCount);

/// <summary>
/// Runtime configuration for the dynamic NPC spawn manager.
/// Stored in <see cref="SolarSystemContent"/>; all NPC spawning is handled at runtime.
/// </summary>
public readonly record struct NpcShipSpawnConfig(
    int TargetPirates,
    int TargetTraders,
    int TargetPatrols,
    int DangerLevel,
    int QualityTier,
    float InitialMinSpawnRadius,
    float InitialMaxSpawnRadius,
    float WarpInMinRadius,
    float WarpInMaxRadius);

// ── Menu start helpers ───────────────────────────────────────────

/// <summary>A star system paired with a planet.</summary>
public readonly record struct SystemPlanet(StarSystemData StarSystem, PlanetData Planet);

/// <summary>A star system paired with a space station.</summary>
public readonly record struct SystemSpaceStation(StarSystemData StarSystem, SpaceStationData SpaceStation);

/// <summary>A star system, planet, and settlement combination.</summary>
public readonly record struct SystemPlanetSettlement(
    StarSystemData StarSystem, PlanetData Planet, SettlementData Settlement);

// ── Projectiles ──────────────────────────────────────────────────

/// <summary>Weapon firing behaviour.</summary>
public enum WeaponBehavior
{
    /// <summary>Standard straight-line projectile.</summary>
    Standard,
    /// <summary>Homing missile that steers toward the nearest enemy.</summary>
    Tracking,
    /// <summary>Fires multiple projectiles in a fan arc.</summary>
    Spread,
    /// <summary>Continuous beam that deals damage per second while held.</summary>
    Beam
}

/// <summary>Weapon stats for a single ship weapon mount.</summary>
public readonly record struct ShipWeaponSpec(
    float Damage,
    float FireRate,
    float Range,
    float ProjectileSpeed,
    WeaponBehavior Behavior = WeaponBehavior.Standard,
    float EnergyCost = 0f,
    float ShieldPierce = 0f);

/// <summary>Pending projectile spawn data for space combat AI.</summary>
public readonly record struct ProjectileSpawn(
    Vector2 Pos, Vector2 Dir, float Damage, float Speed, float Lifetime,
    Faction Faction, Color3 Color, Vector2 InheritedVelocity, Entity OwnerEntity,
    WeaponBehavior Behavior = WeaponBehavior.Standard,
    float ShieldPierce = 0f);

/// <summary>Pending projectile spawn data for surface combat AI (includes lifetime).</summary>
public readonly record struct SurfaceProjectileSpawn(
    Vector2 Pos, Vector2 Dir, float Damage, float Speed,
    Faction Faction, Color3 Color, float Lifetime, Entity OwnerEntity,
    WeaponBehavior Behavior = WeaponBehavior.Standard,
    float ShieldPierce = 0f);

/// <summary>Result of an AI target search.</summary>
public readonly record struct TargetInfo(Vector2 Position, bool HasTarget, Entity? Entity);

/// <summary>Derived stats for an NPC ship built from a ship type and parts.</summary>
public readonly record struct NpcShipStats(
    int SpriteSize,
    float MaxHull,
    float MaxShield,
    float MaxSpeed,
    float RotationSpeed,
    float Acceleration,
    float WeaponDamage,
    float WeaponFireRate,
    float WeaponRange,
    float ProjectileSpeed);

// ── Surface spawns ───────────────────────────────────────────────

/// <summary>Spawn data for a mineable rock on a planet surface.</summary>
public readonly record struct RockSpawn(float X, float Y, ResourceType Resource, int Amount, float Size, float Hp);

/// <summary>Spawn data for a destructible cover obstacle on a planet surface.</summary>
public readonly record struct CoverSpawn(float X, float Y, float Size, float Hp);

/// <summary>
/// Runtime configuration for the dynamic surface NPC spawn manager.
/// Stored in <see cref="PlanetSurfaceData"/>; all NPC spawning is handled at runtime.
/// </summary>
public readonly record struct NpcAvatarSpawnConfig(
    int TargetEnemies,
    int TargetCargo,
    int TargetPatrols,
    int DangerLevel);

/// <summary>Lifecycle phase of a surface NPC that arrives/departs by ship.</summary>
public enum SurfaceNpcPhase
{
    /// <summary>Ship is descending — scale animation, no foot entity yet.</summary>
    Landing,
    /// <summary>NPC is on foot (SurfaceAI drives behaviour).</summary>
    OnFoot,
    /// <summary>NPC is walking back toward its ship to board it.</summary>
    BoardingShip,
    /// <summary>Ship is ascending — scale animation, foot entity already removed.</summary>
    TakingOff
}
