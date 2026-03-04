using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
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
    public PlanetSurfaceData SurfaceData { get; private init; }

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
    /// <summary>All avatar projectiles spawned last tick (player + NPC). Read by state for SFX; use Faction to distinguish.</summary>
    public IReadOnlyList<SurfaceProjectileSpawn> AvatarProjectilesSpawnedLastUpdate =>
        _avatarSystem?.ProjectilesSpawnedLastUpdate ?? (IReadOnlyList<SurfaceProjectileSpawn>)[];

    // ── ECS Systems (planet-surface-specific) ──────────────────────
    private AvatarSystem _avatarSystem = null!;
    private VehicleSystem _vehicleSystem = null!;
    private AvatarEnemyAISystem _enemyAISystem = null!;
    private SurfaceNpcManager _surfaceNpcManager = null!;



    public PlanetSurfaceSimulation(Game game, StarSystemData starSystem, PlanetData planet,
        PlanetSurfaceData? preGeneratedSurfaceData = null, ISimulation? parent = null)
        : base(game, parent)
    {
        StarSystem = starSystem;
        Planet = planet;
        SurfaceData = preGeneratedSurfaceData
            ?? _game.UniverseGenerator.GeneratePlanetSurface(StarSystem, Planet);
    }

    public override void Create()
    {
        SpawnRocks();

        // Initialize shared ECS systems (velocity, projectiles, cleanup)
        InitCoreSystems();

        _avatarSystem = new AvatarSystem(EcsWorld);
        _avatarSystem.Initialize();

        _vehicleSystem = new VehicleSystem(EcsWorld);
        _vehicleSystem.Initialize();

        _enemyAISystem = new AvatarEnemyAISystem(EcsWorld);
        _enemyAISystem.Initialize();

        // Initialize surface NPC manager and spawn initial wave
        _surfaceNpcManager = new SurfaceNpcManager(EcsWorld, SurfaceData,
            SurfaceData.NpcSpawnConfig, CanMoveToTerrain);
        _surfaceNpcManager.SpawnInitialWave();
    }

    public override void Destroy()
    {
        base.Destroy();
    }

    public override void Update(UpdateContext ctx)
    {
        float dt = ctx.Dt;
        var t = _debugTimer;
        t.Begin();

        t.Time("Cleanup", () => _dependentEntityCleanupSystem.Update(in dt));
        t.Time("Enemy AI", () => _enemyAISystem.Update(in dt));
        t.Time("Avatars", () => _avatarSystem.Update(in dt));
        t.Time("Vehicles", () => _vehicleSystem.Update(in dt));
        t.Time("Physics", () => _velocitySystem.Update(in dt));
        t.Time("Surface NPCs", () => _surfaceNpcManager.Update(dt));

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

        t.Time("Proximity", UpdateProximity);
        t.Time("Combat", () => ProcessCombatResults(dt));

        // Death / respawn timer
        UpdateDeathTimer(dt);

        // Sync avatar health to PlayerData
        SyncAllPlayerHealth();

        // Tick message timers
        UpdateCombatTimers(dt);
    }


    public override IReadOnlyList<string>? GetDebugInfo()
    {
        _debugInfo.Begin();
        _debugInfo.Add($"Planet: {Planet.Name}  Type: {Planet.Type}");
        _debugInfo.Add($"Players: {Players.Count}");
        _debugInfo.Add($"NearShip: {NearShip}  NearVehicle: {NearVehicle}");
        return _debugInfo.Entries;
    }

    protected override Entity CreatePlayerEntity(PlayerData player, AddContext ctx)
    {
        int lzTileX = ctx.LandingTileX >= 0 ? ctx.LandingTileX : SurfaceData.LandingZone.X;
        int lzTileY = ctx.LandingTileY >= 0 ? ctx.LandingTileY : SurfaceData.LandingZone.Y;

        // If the landing tile falls inside a settlement, push spawn position below it
        foreach (var s in SurfaceData.Settlements)
        {
            if (lzTileX >= s.TileRect.X && lzTileX < s.TileRect.X + s.TileRect.Width &&
                lzTileY >= s.TileRect.Y && lzTileY < s.TileRect.Y + s.TileRect.Height)
            {
                lzTileX = s.TileRect.CenterX;
                lzTileY = s.TileRect.Y + s.TileRect.Height;
                break;
            }
        }

        float lzX = lzTileX * WindowConfig.TileSize;
        float lzY = lzTileY * WindowConfig.TileSize;

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
            shipX = lzX;
            shipY = lzY;
            playerStartX = lzX;
            playerStartY = lzY;
        }

        var avatarStats = player.GetCombinedAvatarStats();
        var avatarEntity = EntityFactory.CreatePlayerAvatar(EcsWorld, playerStartX, playerStartY, avatarSpeed,
            maxHealth: maxHp, currentHealth: curHp, canMoveTo: CanMoveToTerrain,
            weaponDamage: CombatConfig.BaseAvatarWeaponDamage + avatarStats.WeaponDamage,
            weaponFireRate: CombatConfig.AvatarFireRate,
            weaponProjectileSpeed: CombatConfig.AvatarProjectileSpeed);

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
        player.Missions.NotifyPlanetLanded(StarSystem.Index, Planet.Index);

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

    /// <summary>Snap a player avatar into the vehicle and configure it for driving.</summary>
    public void MountVehicle(SimulationPlayer player)
    {
        var vStats = player.Data.GetCombinedVehicleStats();

        if (!LocalVehicleDeployed)
        {
            var shipTf = EcsWorld.Get<Transform>(LocalShipEntity);
            DeployVehicle(shipTf.Position.X, shipTf.Position.Y);
        }

        ref var avatarTf = ref EcsWorld.Get<Transform>(player.Entity);
        ref var vTf = ref EcsWorld.Get<Transform>(LocalVehicleEntity);
        avatarTf.Position = vTf.Position;
        avatarTf.Rotation = vTf.Rotation;

        if (EcsWorld.Has<Velocity>(player.Entity))
        {
            ref var vel = ref EcsWorld.Get<Velocity>(player.Entity);
            vel.MaxSpeed = vStats.MaxSpeed > 0 ? vStats.MaxSpeed : AvatarConfig.VehicleMaxSpeed;
            vel.MaxRotationSpeed = vStats.RotationSpeed > 0 ? vStats.RotationSpeed : AvatarConfig.VehicleRotationSpeed;
        }

        if (EcsWorld.Has<AvatarComponent>(player.Entity))
        {
            ref var avatar = ref EcsWorld.Get<AvatarComponent>(player.Entity);
            avatar.InVehicle = true;
        }

        var vehicleComp = new VehicleComponent
        {
            Acceleration = vStats.Acceleration > 0 ? vStats.Acceleration : AvatarConfig.VehicleAcceleration,
            MaxSpeed = vStats.MaxSpeed > 0 ? vStats.MaxSpeed : AvatarConfig.VehicleMaxSpeed,
            RotationSpeed = vStats.RotationSpeed > 0 ? vStats.RotationSpeed : AvatarConfig.VehicleRotationSpeed,
            Friction = AvatarConfig.VehicleFriction + vStats.Friction,
            BrakeMultiplier = AvatarConfig.VehicleBrakeMultiplier,
        };
        if (EcsWorld.Has<VehicleComponent>(player.Entity))
            EcsWorld.Get<VehicleComponent>(player.Entity) = vehicleComp;
        else
            EcsWorld.Add(player.Entity, vehicleComp);

        player.Data.InVehicle = true;
    }

    /// <summary>Detach a player avatar from the vehicle and restore walking configuration.</summary>
    public void DismountVehicle(SimulationPlayer player, float walkSpeed)
    {
        ref var avatarTf = ref EcsWorld.Get<Transform>(player.Entity);
        if (LocalVehicleDeployed)
        {
            ref var vehicleTf = ref EcsWorld.Get<Transform>(LocalVehicleEntity);
            avatarTf.Position = vehicleTf.Position + new Vector2(20, 0);
        }
        avatarTf.Rotation = 0f;

        if (EcsWorld.Has<Velocity>(player.Entity))
        {
            ref var vel = ref EcsWorld.Get<Velocity>(player.Entity);
            vel.MaxSpeed = walkSpeed;
            vel.MaxRotationSpeed = 0f;
            vel.Linear = Vector2.Zero;
            vel.Acceleration = Vector2.Zero;
            vel.RotationVelocity = 0f;
        }

        if (EcsWorld.Has<AvatarComponent>(player.Entity))
        {
            ref var avatar = ref EcsWorld.Get<AvatarComponent>(player.Entity);
            avatar.InVehicle = false;
        }

        if (EcsWorld.Has<VehicleComponent>(player.Entity))
            EcsWorld.Remove<VehicleComponent>(player.Entity);

        player.Data.InVehicle = false;
    }

    // ── Private spawn helpers ────────────────────────────────────────


    private void SpawnRocks()
    {
        foreach (var spawn in SurfaceData.RockSpawns)
            EntityFactory.CreateSurfaceRock(EcsWorld, new Vector2(spawn.X, spawn.Y), spawn.Size, spawn.Hp, spawn.Resource, spawn.Amount);
    }

    // ── Terrain collision ───────────────────────────────────────────

    private bool CanMoveToTerrain(Vector2 newPos)
    {
        int tileX = (int)(newPos.X / WindowConfig.TileSize);
        int tileY = (int)(newPos.Y / WindowConfig.TileSize);
        if (tileX < 0 || tileX >= SurfaceData.Width || tileY < 0 || tileY >= SurfaceData.Height)
            return false;
        return SurfaceTerrainRules.IsTraversable(SurfaceData.Tiles[tileX, tileY]);
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
            float sx = (settlement.TileRect.X + settlement.TileRect.Width / 2f) * WindowConfig.TileSize;
            float sy = (settlement.TileRect.Y + settlement.TileRect.Height / 2f) * WindowConfig.TileSize;
            float dist = Vector2.Distance(avatarPos, new Vector2(sx, sy));
            float settlementRadius = Math.Max(settlement.TileRect.Width, settlement.TileRect.Height) * WindowConfig.TileSize / 2f + 20f;
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
            NearVehicle = Vector2.Distance(avatarPos, vehiclePos) < AvatarConfig.VehicleMountRadius;
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

    protected override void OnEnemyDestroyed(DestroyedEntity destroyed)
    {
        // Notify spawn manager so it can schedule a replacement
        _surfaceNpcManager.NotifyDestroyed(destroyed.Faction, destroyed.Entity);

        if (EcsWorld.IsAlive(destroyed.Entity))
            EcsWorld.Destroy(destroyed.Entity);
    }

    protected override string? ApplyDeathPenalties(SimulationPlayer player)
    {
        int creditsLost = (int)(player.Data.Credits * CombatConfig.DeathCreditsLossPercent);
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
            : new Vector2(SurfaceData.LandingZone.X * WindowConfig.TileSize,
                          SurfaceData.LandingZone.Y * WindowConfig.TileSize);

        player.Data.RecalculateAvatarStats();
        player.Data.AvatarHealth = player.Data.AvatarMaxHealth;

        var respawnAvatarStats = player.Data.GetCombinedAvatarStats();
        var avatarEntity = EntityFactory.CreatePlayerAvatar(EcsWorld,
            shipPos.X, shipPos.Y - 20f, player.Data.AvatarWalkSpeed,
            maxHealth: player.Data.AvatarMaxHealth, currentHealth: player.Data.AvatarMaxHealth,
            canMoveTo: CanMoveToTerrain,
            weaponDamage: CombatConfig.BaseAvatarWeaponDamage + respawnAvatarStats.WeaponDamage,
            weaponFireRate: CombatConfig.AvatarFireRate,
            weaponProjectileSpeed: CombatConfig.AvatarProjectileSpeed);

        player.Entity = avatarEntity;

        // Stow vehicle on respawn
        if (LocalVehicleDeployed)
            StowVehicle();
        player.Data.InVehicle = false;

        CombatMessage = "RESPAWNED";
        CombatMessageTimer = 3f;
    }

    public void SyncPlayerAvatarComponent(SimulationPlayer player)
    {
        if (!EcsWorld.IsAlive(player.Entity)) return;
        if (!EcsWorld.Has<AvatarComponent>(player.Entity)) return;
        var avatarStats = player.Data.GetCombinedAvatarStats();
        ref var avatar = ref EcsWorld.Get<AvatarComponent>(player.Entity);
        avatar.WeaponDamage = CombatConfig.BaseAvatarWeaponDamage + avatarStats.WeaponDamage;
    }

    public void SyncPlayerVehicleComponent(SimulationPlayer player)
    {
        if (!EcsWorld.IsAlive(player.Entity)) return;
        if (!EcsWorld.Has<VehicleComponent>(player.Entity)) return;
        var vStats = player.Data.GetCombinedVehicleStats();
        ref var vehicle = ref EcsWorld.Get<VehicleComponent>(player.Entity);
        vehicle.Acceleration = vStats.Acceleration > 0 ? vStats.Acceleration : AvatarConfig.VehicleAcceleration;
        vehicle.MaxSpeed = vStats.MaxSpeed > 0 ? vStats.MaxSpeed : AvatarConfig.VehicleMaxSpeed;
        vehicle.RotationSpeed = vStats.RotationSpeed > 0 ? vStats.RotationSpeed : AvatarConfig.VehicleRotationSpeed;
        vehicle.Friction = AvatarConfig.VehicleFriction + vStats.Friction;
        if (EcsWorld.Has<Velocity>(player.Entity))
        {
            ref var vel = ref EcsWorld.Get<Velocity>(player.Entity);
            vel.MaxSpeed = vehicle.MaxSpeed;
            vel.MaxRotationSpeed = vehicle.RotationSpeed;
        }
    }

    protected override void SyncPlayerHealth(SimulationPlayer player, float hull)
    {
        player.Data.AvatarHealth = hull;
    }
}
