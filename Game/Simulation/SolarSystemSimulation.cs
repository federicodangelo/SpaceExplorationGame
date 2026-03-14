using System.Numerics;
using Arch.Core;
using Engine.Network;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.AI;
using SpaceExplorationGame.ECS.Systems.Combat;
using SpaceExplorationGame.ECS.Systems.Effects;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation.Base;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Simulation for flying through a solar system. Manages all entities (star, planets, moons,
/// stations, asteroids, NPC ships) and runs physics, orbits, combat, and AI systems.
/// Contains NO rendering or audio code — states read simulation state for presentation.
/// </summary>
public class SolarSystemSimulation : CombatSimulationBase
{
    // ── Data ────────────────────────────────────────────────────────
    public StarSystemData StarSystem { get; }
    public List<PlanetData> Planets { get; private set; } = [];
    public List<SpaceStationData> SpaceStations { get; private set; } = [];
    public SolarSystemContent Content { get; private set; }

    // ── Entities ────────────────────────────────────────────────────
    public Entity StarEntity { get; private set; }
    public List<Entity> PlanetEntities { get; } = [];
    public List<Entity> SpaceStationEntities { get; } = [];
    public List<List<Entity>> MoonEntities { get; } = [];
    public List<Entity> AsteroidEntities { get; } = [];
    public List<Entity> EnemyEntities { get; } = [];

    public int LocalNearbyPlanetIndex => LocalSolarState?.NearbyPlanetIndex ?? -1;
    public int LocalNearbySpaceStationIndex => LocalSolarState?.NearbySpaceStationIndex ?? -1;
    public int LocalNearbyMoonPlanetIndex => LocalSolarState?.NearbyMoonPlanetIndex ?? -1;
    public int LocalNearbyMoonIndex => LocalSolarState?.NearbyMoonIndex ?? -1;
    public Entity LocalLastHitAsteroid => LocalSolarState?.LastHitAsteroid ?? default;
    public float LocalMiningHudTimer => LocalSolarState?.MiningHudTimer ?? 0;
    public string? LocalMiningMessage => LocalSolarState?.MiningMessage;
    public float LocalMiningMessageTimer => LocalSolarState?.MiningMessageTimer ?? 0;
    public int LocalRespawnSpaceStationIndex => LocalSolarState?.RespawnSpaceStationIndex ?? -1;

    // ── Combat state (solar-system-specific) ─────────────────────
    protected override float RespawnDelay => 3f;
    protected override CombatPlayerState CreateCombatPlayerState() => new SolarPlayerState();
    private SolarPlayerState? LocalSolarState => LocalPlayer != null ? GetSolarState(LocalPlayer) : null;
    private SolarPlayerState GetSolarState(SimulationPlayer player) => (SolarPlayerState)GetCombatState(player);

    // ── System event outputs (solar-system-specific) ────────────────
    public IReadOnlyList<ProjectileSpawn> ProjectilesSpawnedLastUpdate =>
        _shipSystem?.ProjectilesSpawnedLastUpdate ?? (IReadOnlyList<ProjectileSpawn>)[];

    // ── ECS Systems (solar-system-specific) ─────────────────────────
    private OrbitSystem _orbitSystem = null!;
    private InteractionProximitySystem _proximitySystem = null!;
    private ShipSystem _shipSystem = null!;
    private ShieldRegenSystem _shieldRegenSystem = null!;
    private ShipEnemyAISystem _enemyAISystem = null!;
    private WarpEffectSystem _warpEffectSystem = null!;
    private NpcShipSpawnManager _npcSpawnManager = null!;

    /// <summary>Apply bounty mission world impact — temporarily reduces pirate spawn budget.</summary>
    public void ApplyBountyImpact() => _npcSpawnManager.ApplyBountyImpact();

    private const float InteractionRadius = 20f;



    public SolarSystemSimulation(Game game, StarSystemData starSystem, ISimulation? parent = null)
        : base(game, parent)
    {
        StarSystem = starSystem;
    }

    public override void Create()
    {
        var rng = _game.Seeds.GetStarSystemRandom(StarSystem.Index);
        Content = _game.UniverseGenerator.GenerateSolarSystem(StarSystem);
        Planets = Content.Planets;
        SpaceStations = Content.SpaceStations;

        float totalW = WorldConfig.SolarSystemWidth * WindowConfig.TileSize;
        float totalH = WorldConfig.SolarSystemHeight * WindowConfig.TileSize;
        float centerX = totalW / 2f;
        float centerY = totalH / 2f;
        Vector2 center = new(centerX, centerY);
        float globalTime = (float)_game.GlobalTime;

        SpawnStar(StarSystem, center);
        SpawnPlanets(Content.Planets, center, globalTime);
        SpawnSpaceStations(Content.SpaceStations, globalTime);
        SpawnAsteroids(Content.AsteroidBelts, new SeededRandom(rng.DeriveChildSeed(999)));
        // Initialize ECS systems
        // Shared systems (velocity, projectiles, cleanup)
        InitCoreSystems();

        _orbitSystem = new OrbitSystem(EcsWorld, center);
        _orbitSystem.Initialize();

        _proximitySystem = new InteractionProximitySystem(EcsWorld, InteractionRadius);
        _proximitySystem.Initialize();

        _shipSystem = new ShipSystem(EcsWorld);
        _shipSystem.Initialize();

        _shieldRegenSystem = new ShieldRegenSystem(EcsWorld);
        _shieldRegenSystem.Initialize();

        _enemyAISystem = new ShipEnemyAISystem(EcsWorld, totalW, totalH);
        _enemyAISystem.Initialize();

        _warpEffectSystem = new WarpEffectSystem(EcsWorld);
        _warpEffectSystem.Initialize();

        // Dynamic NPC spawn manager — handles both initial wave and runtime warp-ins
        _npcSpawnManager = new NpcShipSpawnManager(EcsWorld, EnemyEntities, Content.NpcShipSpawnConfig);
        if (!IsMultiplayerClient)
            _npcSpawnManager.SpawnInitialWave();
    }

    public override void Destroy()
    {
        PlanetEntities.Clear();
        SpaceStationEntities.Clear();
        MoonEntities.Clear();
        AsteroidEntities.Clear();
        EnemyEntities.Clear();
        base.Destroy();
    }

    public override void Update(UpdateContext ctx)
    {
        float dt = ctx.Dt;
        float globalTime = (float)_game.GlobalTime;
        var t = _debugTimer;
        t.Begin();

        t.Time("Orbits", () => _orbitSystem.Update(in globalTime)); // Orbits depend on global time, not dt
        t.Time("Warp", () => UpdateWarpEffects(dt));
        t.Time("Enemy AI", () => _enemyAISystem.Update(in dt));
        t.Time("Ships", () => _shipSystem.Update(in dt));
        t.Time("Physics", () => _velocitySystem.Update(in dt));
        t.Time("NetInterp", () => _netInterpolationSystem.Update(in dt));
        t.Time("Combat", () => ProcessProjectilesAndDispatchEvents(dt));
        t.Time("Shields", () => _shieldRegenSystem.Update(in dt));
        t.Time("NpcSpawn", () => { if (!IsMultiplayerClient) _npcSpawnManager.Update(dt); });
        t.Time("Cleanup", () => _dependentEntityCleanupSystem.Update(in dt));
        t.Time("Proximity", UpdateProximity);

        // Death / respawn timer
        UpdateDeathTimer(dt);

        // Sync player health to PlayerData
        SyncPlayersHealth();

        // Tick message timers
        UpdateCombatTimers(dt);

        // Mining-specific timers
        UpdateMiningTimers(dt);
    }

    private void UpdateMiningTimers(float dt)
    {
        foreach (var player in Players)
        {
            var ss = GetSolarState(player);
            if (ss.MiningHudTimer > 0) ss.MiningHudTimer -= dt;
            if (ss.MiningMessageTimer > 0)
            {
                ss.MiningMessageTimer -= dt;
                if (ss.MiningMessageTimer <= 0) ss.MiningMessage = null;
            }
        }
    }

    public override IReadOnlyList<string>? GetDebugInfo()
    {
        _debugInfo.Begin();
        _debugInfo.Add($"Planets: {Planets.Count}  Stations: {SpaceStations.Count}");
        _debugInfo.Add($"Asteroids: {AsteroidEntities.Count}  Enemies: {EnemyEntities.Count}");
        _debugInfo.Add($"Players: {Players.Count}");
        return _debugInfo.Entries;
    }

    protected override Entity CreatePlayerEntity(PlayerData player, AddContext ctx)
    {
        Vector2 startPos = DeterminePlayerStartPosition(player);

        // Clear return context
        player.SolarSystemReturnContext = PlayerData.ReturnContext.Default;
        player.ReturnSpaceStationIndex = -1;
        player.ReturnPlanetIndex = -1;
        player.ReturnMoonPlanetIndex = -1;
        player.ReturnMoonIndex = -1;

        // Notify mission system
        player.Missions.NotifySystemEntered(StarSystem.Index);

        return CreatePlayerShip(player, startPos, player.Type);
    }

    /// <summary>Sync the player ship's ShipComponent with current equipment stats.</summary>
    public void SyncPlayerShipComponent(SimulationPlayer player)
    {
        if (!EcsWorld.IsAlive(player.Entity)) return;
        if (!EcsWorld.Has<ShipComponent>(player.Entity) || !EcsWorld.Has<Velocity>(player.Entity)) return;

        var playerStats = player.Data.GetCombinedShipStats();
        var weapons = CombatHelper.BuildWeaponSpecs(player.Data.EquippedParts);

        ref var ship = ref EcsWorld.Get<ShipComponent>(player.Entity);
        ship.MaxSpeed = playerStats.MaxSpeed;
        ship.MaxRotationSpeed = playerStats.RotationSpeed;
        ship.MaxAcceleration = playerStats.Acceleration;
        ship.BrakeMultiplier = ShipConfig.ShipBrakeMultiplier;
        ship.Weapons = weapons;

        if (ship.WeaponCooldowns == null || ship.WeaponCooldowns.Length != weapons.Length)
            ship.WeaponCooldowns = new float[weapons.Length];

        ref var velocity = ref EcsWorld.Get<Velocity>(player.Entity);
        velocity.MaxSpeed = playerStats.MaxSpeed;
        velocity.MaxRotationSpeed = playerStats.RotationSpeed;
    }

    // ── Private helpers ─────────────────────────────────────────────

    private Vector2 DeterminePlayerStartPosition(PlayerData player)
    {
        var returnCtx = player.SolarSystemReturnContext;

        if (returnCtx == PlayerData.ReturnContext.FromSpaceStation && player.ReturnSpaceStationIndex >= 0
            && player.ReturnSpaceStationIndex < SpaceStationEntities.Count)
        {
            return EcsWorld.Get<Transform>(SpaceStationEntities[player.ReturnSpaceStationIndex]).Position;
        }

        if (returnCtx == PlayerData.ReturnContext.FromPlanet && player.ReturnPlanetIndex >= 0
            && player.ReturnPlanetIndex < PlanetEntities.Count)
        {
            return EcsWorld.Get<Transform>(PlanetEntities[player.ReturnPlanetIndex]).Position;
        }

        if (returnCtx == PlayerData.ReturnContext.FromMoon
            && player.ReturnMoonPlanetIndex >= 0 && player.ReturnMoonPlanetIndex < MoonEntities.Count
            && player.ReturnMoonIndex >= 0 && player.ReturnMoonIndex < MoonEntities[player.ReturnMoonPlanetIndex].Count)
        {
            return EcsWorld.Get<Transform>(MoonEntities[player.ReturnMoonPlanetIndex][player.ReturnMoonIndex]).Position;
        }

        // Use saved ship world position when no specific return context
        if (player.ShipWorldPosition != System.Numerics.Vector2.Zero)
            return player.ShipWorldPosition;

        return Content.StartingPosition;
    }

    private Entity CreatePlayerShip(PlayerData player, Vector2 position, PlayerType playerType)
    {
        int shipSize = player.CurrentShipType.SpriteSize;
        var playerStats = player.GetCombinedShipStats();
        var playerWeapons = CombatHelper.BuildWeaponSpecs(player.EquippedParts);

        return EntityFactory.CreatePlayerShip(EcsWorld, position, shipSize,
            player.ShipMaxHealth, player.ShipHealth, playerStats.ShieldStrength,
            playerStats.MaxSpeed, playerStats.RotationSpeed, playerStats.Acceleration,
            ShipConfig.ShipBrakeMultiplier, playerWeapons, playerType,
            playerStats.ShieldRegenRate, playerStats.ShieldRegenDelay);
    }

    private void SpawnStar(StarSystemData starSystem, Vector2 center)
    {
        StarEntity = EntityFactory.CreateStar(EcsWorld, center, starSystem);
    }

    private void SpawnPlanets(List<PlanetData> planets, Vector2 center, float globalTime)
    {
        foreach (var planet in planets)
        {
            float angle = planet.StartAngle + planet.OrbitSpeed * globalTime;
            var pos = center + new Vector2(
                MathF.Cos(angle) * planet.OrbitRadius,
                MathF.Sin(angle) * planet.OrbitRadius);

            var planetEntity = EntityFactory.CreatePlanet(EcsWorld, pos, StarEntity, planet);
            PlanetEntities.Add(planetEntity);

            var planetMoons = SpawnPlanetMoons(planet, planetEntity, pos, globalTime);
            MoonEntities.Add(planetMoons);
        }
    }

    private List<Entity> SpawnPlanetMoons(PlanetData planet, Entity planetEntity, Vector2 planetPos, float globalTime)
    {
        var planetMoons = new List<Entity>();
        foreach (var moon in planet.Moons)
        {
            float moonAngle = moon.StartAngle + moon.OrbitSpeed * globalTime;
            var moonPos = planetPos + new Vector2(
                MathF.Cos(moonAngle) * moon.OrbitRadius,
                MathF.Sin(moonAngle) * moon.OrbitRadius);

            var moonEntity = EntityFactory.CreateMoon(EcsWorld, moonPos, planetEntity, moon);
            planetMoons.Add(moonEntity);
        }
        return planetMoons;
    }

    private void SpawnSpaceStations(List<SpaceStationData> spaceStations, float globalTime)
    {
        foreach (var spaceStation in spaceStations)
        {
            Entity parent = spaceStation.OrbitParentPlanetIndex >= 0 && spaceStation.OrbitParentPlanetIndex < PlanetEntities.Count
                ? PlanetEntities[spaceStation.OrbitParentPlanetIndex]
                : StarEntity;

            var parentTransform = EcsWorld.Get<Transform>(parent);
            float stAngle = spaceStation.StartAngle + spaceStation.OrbitSpeed * globalTime;
            var stPos = parentTransform.Position + new Vector2(
                MathF.Cos(stAngle) * spaceStation.OrbitRadius,
                MathF.Sin(stAngle) * spaceStation.OrbitRadius);

            var stEntity = EntityFactory.CreateSpaceStation(EcsWorld, stPos, parent, spaceStation);
            SpaceStationEntities.Add(stEntity);
        }
    }

    private void SpawnAsteroids(List<AsteroidBeltData> asteroidBelts, SeededRandom asteroidRng)
    {
        foreach (var belt in asteroidBelts)
        {
            for (int i = 0; i < belt.AsteroidCount; i++)
            {
                float size = asteroidRng.NextFloat(40, 100);
                float hp = size * 0.5f;
                var resource = asteroidRng.NextFloat() switch
                {
                    < 0.30f => ResourceType.Iron,
                    < 0.55f => ResourceType.Nickel,
                    < 0.70f => ResourceType.Ice,
                    < 0.85f => ResourceType.Gold,
                    < 0.95f => ResourceType.Platinum,
                    _ => ResourceType.Crystal
                };
                int resourceAmount = (int)Math.Ceiling(size * asteroidRng.NextFloat(0.1f, 0.3f));

                var entity = EntityFactory.CreateAsteroid(EcsWorld, StarEntity, size, hp,
                    resource, resourceAmount,
                    asteroidRng.NextFloat(belt.InnerRadius, belt.OuterRadius),
                    asteroidRng.NextFloat(0.002f, 0.008f),
                    asteroidRng.NextFloat(0, MathF.PI * 2));

                AsteroidEntities.Add(entity);
            }
        }
    }





    private void UpdateProximity()
    {
        foreach (var player in Players)
        {
            var ss = GetSolarState(player);
            ss.NearbyPlanetIndex = -1;
            ss.NearbySpaceStationIndex = -1;
            ss.NearbyMoonPlanetIndex = -1;
            ss.NearbyMoonIndex = -1;

            if (!EcsWorld.IsAlive(player.Entity)) continue;

            ref var shipTransform = ref EcsWorld.Get<Transform>(player.Entity);
            player.Data.ShipWorldPosition = shipTransform.Position;

            _proximitySystem.FindNearest(shipTransform.Position);

            if (_proximitySystem.HasNearest)
            {
                var nearBody = EcsWorld.Get<CelestialBody>(_proximitySystem.NearestEntity);
                switch (nearBody.Type)
                {
                    case CelestialType.Planet:
                        ss.NearbyPlanetIndex = nearBody.DataIndex;
                        break;
                    case CelestialType.Moon:
                        for (int pi = 0; pi < MoonEntities.Count; pi++)
                        {
                            int mi = MoonEntities[pi].IndexOf(_proximitySystem.NearestEntity);
                            if (mi >= 0) { ss.NearbyMoonPlanetIndex = pi; ss.NearbyMoonIndex = mi; break; }
                        }
                        break;
                    case CelestialType.SpaceStation:
                        ss.NearbySpaceStationIndex = SpaceStationEntities.IndexOf(_proximitySystem.NearestEntity);
                        break;
                }
            }
        }
    }

    // ── Virtual hook overrides ────────────────────────────────────

    protected override void OnProjectileDamageEvent(DamageEvent evt)
    {
        // Track last asteroid hit for mining HUD
        if (evt.OwnerFaction == Faction.Player
            && EcsWorld.IsAlive(evt.Target) && EcsWorld.Has<AsteroidField>(evt.Target))
        {
            var shooter = FindPlayerByEntity(evt.OwnerEntity);
            if (shooter != null)
            {
                var ss = GetSolarState(shooter);
                ss.LastHitAsteroid = evt.Target;
                ss.MiningHudTimer = 2f;
            }
        }
    }

    protected override void OnAsteroidDestroyed(KilledEntity destroyed, SimulationPlayer? miner, string? resourceMsg)
    {
        // Clear mining HUD for any player tracking this asteroid
        foreach (var player in Players)
        {
            var ss = GetSolarState(player);
            if (destroyed.Entity == ss.LastHitAsteroid)
                ss.MiningHudTimer = 0;
        }

        AsteroidEntities.Remove(destroyed.Entity);

        base.OnAsteroidDestroyed(destroyed, miner, resourceMsg);
    }

    protected override string? ApplyDeathPenalties(SimulationPlayer player)
    {
        int creditsLost = (int)(player.Data.Credits * CombatConfig.DeathCreditsLossPercent);
        player.Data.Credits -= creditsLost;

        var cargoKeys = player.Data.Cargo.Keys.ToList();
        foreach (var key in cargoKeys)
        {
            int loss = (int)(player.Data.Cargo[key] * CombatConfig.DeathCargoLossPercent);
            player.Data.Cargo[key] -= loss;
            if (player.Data.Cargo[key] <= 0) player.Data.Cargo.Remove(key);
        }

        return $"DESTROYED! -{creditsLost} CREDITS";
    }

    protected override string? ProcessEnemyLoot(SimulationPlayer killer, LootDrop loot, SeededRandom rng)
        => CombatHelper.ProcessLootDrop(_game, killer.Data, loot, rng,
            resourceAmountMax: 5 + loot.DangerLevel * 2, enablePartDrops: true);

    protected override void OnEnemyDestroyed(KilledEntity destroyed)
    {
        EnemyEntities.Remove(destroyed.Entity);

        if (destroyed.KillerFaction == Faction.Player && destroyed.Faction == Faction.Pirate
            && FindLocalPlayerByEntity(destroyed.KillerEntity) is { } pirateStopper)
        {
            pirateStopper.Data.Missions.NotifyPirateKilled(StarSystem.Index);
        }

        // Notify spawn manager so it can schedule a replacement
        _npcSpawnManager.NotifyDestroyed(destroyed.Faction);

        base.OnEnemyDestroyed(destroyed);
    }

    /// <summary>Tick warp animations and clean up ships that finished warping out.</summary>
    private void UpdateWarpEffects(float dt)
    {
        _warpEffectSystem.Update(in dt);

        // Remove entities that completed warp-out
        foreach (var entity in _warpEffectSystem.WarpOutCompleted)
        {
            EnemyEntities.Remove(entity);

            if (EcsWorld.IsAlive(entity))
                EcsWorld.Destroy(entity);
        }
    }

    protected override void HandlePlayerRespawn(SimulationPlayer player)
    {
        var state = GetCombatState(player);
        state.Dead = false;

        Vector2 respawnPos = Content.StartingPosition;
        int nearestSpaceStationIdx = -1;

        if (SpaceStationEntities.Count > 0)
        {
            float bestDist = float.MaxValue;
            for (int i = 0; i < SpaceStationEntities.Count; i++)
            {
                var stEntity = SpaceStationEntities[i];
                if (!EcsWorld.IsAlive(stEntity)) continue;
                var stPos = EcsWorld.Get<Transform>(stEntity).Position;
                float dist = Vector2.Distance(stPos, respawnPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    respawnPos = stPos + new Vector2(50, 0);
                    nearestSpaceStationIdx = i;
                }
            }
        }

        player.Data.ShipHealth = player.Data.ShipMaxHealth;
        player.Entity = CreatePlayerShip(player.Data, respawnPos, player.Type);

        state.CombatMessage = "RESPAWNED";
        state.CombatMessageTimer = 3f;

        // Expose respawn station index for state to auto-open station menu
        GetSolarState(player).RespawnSpaceStationIndex = nearestSpaceStationIdx;
    }

    public void ResetLocalPlayerRespawnStation()
    {
        if (LocalPlayer != null)
            GetSolarState(LocalPlayer).RespawnSpaceStationIndex = -1;
    }

    protected override void SyncPlayerHealth(SimulationPlayer player, float hull)
    {
        player.Data.ShipHealth = hull;
    }

    public override Vector2 GetDefaultSpawnCoordinates()
    {
        return Content.StartingPosition;
    }

    public override NetPlayerLocation GetNetPlayerLocation()
    {
        return NetPlayerLocation.ForSolarSystem(StarSystem.Index);
    }

    public override NetPlayerState GetNetPlayerState(SimulationPlayer player)
    {
        var world = EcsWorld;
        var entity = player.Entity;

        var state = new NetPlayerState();

        if (!world.IsAlive(entity))
        {
            state.Alive = false;
            return state;
        }

        state.Alive = true;

        if (world.TryGet<Transform>(entity, out var transform))
        {
            state.Position = transform.Position;
            state.Rotation = transform.Rotation;
        }

        if (world.TryGet<Velocity>(entity, out var velocity))
        {
            state.Velocity = velocity.Linear;
        }

        if (world.TryGet<Health>(entity, out var health))
        {
            state.Hull = health.Hull;
            state.Shield = health.Shield;
        }

        if (world.TryGet<ShipInputComponent>(entity, out var input))
        {
            state.Shooting = input.Shoot;
            state.RotationSpeed = input.RotationSpeed;
            state.AccelerationDirection = input.AccelerationDirection;
        }

        return state;
    }

    protected override void ApplyCombatNetPlayerState(SimulationPlayer player, NetPlayerState netState)
    {
        var world = EcsWorld;
        var entity = player.Entity;

        // Write position/rotation to interpolation targets for smooth movement
        ref var interpRef = ref world.TryGetRef<NetInterpolation>(entity, out var interpFound);
        if (interpFound)
        {
            interpRef.TargetPosition = netState.Position;
            interpRef.TargetRotation = netState.Rotation;
            interpRef.TargetVelocity = netState.Velocity;
            interpRef.TimeSinceUpdate = 0f;
            interpRef.HasTarget = true;
        }
        else
        {
            ref var transformRef = ref world.TryGetRef<Transform>(entity, out var transformFound);
            if (transformFound)
            {
                transformRef.Position = netState.Position;
                transformRef.Rotation = netState.Rotation;
            }
        }

        ref var velocityRef = ref world.TryGetRef<Velocity>(entity, out var velocityFound);
        if (velocityFound)
        {
            velocityRef.Linear = netState.Velocity;
        }

        ref var healthRef = ref world.TryGetRef<Health>(entity, out var healthFound);
        if (healthFound)
        {
            healthRef.Hull = netState.Hull;
            healthRef.Shield = netState.Shield;
        }

        ref var inputRef = ref world.TryGetRef<ShipInputComponent>(entity, out var inputFound);
        if (inputFound)
        {
            inputRef.Shoot = netState.Shooting;
            inputRef.RotationSpeed = netState.RotationSpeed;
            inputRef.AccelerationDirection = netState.AccelerationDirection;
        }
    }

    // ── Network NPC overrides ────────────────────────────────────────

    public override NetNpcState[] CollectNpcStates()
    {
        var states = new List<NetNpcState>();
        foreach (var entity in EnemyEntities)
        {
            if (!EcsWorld.IsAlive(entity)) continue;
            if (!EcsWorld.Has<NetNpcId>(entity)) continue;

            var npcId = EcsWorld.Get<NetNpcId>(entity).Id;
            var transform = EcsWorld.Get<Transform>(entity);
            var health = EcsWorld.Get<Health>(entity);
            var ship = EcsWorld.Get<ShipComponent>(entity);

            var vel = EcsWorld.Has<Velocity>(entity) ? EcsWorld.Get<Velocity>(entity).Linear : Vector2.Zero;

            bool warping = EcsWorld.Has<WarpEffect>(entity);
            bool warpIn = false;
            float warpProgress = 0f;
            float warpDuration = 0f;
            if (warping)
            {
                ref var warp = ref EcsWorld.Get<WarpEffect>(entity);
                warpIn = warp.IsWarpingIn;
                warpProgress = warp.Progress;
                warpDuration = warp.Duration;
            }

            states.Add(new NetNpcState
            {
                NpcId = npcId,
                NpcType = NetNpcType.Ship,
                Faction = (byte)ship.Faction,
                ShipTypeId = "",
                QualityTier = Content.NpcShipSpawnConfig.QualityTier,
                DangerLevel = Content.NpcShipSpawnConfig.DangerLevel,
                Position = transform.Position,
                Rotation = transform.Rotation,
                Velocity = vel,
                Hull = health.Hull,
                Shield = health.Shield,
                Dead = false,
                Warping = warping,
                WarpingIn = warpIn,
                WarpProgress = warpProgress,
                WarpDuration = warpDuration,
            });
        }
        return states.ToArray();
    }

    protected override Entity CreateNpcFromNetState(NetNpcState state)
    {
        if (state.NpcType != NetNpcType.Ship) return Entity.Null;

        var faction = (Faction)state.Faction;
        var rng = NpcShipLoadoutHelper.CreateNpcRng(state.NpcId);
        var shipType = NpcShipLoadoutHelper.ChooseNpcShipType(faction, state.DangerLevel, rng);
        var loadout = NpcShipLoadoutHelper.BuildNpcLoadout(shipType, faction, state.QualityTier, rng);
        var stats = NpcShipLoadoutHelper.BuildNpcShipStats(shipType, loadout);
        var weapons = CombatHelper.BuildWeaponSpecs(loadout);
        int lootCredits = faction == Faction.Pirate
            ? NpcShipLoadoutHelper.ComputeNpcLootCredits(shipType, loadout)
            : 0;

        var spawnData = new NpcShipSpawnData
        {
            Position = state.Position,
            Rotation = state.Rotation,
            Faction = faction,
            Stats = stats,
            Weapons = weapons,
            DangerLevel = state.DangerLevel,
            LootCredits = lootCredits
        };

        var entity = EntityFactory.CreateNpcShip(EcsWorld, spawnData, state.NpcId);

        if (state.Warping)
        {
            EcsWorld.Add(entity, new WarpEffect
            {
                IsWarpingIn = state.WarpingIn,
                Progress = state.WarpProgress,
                Duration = state.WarpDuration
            });
        }

        EnemyEntities.Add(entity);
        return entity;
    }

    protected override void DestroyNPCFromNetState(Entity entity)
    {
        EnemyEntities.Remove(entity);

        base.DestroyNPCFromNetState(entity);
    }

    protected override void UpdateNpcFromNetState(Entity entity, NetNpcState state)
    {
        base.UpdateNpcFromNetState(entity, state);

        // Update warp effect
        if (state.Warping)
        {
            if (EcsWorld.Has<WarpEffect>(entity))
            {
                ref var warp = ref EcsWorld.Get<WarpEffect>(entity);
                warp.IsWarpingIn = state.WarpingIn;
                warp.Progress = state.WarpProgress;
                warp.Duration = state.WarpDuration;
            }
            else
            {
                EcsWorld.Add(entity, new WarpEffect
                {
                    IsWarpingIn = state.WarpingIn,
                    Progress = state.WarpProgress,
                    Duration = state.WarpDuration
                });
            }
        }
        else if (EcsWorld.Has<WarpEffect>(entity))
        {
            EcsWorld.Remove<WarpEffect>(entity);
        }
    }
}
