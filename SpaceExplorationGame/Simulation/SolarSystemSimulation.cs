using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.Movement;
using SpaceExplorationGame.ECS.Systems.AI;
using SpaceExplorationGame.ECS.Systems.Combat;
using SpaceExplorationGame.ECS.Systems.Effects;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation.Base;

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
    public List<AsteroidBeltData> AsteroidBelts { get; private set; } = [];
    public List<SpaceStationData> Stations { get; private set; } = [];
    public SolarSystemContent Content { get; private set; }

    // ── Entities ────────────────────────────────────────────────────
    public Entity StarEntity { get; private set; }
    public List<Entity> PlanetEntities { get; } = [];
    public List<Entity> StationEntities { get; } = [];
    public List<List<Entity>> MoonEntities { get; } = [];
    public List<Entity> AsteroidEntities { get; } = [];
    public List<Entity> EnemyEntities { get; } = [];

    // ── Background (cosmetic data, no entities) ─────────────────────
    public List<BackgroundStar> BackgroundStars { get; } = [];
    public List<NebulaCloud> BackgroundNebulae { get; } = [];


    // ── Proximity (updated per-player) ──────────────────────────────
    public int NearbyPlanetIndex { get; private set; } = -1;
    public int NearbyStationIndex { get; private set; } = -1;
    public int NearbyMoonPlanetIndex { get; private set; } = -1;
    public int NearbyMoonIndex { get; private set; } = -1;

    // ── Combat state (solar-system-specific) ─────────────────────
    protected override float RespawnDelay => 3f;

    // Mining tracking
    public Entity LastHitAsteroid { get; private set; }
    public float MiningHudTimer { get; private set; }
    public string? MiningMessage { get; private set; }
    public float MiningMessageTimer { get; private set; }

    // ── System event outputs (solar-system-specific) ────────────────
    public IReadOnlyList<ProjectileSpawn> ProjectilesSpawnedLastUpdate =>
        _shipSystem?.ProjectilesSpawnedLastUpdate ?? (IReadOnlyList<ProjectileSpawn>)[];

    // ── ECS Systems (solar-system-specific) ─────────────────────────
    private OrbitSystem _orbitSystem = null!;
    private InteractionProximitySystem _proximitySystem = null!;
    private ShipSystem _shipSystem = null!;
    private ShieldRegenSystem _shieldRegenSystem = null!;
    private ShipEnemyAISystem _enemyAISystem = null!;
    private ParticleSystem _particleSystem = null!;

    private const float InteractionRadius = 20f;

    private readonly List<string> _dbgInfo = [];

    public SolarSystemSimulation(Game game, StarSystemData starSystem, ISimulation? parent = null)
        : base(game, parent)
    {
        StarSystem = starSystem;
    }

    public override void Create()
    {
        var rng = _game.Seeds.GetStarSystemRandom(StarSystem.Index);
        Content = _game.WorldGenerator.GenerateSolarSystem(_game.Seeds, StarSystem);
        Planets = Content.Planets;
        AsteroidBelts = Content.AsteroidBelts;
        Stations = Content.Stations;

        float centerX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float centerY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 center = new(centerX, centerY);
        float time = (float)_game.GlobalTime;

        SpawnStar(center);
        SpawnPlanets(center, time);
        SpawnStations(time);
        SpawnAsteroids(new SeededRandom(rng.DeriveChildSeed(999)));
        SpawnNPCShips(Content.NpcShipSpawns);
        SpawnBackground();

        // Initialize ECS systems
        float sysW = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float sysH = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        _orbitSystem = new OrbitSystem(
            EcsWorld,
            () => (float)_game.GlobalTime,
            () => new Vector2(sysW, sysH));
        _orbitSystem.Initialize();

        // Shared systems (velocity, projectiles, cleanup)
        InitCoreSystems();

        _proximitySystem = new InteractionProximitySystem(EcsWorld, InteractionRadius);
        _proximitySystem.Initialize();

        _shipSystem = new ShipSystem(EcsWorld);
        _shipSystem.Initialize();

        _shieldRegenSystem = new ShieldRegenSystem(EcsWorld);
        _shieldRegenSystem.Initialize();

        float totalMapW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float totalMapH = GameConfig.SolarSystemHeight * GameConfig.TileSize;
        _enemyAISystem = new ShipEnemyAISystem(EcsWorld, totalMapW, totalMapH);
        _enemyAISystem.Initialize();

        _particleSystem = new ParticleSystem(EcsWorld);
        _particleSystem.Initialize();
    }

    public override void Destroy()
    {
        PlanetEntities.Clear();
        StationEntities.Clear();
        MoonEntities.Clear();
        AsteroidEntities.Clear();
        EnemyEntities.Clear();
        BackgroundStars.Clear();
        BackgroundNebulae.Clear();
        base.Destroy();
    }

    public override void Update(UpdateContext ctx)
    {
        float dt = ctx.Dt;
        var t = _debugTimer;
        t.Begin();

        t.Time("Cleanup", () => _dependentEntityCleanupSystem.Update(in dt));
        t.Time("Orbits", () => _orbitSystem.Update(in dt));
        t.Time("Particles", () => _particleSystem.Update(in dt));
        t.Time("Enemy AI", () => _enemyAISystem.Update(in dt));
        t.Time("Ships", () => _shipSystem.Update(in dt));
        t.Time("Physics", () => _velocitySystem.Update(in dt));
        t.Time("Proximity", UpdateProximity);
        t.Time("Combat", () => ProcessCombatResults(dt));

        // Death / respawn timer
        UpdateDeathTimer(dt);

        // Sync player health to PlayerData
        SyncAllPlayerHealth();

        // Mining-specific timers
        if (MiningHudTimer > 0) MiningHudTimer -= dt;
        if (MiningMessageTimer > 0)
        {
            MiningMessageTimer -= dt;
            if (MiningMessageTimer <= 0) MiningMessage = null;
        }
        UpdateCombatTimers(dt);
    }

    public override IReadOnlyList<string>? GetDebugInfo()
    {
        _dbgInfo.Clear();
        _dbgInfo.Add($"Planets: {Planets.Count}  Stations: {Stations.Count}");
        _dbgInfo.Add($"Asteroids: {AsteroidEntities.Count}  Enemies: {EnemyEntities.Count}");
        _dbgInfo.Add($"Players: {Players.Count}");
        return _dbgInfo;
    }

    protected override Entity CreatePlayerEntity(PlayerData player, AddContext ctx)
    {
        Vector2 startPos = DeterminePlayerStartPosition(player);

        // Clear return context
        player.SolarSystemReturnContext = PlayerData.ReturnContext.Default;
        player.ReturnStationIndex = -1;
        player.ReturnPlanetIndex = -1;
        player.ReturnMoonPlanetIndex = -1;
        player.ReturnMoonIndex = -1;

        // Notify mission system
        player.Missions.NotifySystemEntered(StarSystem.Index);

        return CreatePlayerShip(player, startPos);
    }

    /// <summary>Sync the player ship's ShipComponent with current equipment stats.</summary>
    public void SyncPlayerShipComponent(SimulationPlayer player)
    {
        if (!EcsWorld.IsAlive(player.Entity)) return;
        if (!EcsWorld.Has<ShipComponent>(player.Entity) || !EcsWorld.Has<Velocity>(player.Entity)) return;

        var playerStats = player.Data.GetCombinedStats();
        var weapons = CombatHelper.BuildWeaponSpecs(player.Data.EquippedParts);

        ref var ship = ref EcsWorld.Get<ShipComponent>(player.Entity);
        ship.MaxSpeed = playerStats.MaxSpeed;
        ship.MaxRotationSpeed = playerStats.RotationSpeed;
        ship.MaxAcceleration = playerStats.Acceleration;
        ship.BrakeMultiplier = GameConfig.ShipBrakeMultiplier;
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

        if (returnCtx == PlayerData.ReturnContext.FromStation && player.ReturnStationIndex >= 0
            && player.ReturnStationIndex < StationEntities.Count)
        {
            return EcsWorld.Get<Transform>(StationEntities[player.ReturnStationIndex]).Position;
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

        return Content.StartingPosition;
    }

    private Entity CreatePlayerShip(PlayerData player, Vector2 position)
    {
        int shipSize = player.CurrentShipType.SpriteSize;
        var playerStats = player.GetCombinedStats();
        var playerWeapons = CombatHelper.BuildWeaponSpecs(player.EquippedParts);

        return EntityFactory.CreatePlayerShip(EcsWorld, position, shipSize,
            player.ShipMaxHealth, player.ShipHealth, playerStats.ShieldStrength,
            playerStats.MaxSpeed, playerStats.RotationSpeed, playerStats.Acceleration,
            GameConfig.ShipBrakeMultiplier, playerWeapons);
    }

    private void SpawnStar(Vector2 center)
    {
        float starDisplayRadius = StarSystem.StarRadius * 2f;
        StarEntity = EntityFactory.CreateStar(EcsWorld, center, starDisplayRadius,
            StarSystem.Name, StarSystem.StarColor, StarSystem.Index);
    }

    private void SpawnPlanets(Vector2 center, float time)
    {
        for (int i = 0; i < Planets.Count; i++)
        {
            var planet = Planets[i];
            float angle = planet.StartAngle + planet.OrbitSpeed * time;
            var pos = center + new Vector2(
                MathF.Cos(angle) * planet.OrbitRadius,
                MathF.Sin(angle) * planet.OrbitRadius);

            var planetEntity = EntityFactory.CreatePlanet(EcsWorld, pos, StarEntity,
                planet.Name, planet.Radius, planet.Color,
                planet.OrbitRadius, planet.OrbitSpeed, planet.StartAngle,
                i, planet.HasSolidSurface);
            PlanetEntities.Add(planetEntity);

            var moons = new List<Entity>();
            foreach (var moon in planet.Moons)
            {
                float moonAngle = moon.StartAngle + moon.OrbitSpeed * time;
                var moonPos = pos + new Vector2(
                    MathF.Cos(moonAngle) * moon.OrbitRadius,
                    MathF.Sin(moonAngle) * moon.OrbitRadius);

                var moonEntity = EntityFactory.CreateMoon(EcsWorld, moonPos, planetEntity,
                    moon.Name, moon.Radius, moon.Color,
                    moon.OrbitRadius, moon.OrbitSpeed, moon.StartAngle, moon.Index);
                moons.Add(moonEntity);
            }
            MoonEntities.Add(moons);
        }
    }

    private void SpawnStations(float time)
    {
        foreach (var station in Stations)
        {
            Entity parent = station.OrbitParentPlanetIndex >= 0 && station.OrbitParentPlanetIndex < PlanetEntities.Count
                ? PlanetEntities[station.OrbitParentPlanetIndex]
                : StarEntity;

            var parentTransform = EcsWorld.Get<Transform>(parent);
            float stAngle = station.StartAngle + station.OrbitSpeed * time;
            var stPos = parentTransform.Position + new Vector2(
                MathF.Cos(stAngle) * station.OrbitRadius,
                MathF.Sin(stAngle) * station.OrbitRadius);

            var stEntity = EntityFactory.CreateStation(EcsWorld, stPos, parent,
                station.Name, station.OrbitRadius, station.OrbitSpeed, station.StartAngle, station.Index);
            StationEntities.Add(stEntity);
        }
    }

    private void SpawnAsteroids(SeededRandom asteroidRng)
    {
        foreach (var belt in AsteroidBelts)
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

    private void SpawnNPCShips(List<NpcShipSpawnData> npcShipSpawns)
    {
        foreach (var spawn in npcShipSpawns)
        {
            if (spawn.Faction is not (Faction.Pirate or Faction.Trader or Faction.Patrol))
                continue;

            var entity = EntityFactory.CreateNpcShip(EcsWorld, spawn);
            EnemyEntities.Add(entity);
        }
    }

    private void SpawnBackground()
    {
        var bgRng = new SeededRandom(_game.Seeds.GalaxySeed ^ 0xCAFEBABE);
        var nebRng = new SeededRandom(_game.Seeds.GalaxySeed ^ 0xFACEFEED);
        float mapW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float mapH = GameConfig.SolarSystemHeight * GameConfig.TileSize;

        for (int i = 0; i < 4000; i++)
        {
            BackgroundStars.Add(new BackgroundStar(
                bgRng.NextFloat(-mapW * 0.5f, mapW * 1.5f),
                bgRng.NextFloat(-mapH * 0.5f, mapH * 1.5f),
                (byte)bgRng.NextInt(50, 150)));
        }

        for (int i = 0; i < 32; i++)
        {
            byte[] choices = [(byte)nebRng.NextInt(20, 60), (byte)nebRng.NextInt(10, 40), (byte)nebRng.NextInt(30, 70)];
            int ci = nebRng.NextInt(0, 3);
            BackgroundNebulae.Add(new NebulaCloud(
                bgRng.NextFloat(-mapW * 0.5f, mapW * 1.5f),
                bgRng.NextFloat(-mapH * 0.5f, mapH * 1.5f),
                nebRng.NextFloat(1200, 5000),
                new Color3(ci == 0 ? choices[0] : (byte)10, ci == 1 ? choices[1] : (byte)10, ci == 2 ? choices[2] : (byte)15)));
        }
    }

    private void UpdateProximity()
    {
        NearbyPlanetIndex = -1;
        NearbyStationIndex = -1;
        NearbyMoonPlanetIndex = -1;
        NearbyMoonIndex = -1;

        if (LocalPlayer is not { } local) return;
        if (!EcsWorld.IsAlive(local.Entity)) return;

        ref var shipTransform = ref EcsWorld.Get<Transform>(local.Entity);
        local.Data.ShipWorldPosition = shipTransform.Position;

        _proximitySystem.FindNearest(shipTransform.Position);

        if (_proximitySystem.HasNearest)
        {
            var nearBody = EcsWorld.Get<CelestialBody>(_proximitySystem.NearestEntity);
            switch (nearBody.Type)
            {
                case CelestialType.Planet:
                    NearbyPlanetIndex = nearBody.DataIndex;
                    break;
                case CelestialType.Moon:
                    for (int pi = 0; pi < MoonEntities.Count; pi++)
                    {
                        int mi = MoonEntities[pi].IndexOf(_proximitySystem.NearestEntity);
                        if (mi >= 0) { NearbyMoonPlanetIndex = pi; NearbyMoonIndex = mi; break; }
                    }
                    break;
                case CelestialType.SpaceStation:
                    NearbyStationIndex = StationEntities.IndexOf(_proximitySystem.NearestEntity);
                    break;
            }
        }
    }

    // ── Virtual hook overrides ────────────────────────────────────

    protected override void OnPostProjectileUpdate(float dt)
    {
        _shieldRegenSystem.Update(in dt);
    }

    protected override void OnDamageEvent(DamageEvent evt)
    {
        // Track last asteroid hit for mining HUD (only for local player's projectiles)
        if (evt.OwnerFaction == Faction.Player
            && IsLocalPlayerEntity(evt.OwnerEntity)
            && EcsWorld.IsAlive(evt.Target) && EcsWorld.Has<AsteroidField>(evt.Target))
        {
            LastHitAsteroid = evt.Target;
            MiningHudTimer = 2f;
        }
    }

    protected override void OnAsteroidDestroyed(DestroyedEntity destroyed, string? resourceMsg)
    {
        if (resourceMsg != null)
        {
            MiningMessage = resourceMsg;
            MiningMessageTimer = 2.5f;
        }

        if (EcsWorld.IsAlive(destroyed.Entity) && destroyed.Entity == LastHitAsteroid)
            MiningHudTimer = 0;

        if (EcsWorld.IsAlive(destroyed.Entity))
        {
            AsteroidEntities.Remove(destroyed.Entity);
            EcsWorld.Destroy(destroyed.Entity);
        }
    }

    protected override string? ApplyDeathPenalties(SimulationPlayer player)
    {
        int creditsLost = (int)(player.Data.Credits * GameConfig.DeathCreditsLossPercent);
        player.Data.Credits -= creditsLost;

        var cargoKeys = player.Data.Cargo.Keys.ToList();
        foreach (var key in cargoKeys)
        {
            int loss = (int)(player.Data.Cargo[key] * GameConfig.DeathCargoLossPercent);
            player.Data.Cargo[key] -= loss;
            if (player.Data.Cargo[key] <= 0) player.Data.Cargo.Remove(key);
        }

        return $"DESTROYED! -{creditsLost} CREDITS";
    }

    protected override string? ProcessEnemyLoot(LootDrop loot, SeededRandom rng)
        => CombatHelper.ProcessLootDrop(_game, loot, rng,
            resourceAmountMax: 5 + loot.DangerLevel * 2, enablePartDrops: true);

    protected override void OnEnemyDestroyed(DestroyedEntity destroyed)
    {
        if (destroyed.KillerFaction == Faction.Player && destroyed.Faction == Faction.Pirate
            && FindLocalPlayerByEntity(destroyed.KillerEntity) is { } pirateStopper)
        {
            pirateStopper.Data.Missions.NotifyPirateKilled();
        }

        if (EcsWorld.IsAlive(destroyed.Entity))
        {
            EnemyEntities.Remove(destroyed.Entity);
            EcsWorld.Destroy(destroyed.Entity);
        }
    }

    protected override void HandlePlayerRespawn()
    {
        if (LocalPlayer is not { } player) return;

        PlayerDead = false;

        Vector2 respawnPos = Content.StartingPosition;
        int nearestStationIdx = -1;

        if (StationEntities.Count > 0)
        {
            float bestDist = float.MaxValue;
            for (int i = 0; i < StationEntities.Count; i++)
            {
                var stEntity = StationEntities[i];
                if (!EcsWorld.IsAlive(stEntity)) continue;
                var stPos = EcsWorld.Get<Transform>(stEntity).Position;
                float dist = Vector2.Distance(stPos, respawnPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    respawnPos = stPos + new Vector2(50, 0);
                    nearestStationIdx = i;
                }
            }
        }

        player.Data.ShipHealth = player.Data.ShipMaxHealth;
        player.Entity = CreatePlayerShip(player.Data, respawnPos);

        CombatMessage = "RESPAWNED";
        CombatMessageTimer = 3f;

        // Expose respawn station index for state to auto-open station menu
        RespawnStationIndex = nearestStationIdx;
    }

    protected override void SyncPlayerHealth(SimulationPlayer player, float hull)
    {
        player.Data.ShipHealth = hull;
    }

    /// <summary>Index of station where player respawned (-1 if none). Reset by state after reading.</summary>
    public int RespawnStationIndex { get; set; } = -1;
}
