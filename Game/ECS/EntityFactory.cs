using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
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
            Sprite.Build((int)(displayRadius * 2), (int)(displayRadius * 2)),
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
            Sprite.Build((int)(planet.Radius * 2), (int)(planet.Radius * 2)),
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
            Sprite.Build((int)(moon.Radius * 2), (int)(moon.Radius * 2)),
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
            Sprite.Build(24, 24),
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
            Sprite.Build((int)(size + 4), (int)(size + 4)),
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
            Sprite.Build(spriteSize, spriteSize),
            new Velocity(maxSpeed, rotationSpeed),
            new ShipComponent(Faction.Player, maxSpeed, rotationSpeed, acceleration, brakeMultiplier,
                weapons),
            ShipInputComponent.Default(),
            new PlayerControlled(),
            new Health(maxHull, maxShield,
                ShipConfig.BaseShieldRegenRate, ShipConfig.ShieldRegenDelay)
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
            Sprite.Build(12, 12),
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
            new Transform(x, y), // 0° = horizontal (nose right), matching the landing animation's touchdown orientation
            Sprite.Build(20, 16),
            new Label { Text = "SHIP", OffsetY = 14 }
        );
    }

    /// <summary>Create the player's vehicle entity on a planet surface.</summary>
    public static Entity CreateVehicle(World world, float x, float y)
    {
        return world.Create(
            new Transform(x, y),
            Sprite.Build(16, 16),
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

        return world.Create(
            new Transform(position),
            Sprite.Build((int)(size + 4), (int)(size + 4)),
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
        var (thrusterColor, aiConfig, shieldRegenRate, healthMultiplier) = GetNpcShipProfile(spawn);

        var ship = world.Create(
            new Transform(spawn.Position, spawn.Rotation),
            Sprite.Build(stats.SpriteSize, stats.SpriteSize),
            new Velocity(stats.MaxSpeed, stats.RotationSpeed),
            new ShipComponent(spawn.Faction, stats.MaxSpeed, stats.RotationSpeed,
                stats.Acceleration, ShipConfig.ShipBrakeMultiplier,
                spawn.Weapons),
            ShipInputComponent.Default(),
            new Health(stats.MaxHull * healthMultiplier, stats.MaxShield * healthMultiplier, shieldRegenRate, ShipConfig.ShieldRegenDelay),
            new EnemyAI { Config = aiConfig, State = AIState.Patrol }
        );

        if (spawn.LootCredits > 0)
        {
            world.Add(ship, new LootDrop
            {
                MinCredits = Math.Max(1, spawn.LootCredits / 2),
                MaxCredits = spawn.LootCredits * 2,
                ResourceDropChance = CombatConfig.ResourceDropChance,
                PartDropChance = CombatConfig.PartDropChance,
                DangerLevel = spawn.DangerLevel
            });
        }

        CreateShipThrusterEmitters(world, ship, stats.SpriteSize, thrusterColor);
        return ship;
    }

    private static (Color3 Thruster, EnemyAIConfig Ai, float ShieldRegen, float HealthMultiplier) GetNpcShipProfile(NpcShipSpawnData spawn)
    {
        float baseInaccuracy = DangerConfig.GetInnacuracy(spawn.DangerLevel);
        float healthMultiplier = DangerConfig.GetHealthMultiplier(spawn.DangerLevel);

        return spawn.Faction switch
        {
            Faction.Pirate => (
                new Color3(255, 130, 110),
                new EnemyAIConfig(
                    Faction: Faction.Pirate,
                    DetectRange: NpcConfig.EnemyDetectRange,
                    LootCredits: spawn.LootCredits,
                    EngageDistance: NpcConfig.EnemyEngageDistance,
                    FleeHealthPercent: 0.0f,   // pirates fight to the death
                    AimInaccuracyRadius: baseInaccuracy),
                ShipConfig.BaseShieldRegenRate * 0.5f,
                healthMultiplier),
            Faction.Trader => (
                new Color3(255, 210, 120),
                new EnemyAIConfig(
                    Faction: Faction.Trader,
                    DetectRange: 300f,
                    LootCredits: 0,
                    EngageDistance: 0f,
                    FleeHealthPercent: 0.25f, // traders flee when hull drops below 25%
                    AimInaccuracyRadius: 0f),   // traders don't shoot
                ShipConfig.BaseShieldRegenRate * 0.5f,
                healthMultiplier),
            Faction.Patrol => (
                new Color3(130, 200, 255),
                new EnemyAIConfig(
                    Faction: Faction.Patrol,
                    DetectRange: NpcConfig.EnemyDetectRange * 1.5f,
                    LootCredits: 0,
                    EngageDistance: NpcConfig.EnemyEngageDistance,
                    FleeHealthPercent: 0f, // patrols are disciplined – fight to the death, no fleeing
                    AimInaccuracyRadius: baseInaccuracy * 0.6f),  // patrols are trained – tighter spread
                ShipConfig.BaseShieldRegenRate,
                healthMultiplier),
            _ => throw new ArgumentException($"Unsupported NPC faction: {spawn.Faction}")
        };
    }

    private static void CreateShipThrusterEmitters(World world, Entity shipEntity, int shipSize, Color3 color)
    {
        Vector2 shipPos = world.Get<Transform>(shipEntity).Position;
        float half = shipSize * 0.5f;

        // Ships taper toward the nose, so the forward fuselage is much narrower than the wing root.
        // Use separate Y offsets for front vs rear side thrusters so they sit on the visible hull edge
        // rather than floating out in empty space beyond the wings.
        float sideYFront = shipSize * 0.18f;   // forward hull edge (cockpit / canard region)
        float sideYRear = shipSize * 0.30f;   // rearward hull / wing-root junction
        float sideFrontX = shipSize * 0.15f;   // slightly ahead of centre
        float sideRearX = -shipSize * 0.22f;  // behind centre, at the wing-root band

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateMainThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(-half * 1.1f, 0f),
            localEjectDirection: new Vector2(-1f, 0f),
            activation: ThrusterActivation.Forward));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(half * 0.95f, 0f),
            localEjectDirection: new Vector2(1f, 0f),
            activation: ThrusterActivation.Backward));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(sideFrontX, -sideYFront),
            localEjectDirection: new Vector2(0f, -1f),
            activation: ThrusterActivation.StrafeRight | ThrusterActivation.RotateRight));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(sideRearX, -sideYRear),
            localEjectDirection: new Vector2(0f, -1f),
            activation: ThrusterActivation.StrafeRight | ThrusterActivation.RotateLeft));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(sideFrontX, sideYFront),
            localEjectDirection: new Vector2(0f, 1f),
            activation: ThrusterActivation.StrafeLeft | ThrusterActivation.RotateLeft));

        world.Create(new Transform(shipPos), new OwnedBy(shipEntity), CreateRcsThrusterEmitter(shipEntity, color,
            localOffset: new Vector2(sideRearX, sideYRear),
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
                CollisionRadius = CombatConfig.ProjectileRadius,
                OwnerFaction = ownerFaction,
                OwnerEntity = ownerEntity,
                Color = color
            }
        );
    }

    // ── Surface NPCs ──────────────────────────────────────────────

    /// <summary>Create a surface NPC entity on foot (pirate, trader, or patrol).</summary>
    public static Entity CreateSurfaceNpc(World world, Vector2 position, float wanderAngle,
        Faction faction, int dangerLevel, Func<Vector2, bool>? canMoveTo = null)
    {
        // Scale stats by danger level
        float healthMultiplier = DangerConfig.GetHealthMultiplier(dangerLevel);
        float damageMultiplier = DangerConfig.GetDamageMultiplier(dangerLevel);

        var (config, loot, spriteSize) = GetSurfaceNpcProfile(faction, dangerLevel, damageMultiplier);

        return world.Create(
            new Transform(position),
            Sprite.Build(spriteSize, spriteSize),
            new Velocity(config.MoveSpeed) { CanMoveTo = canMoveTo },
            new Health(CombatConfig.BanditBaseHull * healthMultiplier),
            new SurfaceAI
            {
                Config = config,
                State = faction == Faction.Pirate ? AIState.Patrol : AIState.Idle,
                FireCooldown = 0f,
                WanderAngle = wanderAngle,
                WanderTimer = 3f
            },
            loot
        );
    }

    private static (SurfaceAIConfig Config, LootDrop Loot, int SpriteSize) GetSurfaceNpcProfile(
        Faction faction, int dangerLevel, float damageMult)
    {
        return faction switch
        {
            Faction.Pirate => (
                new SurfaceAIConfig(
                    Faction: Faction.Pirate,
                    MoveSpeed: CombatConfig.BanditSpeed,
                    DetectRange: CombatConfig.BanditDetectRange,
                    AttackRange: CombatConfig.BanditAttackRange,
                    FireRate: CombatConfig.BanditFireRate,
                    WeaponDamage: CombatConfig.BanditBaseDamage * damageMult,
                    ProjectileSpeed: CombatConfig.BanditProjectileSpeed),
                new LootDrop
                {
                    MinCredits = CombatConfig.SurfaceLootCreditsMin * 2,
                    MaxCredits = CombatConfig.SurfaceLootCreditsMax * 2,
                    ResourceDropChance = 0.4f,
                    PartDropChance = 0.05f,
                    DangerLevel = dangerLevel
                },
                12),
            Faction.Trader => (
                new SurfaceAIConfig(
                    Faction: Faction.Trader,
                    MoveSpeed: CombatConfig.BanditSpeed * 0.7f, // traders walk slower
                    DetectRange: 0f, // traders don't fight
                    AttackRange: 0f,
                    FireRate: 0f,
                    WeaponDamage: 0f,
                    ProjectileSpeed: 0f),
                new LootDrop
                {
                    MinCredits = CombatConfig.SurfaceLootCreditsMin,
                    MaxCredits = CombatConfig.SurfaceLootCreditsMax,
                    ResourceDropChance = 0.6f,
                    PartDropChance = 0f,
                    DangerLevel = dangerLevel
                },
                12),
            Faction.Patrol => (
                new SurfaceAIConfig(
                    Faction: Faction.Patrol,
                    MoveSpeed: CombatConfig.BanditSpeed * 0.9f,
                    DetectRange: CombatConfig.BanditDetectRange * 1.3f,
                    AttackRange: CombatConfig.BanditAttackRange,
                    FireRate: CombatConfig.BanditFireRate * 0.9f,
                    WeaponDamage: CombatConfig.BanditBaseDamage * damageMult * 0.8f,
                    ProjectileSpeed: CombatConfig.BanditProjectileSpeed * 1.1f),
                new LootDrop
                {
                    MinCredits = 0,
                    MaxCredits = 0,
                    ResourceDropChance = 0f,
                    PartDropChance = 0f,
                    DangerLevel = dangerLevel
                },
                12),
            _ => throw new ArgumentException($"Unsupported surface NPC faction: {faction}")
        };
    }

    /// <summary>Create a landed NPC ship entity on a planet surface (static marker with animation state).</summary>
    public static Entity CreateLandedNpcShip(World world, Vector2 position, Faction faction,
        bool isLanding, float animProgress = 0f)
    {
        int size = (int)NpcConfig.SurfaceNpcShipSize;
        return world.Create(
            new Transform(position),
            Sprite.Build(size, size),
            new LandedNpcShip
            {
                OwnerNpc = Entity.Null,
                AnimProgress = animProgress,
                IsLanding = isLanding,
                Faction = faction
            }
        );
    }
}
