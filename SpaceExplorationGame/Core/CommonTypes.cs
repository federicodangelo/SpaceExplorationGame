using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Core;

// ── Color ────────────────────────────────────────────────────────

/// <summary>An RGB color with byte components.</summary>
public readonly record struct Color3(byte R, byte G, byte B);

/// <summary>An RGB color paired with a radius value, used for celestial body visuals.</summary>
public readonly record struct ColoredRadius(byte R, byte G, byte B, float Radius);

// ── 2D positions / rectangles ────────────────────────────────────

/// <summary>An integer 2D position (tile coordinates).</summary>
public readonly record struct TilePos(int X, int Y);

/// <summary>A float 2D position.</summary>
public readonly record struct FloatPos(float X, float Y);

/// <summary>A float axis-aligned rectangle (position + size).</summary>
public readonly record struct FloatRect(float X, float Y, float W, float H);

// ── Camera / view ────────────────────────────────────────────────

/// <summary>Visible world-space bounds (top-left and bottom-right corners).</summary>
public readonly record struct VisibleBounds(Vector2 TopLeft, Vector2 BottomRight);

/// <summary>A view area defined by an origin and size.</summary>
public readonly record struct ViewArea(Vector2 Origin, Vector2 Size);

// ── Background decorations ───────────────────────────────────────

/// <summary>A cosmetic background star with brightness.</summary>
public readonly record struct BackgroundStar(float X, float Y, byte Brightness);

/// <summary>A cosmetic background star with brightness and animation speed.</summary>
public readonly record struct AnimatedStar(float X, float Y, byte Brightness, float Speed);

/// <summary>A cosmetic nebula cloud with position, radius, and color.</summary>
public readonly record struct NebulaCloud(float X, float Y, float Radius, byte R, byte G, byte B);

// ── Stat comparison ──────────────────────────────────────────────

/// <summary>A labeled stat difference for equipment comparison UI.</summary>
public readonly record struct StatDiff(string Label, float Diff);

// ── Solar system generation ──────────────────────────────────────

/// <summary>Result of generating a solar system's contents.</summary>
public readonly record struct SolarSystemContent(
    List<PlanetData> Planets,
    List<AsteroidBeltData> AsteroidBelts,
    List<SpaceStationData> Stations);

// ── Menu start helpers ───────────────────────────────────────────

/// <summary>A star system paired with a planet.</summary>
public readonly record struct SystemPlanet(StarSystemData System, PlanetData Planet);

/// <summary>A star system paired with a space station.</summary>
public readonly record struct SystemStation(StarSystemData System, SpaceStationData Station);

/// <summary>A star system, planet, and settlement combination.</summary>
public readonly record struct SystemPlanetSettlement(
    StarSystemData System, PlanetData Planet, SettlementData Settlement);

// ── Projectiles ──────────────────────────────────────────────────

/// <summary>Pending projectile spawn data for space combat AI.</summary>
public readonly record struct ProjectileSpawn(
    Vector2 Pos, Vector2 Dir, float Damage, float Speed,
    Faction Faction, byte R, byte G, byte B);

/// <summary>Pending projectile spawn data for surface combat AI (includes lifetime).</summary>
public readonly record struct SurfaceProjectileSpawn(
    Vector2 Pos, Vector2 Dir, float Damage, float Speed,
    Faction Faction, byte R, byte G, byte B, float Lifetime);

/// <summary>Result of an AI target search.</summary>
public readonly record struct TargetInfo(Vector2 Position, bool HasTarget, Entity? Entity);

// ── Surface spawns ───────────────────────────────────────────────

/// <summary>Spawn position for a creature (fauna or bandit) on a planet surface.</summary>
public readonly record struct CreatureSpawn(float X, float Y, float WanderAngle);

/// <summary>Spawn data for a mineable rock on a planet surface.</summary>
public readonly record struct RockSpawn(float X, float Y, ResourceType Resource, int Amount, float Size, float Hp);
