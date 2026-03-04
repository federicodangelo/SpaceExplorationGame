using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Runtime manager that dynamically spawns / despawns NPC entities on a planet surface.
/// NPCs arrive by landing a ship, walk around on foot, and depart by taking off.
/// Mirrors <see cref="NpcSpawnManager"/> for the solar-system layer.
/// </summary>
public class SurfaceNpcManager
{
    private readonly World _world;
    private readonly PlanetSurfaceData _surfaceData;
    private readonly SurfaceNpcSpawnConfig _config;
    private readonly Func<Vector2, bool> _canMoveTo;
    private readonly Random _rng = new();

    // Per-faction respawn tracking
    private float _enemyRespawnTimer;
    private float _cargoRespawnTimer;
    private float _patrolRespawnTimer;

    // Periodic spawn check
    private float _spawnCheckTimer;

    // Track all NPC entities (foot avatars) and their ships
    private readonly List<(Entity Npc, Entity Ship)> _npcEntities = [];

    /// <summary>Read-only view of all tracked NPC/ship pairs.</summary>
    public IReadOnlyList<(Entity Npc, Entity Ship)> NpcEntities => _npcEntities;

    public SurfaceNpcManager(World world, PlanetSurfaceData surfaceData,
        SurfaceNpcSpawnConfig config, Func<Vector2, bool> canMoveTo)
    {
        _world = world;
        _surfaceData = surfaceData;
        _config = config;
        _canMoveTo = canMoveTo;

        _spawnCheckTimer = GameConfig.SurfaceNpcSpawnCheckInterval * 0.5f;
    }

    // ── Initial Wave ────────────────────────────────────────────────

    /// <summary>
    /// Spawn the initial wave of surface NPCs instantly (no landing animation).
    /// Ships appear already landed; NPCs are already on foot.
    /// </summary>
    public void SpawnInitialWave()
    {
        int enemies = (int)(_config.TargetEnemies * GameConfig.SurfaceNpcInitialSpawnFraction);
        int cargo = (int)(_config.TargetCargo * GameConfig.SurfaceNpcInitialSpawnFraction);
        int patrols = (int)(_config.TargetPatrols * GameConfig.SurfaceNpcInitialSpawnFraction);

        for (int i = 0; i < enemies; i++)
            SpawnNpcInstant(Faction.Pirate);
        for (int i = 0; i < cargo; i++)
            SpawnNpcInstant(Faction.Trader);
        for (int i = 0; i < patrols; i++)
            SpawnNpcInstant(Faction.Patrol);
    }

    // ── Runtime Lifecycle ───────────────────────────────────────────

    /// <summary>
    /// Notify the manager that an NPC of the given faction was destroyed.
    /// Starts the per-faction respawn timer.
    /// </summary>
    public void NotifyDestroyed(Faction faction, Entity npcEntity)
    {
        // Remove from tracking list
        for (int i = _npcEntities.Count - 1; i >= 0; i--)
        {
            if (_npcEntities[i].Npc == npcEntity)
            {
                // Destroy the landed ship too if it's still alive
                var shipEntity = _npcEntities[i].Ship;
                if (_world.IsAlive(shipEntity))
                    _world.Destroy(shipEntity);
                _npcEntities.RemoveAt(i);
                break;
            }
        }

        switch (faction)
        {
            case Faction.Pirate:
                _enemyRespawnTimer = Math.Max(_enemyRespawnTimer, GameConfig.SurfaceNpcEnemyRespawnDelay);
                break;
            case Faction.Trader:
                _cargoRespawnTimer = Math.Max(_cargoRespawnTimer, GameConfig.SurfaceNpcCargoRespawnDelay);
                break;
            case Faction.Patrol:
                _patrolRespawnTimer = Math.Max(_patrolRespawnTimer, GameConfig.SurfaceNpcPatrolRespawnDelay);
                break;
        }
    }

    /// <summary>Called every frame. Ticks respawn timers and manages landing/takeoff lifecycle.</summary>
    public void Update(float dt)
    {
        // Tick respawn timers
        if (_enemyRespawnTimer > 0) _enemyRespawnTimer -= dt;
        if (_cargoRespawnTimer > 0) _cargoRespawnTimer -= dt;
        if (_patrolRespawnTimer > 0) _patrolRespawnTimer -= dt;

        // Update landing/takeoff animations
        UpdateLandingAnimations(dt);

        // Check for idle NPCs that should depart
        CheckInactivityDepartures();

        // Update NPCs that are boarding their ship
        UpdateBoardingNpcs(dt);

        // Periodic spawn check
        _spawnCheckTimer -= dt;
        if (_spawnCheckTimer <= 0)
        {
            _spawnCheckTimer = GameConfig.SurfaceNpcSpawnCheckInterval;
            SpawnMissingNpcs();
        }

        // Cleanup dead entries
        CleanupDeadEntities();
    }

    // ── Private Helpers ─────────────────────────────────────────────

    private void SpawnMissingNpcs()
    {
        int enemies = 0, cargo = 0, patrols = 0;
        foreach (var (npc, _) in _npcEntities)
        {
            if (!_world.IsAlive(npc)) continue;
            if (!_world.Has<SurfaceAI>(npc)) continue;
            var faction = _world.Get<SurfaceAI>(npc).Config.Faction;
            switch (faction)
            {
                case Faction.Pirate: enemies++; break;
                case Faction.Trader: cargo++; break;
                case Faction.Patrol: patrols++; break;
            }
        }

        // Also count ships that are currently landing (NPC not on foot yet)
        foreach (var (_, ship) in _npcEntities)
        {
            if (!_world.IsAlive(ship)) continue;
            if (!_world.Has<LandedNpcShip>(ship)) continue;
            ref var landed = ref _world.Get<LandedNpcShip>(ship);
            if (landed.IsLanding && landed.AnimProgress < 1f)
            {
                switch (landed.Faction)
                {
                    case Faction.Pirate: enemies++; break;
                    case Faction.Trader: cargo++; break;
                    case Faction.Patrol: patrols++; break;
                }
            }
        }

        if (enemies < _config.TargetEnemies && _enemyRespawnTimer <= 0)
            SpawnNpcWithLanding(Faction.Pirate);
        if (cargo < _config.TargetCargo && _cargoRespawnTimer <= 0)
            SpawnNpcWithLanding(Faction.Trader);
        if (patrols < _config.TargetPatrols && _patrolRespawnTimer <= 0)
            SpawnNpcWithLanding(Faction.Patrol);
    }

    /// <summary>Spawn an NPC already on foot with a pre-landed ship (initial wave).</summary>
    private void SpawnNpcInstant(Faction faction)
    {
        if (!TryFindSpawnPosition(out var pos)) return;

        // Create landed ship (already grounded, animation complete)
        var shipEntity = EntityFactory.CreateLandedNpcShip(_world, pos, faction,
            isLanding: false, animProgress: 1f);

        // Create on-foot NPC near the ship
        float wanderAngle = _rng.NextSingle() * MathF.PI * 2f;
        var npcOffset = new Vector2(MathF.Cos(wanderAngle), MathF.Sin(wanderAngle)) * 20f;
        var npcEntity = EntityFactory.CreateSurfaceNpc(_world, pos + npcOffset, wanderAngle,
            faction, _config.DangerLevel, _canMoveTo);

        // Add lifecycle state to the NPC
        _world.Add(npcEntity, new SurfaceNpcState
        {
            Phase = SurfaceNpcPhase.OnFoot,
            Timer = 0f,
            ShipEntity = shipEntity,
            Faction = faction,
            // start with random inactivity to stagger departures
            InactivityTimer = _rng.NextSingle() * GameConfig.SurfaceNpcInactivityTimeout * 0.5f
        });

        // Cross-reference
        ref var ship = ref _world.Get<LandedNpcShip>(shipEntity);
        ship.OwnerNpc = npcEntity;

        _npcEntities.Add((npcEntity, shipEntity));
    }

    /// <summary>Spawn a ship at the map edge that will land with animation, then disembark NPC.</summary>
    private void SpawnNpcWithLanding(Faction faction)
    {
        if (!TryFindSpawnPosition(out var pos)) return;

        // Create ship entity at spawn position with landing animation
        var shipEntity = EntityFactory.CreateLandedNpcShip(_world, pos, faction,
            isLanding: true, animProgress: 0f);

        // NPC entity is created when the landing completes (see UpdateLandingAnimations)
        _npcEntities.Add((Entity.Null, shipEntity));
    }

    /// <summary>Tick landing/takeoff animations on all LandedNpcShip entities.</summary>
    private void UpdateLandingAnimations(float dt)
    {
        float step = dt / GameConfig.SurfaceNpcLandingDuration;

        for (int i = _npcEntities.Count - 1; i >= 0; i--)
        {
            var (npc, ship) = _npcEntities[i];
            if (!_world.IsAlive(ship)) continue;
            if (!_world.Has<LandedNpcShip>(ship)) continue;

            ref var landed = ref _world.Get<LandedNpcShip>(ship);

            if (landed.IsLanding && landed.AnimProgress < 1f)
            {
                // Advance landing animation
                landed.AnimProgress = MathF.Min(1f, landed.AnimProgress + step);

                if (landed.AnimProgress >= 1f)
                {
                    // Landing complete — create foot NPC
                    var shipPos = _world.Get<Transform>(ship).Position;
                    float wanderAngle = _rng.NextSingle() * MathF.PI * 2f;
                    var offset = new Vector2(MathF.Cos(wanderAngle), MathF.Sin(wanderAngle)) * 20f;
                    var npcEntity = EntityFactory.CreateSurfaceNpc(_world, shipPos + offset, wanderAngle,
                        landed.Faction, _config.DangerLevel, _canMoveTo);

                    _world.Add(npcEntity, new SurfaceNpcState
                    {
                        Phase = SurfaceNpcPhase.OnFoot,
                        Timer = 0f,
                        ShipEntity = ship,
                        Faction = landed.Faction
                    });

                    landed.OwnerNpc = npcEntity;
                    _npcEntities[i] = (npcEntity, ship);
                }
            }
            else if (!landed.IsLanding && landed.AnimProgress < 1f)
            {
                // Advance takeoff animation
                landed.AnimProgress = MathF.Min(1f, landed.AnimProgress + step);

                if (landed.AnimProgress >= 1f)
                {
                    // Takeoff complete — destroy ship entity and remove from list
                    if (_world.IsAlive(ship))
                        _world.Destroy(ship);
                    _npcEntities.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>Start a takeoff sequence for an NPC — NPC walks to ship, boards, ship lifts off.</summary>
    public void DepartNpc(Entity npcEntity)
    {
        if (!_world.IsAlive(npcEntity)) return;
        if (!_world.Has<SurfaceNpcState>(npcEntity)) return;

        ref var state = ref _world.Get<SurfaceNpcState>(npcEntity);
        if (state.Phase != SurfaceNpcPhase.OnFoot) return;

        state.Phase = SurfaceNpcPhase.BoardingShip;
        state.Timer = 0f;
    }

    /// <summary>Check all on-foot NPCs for inactivity and trigger departure.</summary>
    private void CheckInactivityDepartures()
    {
        foreach (var (npc, _) in _npcEntities)
        {
            if (npc == Entity.Null || !_world.IsAlive(npc)) continue;
            if (!_world.Has<SurfaceNpcState>(npc)) continue;

            ref var state = ref _world.Get<SurfaceNpcState>(npc);
            float inactivityMultiplier = 1 + (npc.Id % 5 - 2) * 0.1f; // add some random variation to departure timing (+/-20%)
            if (state.Phase == SurfaceNpcPhase.OnFoot &&
                state.InactivityTimer >= GameConfig.SurfaceNpcInactivityTimeout * inactivityMultiplier)
            {
                DepartNpc(npc);
            }
        }
    }

    /// <summary>
    /// Update NPCs in the BoardingShip phase — once the NPC reaches its ship,
    /// destroy the foot entity and start the takeoff animation.
    /// </summary>
    private void UpdateBoardingNpcs(float dt)
    {
        for (int i = _npcEntities.Count - 1; i >= 0; i--)
        {
            var (npc, ship) = _npcEntities[i];
            if (npc == Entity.Null || !_world.IsAlive(npc)) continue;
            if (!_world.Has<SurfaceNpcState>(npc)) continue;

            ref var state = ref _world.Get<SurfaceNpcState>(npc);
            if (state.Phase != SurfaceNpcPhase.BoardingShip) continue;

            // Check if ship is still alive
            if (!_world.IsAlive(ship))
            {
                // Ship gone — just go back to wandering
                state.Phase = SurfaceNpcPhase.OnFoot;
                state.InactivityTimer = 0f;
                continue;
            }

            var shipPos = _world.Get<Transform>(ship).Position;
            var npcPos = _world.Get<Transform>(npc).Position;
            float dist = Vector2.Distance(npcPos, shipPos);

            if (dist < 8f)
            {
                // NPC reached its ship — destroy foot entity, start takeoff
                _world.Destroy(npc);
                _npcEntities[i] = (Entity.Null, ship);

                ref var landed = ref _world.Get<LandedNpcShip>(ship);
                landed.IsLanding = false;
                landed.AnimProgress = 0f;
                landed.OwnerNpc = Entity.Null;
            }
        }
    }

    /// <summary>Remove dead entities from the tracking list.</summary>
    private void CleanupDeadEntities()
    {
        for (int i = _npcEntities.Count - 1; i >= 0; i--)
        {
            var (npc, ship) = _npcEntities[i];
            bool npcDead = npc != Entity.Null && !_world.IsAlive(npc);
            bool shipDead = !_world.IsAlive(ship);

            if (npcDead && shipDead)
            {
                _npcEntities.RemoveAt(i);
            }
            else if (npcDead && !shipDead)
            {
                // NPC killed — clean up the ship too
                _world.Destroy(ship);
                _npcEntities.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Find a walkable spawn position on the surface, away from the landing zone and settlements.
    /// </summary>
    private bool TryFindSpawnPosition(out Vector2 position)
    {
        float ts = GameConfig.TileSize;
        float lzX = _surfaceData.LandingZone.X * ts;
        float lzY = _surfaceData.LandingZone.Y * ts;
        float safeRadius = 8 * ts;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            int tx = _rng.Next(5, _surfaceData.Width - 5);
            int ty = _rng.Next(5, _surfaceData.Height - 5);

            if (SurfaceTerrainRules.IsBlockedForTraversal(_surfaceData.Tiles[tx, ty]))
                continue;

            float worldX = tx * ts + ts / 2f;
            float worldY = ty * ts + ts / 2f;

            float distToLz = MathF.Sqrt((worldX - lzX) * (worldX - lzX) + (worldY - lzY) * (worldY - lzY));
            if (distToLz < safeRadius)
                continue;

            // Away from settlements
            bool tooClose = false;
            foreach (var s in _surfaceData.Settlements)
            {
                float sx = (s.TileRect.X + s.TileRect.Width / 2f) * ts;
                float sy = (s.TileRect.Y + s.TileRect.Height / 2f) * ts;
                if (MathF.Sqrt((worldX - sx) * (worldX - sx) + (worldY - sy) * (worldY - sy)) < 4 * ts)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            position = new Vector2(worldX, worldY);
            return true;
        }

        position = Vector2.Zero;
        return false;
    }
}
