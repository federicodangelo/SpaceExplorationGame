using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.AI;
using SpaceExplorationGame.ECS.Systems.Combat;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Simulation for a planet surface. Manages terrain, creatures, rocks, combat, and physics.
/// The player avatar, ship, and vehicle entities are created via AddPlayer and helper methods.
/// Contains NO rendering or audio code.
/// </summary>
public class PlanetSurfaceSimulation : CombatSimulationBase
{
    // ── Data ────────────────────────────────────────────────────────
    public StarSystemData StarSystem { get; }
    public PlanetData Planet { get; }
    public PlanetSurfaceData SurfaceData { get; private set; } = null!;

    // ── Entities ────────────────────────────────────────────────────
    public Entity ShipEntity { get; set; }
    public Entity VehicleEntity { get; set; }
    public bool VehicleDeployed { get; set; }

    // ── Proximity state ─────────────────────────────────────────────
    public SettlementData? NearSettlement { get; private set; }
    public bool NearShip { get; private set; }
    public bool NearVehicle { get; private set; }

    private const float BoardShipRadius = 30f;
    private const float RespawnDelay = 2.5f;

    // ── System event outputs ────────────────────────────────────────
    public IReadOnlyList<DamageEvent> DamageEventsLastUpdate =>
        _projectileSystem?.DamageEventsLastUpdate ?? (IReadOnlyList<DamageEvent>)[];
    public IReadOnlyList<DestroyedEntity> DestroyedEntitiesLastUpdate =>
        _projectileSystem?.DestroyedLastUpdate ?? (IReadOnlyList<DestroyedEntity>)[];
    public IReadOnlyList<SurfaceProjectileSpawn> EnemyProjectilesSpawnedLastUpdate =>
        _enemyAISystem?.ProjectilesSpawnedLastUpdate ?? (IReadOnlyList<SurfaceProjectileSpawn>)[];

    // ── ECS Systems ─────────────────────────────────────────────────
    private DependentEntityCleanupSystem _dependentEntityCleanupSystem = null!;
    private VelocitySystem _velocitySystem = null!;
    private ProjectileSystem _projectileSystem = null!;
    private AvatarEnemyAISystem _enemyAISystem = null!;

    private readonly PlanetSurfaceData? _preGeneratedSurfaceData;

    public PlanetSurfaceSimulation(Game game, StarSystemData starSystem, PlanetData planet,
        PlanetSurfaceData? preGeneratedSurfaceData = null,
        ISimulation? parent = null)
        : base(game, parent)
    {
        StarSystem = starSystem;
        Planet = planet;
        _preGeneratedSurfaceData = preGeneratedSurfaceData;
    }

    public override void Create()
    {
        // Generate surface
        SurfaceData = _preGeneratedSurfaceData
            ?? _game.WorldGenerator.GeneratePlanetSurface(_game.Seeds, StarSystem, Planet);

        // Initialize ECS systems
        _dependentEntityCleanupSystem = new DependentEntityCleanupSystem(EcsWorld);
        _dependentEntityCleanupSystem.Initialize();

        _velocitySystem = new VelocitySystem(EcsWorld);
        _velocitySystem.Initialize();

        _projectileSystem = new ProjectileSystem(EcsWorld);
        _projectileSystem.Initialize();

        _enemyAISystem = new AvatarEnemyAISystem(EcsWorld);
        _enemyAISystem.Initialize();

        // Spawn fauna
        foreach (var (fx, fy, angle) in SurfaceData.FaunaSpawns)
        {
            var fauna = EntityFactory.CreateFauna(EcsWorld, new Vector2(fx, fy), angle);
            if (EcsWorld.Has<Velocity>(fauna))
            {
                ref var faunaVelocity = ref EcsWorld.Get<Velocity>(fauna);
                faunaVelocity.CanMoveTo = CanMoveToTerrain;
            }
        }

        // Spawn bandits
        foreach (var (bx, by, angle) in SurfaceData.BanditSpawns)
        {
            var bandit = EntityFactory.CreateBandit(EcsWorld, new Vector2(bx, by), angle);
            if (EcsWorld.Has<Velocity>(bandit))
            {
                ref var banditVelocity = ref EcsWorld.Get<Velocity>(bandit);
                banditVelocity.CanMoveTo = CanMoveToTerrain;
            }
        }

        // Spawn mineable rocks
        foreach (var (rx, ry, resource, amount, size, hp) in SurfaceData.RockSpawns)
        {
            EntityFactory.CreateSurfaceRock(EcsWorld, new Vector2(rx, ry), size, hp, resource, amount);
        }
    }

    public override void Destroy()
    {
        base.Destroy();
    }

    public override void Update(UpdateContext ctx)
    {
        float dt = ctx.Dt;

        _dependentEntityCleanupSystem.Update(in dt);

        // AI (enemy movement + shooting)
        _enemyAISystem.Update(in dt);

        // Physics (moves all entities with velocity: projectiles, player, enemies)
        _velocitySystem.Update(in dt);

        // Sync vehicle position/rotation when driving
        if (LocalPlayer is { } vehicleDriver && VehicleDeployed)
        {
            if (EcsWorld.IsAlive(vehicleDriver.Entity) && EcsWorld.IsAlive(VehicleEntity) && vehicleDriver.Data.InVehicle)
            {
                ref var avatarTf = ref EcsWorld.Get<Transform>(vehicleDriver.Entity);
                ref var vehicleTf = ref EcsWorld.Get<Transform>(VehicleEntity);
                vehicleTf.Position = avatarTf.Position;
                vehicleTf.Rotation = avatarTf.Rotation;
            }
        }

        // Update proximity
        UpdateProximity();

        // Combat
        UpdateCombat(dt);

        // Death timer / auto-respawn
        if (PlayerDead)
        {
            RespawnTimer -= dt;
            if (RespawnTimer <= 0)
            {
                HandleAvatarRespawn();
            }
        }

        // Sync avatar health to PlayerData
        foreach (var player in Players)
        {
            if (EcsWorld.IsAlive(player.Entity) && EcsWorld.Has<Health>(player.Entity))
            {
                var health = EcsWorld.Get<Health>(player.Entity);
                player.Data.AvatarHealth = health.Hull;
            }
        }

        // Tick message timers
        UpdateCombatTimers(dt);
    }

    protected override Entity CreatePlayerEntity(PlayerData player, AddContext ctx)
    {
        int lzTileX = ctx.LandingTileX >= 0 ? ctx.LandingTileX : SurfaceData.LandingZone.X;
        int lzTileY = ctx.LandingTileY >= 0 ? ctx.LandingTileY : SurfaceData.LandingZone.Y;
        float lzX = lzTileX * GameConfig.TileSize;
        float lzY = lzTileY * GameConfig.TileSize;

        // Recalculate avatar stats
        player.RecalculateAvatarStats();
        float avatarSpeed = 200f + player.GetCombinedAvatarStats().WalkSpeed;
        float maxHp = player.AvatarMaxHealth;
        float curHp = player.AvatarHealth;

        float playerStartX, playerStartY;
        float shipX, shipY;

        if (player.HasSavedSurfacePositions)
        {
            shipX = player.SavedShipX;
            shipY = player.SavedShipY;
            playerStartX = player.SavedPlayerX;
            playerStartY = player.SavedPlayerY;
        }
        else
        {
            shipX = lzX + 30;
            shipY = lzY;
            playerStartX = lzX;
            playerStartY = lzY;
        }

        var avatarEntity = EntityFactory.CreatePlayerAvatar(EcsWorld, playerStartX, playerStartY, avatarSpeed,
            maxHealth: maxHp, currentHealth: curHp);
        ref var avatarVelocity = ref EcsWorld.Get<Velocity>(avatarEntity);
        avatarVelocity.CanMoveTo = CanMoveToTerrain;

        // Create ship entity
        ShipEntity = EntityFactory.CreateLandedShip(EcsWorld, shipX, shipY);

        // Deploy vehicle if it was deployed before
        if (player.HasSavedSurfacePositions && player.SavedVehicleDeployed)
        {
            VehicleEntity = EntityFactory.CreateVehicle(EcsWorld,
                player.SavedVehicleX, player.SavedVehicleY);
            VehicleDeployed = true;
        }

        // Notify mission system
        player.NotifyPlanetLanded(StarSystem.Index, Planet.Index);

        return avatarEntity;
    }

    /// <summary>Create a vehicle entity in the simulation world.</summary>
    public Entity DeployVehicle(float x, float y)
    {
        VehicleEntity = EntityFactory.CreateVehicle(EcsWorld, x, y);
        VehicleDeployed = true;
        return VehicleEntity;
    }

    /// <summary>Remove the vehicle entity from the simulation.</summary>
    public void StowVehicle()
    {
        if (VehicleDeployed && EcsWorld.IsAlive(VehicleEntity))
            EcsWorld.Destroy(VehicleEntity);
        VehicleDeployed = false;
    }

    // ── Terrain collision ───────────────────────────────────────────

    public bool CanMoveToTerrain(Vector2 newPos)
    {
        int tileX = (int)(newPos.X / GameConfig.TileSize);
        int tileY = (int)(newPos.Y / GameConfig.TileSize);
        if (tileX < 0 || tileX >= SurfaceData.Width || tileY < 0 || tileY >= SurfaceData.Height)
            return false;
        var terrain = SurfaceData.Tiles[tileX, tileY];
        return SurfaceTerrainRules.IsTraversable(terrain);
    }

    // ── Proximity ───────────────────────────────────────────────────

    private void UpdateProximity()
    {
        NearSettlement = null;
        NearShip = false;
        NearVehicle = false;

        if (LocalPlayer is not { } local) return;
        if (!EcsWorld.IsAlive(local.Entity)) return;

        var avatarPos = EcsWorld.Get<Transform>(local.Entity).Position;

        // Settlement proximity
        foreach (var settlement in SurfaceData.Settlements)
        {
            float sx = (settlement.TileRect.X + settlement.TileRect.Width / 2f) * GameConfig.TileSize;
            float sy = (settlement.TileRect.Y + settlement.TileRect.Height / 2f) * GameConfig.TileSize;
            float dist = Vector2.Distance(avatarPos, new Vector2(sx, sy));
            float settlementRadius = Math.Max(settlement.TileRect.Width, settlement.TileRect.Height) * GameConfig.TileSize / 2f + 20f;
            if (dist < settlementRadius)
            {
                NearSettlement = settlement;
                break;
            }
        }

        // Ship proximity
        if (EcsWorld.IsAlive(ShipEntity))
        {
            var shipPos = EcsWorld.Get<Transform>(ShipEntity).Position;
            NearShip = Vector2.Distance(avatarPos, shipPos) < BoardShipRadius;
        }

        // Vehicle proximity
        if (VehicleDeployed && EcsWorld.IsAlive(VehicleEntity))
        {
            var vehiclePos = EcsWorld.Get<Transform>(VehicleEntity).Position;
            NearVehicle = Vector2.Distance(avatarPos, vehiclePos) < GameConfig.VehicleMountRadius;
        }
    }

    // ── Combat ──────────────────────────────────────────────────────

    private void UpdateCombat(float dt)
    {
        _projectileSystem.Update(in dt);

        // Process damage events
        foreach (var evt in _projectileSystem.DamageEventsLastUpdate)
        {
            bool localPlayerInvolved =
                (evt.OwnerFaction == Faction.Player && IsLocalPlayerEntity(evt.OwnerEntity))
                || (EcsWorld.IsAlive(evt.Target) && EcsWorld.Has<PlayerControlled>(evt.Target)
                    && IsLocalPlayerEntity(evt.Target));
            if (localPlayerInvolved)
                CombatMusicTimer = GameConfig.CombatMusicDelay;
        }

        // Process destroyed entities
        var combatRng = new SeededRandom((ulong)(_game.GlobalTime * 1000) ^ 0xBEEFCAFE);
        foreach (var destroyed in _projectileSystem.DestroyedLastUpdate)
        {
            if (destroyed.Asteroid.HasValue)
            {
                var rock = destroyed.Asteroid.Value;
                if (destroyed.KillerFaction == Faction.Player
                    && FindLocalPlayerByEntity(destroyed.KillerEntity) is { } miner)
                {
                    var playerData = miner.Data;
                    int added = playerData.AddCargo(rock.Resource, rock.ResourceAmount);
                    var resInfo = ResourceCatalog.Get(rock.Resource);
                    if (added > 0)
                    {
                        _combatMessage = $"+{added} {resInfo.Name.ToUpper()}";
                        _combatMessageTimer = 2.5f;
                        playerData.NotifyResourceMined(rock.Resource, added);
                    }
                    else
                    {
                        _combatMessage = "CARGO FULL!";
                        _combatMessageTimer = 2.5f;
                    }
                }

                if (EcsWorld.IsAlive(destroyed.Entity))
                    EcsWorld.Destroy(destroyed.Entity);
            }
            else if (destroyed.Faction == Faction.Player)
            {
                if (IsLocalPlayerEntity(destroyed.Entity))
                    HandleAvatarDeath();
            }
            else
            {
                // Enemy died — apply loot if local player killed it
                if (destroyed.KillerFaction == Faction.Player && destroyed.Loot.HasValue
                    && IsLocalPlayerEntity(destroyed.KillerEntity))
                {
                    _combatMessage = CombatHelper.ProcessLootDrop(_game, destroyed.Loot.Value, combatRng);
                    _combatMessageTimer = 3f;
                }

                if (EcsWorld.IsAlive(destroyed.Entity))
                    EcsWorld.Destroy(destroyed.Entity);
            }
        }
    }

    private void HandleAvatarDeath()
    {
        if (LocalPlayer is not { } player) return;
        PlayerDead = true;
        RespawnTimer = RespawnDelay;

        if (EcsWorld.IsAlive(player.Entity))
            EcsWorld.Destroy(player.Entity);

        int creditsLost = (int)(player.Data.Credits * 0.1f);
        player.Data.Credits -= creditsLost;

        _combatMessage = creditsLost > 0 ? $"LOST {creditsLost} CREDITS" : null;
        _combatMessageTimer = RespawnDelay;
    }

    private void HandleAvatarRespawn()
    {
        if (LocalPlayer is not { } player) return;
        PlayerDead = false;

        // Respawn near the landed ship
        var shipPos = EcsWorld.IsAlive(ShipEntity)
            ? EcsWorld.Get<Transform>(ShipEntity).Position
            : new Vector2(SurfaceData.LandingZone.X * GameConfig.TileSize,
                          SurfaceData.LandingZone.Y * GameConfig.TileSize);

        player.Data.RecalculateAvatarStats();
        float avatarSpeed = 200f + player.Data.GetCombinedAvatarStats().WalkSpeed;
        player.Data.AvatarHealth = player.Data.AvatarMaxHealth;

        var avatarEntity = EntityFactory.CreatePlayerAvatar(EcsWorld,
            shipPos.X, shipPos.Y - 20f, avatarSpeed,
            maxHealth: player.Data.AvatarMaxHealth, currentHealth: player.Data.AvatarMaxHealth);
        ref var avatarVelocity = ref EcsWorld.Get<Velocity>(avatarEntity);
        avatarVelocity.CanMoveTo = CanMoveToTerrain;

        player.Entity = avatarEntity;

        // Stow vehicle on respawn
        if (VehicleDeployed)
            StowVehicle();
        player.Data.InVehicle = false;

        _combatMessage = "RESPAWNED";
        _combatMessageTimer = 3f;
    }
}
