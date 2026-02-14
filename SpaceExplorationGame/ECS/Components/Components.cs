using System.Numerics;

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
    public float CurrentAngle;         // current angle in radians

    public Orbit(Arch.Core.Entity parent, float radius, float speed, float startAngle)
    {
        Parent = parent;
        OrbitRadius = radius;
        OrbitSpeed = speed;
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
