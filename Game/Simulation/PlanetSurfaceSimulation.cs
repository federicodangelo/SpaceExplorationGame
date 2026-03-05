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

    // ── Per-player state (delegates to local player for backward compat) ──
    private SurfacePlayerState? LocalSurfaceState =>
        LocalPlayer != null && TryGetCombatState(LocalPlayer, out var s) ? (SurfacePlayerState)s : null;

    public Entity LocalShipEntity
    {
        get => LocalSurfaceState?.ShipEntity ?? default;
        set { if (LocalSurfaceState is { } s) s.ShipEntity = value; }
    }
    public Entity LocalVehicleEntity
    {
        get => LocalSurfaceState?.VehicleEntity ?? default;
        set { if (LocalSurfaceState is { } s) s.VehicleEntity = value; }
    }
    public bool LocalVehicleDeployed
    {
        get => LocalSurfaceState?.VehicleDeployed ?? false;
        set { if (LocalSurfaceState is { } s) s.VehicleDeployed = value; }
    }

    // ── Proximity state ─────────────────────────────────────────────
    public SettlementData? NearSettlement => LocalSurfaceState?.NearSettlement;
    public bool NearShip => LocalSurfaceState?.NearShip ?? false;
    public bool NearVehicle => LocalSurfaceState?.NearVehicle ?? false;

    private const float BoardShipRadius = 30f;
    protected override float RespawnDelay => 2.5f;
    protected override CombatPlayerState CreateCombatPlayerState() => new SurfacePlayerState();

    /// <summary>Get the surface-specific per-player state.</summary>
    public SurfacePlayerState GetSurfaceState(SimulationPlayer player) => (SurfacePlayerState)GetCombatState(player);

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

        // Sync vehicle position/rotation when driving (all players)
        foreach (var p in Players)
        {
            var ss = GetSurfaceState(p);
            if (ss.VehicleDeployed && EcsWorld.IsAlive(p.Entity) && EcsWorld.IsAlive(ss.VehicleEntity) && p.Data.InVehicle)
            {
                ref var avatarTf = ref EcsWorld.Get<Transform>(p.Entity);
                ref var vehicleTf = ref EcsWorld.Get<Transform>(ss.VehicleEntity);
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

        // Store in per-player state (state already exists thanks to AddPlayer ordering)
        var ss = GetSurfaceState(FindPlayerByData(player)!);
        ss.ShipEntity = shipEntity;

        // Deploy vehicle if it was deployed before
        if (player.HasSavedSurfacePositions && player.SavedVehicleDeployed)
        {
            var vehicleEntity = EntityFactory.CreateVehicle(EcsWorld,
                player.SavedVehicleX, player.SavedVehicleY);

            ss.VehicleEntity = vehicleEntity;
            ss.VehicleDeployed = true;
        }

        // Notify mission system
        player.Missions.NotifyPlanetLanded(StarSystem.Index, Planet.Index);

        return avatarEntity;
    }

    /// <summary>Create a vehicle entity for the given player.</summary>
    public Entity DeployVehicle(SimulationPlayer player, float x, float y)
    {
        var ss = GetSurfaceState(player);
        ss.VehicleEntity = EntityFactory.CreateVehicle(EcsWorld, x, y);
        ss.VehicleDeployed = true;
        return ss.VehicleEntity;
    }

    /// <summary>Create a vehicle entity for the local player.</summary>
    public Entity DeployVehicle(float x, float y)
    {
        return DeployVehicle(LocalPlayer!, x, y);
    }

    /// <summary>Remove the vehicle entity for the given player.</summary>
    public void StowVehicle(SimulationPlayer player)
    {
        var ss = GetSurfaceState(player);
        if (ss.VehicleDeployed && EcsWorld.IsAlive(ss.VehicleEntity))
            EcsWorld.Destroy(ss.VehicleEntity);
        ss.VehicleDeployed = false;
    }

    /// <summary>Remove the vehicle entity for the local player.</summary>
    public void StowVehicle()
    {
        StowVehicle(LocalPlayer!);
    }

    /// <summary>Snap a player avatar into the vehicle and configure it for driving.</summary>
    public void MountVehicle(SimulationPlayer player)
    {
        var ss = GetSurfaceState(player);
        var vStats = player.Data.GetCombinedVehicleStats();

        if (!ss.VehicleDeployed)
        {
            var shipTf = EcsWorld.Get<Transform>(ss.ShipEntity);
            DeployVehicle(player, shipTf.Position.X, shipTf.Position.Y);
        }

        ref var avatarTf = ref EcsWorld.Get<Transform>(player.Entity);
        ref var vTf = ref EcsWorld.Get<Transform>(ss.VehicleEntity);
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
        var ss = GetSurfaceState(player);
        ref var avatarTf = ref EcsWorld.Get<Transform>(player.Entity);
        if (ss.VehicleDeployed)
        {
            ref var vehicleTf = ref EcsWorld.Get<Transform>(ss.VehicleEntity);
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
        foreach (var player in Players)
        {
            var ss = GetSurfaceState(player);
            ss.NearSettlement = null;
            ss.NearShip = false;
            ss.NearVehicle = false;

            if (!EcsWorld.IsAlive(player.Entity)) continue;

            var avatarPos = EcsWorld.Get<Transform>(player.Entity).Position;

            // Settlement proximity
            foreach (var settlement in SurfaceData.Settlements)
            {
                float sx = (settlement.TileRect.X + settlement.TileRect.Width / 2f) * WindowConfig.TileSize;
                float sy = (settlement.TileRect.Y + settlement.TileRect.Height / 2f) * WindowConfig.TileSize;
                float dist = Vector2.Distance(avatarPos, new Vector2(sx, sy));
                float settlementRadius = Math.Max(settlement.TileRect.Width, settlement.TileRect.Height) * WindowConfig.TileSize / 2f + 20f;
                if (dist < settlementRadius)
                {
                    ss.NearSettlement = settlement;
                    break;
                }
            }

            // Ship proximity
            if (EcsWorld.IsAlive(ss.ShipEntity))
            {
                var shipPos = EcsWorld.Get<Transform>(ss.ShipEntity).Position;
                ss.NearShip = Vector2.Distance(avatarPos, shipPos) < BoardShipRadius;
            }

            // Vehicle proximity
            if (ss.VehicleDeployed && EcsWorld.IsAlive(ss.VehicleEntity))
            {
                var vehiclePos = EcsWorld.Get<Transform>(ss.VehicleEntity).Position;
                ss.NearVehicle = Vector2.Distance(avatarPos, vehiclePos) < AvatarConfig.VehicleMountRadius;
            }
        }
    }

    // ── Virtual hook overrides ──────────────────────────────────────

    protected override ulong CombatRngSeed => 0xBEEFCAFE;

    protected override void OnAsteroidDestroyed(DestroyedEntity destroyed, SimulationPlayer? miner, string? resourceMsg)
    {
        if (resourceMsg != null && miner != null && TryGetCombatState(miner, out var minerState))
        {
            minerState.CombatMessage = resourceMsg;
            minerState.CombatMessageTimer = 2.5f;
        }
        base.OnAsteroidDestroyed(destroyed, miner, resourceMsg);
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

    protected override void HandlePlayerRespawn(SimulationPlayer player)
    {
        var state = GetCombatState(player);
        state.Dead = false;

        // Respawn near the landed ship
        var ss = GetSurfaceState(player);
        var shipPos = EcsWorld.IsAlive(ss.ShipEntity)
            ? EcsWorld.Get<Transform>(ss.ShipEntity).Position
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
        if (ss.VehicleDeployed)
            StowVehicle(player);
        player.Data.InVehicle = false;

        state.CombatMessage = "RESPAWNED";
        state.CombatMessageTimer = 3f;
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
