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
        string name, byte r, byte g, byte b, int dataIndex)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(displayRadius * 2), (int)(displayRadius * 2), r, g, b),
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
        string name, float radius, byte r, byte g, byte b,
        float orbitRadius, float orbitSpeed, float startAngle,
        int dataIndex, bool hasSolidSurface)
    {
        var entity = world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(radius * 2), (int)(radius * 2), r, g, b),
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
        string name, float radius, byte r, byte g, byte b,
        float orbitRadius, float orbitSpeed, float startAngle, int dataIndex)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(radius * 2), (int)(radius * 2), r, g, b),
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
            Sprite.ColoredRect(24, 24, 200, 200, 255),
            new CelestialBody
            {
                Type = CelestialType.SpaceStation,
                Name = name,
                Radius = 12,
                DataIndex = dataIndex
            },
            new Orbit(parent, orbitRadius, orbitSpeed, startAngle),
            new Label { Text = name, OffsetY = 28 },
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
            Sprite.ColoredRect((int)(size + 4), (int)(size + 4), 140, 120, 100),
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
            Sprite.ColoredRect(spriteSize, spriteSize, 100, 255, 100),
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
            Sprite.ColoredRect(12, 12, 100, 255, 100),
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
            Sprite.ColoredRect(20, 16, 150, 150, 200),
            new Label { Text = "SHIP", OffsetY = 14 }
        );
    }

    /// <summary>Create the player's vehicle entity on a planet surface.</summary>
    public static Entity CreateVehicle(World world, float x, float y)
    {
        return world.Create(
            new Transform(x, y),
            Sprite.ColoredRect(16, 16, 180, 140, 80),
            new Label { Text = "VEHICLE", OffsetY = 14 }
        );
    }

    // ── NPC Ships ───────────────────────────────────────────────────

    /// <summary>Create a pirate NPC ship entity.</summary>
    public static Entity CreatePirateShip(World world, Vector2 position, float rotation,
        int dangerLevel, float hullMultiplier, float damageMultiplier, int creditMultiplier,
        float fireCooldown)
    {
        float baseHull = 40f + dangerLevel * 20f;
        float baseShield = dangerLevel >= 3 ? 15f + dangerLevel * 10f : 0f;

        return world.Create(
            new Transform(position, rotation),
            Sprite.ColoredRect(28, 28, 255, 80, 80),
            new Velocity(GameConfig.PirateSpeed),
            new Health(baseHull * hullMultiplier, baseShield,
                GameConfig.BaseShieldRegenRate * 0.5f, GameConfig.ShieldRegenDelay),
            new EnemyAI
            {
                Faction = Faction.Pirate,
                State = AIState.Patrol,
                FireRate = GameConfig.EnemyFireRate / (1f + dangerLevel * 0.1f),
                FireCooldown = fireCooldown,
                WeaponDamage = (5f + dangerLevel * 3f) * damageMultiplier,
                WeaponRange = GameConfig.EnemyWeaponRange,
                DetectRange = GameConfig.EnemyDetectRange,
                ProjectileSpeed = GameConfig.EnemyProjectileSpeed,
                LootCredits = GameConfig.BaseLootCredits * creditMultiplier,
                EngageDistance = GameConfig.EnemyEngageDistance,
                FleeHealthPercent = GameConfig.EnemyFleeHealthPercent
            },
            new LootDrop
            {
                MinCredits = GameConfig.BaseLootCredits * creditMultiplier / 2,
                MaxCredits = GameConfig.BaseLootCredits * creditMultiplier * 2,
                ResourceDropChance = GameConfig.ResourceDropChance,
                PartDropChance = GameConfig.PartDropChance * (1f + dangerLevel * 0.05f),
                DangerLevel = dangerLevel
            }
        );
    }

    /// <summary>Create a trader NPC ship entity.</summary>
    public static Entity CreateTraderShip(World world, Vector2 position, float rotation)
    {
        return world.Create(
            new Transform(position, rotation),
            Sprite.ColoredRect(32, 32, 200, 160, 80),
            new Velocity(GameConfig.TraderSpeed),
            new Health(80f, 0f, 0f, 0f),
            new EnemyAI
            {
                Faction = Faction.Trader,
                State = AIState.Patrol,
                FireRate = 1f,
                FireCooldown = 0,
                WeaponDamage = 0f,
                WeaponRange = 0f,
                DetectRange = 300f,
                ProjectileSpeed = 0f,
                LootCredits = 0,
                EngageDistance = 0f,
                FleeHealthPercent = 0.5f
            }
        );
    }

    /// <summary>Create a patrol NPC ship entity.</summary>
    public static Entity CreatePatrolShip(World world, Vector2 position, float rotation)
    {
        return world.Create(
            new Transform(position, rotation),
            Sprite.ColoredRect(30, 30, 80, 140, 220),
            new Velocity(GameConfig.PatrolSpeed),
            new Health(120f, 50f, GameConfig.BaseShieldRegenRate, GameConfig.ShieldRegenDelay),
            new EnemyAI
            {
                Faction = Faction.Patrol,
                State = AIState.Patrol,
                FireRate = 0.5f,
                FireCooldown = 0,
                WeaponDamage = 12f,
                WeaponRange = GameConfig.EnemyWeaponRange * 1.2f,
                DetectRange = GameConfig.EnemyDetectRange * 1.5f,
                ProjectileSpeed = GameConfig.EnemyProjectileSpeed * 1.1f,
                LootCredits = 0,
                EngageDistance = GameConfig.EnemyEngageDistance,
                FleeHealthPercent = 0f
            }
        );
    }

    // ── Projectiles ─────────────────────────────────────────────────

    /// <summary>Create a projectile entity fired in a given direction.</summary>
    public static Entity CreateProjectile(World world, Vector2 position, Vector2 direction,
        float damage, float speed, Faction ownerFaction, byte r, byte g, byte b,
        float lifetime = GameConfig.ProjectileLifetime)
    {
        float angle = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
        return world.Create(
            new Transform(position, angle),
            new Velocity(speed) { Value = direction * speed },
            new Projectile
            {
                Damage = damage,
                Speed = speed,
                Lifetime = lifetime,
                CollisionRadius = GameConfig.ProjectileRadius,
                OwnerFaction = ownerFaction,
                R = r, G = g, B = b
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
            Sprite.ColoredRect(14, 14, 180, 60, 60),
            new Velocity(GameConfig.FaunaSpeed),
            new Health(GameConfig.FaunaBaseHull * hullMultiplier),
            new SurfaceAI
            {
                Faction = Faction.Fauna,
                State = AIState.Idle,
                MoveSpeed = GameConfig.FaunaSpeed,
                DetectRange = GameConfig.FaunaDetectRange,
                AttackRange = GameConfig.FaunaAttackRange,
                FireRate = GameConfig.FaunaAttackRate,
                FireCooldown = 0f,
                WeaponDamage = GameConfig.FaunaBaseDamage * damageMultiplier,
                ProjectileSpeed = 500f, // fast short-range "bite" projectile
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
            Sprite.ColoredRect(12, 12, 200, 100, 60),
            new Velocity(GameConfig.BanditSpeed),
            new Health(GameConfig.BanditBaseHull * hullMultiplier),
            new SurfaceAI
            {
                Faction = Faction.Bandit,
                State = AIState.Patrol,
                MoveSpeed = GameConfig.BanditSpeed,
                DetectRange = GameConfig.BanditDetectRange,
                AttackRange = GameConfig.BanditAttackRange,
                FireRate = GameConfig.BanditFireRate,
                FireCooldown = 0f,
                WeaponDamage = GameConfig.BanditBaseDamage * damageMultiplier,
                ProjectileSpeed = GameConfig.BanditProjectileSpeed,
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
