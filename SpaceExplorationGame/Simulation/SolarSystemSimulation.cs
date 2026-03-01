using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
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
    public List<SpaceStationData> SpaceStations { get; private set; } = [];
    public SolarSystemContent Content { get; private set; }

    // ── Entities ────────────────────────────────────────────────────
    public Entity StarEntity { get; private set; }
    public List<Entity> PlanetEntities { get; } = [];
    public List<Entity> SpaceStationEntities { get; } = [];
    public List<List<Entity>> MoonEntities { get; } = [];
    public List<Entity> AsteroidEntities { get; } = [];
    public List<Entity> EnemyEntities { get; } = [];

    // ── Proximity (updated per-player) ──────────────────────────────
    public int NearbyPlanetIndex { get; private set; } = -1;
    public int NearbySpaceStationIndex { get; private set; } = -1;
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

        float totalW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float totalH = GameConfig.SolarSystemHeight * GameConfig.TileSize;
        float centerX = totalW / 2f;
        float centerY = totalH / 2f;
        Vector2 center = new(centerX, centerY);
        float globalTime = (float)_game.GlobalTime;

        SpawnStar(StarSystem, center);
        SpawnPlanets(Content.Planets, center, globalTime);
        SpawnSpaceStations(Content.SpaceStations, globalTime);
        SpawnAsteroids(Content.AsteroidBelts, new SeededRandom(rng.DeriveChildSeed(999)));
        SpawnNPCShips(Content.NpcShipSpawns);

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

        _particleSystem = new ParticleSystem(EcsWorld);
        _particleSystem.Initialize();
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

        t.Time("Cleanup", () => _dependentEntityCleanupSystem.Update(in dt));
        t.Time("Orbits", () => _orbitSystem.Update(in globalTime)); // Orbits depend on global time, not dt
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



    private void UpdateProximity()
    {
        NearbyPlanetIndex = -1;
        NearbySpaceStationIndex = -1;
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
                    NearbySpaceStationIndex = SpaceStationEntities.IndexOf(_proximitySystem.NearestEntity);
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
        player.Entity = CreatePlayerShip(player.Data, respawnPos);

        CombatMessage = "RESPAWNED";
        CombatMessageTimer = 3f;

        // Expose respawn station index for state to auto-open station menu
        RespawnSpaceStationIndex = nearestSpaceStationIdx;
    }

    protected override void SyncPlayerHealth(SimulationPlayer player, float hull)
    {
        player.Data.ShipHealth = hull;
    }

    /// <summary>Index of space station where player respawned (-1 if none). Reset by state after reading.</summary>
    public int RespawnSpaceStationIndex { get; set; } = -1;
}
