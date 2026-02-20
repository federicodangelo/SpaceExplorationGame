using System;
using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.ECS;

/// <summary>
/// Centralized factory methods for creating all ECS entities in the game.
/// Avoids scattered inline entity creation and ensures consistent component composition.
/// </summary>
public static class EntityFactory
{
    // ── Celestial Bodies ────────────────────────────────────────────

    /// <summary>Create a star entity at the center of a solar system.</summary>
    public static Entity CreateStar(World world, Vector2 position, float displayRadius,
        string name, Color3 color, int dataIndex)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(displayRadius * 2), (int)(displayRadius * 2), color),
            new CelestialBody
            {
                Type = CelestialType.Star,
                Name = name,
                Radius = displayRadius,
                DataIndex = dataIndex
            },
            new Label { Text = name, OffsetY = (int)(displayRadius + 15) }
        );
    }

    /// <summary>Create a planet entity orbiting a star.</summary>
    public static Entity CreatePlanet(World world, Vector2 position, Entity starEntity,
        string name, float radius, Color3 color,
        float orbitRadius, float orbitSpeed, float startAngle,
        int dataIndex, bool hasSolidSurface)
    {
        var entity = world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(radius * 2), (int)(radius * 2), color),
            new CelestialBody
            {
                Type = CelestialType.Planet,
                Name = name,
                Radius = radius,
                DataIndex = dataIndex,
                HasSolidSurface = hasSolidSurface
            },
            new Orbit(starEntity, orbitRadius, orbitSpeed, startAngle),
            new Label { Text = name, OffsetY = (int)(radius + 10) }
        );

        if (hasSolidSurface)
        {
            world.Add(entity, new Interactable
            {
                Type = InteractionType.LandOnPlanet,
                Label = "Land"
            });
        }

        return entity;
    }

    /// <summary>Create a moon entity orbiting a planet.</summary>
    public static Entity CreateMoon(World world, Vector2 position, Entity parentPlanet,
        string name, float radius, Color3 color,
        float orbitRadius, float orbitSpeed, float startAngle, int dataIndex)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(radius * 2), (int)(radius * 2), color),
            new CelestialBody
            {
                Type = CelestialType.Moon,
                Name = name,
                Radius = radius,
                DataIndex = dataIndex,
                HasSolidSurface = true
            },
            new Orbit(parentPlanet, orbitRadius, orbitSpeed, startAngle),
            new Label { Text = name, OffsetY = (int)(radius + 8) },
            new Interactable
            {
                Type = InteractionType.LandOnPlanet,
                Label = "Land"
            }
        );
    }

    /// <summary>Create a space station entity orbiting a parent body (star or planet).</summary>
    public static Entity CreateStation(World world, Vector2 position, Entity parent,
        string name, float orbitRadius, float orbitSpeed, float startAngle, int dataIndex)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect(24, 24, new Color3(200, 200, 255)),
            new CelestialBody
            {
                Type = CelestialType.SpaceStation,
                Name = name,
                Radius = 120,
                DataIndex = dataIndex
            },
            new Orbit(parent, orbitRadius, orbitSpeed, startAngle),
            new Label { Text = name, OffsetY = 280 },
            new Interactable
            {
                Type = InteractionType.DockAtStation,
                Label = "Dock"
            }
        );
    }

    // ── Player Entities ─────────────────────────────────────────────

    /// <summary>Create a mineable asteroid entity orbiting the star.</summary>
    public static Entity CreateAsteroid(World world, Entity starEntity, float size, float hp,
        ResourceType resource, int resourceAmount,
        float orbitRadius, float orbitSpeed, float baseAngle)
    {
        return world.Create(
            new Transform(Vector2.Zero), // OrbitSystem will compute position
            Sprite.ColoredRect((int)(size + 4), (int)(size + 4), new Color3(140, 120, 100)),
            new Orbit(starEntity, orbitRadius, orbitSpeed, baseAngle),
            new Health(hp, 0f, 0f, 0f),
            new AsteroidField
            {
                Resource = resource,
                ResourceAmount = resourceAmount,
                Size = size
            }
        );
    }

    /// <summary>Create the player's ship entity for solar system flight.</summary>
    public static Entity CreatePlayerShip(World world, Vector2 position, int spriteSize,
        float maxHull, float currentHull, float maxShield, float maxSpeed)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect(spriteSize, spriteSize, new Color3(100, 255, 100)),
            new Velocity(maxSpeed),
            new PlayerControlled(),
            new Health(maxHull, maxShield,
                GameConfig.BaseShieldRegenRate, GameConfig.ShieldRegenDelay)
            {
                Hull = currentHull,
                Shield = maxShield // Start with full shields
            }
        );
    }

    /// <summary>Create the player avatar entity for planet surface or interior walking.</summary>
    public static Entity CreatePlayerAvatar(World world, float x, float y, float speed,
        float maxHealth = 0f, float currentHealth = 0f)
    {
        var entity = world.Create(
            new Transform(x, y),
            Sprite.ColoredRect(12, 12, new Color3(100, 255, 100)),
            new Velocity(speed),
            new PlayerControlled()
        );

        // Add Health component only on planet surface (maxHealth > 0)
        if (maxHealth > 0f)
        {
            world.Add(entity, new Health(maxHealth) { Hull = currentHealth > 0 ? currentHealth : maxHealth });
        }

        return entity;
    }

    /// <summary>Create the landed ship marker entity on a planet surface.</summary>
    public static Entity CreateLandedShip(World world, float x, float y)
    {
        return world.Create(
            new Transform(x, y),
            Sprite.ColoredRect(20, 16, new Color3(150, 150, 200)),
            new Label { Text = "SHIP", OffsetY = 14 }
        );
    }

    /// <summary>Create the player's vehicle entity on a planet surface.</summary>
    public static Entity CreateVehicle(World world, float x, float y)
    {
        return world.Create(
            new Transform(x, y),
            Sprite.ColoredRect(16, 16, new Color3(180, 140, 80)),
            new Label { Text = "VEHICLE", OffsetY = 14 }
        );
    }

    // ── Surface Mining Rocks ────────────────────────────────────────

    /// <summary>Create a mineable rock entity on a planet surface.</summary>
    public static Entity CreateSurfaceRock(World world, Vector2 position, float size, float hp,
        ResourceType resource, int resourceAmount)
    {
        // Tint color based on resource type
        var resInfo = ResourceCatalog.Get(resource);
        byte r = (byte)Math.Clamp(resInfo.Color.R * 0.6f + 50, 0, 255);
        byte g = (byte)Math.Clamp(resInfo.Color.G * 0.6f + 40, 0, 255);
        byte b = (byte)Math.Clamp(resInfo.Color.B * 0.6f + 30, 0, 255);

        return world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(size + 4), (int)(size + 4), new Color3(r, g, b)),
            new Health(hp, 0f, 0f, 0f),
            new AsteroidField
            {
                Resource = resource,
                ResourceAmount = resourceAmount,
                Size = size
            }
        );
    }

    // ── NPC Ships ───────────────────────────────────────────────────

    /// <summary>Create a pirate NPC ship entity.</summary>
    public static Entity CreatePirateShip(World world, Vector2 position, float rotation,
        NpcShipStats stats, int dangerLevel, int lootCredits, float fireCooldown,
        ShipWeaponSpec[] weapons)
    {
        return world.Create(
            new Transform(position, rotation),
            Sprite.ColoredRect(stats.SpriteSize, stats.SpriteSize, new Color3(255, 80, 80)),
            new Velocity(stats.MaxSpeed, stats.RotationSpeed),
            new Health(stats.MaxHull, stats.MaxShield,
                GameConfig.BaseShieldRegenRate * 0.5f, GameConfig.ShieldRegenDelay),
            new EnemyAI
            {
                Config = new EnemyAIConfig(
                    Faction: Faction.Pirate,
                    DetectRange: GameConfig.EnemyDetectRange,
                    Weapons: weapons,
                    LootCredits: lootCredits,
                    EngageDistance: GameConfig.EnemyEngageDistance,
                    FleeHealthPercent: GameConfig.EnemyFleeHealthPercent,
                    Acceleration: stats.Acceleration,
                    MaxRotationSpeed: stats.RotationSpeed),
                State = AIState.Patrol,
                WeaponCooldowns = InitializeWeaponCooldowns(weapons.Length, fireCooldown)
            },
            new LootDrop
            {
                MinCredits = Math.Max(1, lootCredits / 2),
                MaxCredits = lootCredits * 2,
                ResourceDropChance = GameConfig.ResourceDropChance,
                PartDropChance = GameConfig.PartDropChance,
                DangerLevel = dangerLevel
            }
        );
    }

    /// <summary>Create a trader NPC ship entity.</summary>
    public static Entity CreateTraderShip(World world, Vector2 position, float rotation, NpcShipStats stats,
        ShipWeaponSpec[] weapons)
    {
        return world.Create(
            new Transform(position, rotation),
            Sprite.ColoredRect(stats.SpriteSize, stats.SpriteSize, new Color3(200, 160, 80)),
            new Velocity(stats.MaxSpeed, stats.RotationSpeed),
            new Health(stats.MaxHull, stats.MaxShield, GameConfig.BaseShieldRegenRate * 0.5f, GameConfig.ShieldRegenDelay),
            new EnemyAI
            {
                Config = new EnemyAIConfig(
                    Faction: Faction.Trader,
                    DetectRange: 300f,
                    Weapons: weapons,
                    LootCredits: 0,
                    EngageDistance: 0f,
                    FleeHealthPercent: 0.5f,
                    Acceleration: stats.Acceleration,
                    MaxRotationSpeed: stats.RotationSpeed),
                State = AIState.Patrol,
                WeaponCooldowns = InitializeWeaponCooldowns(weapons.Length, 0f)
            }
        );
    }

    /// <summary>Create a patrol NPC ship entity.</summary>
    public static Entity CreatePatrolShip(World world, Vector2 position, float rotation, NpcShipStats stats,
        ShipWeaponSpec[] weapons)
    {
        return world.Create(
            new Transform(position, rotation),
            Sprite.ColoredRect(stats.SpriteSize, stats.SpriteSize, new Color3(80, 140, 220)),
            new Velocity(stats.MaxSpeed, stats.RotationSpeed),
            new Health(stats.MaxHull, stats.MaxShield, GameConfig.BaseShieldRegenRate, GameConfig.ShieldRegenDelay),
            new EnemyAI
            {
                Config = new EnemyAIConfig(
                    Faction: Faction.Patrol,
                    DetectRange: GameConfig.EnemyDetectRange * 1.5f,
                    Weapons: weapons,
                    LootCredits: 0,
                    EngageDistance: GameConfig.EnemyEngageDistance,
                    FleeHealthPercent: 0f,
                    Acceleration: stats.Acceleration,
                    MaxRotationSpeed: stats.RotationSpeed),
                State = AIState.Patrol,
                WeaponCooldowns = InitializeWeaponCooldowns(weapons.Length, 0f)
            }
        );
    }

    private static float[] InitializeWeaponCooldowns(int count, float initialCooldown)
    {
        if (count <= 0) return Array.Empty<float>();

        var cooldowns = new float[count];
        for (int i = 0; i < count; i++)
            cooldowns[i] = initialCooldown;

        return cooldowns;
    }

    // ── Projectiles ─────────────────────────────────────────────────

    /// <summary>Create a projectile entity fired in a given direction.</summary>
    public static Entity CreateProjectile(World world, Vector2 position, Vector2 direction,
        float damage, float speed, Faction ownerFaction, Color3 color,
        float lifetime, Vector2 inheritedVelocity = default)
    {
        float angle = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
        var projectileVelocity = direction * speed + inheritedVelocity;
        return world.Create(
            new Transform(position, angle),
            new Velocity(0f) { Velocity = projectileVelocity },
            new Projectile
            {
                Damage = damage,
                Speed = speed,
                Lifetime = lifetime,
                CollisionRadius = GameConfig.ProjectileRadius,
                OwnerFaction = ownerFaction,
                Color = color
            }
        );
    }

    // ── Surface Enemies ─────────────────────────────────────────

    /// <summary>Create a hostile fauna entity on a planet surface.</summary>
    public static Entity CreateFauna(World world, Vector2 position, float wanderAngle,
        float hullMultiplier = 1f, float damageMultiplier = 1f)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect(14, 14, new Color3(180, 60, 60)),
            new Velocity(GameConfig.FaunaSpeed),
            new Health(GameConfig.FaunaBaseHull * hullMultiplier),
            new SurfaceAI
            {
                Config = new SurfaceAIConfig(
                    Faction: Faction.Fauna,
                    MoveSpeed: GameConfig.FaunaSpeed,
                    DetectRange: GameConfig.FaunaDetectRange,
                    AttackRange: GameConfig.FaunaAttackRange,
                    FireRate: GameConfig.FaunaAttackRate,
                    WeaponDamage: GameConfig.FaunaBaseDamage * damageMultiplier,
                    ProjectileSpeed: 500f),
                State = AIState.Idle,
                FireCooldown = 0f,
                WanderAngle = wanderAngle,
                WanderTimer = 2f
            },
            new LootDrop
            {
                MinCredits = GameConfig.SurfaceLootCreditsMin,
                MaxCredits = GameConfig.SurfaceLootCreditsMax,
                ResourceDropChance = 0.3f,
                PartDropChance = 0f,
                DangerLevel = 1
            }
        );
    }

    /// <summary>Create a hostile bandit NPC entity on a planet surface.</summary>
    public static Entity CreateBandit(World world, Vector2 position, float wanderAngle,
        float hullMultiplier = 1f, float damageMultiplier = 1f)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect(12, 12, new Color3(200, 100, 60)),
            new Velocity(GameConfig.BanditSpeed),
            new Health(GameConfig.BanditBaseHull * hullMultiplier),
            new SurfaceAI
            {
                Config = new SurfaceAIConfig(
                    Faction: Faction.Bandit,
                    MoveSpeed: GameConfig.BanditSpeed,
                    DetectRange: GameConfig.BanditDetectRange,
                    AttackRange: GameConfig.BanditAttackRange,
                    FireRate: GameConfig.BanditFireRate,
                    WeaponDamage: GameConfig.BanditBaseDamage * damageMultiplier,
                    ProjectileSpeed: GameConfig.BanditProjectileSpeed),
                State = AIState.Patrol,
                FireCooldown = 0f,
                WanderAngle = wanderAngle,
                WanderTimer = 3f
            },
            new LootDrop
            {
                MinCredits = GameConfig.SurfaceLootCreditsMin * 2,
                MaxCredits = GameConfig.SurfaceLootCreditsMax * 2,
                ResourceDropChance = 0.4f,
                PartDropChance = 0.05f,
                DangerLevel = 1
            }
        );
    }
}
