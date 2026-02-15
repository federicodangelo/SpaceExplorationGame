using System.Numerics;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.ECS.Components;

/// <summary>Position and rotation in world space.</summary>
public struct Transform
{
    public Vector2 Position;
    public float Rotation; // degrees

    public Transform(Vector2 position, float rotation = 0f)
    {
        Position = position;
        Rotation = rotation;
    }

    public Transform(float x, float y, float rotation = 0f)
    {
        Position = new Vector2(x, y);
        Rotation = rotation;
    }
}

/// <summary>Linear velocity.</summary>
public struct Velocity
{
    public Vector2 Value;
    public float MaxSpeed;

    public Velocity(float maxSpeed)
    {
        Value = Vector2.Zero;
        MaxSpeed = maxSpeed;
    }
}

/// <summary>Marks an entity as the player-controlled entity.</summary>
public struct PlayerControlled;

/// <summary>Sprite rendering info.</summary>
public struct Sprite
{
    public int TextureId;       // index into SpriteRenderer's texture list
    public int SrcX, SrcY;     // source rect in spritesheet
    public int SrcW, SrcH;
    public int Width, Height;  // render size in world pixels
    public byte R, G, B, A;   // color tint (for procedural colored rects)
    public bool UseColor;      // if true, render as colored rect instead of texture

    public static Sprite ColoredRect(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        return new Sprite
        {
            Width = width,
            Height = height,
            R = r, G = g, B = b, A = a,
            UseColor = true
        };
    }
}

/// <summary>Tag for celestial bodies (stars, planets, moons).</summary>
public struct CelestialBody
{
    public CelestialType Type;
    public string Name;
    public float Radius;          // visual radius in world pixels
    public int DataIndex;         // index into generation data
    public bool HasSolidSurface;  // can player land on it?
}

public enum CelestialType
{
    Star,
    Planet,
    Moon,
    Asteroid,
    SpaceStation
}

/// <summary>Orbital mechanics around a parent body.</summary>
public struct Orbit
{
    public Arch.Core.Entity Parent;    // entity being orbited
    public float OrbitRadius;          // distance from parent center
    public float OrbitSpeed;           // radians per second
    public float BaseAngle;            // angle at globalTime=0 (for deterministic position)
    public float CurrentAngle;         // current computed angle in radians

    public Orbit(Arch.Core.Entity parent, float radius, float speed, float startAngle)
    {
        Parent = parent;
        OrbitRadius = radius;
        OrbitSpeed = speed;
        BaseAngle = startAngle;
        CurrentAngle = startAngle;
    }
}

/// <summary>Circular collision shape.</summary>
public struct CircleCollider
{
    public float Radius;
}

/// <summary>Marks an entity as interactable (e.g., land on planet, dock at station)</summary>
public struct Interactable
{
    public InteractionType Type;
    public string Label;  // "Land", "Dock", etc.
}

public enum InteractionType
{
    LandOnPlanet,
    DockAtStation
}

/// <summary>Used for star system markers on the galaxy map.</summary>
public struct StarSystemMarker
{
    public int SystemIndex;
    public string Name;
    public StarClass StarClass;
}

public enum StarClass
{
    O, // Blue
    B, // Blue-white
    A, // White
    F, // Yellow-white
    G, // Yellow (like our Sun)
    K, // Orange
    M  // Red
}

/// <summary>Label that renders text near an entity.</summary>
public struct Label
{
    public string Text;
    public int OffsetY;  // pixel offset below the entity
}

// ── Combat Components ──────────────────────────────────────────────

/// <summary>Faction affiliation for combat entities (determines friend/foe).</summary>
public enum Faction
{
    Player,
    Pirate,      // Hostile — attacks player and traders
    Trader,      // Friendly — attacked by pirates, can be defended
    Patrol       // Neutral defender — attacks pirates, helps player
}

/// <summary>Health and shields for a combat-capable entity.</summary>
public struct Health
{
    public float Hull;
    public float MaxHull;
    public float Shield;
    public float MaxShield;
    public float ShieldRegenRate;     // shield points restored per second
    public float ShieldRegenDelay;     // seconds after last hit before regen starts
    public float TimeSinceLastHit;     // tracks time since last damage for regen delay

    public Health(float maxHull, float maxShield = 0f, float shieldRegenRate = 0f, float shieldRegenDelay = 2f)
    {
        Hull = maxHull;
        MaxHull = maxHull;
        Shield = maxShield;
        MaxShield = maxShield;
        ShieldRegenRate = shieldRegenRate;
        ShieldRegenDelay = shieldRegenDelay;
        TimeSinceLastHit = float.MaxValue; // allow immediate regen at start
    }

    /// <summary>Apply damage: shields absorb first, remainder goes to hull. Returns actual hull damage dealt.</summary>
    public float TakeDamage(float damage)
    {
        TimeSinceLastHit = 0f;

        // Shields absorb damage first
        float shieldAbsorbed = MathF.Min(Shield, damage);
        Shield -= shieldAbsorbed;
        float remaining = damage - shieldAbsorbed;

        // Remaining goes to hull
        float hullDamage = MathF.Min(Hull, remaining);
        Hull -= hullDamage;
        return hullDamage;
    }

    public readonly bool IsDead => Hull <= 0f;
    public readonly float HullPercent => MaxHull > 0 ? Hull / MaxHull : 0f;
    public readonly float ShieldPercent => MaxShield > 0 ? Shield / MaxShield : 0f;
}

/// <summary>A projectile entity that travels in a direction and deals damage on hit.</summary>
public struct Projectile
{
    public float Damage;
    public float Speed;
    public float Lifetime;           // seconds remaining before despawn
    public float CollisionRadius;    // hit detection radius
    public Faction OwnerFaction;     // who fired it (to avoid self-hits)
    public byte R, G, B;            // projectile color
}

/// <summary>AI-controlled ship with combat behavior.</summary>
public struct EnemyAI
{
    public Faction Faction;
    public AIState State;
    public float StateTimer;         // time in current state
    public float FireCooldown;       // seconds until next shot
    public float FireRate;           // seconds between shots
    public float WeaponDamage;       // damage per projectile
    public float WeaponRange;        // max firing range
    public float DetectRange;        // range to detect targets
    public float ProjectileSpeed;    // speed of fired projectiles
    public int LootCredits;          // credits dropped on death
    public float EngageDistance;     // preferred combat distance
    public float FleeHealthPercent;  // flee when hull % drops below this
}

public enum AIState
{
    Idle,       // Stationary or slow drift
    Patrol,     // Moving between waypoints
    Chase,      // Pursuing a target
    Attack,     // In weapons range, firing
    Flee,       // Low health, running away
    Defend      // Moving to defend a friendly (patrol/trader)
}

/// <summary>Loot table for an enemy entity — dropped on destruction.</summary>
public struct LootDrop
{
    public int MinCredits;
    public int MaxCredits;
    public float ResourceDropChance;   // 0-1 chance to drop resources
    public float PartDropChance;       // 0-1 chance to drop an equipment part
    public int DangerLevel;            // used to determine loot quality
}

/// <summary>Marks an entity as a mineable asteroid that drops resources when destroyed.</summary>
public struct AsteroidField
{
    public ResourceType Resource;
    public int ResourceAmount;         // units to drop on depletion
    public float Size;                 // visual size for rendering
}
