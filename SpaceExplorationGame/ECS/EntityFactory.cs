using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.ECS;

/// <summary>
/// Centralized factory methods for creating all ECS entities in the game.
/// Avoids scattered inline entity creation and ensures consistent component composition.
/// </summary>
public static class EntityFactory
{
    // ── Celestial Bodies ────────────────────────────────────────────

    /// <summary>Create a star entity at the center of a solar system.</summary>
    public static Entity CreateStar(World world, Vector2 position, StarSystemData starSystem)
    {
        float displayRadius = starSystem.StarRadius * 2f;
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(displayRadius * 2), (int)(displayRadius * 2), starSystem.StarColor),
            new CelestialBody
            {
                Type = CelestialType.Star,
                Name = starSystem.Name,
                Radius = displayRadius,
                DataIndex = starSystem.Index
            },
            new Label { Text = starSystem.Name, OffsetY = (int)(displayRadius + 15) }
        );
    }

    /// <summary>Create a planet entity orbiting a star.</summary>
    public static Entity CreatePlanet(World world, Vector2 position, Entity starEntity, PlanetData planet)
    {
        var entity = world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(planet.Radius * 2), (int)(planet.Radius * 2), planet.Color),
            new CelestialBody
            {
                Type = CelestialType.Planet,
                Name = planet.Name,
                Radius = planet.Radius,
                DataIndex = planet.Index,
                HasSolidSurface = planet.HasSolidSurface
            },
            new Orbit(starEntity, planet.OrbitRadius, planet.OrbitSpeed, planet.StartAngle),
            new Label { Text = planet.Name, OffsetY = (int)(planet.Radius + 30) }
        );

        world.Add(entity, new Interactable
        {
            Type = InteractionType.LandOnPlanet,
            Label = planet.HasSolidSurface ? "Land" : "Info"
        });

        return entity;
    }

    /// <summary>Create a moon entity orbiting a planet.</summary>
    public static Entity CreateMoon(World world, Vector2 position, Entity parentPlanet, MoonData moon)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect((int)(moon.Radius * 2), (int)(moon.Radius * 2), moon.Color),
            new CelestialBody
            {
                Type = CelestialType.Moon,
                Name = moon.Name,
                Radius = moon.Radius,
                DataIndex = moon.Index,
                HasSolidSurface = true
            },
            new Orbit(parentPlanet, moon.OrbitRadius, moon.OrbitSpeed, moon.StartAngle),
            new Label { Text = moon.Name, OffsetY = (int)(moon.Radius + 30) },
            new Interactable
            {
                Type = InteractionType.LandOnPlanet,
                Label = "Land"
            }
        );
    }

    /// <summary>Create a space station entity orbiting a parent body (star or planet).</summary>
    public static Entity CreateSpaceStation(World world, Vector2 position, Entity parent, SpaceStationData spaceStation)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect(24, 24, new Color3(200, 200, 255)),
            new CelestialBody
            {
                Type = CelestialType.SpaceStation,
                Name = spaceStation.Name,
                Radius = 120,
                DataIndex = spaceStation.Index
            },
            new Orbit(parent, spaceStation.OrbitRadius, spaceStation.OrbitSpeed, spaceStation.StartAngle),
            new Label { Text = spaceStation.Name, OffsetY = 140 },
            new Interactable
            {
                Type = InteractionType.DockAtSpaceStation,
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
        float maxHull, float currentHull, float maxShield,
        float maxSpeed, float rotationSpeed, float acceleration, float brakeMultiplier,
        ShipWeaponSpec[] weapons)
    {
        var ship = world.Create(
            new Transform(position),
            Sprite.ColoredRect(spriteSize, spriteSize, new Color3(100, 255, 100)),
            new Velocity(maxSpeed, rotationSpeed),
            new ShipComponent(Faction.Player, maxSpeed, rotationSpeed, acceleration, brakeMultiplier,
                weapons),
            ShipInputComponent.Default(),
            new PlayerControlled(),
            new Health(maxHull, maxShield,
                GameConfig.BaseShieldRegenRate, GameConfig.ShieldRegenDelay)
            {
                Hull = currentHull,
                Shield = maxShield // Start with full shields
            }
        );

        CreateShipThrusterEmitters(world, ship, spriteSize, new Color3(130, 220, 255));
        return ship;
    }

    /// <summary>Create the player avatar entity for planet surface or interior walking.</summary>
    public static Entity CreatePlayerAvatar(World world, float x, float y, float speed,
        float maxHealth = 0f, float currentHealth = 0f, Func<Vector2, bool>? canMoveTo = null)
    {
        var entity = world.Create(
            new Transform(x, y),
            Sprite.ColoredRect(12, 12, new Color3(100, 255, 100)),
            new Velocity(speed) { CanMoveTo = canMoveTo },
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

    /// <summary>Create any NPC ship entity from spawn data.</summary>
    public static Entity CreateNpcShip(World world, NpcShipSpawnData spawn)
    {
        var stats = spawn.Stats;
        var (spriteColor, thrusterColor, aiConfig, shieldRegenRate) = GetNpcShipProfile(spawn);

        var ship = world.Create(
            new Transform(spawn.Position, spawn.Rotation),
            Sprite.ColoredRect(stats.SpriteSize, stats.SpriteSize, spriteColor),
            new Velocity(stats.MaxSpeed, stats.RotationSpeed),
            new ShipComponent(spawn.Faction, stats.MaxSpeed, stats.RotationSpeed,
                stats.Acceleration, GameConfig.ShipBrakeMultiplier,
                spawn.Weapons),
            ShipInputComponent.Default(),
            new Health(stats.MaxHull, stats.MaxShield, shieldRegenRate, GameConfig.ShieldRegenDelay),
            new EnemyAI { Config = aiConfig, State = AIState.Patrol }
        );

        if (spawn.LootCredits > 0)
        {
            world.Add(ship, new LootDrop
            {
                MinCredits = Math.Max(1, spawn.LootCredits / 2),
                MaxCredits = spawn.LootCredits * 2,
                ResourceDropChance = GameConfig.ResourceDropChance,
                PartDropChance = GameConfig.PartDropChance,
                DangerLevel = spawn.DangerLevel
            });
        }

        CreateShipThrusterEmitters(world, ship, stats.SpriteSize, thrusterColor);
        return ship;
    }

    private static (Color3 Sprite, Color3 Thruster, EnemyAIConfig Ai, float ShieldRegen) GetNpcShipProfile(NpcShipSpawnData spawn)
    {
        return spawn.Faction switch
        {
            Faction.Pirate => (
                new Color3(255, 80, 80),
                new Color3(255, 130, 110),
                new EnemyAIConfig(
                    Faction: Faction.Pirate,
                    DetectRange: GameConfig.EnemyDetectRange,
                    LootCredits: spawn.LootCredits,
                    EngageDistance: GameConfig.EnemyEngageDistance,
                    FleeHealthPercent: GameConfig.EnemyFleeHealthPercent),
                GameConfig.BaseShieldRegenRate * 0.5f),
            Faction.Trader => (
                new Color3(200, 160, 80),
                new Color3(255, 210, 120),
                new EnemyAIConfig(
                    Faction: Faction.Trader,
                    DetectRange: 300f,
                    LootCredits: 0,
                    EngageDistance: 0f,
                    FleeHealthPercent: 0.5f),
                GameConfig.BaseShieldRegenRate * 0.5f),
            Faction.Patrol => (
                new Color3(80, 140, 220),
                new Color3(130, 200, 255),
                new EnemyAIConfig(
                    Faction: Faction.Patrol,
                    DetectRange: GameConfig.EnemyDetectRange * 1.5f,
                    LootCredits: 0,
                    EngageDistance: GameConfig.EnemyEngageDistance,
                    FleeHealthPercent: 0f),
                GameConfig.BaseShieldRegenRate),
            _ => throw new ArgumentException($"Unsupported NPC faction: {spawn.Faction}")
        };
    }

    private static void CreateShipThrusterEmitters(World world, Entity shipEntity, int shipSize, Color3 color)
    {
        Vector2 shipPos = world.Get<Transform>(shipEntity).Position;
        float half = shipSize * 0.5f;
        float side = shipSize * 0.42f;
        float sideFrontX = shipSize * 0.22f;
        float sideRearX = -shipSize * 0.22f;

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateMainThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(-half * 1.1f, 0f),
            localEjectDirection: new Vector2(-1f, 0f),
            activation: ThrusterActivation.Forward));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(half * 0.95f, 0f),
            localEjectDirection: new Vector2(1f, 0f),
            activation: ThrusterActivation.Backward));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(sideFrontX, -side),
            localEjectDirection: new Vector2(0f, -1f),
            activation: ThrusterActivation.StrafeRight | ThrusterActivation.RotateRight));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(sideRearX, -side),
            localEjectDirection: new Vector2(0f, -1f),
            activation: ThrusterActivation.StrafeRight | ThrusterActivation.RotateLeft));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(sideFrontX, side),
            localEjectDirection: new Vector2(0f, 1f),
            activation: ThrusterActivation.StrafeLeft | ThrusterActivation.RotateLeft));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(sideRearX, side),
            localEjectDirection: new Vector2(0f, 1f),
            activation: ThrusterActivation.StrafeLeft | ThrusterActivation.RotateRight));
    }

    private static ParticleEmitter CreateMainThrusterEmitter(Entity shipEntity, Color3 color,
        Vector2 localOffset, Vector2 localEjectDirection, ThrusterActivation activation)
    {
        return new ParticleEmitter
        {
            EmitCondition = EmitCondition.Always,
            SpawnInterval = 0.024f,
            SpawnAccumulator = 0f,
            CarrierEntity = shipEntity,
            LocalOffset = localOffset,
            LocalEjectDirection = localEjectDirection,
            ActivationMask = activation,
            EjectSpeedMin = 130f,
            EjectSpeedMax = 220f,
            LateralDrift = 22f,
            ParticleLifeMin = 0.72f,
            ParticleLifeMax = 1.05f,
            ParticleSizeMin = 0.8f,
            ParticleSizeMax = 1.55f,
            ParticleDrag = 1.35f,
            ParticleColor = color
        };
    }

    private static ParticleEmitter CreateRcsThrusterEmitter(Entity shipEntity, Color3 color,
        Vector2 localOffset, Vector2 localEjectDirection, ThrusterActivation activation)
    {
        return new ParticleEmitter
        {
            EmitCondition = EmitCondition.Always,
            SpawnInterval = 0.048f,
            SpawnAccumulator = 0f,
            CarrierEntity = shipEntity,
            LocalOffset = localOffset,
            LocalEjectDirection = localEjectDirection,
            ActivationMask = activation,
            EjectSpeedMin = 88f,
            EjectSpeedMax = 150f,
            LateralDrift = 14f,
            ParticleLifeMin = 0.42f,
            ParticleLifeMax = 0.70f,
            ParticleSizeMin = 0.5f,
            ParticleSizeMax = 1.0f,
            ParticleDrag = 1.65f,
            ParticleColor = color
        };
    }

    // ── Projectiles ─────────────────────────────────────────────────

    /// <summary>Create a projectile entity fired in a given direction.</summary>
    public static Entity CreateProjectile(World world, Entity ownerEntity, Vector2 position, Vector2 direction,
        float damage, float speed, Faction ownerFaction, Color3 color,
        float lifetime, Vector2 inheritedVelocity)
    {
        float angle = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
        var projectileVelocity = direction * speed + inheritedVelocity;
        return world.Create(
            new Transform(position, angle),
            new Velocity(0f) { Linear = projectileVelocity },
            new Projectile
            {
                Damage = damage,
                Speed = speed,
                Lifetime = lifetime,
                CollisionRadius = GameConfig.ProjectileRadius,
                OwnerFaction = ownerFaction,
                OwnerEntity = ownerEntity,
                Color = color
            }
        );
    }

    // ── Surface Enemies ─────────────────────────────────────────

    /// <summary>Create a hostile fauna entity on a planet surface.</summary>
    public static Entity CreateFauna(World world, Vector2 position, float wanderAngle,
        float hullMultiplier = 1f, float damageMultiplier = 1f, Func<Vector2, bool>? canMoveTo = null)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect(14, 14, new Color3(180, 60, 60)),
            new Velocity(GameConfig.FaunaSpeed) { CanMoveTo = canMoveTo },
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
        float hullMultiplier = 1f, float damageMultiplier = 1f, Func<Vector2, bool>? canMoveTo = null)
    {
        return world.Create(
            new Transform(position),
            Sprite.ColoredRect(12, 12, new Color3(200, 100, 60)),
            new Velocity(GameConfig.BanditSpeed) { CanMoveTo = canMoveTo },
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
