using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Runtime manager that dynamically warps NPC ships in and out of a solar system.
/// Maintains per-faction budgets derived from <see cref="NpcShipSpawnConfig"/> and
/// respawns ships over time to replace destroyed ones.
/// </summary>
public class NpcShipSpawnManager
{
    private readonly World _world;
    private readonly List<Entity> _enemyEntities;
    private readonly NpcShipSpawnConfig _config;
    private readonly Random _rng = new();

    // NPC ID generation
    private int _nextNpcId;

    // Per-faction respawn tracking
    private float _pirateRespawnTimer;
    private float _traderRespawnTimer;
    private float _patrolRespawnTimer;

    // World impact: temporary pirate budget reduction from completed bounty missions
    private int _pirateBudgetReduction;
    private float _pirateBudgetReductionTimer;

    // Periodic spawn check
    private float _spawnCheckTimer;

    // System center (for position calculations)
    private readonly Vector2 _systemCenter;

    /// <summary>Allocate the next unique NPC ID.</summary>
    public int AllocateNpcId() => _nextNpcId++;

    public NpcShipSpawnManager(World world, List<Entity> enemyEntities, NpcShipSpawnConfig config, int startingNpcId = 0)
    {
        _world = world;
        _enemyEntities = enemyEntities;
        _config = config;
        _nextNpcId = startingNpcId;

        float centerX = WorldConfig.SolarSystemWidth * WindowConfig.TileSize / 2f;
        float centerY = WorldConfig.SolarSystemHeight * WindowConfig.TileSize / 2f;
        _systemCenter = new Vector2(centerX, centerY);

        // Start the first spawn check after a short delay so the initial wave settles
        _spawnCheckTimer = NpcConfig.NpcSpawnCheckInterval * 0.5f;
    }

    /// <summary>
    /// Spawn the initial wave of NPC ships instantly (no warp effect).
    /// Ships are placed across the full system orbit zone.
    /// Call once during simulation creation.
    /// </summary>
    public void SpawnInitialWave()
    {
        int initialPirates = (int)(_config.TargetPirates * NpcConfig.NpcInitialSpawnFraction);
        int initialTraders = (int)(_config.TargetTraders * NpcConfig.NpcInitialSpawnFraction);
        int initialPatrols = (int)(_config.TargetPatrols * NpcConfig.NpcInitialSpawnFraction);

        for (int i = 0; i < initialPirates; i++)
            SpawnShip(Faction.Pirate, useInitialRadius: true, withWarpEffect: false);
        for (int i = 0; i < initialTraders; i++)
            SpawnShip(Faction.Trader, useInitialRadius: true, withWarpEffect: false);
        for (int i = 0; i < initialPatrols; i++)
            SpawnShip(Faction.Patrol, useInitialRadius: true, withWarpEffect: false);
    }

    /// <summary>
    /// Notify the manager that an NPC of the given faction was destroyed.
    /// Starts the per-faction respawn timer.
    /// </summary>
    public void NotifyDestroyed(Faction faction)
    {
        switch (faction)
        {
            case Faction.Pirate:
                _pirateRespawnTimer = Math.Max(_pirateRespawnTimer, NpcConfig.NpcPirateRespawnDelay);
                break;
            case Faction.Trader:
                _traderRespawnTimer = Math.Max(_traderRespawnTimer, NpcConfig.NpcTraderRespawnDelay);
                break;
            case Faction.Patrol:
                _patrolRespawnTimer = Math.Max(_patrolRespawnTimer, NpcConfig.NpcPatrolRespawnDelay);
                break;
        }
    }

    /// <summary>
    /// Reduce the pirate spawn budget temporarily (world impact from bounty completion).
    /// Lasts for 120 seconds, reducing max pirate count by 2 (stacks with existing reduction).
    /// </summary>
    public void ApplyBountyImpact()
    {
        _pirateBudgetReduction = Math.Min(_pirateBudgetReduction + 2, _config.TargetPirates);
        _pirateBudgetReductionTimer = 120f;
    }

    /// <summary>
    /// Called every frame. Ticks respawn timers and periodically spawns ships
    /// that warp into the system to maintain the target population.
    /// </summary>
    public void Update(float dt)
    {
        // Tick respawn timers
        if (_pirateRespawnTimer > 0) _pirateRespawnTimer -= dt;
        if (_traderRespawnTimer > 0) _traderRespawnTimer -= dt;
        if (_patrolRespawnTimer > 0) _patrolRespawnTimer -= dt;

        // Decay bounty impact
        if (_pirateBudgetReductionTimer > 0)
        {
            _pirateBudgetReductionTimer -= dt;
            if (_pirateBudgetReductionTimer <= 0)
                _pirateBudgetReduction = 0;
        }

        // Periodic spawn check
        _spawnCheckTimer -= dt;
        if (_spawnCheckTimer <= 0)
        {
            _spawnCheckTimer = NpcConfig.NpcSpawnCheckInterval;
            SpawnMissingShips();
        }
    }

    private void SpawnMissingShips()
    {
        // Count living ships per faction
        int pirates = 0, traders = 0, patrols = 0;
        foreach (var entity in _enemyEntities)
        {
            if (!_world.IsAlive(entity)) continue;
            if (!_world.Has<ShipComponent>(entity)) continue;
            var faction = _world.Get<ShipComponent>(entity).Faction;
            switch (faction)
            {
                case Faction.Pirate: pirates++; break;
                case Faction.Trader: traders++; break;
                case Faction.Patrol: patrols++; break;
            }
        }

        // Spawn one ship per faction per check cycle (to stagger arrivals)
        int effectivePirateTarget = Math.Max(0, _config.TargetPirates - _pirateBudgetReduction);
        if (pirates < effectivePirateTarget && _pirateRespawnTimer <= 0)
            SpawnShip(Faction.Pirate, useInitialRadius: false, withWarpEffect: true);

        if (traders < _config.TargetTraders && _traderRespawnTimer <= 0)
            SpawnShip(Faction.Trader, useInitialRadius: false, withWarpEffect: true);

        if (patrols < _config.TargetPatrols && _patrolRespawnTimer <= 0)
            SpawnShip(Faction.Patrol, useInitialRadius: false, withWarpEffect: true);
    }

    private void SpawnShip(Faction faction, bool useInitialRadius, bool withWarpEffect)
    {
        int npcId = AllocateNpcId();
        var rng = NpcShipLoadoutHelper.CreateNpcRng(npcId);

        var shipType = NpcShipLoadoutHelper.ChooseNpcShipType(faction, _config.DangerLevel, rng);
        var loadout = NpcShipLoadoutHelper.BuildNpcLoadout(shipType, faction, _config.QualityTier, rng);
        var stats = NpcShipLoadoutHelper.BuildNpcShipStats(shipType, loadout);
        var weapons = CombatHelper.BuildWeaponSpecs(loadout);
        int lootCredits = faction == Faction.Pirate
            ? NpcShipLoadoutHelper.ComputeNpcLootCredits(shipType, loadout)
            : 0;

        // Use a secondary RNG for position so the NPC-ID-based rng is consumed consistently
        var posRng = new SeededRandom((ulong)(_rng.Next() ^ Environment.TickCount64));
        var position = useInitialRadius
            ? RandomPosition(posRng, _config.InitialMinSpawnRadius, _config.InitialMaxSpawnRadius)
            : RandomPosition(posRng, _config.WarpInMinRadius, _config.WarpInMaxRadius);

        var spawnData = new NpcShipSpawnData
        {
            Position = position,
            Rotation = posRng.NextFloat(0, 360),
            Faction = faction,
            Stats = stats,
            Weapons = weapons,
            DangerLevel = _config.DangerLevel,
            LootCredits = lootCredits
        };

        var entity = EntityFactory.CreateNpcShip(_world, spawnData, npcId);

        if (withWarpEffect)
        {
            _world.Add(entity, new WarpEffect
            {
                IsWarpingIn = true,
                Progress = 0f,
                Duration = NpcConfig.NpcWarpDuration
            });
        }

        _enemyEntities.Add(entity);
    }

    /// <summary>Spawn a ship of the given faction at a specific position (used for event-triggered spawns).</summary>
    public void SpawnShipAt(Vector2 position, Faction faction)
    {
        int npcId = AllocateNpcId();
        var rng = NpcShipLoadoutHelper.CreateNpcRng(npcId);

        var shipType = NpcShipLoadoutHelper.ChooseNpcShipType(faction, _config.DangerLevel, rng);
        var loadout = NpcShipLoadoutHelper.BuildNpcLoadout(shipType, faction, _config.QualityTier, rng);
        var stats = NpcShipLoadoutHelper.BuildNpcShipStats(shipType, loadout);
        var weapons = CombatHelper.BuildWeaponSpecs(loadout);
        int lootCredits = faction == Faction.Pirate
            ? NpcShipLoadoutHelper.ComputeNpcLootCredits(shipType, loadout)
            : 0;

        var spawnData = new NpcShipSpawnData
        {
            Position = position,
            Rotation = rng.NextFloat(0, 360),
            Faction = faction,
            Stats = stats,
            Weapons = weapons,
            DangerLevel = _config.DangerLevel,
            LootCredits = lootCredits
        };

        var entity = EntityFactory.CreateNpcShip(_world, spawnData, npcId);
        _world.Add(entity, new WarpEffect
        {
            IsWarpingIn = true,
            Progress = 0f,
            Duration = NpcConfig.NpcWarpDuration
        });
        _enemyEntities.Add(entity);
    }

    private Vector2 RandomPosition(SeededRandom rng, float minRadius, float maxRadius)
    {
        float angle = rng.NextFloat(0, MathF.PI * 2f);
        float dist = rng.NextFloat(minRadius, maxRadius);
        return _systemCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
    }

    /// <summary>
    /// Start a warp-out animation on an NPC entity. Once the animation completes,
    /// the <see cref="WarpEffectSystem"/> marks it; the simulation removes it.
    /// </summary>
    public void WarpOutShip(Entity entity)
    {
        if (!_world.IsAlive(entity)) return;
        if (_world.Has<WarpEffect>(entity)) return; // already warping

        _world.Add(entity, new WarpEffect
        {
            IsWarpingIn = false,
            Progress = 0f,
            Duration = NpcConfig.NpcWarpDuration
        });
    }
}
