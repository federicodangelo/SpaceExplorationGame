using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.AI;
using SpaceExplorationGame.ECS.Systems.Combat;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation.Base;

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
    public Entity LocalShipEntity { get; set; }
    public Entity LocalVehicleEntity { get; set; }
    public bool LocalVehicleDeployed { get; set; }

    // ── Proximity state ─────────────────────────────────────────────
    public SettlementData? NearSettlement { get; private set; }
    public bool NearShip { get; private set; }
    public bool NearVehicle { get; private set; }

    private const float BoardShipRadius = 30f;
    protected override float RespawnDelay => 2.5f;

    // ── System event outputs (planet-surface-specific) ─────────────
    public IReadOnlyList<SurfaceProjectileSpawn> EnemyProjectilesSpawnedLastUpdate =>
        _enemyAISystem?.ProjectilesSpawnedLastUpdate ?? (IReadOnlyList<SurfaceProjectileSpawn>)[];

    // ── ECS Systems (planet-surface-specific) ──────────────────────
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

        // Initialize shared ECS systems (velocity, projectiles, cleanup)
        InitCoreSystems();

        _enemyAISystem = new AvatarEnemyAISystem(EcsWorld);
        _enemyAISystem.Initialize();

        // Spawn fauna
        foreach (var (fx, fy, angle) in SurfaceData.FaunaSpawns)
        {
            EntityFactory.CreateFauna(EcsWorld, new Vector2(fx, fy), angle, canMoveTo: CanMoveToTerrain);
        }

        // Spawn bandits
        foreach (var (bx, by, angle) in SurfaceData.BanditSpawns)
        {
            EntityFactory.CreateBandit(EcsWorld, new Vector2(bx, by), angle, canMoveTo: CanMoveToTerrain);
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
        if (LocalPlayer is { } vehicleDriver && LocalVehicleDeployed)
        {
            if (EcsWorld.IsAlive(vehicleDriver.Entity) && EcsWorld.IsAlive(LocalVehicleEntity) && vehicleDriver.Data.InVehicle)
            {
                ref var avatarTf = ref EcsWorld.Get<Transform>(vehicleDriver.Entity);
                ref var vehicleTf = ref EcsWorld.Get<Transform>(LocalVehicleEntity);
                vehicleTf.Position = avatarTf.Position;
                vehicleTf.Rotation = avatarTf.Rotation;
            }
        }

        // Update proximity
        UpdateProximity();

        // Combat
        ProcessCombatResults(dt);

        // Death / respawn timer
        UpdateDeathTimer(dt);

        // Sync avatar health to PlayerData
        SyncAllPlayerHealth();

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
        float avatarSpeed = player.AvatarWalkSpeed;
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
            maxHealth: maxHp, currentHealth: curHp, canMoveTo: CanMoveToTerrain);

        // Create ship entity
        var shipEntity = EntityFactory.CreateLandedShip(EcsWorld, shipX, shipY);

        if (player.Type == PlayerType.Local)
        {
            LocalShipEntity = shipEntity;
        }

        // Deploy vehicle if it was deployed before
        if (player.HasSavedSurfacePositions && player.SavedVehicleDeployed)
        {
            var vehicleEntity = EntityFactory.CreateVehicle(EcsWorld,
                player.SavedVehicleX, player.SavedVehicleY);

            if (player.Type == PlayerType.Local)
            {
                LocalVehicleEntity = vehicleEntity;
                LocalVehicleDeployed = true;
            }
        }

        // Notify mission system
        player.NotifyPlanetLanded(StarSystem.Index, Planet.Index);

        return avatarEntity;
    }

    /// <summary>Create a vehicle entity in the simulation world.</summary>
    public Entity DeployVehicle(float x, float y)
    {
        LocalVehicleEntity = EntityFactory.CreateVehicle(EcsWorld, x, y);
        LocalVehicleDeployed = true;
        return LocalVehicleEntity;
    }

    /// <summary>Remove the vehicle entity from the simulation.</summary>
    public void StowVehicle()
    {
        if (LocalVehicleDeployed && EcsWorld.IsAlive(LocalVehicleEntity))
            EcsWorld.Destroy(LocalVehicleEntity);
        LocalVehicleDeployed = false;
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
        if (EcsWorld.IsAlive(LocalShipEntity))
        {
            var shipPos = EcsWorld.Get<Transform>(LocalShipEntity).Position;
            NearShip = Vector2.Distance(avatarPos, shipPos) < BoardShipRadius;
        }

        // Vehicle proximity
        if (LocalVehicleDeployed && EcsWorld.IsAlive(LocalVehicleEntity))
        {
            var vehiclePos = EcsWorld.Get<Transform>(LocalVehicleEntity).Position;
            NearVehicle = Vector2.Distance(avatarPos, vehiclePos) < GameConfig.VehicleMountRadius;
        }
    }

    // ── Virtual hook overrides ──────────────────────────────────────

    protected override ulong CombatRngSeed => 0xBEEFCAFE;

    protected override void OnAsteroidDestroyed(DestroyedEntity destroyed, string? resourceMsg)
    {
        if (resourceMsg != null)
        {
            CombatMessage = resourceMsg;
            CombatMessageTimer = 2.5f;
        }
        base.OnAsteroidDestroyed(destroyed, resourceMsg);
    }

    protected override string? ApplyDeathPenalties(SimulationPlayer player)
    {
        int creditsLost = (int)(player.Data.Credits * 0.1f);
        player.Data.Credits -= creditsLost;
        return creditsLost > 0 ? $"LOST {creditsLost} CREDITS" : null;
    }

    protected override void HandlePlayerRespawn()
    {
        if (LocalPlayer is not { } player) return;
        PlayerDead = false;

        // Respawn near the landed ship
        var shipPos = EcsWorld.IsAlive(LocalShipEntity)
            ? EcsWorld.Get<Transform>(LocalShipEntity).Position
            : new Vector2(SurfaceData.LandingZone.X * GameConfig.TileSize,
                          SurfaceData.LandingZone.Y * GameConfig.TileSize);

        player.Data.RecalculateAvatarStats();
        player.Data.AvatarHealth = player.Data.AvatarMaxHealth;

        var avatarEntity = EntityFactory.CreatePlayerAvatar(EcsWorld,
            shipPos.X, shipPos.Y - 20f, player.Data.AvatarWalkSpeed,
            maxHealth: player.Data.AvatarMaxHealth, currentHealth: player.Data.AvatarMaxHealth,
            canMoveTo: CanMoveToTerrain);

        player.Entity = avatarEntity;

        // Stow vehicle on respawn
        if (LocalVehicleDeployed)
            StowVehicle();
        player.Data.InVehicle = false;

        CombatMessage = "RESPAWNED";
        CombatMessageTimer = 3f;
    }

    protected override void SyncPlayerHealth(SimulationPlayer player, float hull)
    {
        player.Data.AvatarHealth = hull;
    }
}
