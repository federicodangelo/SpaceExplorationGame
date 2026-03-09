using System.Numerics;
using Arch.Core;
using Engine.Network;
using Engine.Network.Client;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.Combat;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Simulation.Base;

/// <summary>
/// Intermediate base class for simulations that feature combat (solar system, planet surface).
/// Provides shared combat state, death/respawn tracking, combat messages, music timer,
/// common ECS systems, and a unified combat-results pipeline with virtual hooks.
/// </summary>
public abstract class CombatSimulationBase : SimulationBase
{
    // ── Per-player combat state ─────────────────────────────────────
    private readonly Dictionary<SimulationPlayer, CombatPlayerState> _combatStates = new();

    // ── Convenience properties (delegate to local player) ───────────
    public bool LocalPlayerDead => LocalPlayer != null ? GetCombatState(LocalPlayer).Dead : false;
    public float LocalRespawnTimer => LocalPlayer != null ? GetCombatState(LocalPlayer).RespawnTimer : 0;
    public string? LocalCombatMessage => LocalPlayer != null ? GetCombatState(LocalPlayer).CombatMessage : null;
    public float CombatMessageTimer => LocalPlayer != null ? GetCombatState(LocalPlayer).CombatMessageTimer : 0;
    public float CombatMusicTimer => LocalPlayer != null ? GetCombatState(LocalPlayer).CombatMusicTimer : 0;

    // ── Shared ECS Systems ──────────────────────────────────────────
    protected DependentEntityCleanupSystem _dependentEntityCleanupSystem = null!;
    protected VelocitySystem _velocitySystem = null!;
    protected ProjectileSystem _projectileSystem = null!;
    protected NetInterpolationSystem _netInterpolationSystem = null!;

    // ── Network NPC tracking (multiplayer client) ───────────────────
    /// <summary>Maps server NPC IDs to local ECS entities (only on multiplayer clients).</summary>
    private readonly Dictionary<int, Entity> _netNpcEntities = new();

    // ── System event outputs ────────────────────────────────────────
    public IReadOnlyList<DamageEvent> DamageEventsLastUpdate =>
        _projectileSystem?.DamageEventsLastUpdate ?? (IReadOnlyList<DamageEvent>)[];
    public IReadOnlyList<DestroyedEntity> DestroyedEntitiesLastUpdate =>
        _projectileSystem?.DestroyedLastUpdate ?? (IReadOnlyList<DestroyedEntity>)[];

    // ── Abstract hooks ──────────────────────────────────────────────

    /// <summary>Time in seconds before auto-respawn after player death.</summary>
    protected abstract float RespawnDelay { get; }

    /// <summary>Handle player respawn after death timer expires.</summary>
    protected abstract void HandlePlayerRespawn(SimulationPlayer player);

    /// <summary>Sync a player's health from the ECS entity to PlayerData.</summary>
    protected abstract void SyncPlayerHealth(SimulationPlayer player, float health);

    protected CombatSimulationBase(Game game, ISimulation? parent = null)
        : base(game, parent)
    {
    }

    /// <summary>Create the per-player combat state. Override in subclasses to return a derived type.</summary>
    protected virtual CombatPlayerState CreateCombatPlayerState() => new();
    protected CombatPlayerState GetCombatState(SimulationPlayer player) => _combatStates[player];

    protected override void OnPlayerAdded(SimulationPlayer player)
    {
        _combatStates[player] = CreateCombatPlayerState();
    }

    protected override void OnPlayerRemoved(SimulationPlayer player)
    {
        _combatStates.Remove(player);
    }

    /// <summary>Initialize ECS systems shared by all combat simulations.</summary>
    protected void InitCoreSystems()
    {
        _dependentEntityCleanupSystem = new DependentEntityCleanupSystem(EcsWorld);
        _dependentEntityCleanupSystem.Initialize();

        _velocitySystem = new VelocitySystem(EcsWorld);
        _velocitySystem.Initialize();

        _projectileSystem = new ProjectileSystem(EcsWorld);
        _projectileSystem.Initialize();

        _netInterpolationSystem = new NetInterpolationSystem(EcsWorld);
        _netInterpolationSystem.Initialize();
    }

    public override void Destroy()
    {
        _combatStates.Clear();
        base.Destroy();
    }

    // ── Unified combat pipeline ─────────────────────────────────────

    /// <summary>
    /// Process projectile hits, damage events (combat music), and destroyed entities
    /// (resource collection, player death, enemy loot). Calls virtual hooks for
    /// subclass-specific behaviour.
    /// </summary>
    protected void ProcessProjectilesAndDispatchEvents(float dt)
    {
        _projectileSystem.Update(in dt);

        // Process damage events
        foreach (var evt in _projectileSystem.DamageEventsLastUpdate)
        {
            // Track combat music for any player involved
            var ownerPlayer = evt.OwnerFaction == Faction.Player ? FindPlayerByEntity(evt.OwnerEntity) : null;
            var targetPlayer = EcsWorld.IsAlive(evt.Target) && EcsWorld.Has<PlayerControlled>(evt.Target)
                ? FindPlayerByEntity(evt.Target) : null;

            if (ownerPlayer != null && _combatStates.TryGetValue(ownerPlayer, out var ownerState))
                ownerState.CombatMusicTimer = AudioConfig.CombatMusicDelay;
            if (targetPlayer != null && _combatStates.TryGetValue(targetPlayer, out var targetState))
                targetState.CombatMusicTimer = AudioConfig.CombatMusicDelay;

            // Report NPC hits to server in multiplayer
            if (IsMultiplayerClient && evt.OwnerFaction == Faction.Player
                && FindLocalPlayerByEntity(evt.OwnerEntity) != null
                && EcsWorld.IsAlive(evt.Target) && EcsWorld.Has<NetNpcId>(evt.Target))
            {
                var npcId = EcsWorld.Get<NetNpcId>(evt.Target).Id;
                ref var targetHealth = ref EcsWorld.Get<Health>(evt.Target);
                _game.Network!.SendNpcHit(new C_NpcHitMessage
                {
                    NpcId = npcId,
                    Damage = evt.Damage,
                    RemainingHull = targetHealth.Hull,
                    RemainingShield = targetHealth.Shield,
                    Killed = false
                });
            }

            OnProjectileDamageEvent(evt);
        }

        // Process destroyed entities
        var combatRng = new SeededRandom((ulong)(_game.GlobalTime * 1000) ^ CombatRngSeed);
        foreach (var destroyed in _projectileSystem.DestroyedLastUpdate)
        {
            if (destroyed.Asteroid.HasValue)
            {
                var asteroid = destroyed.Asteroid.Value;
                string? resourceMsg = null;
                SimulationPlayer? miner = null;
                if (destroyed.KillerFaction == Faction.Player)
                {
                    miner = FindPlayerByEntity(destroyed.KillerEntity);
                    if (miner != null)
                        resourceMsg = CollectResource(miner.Data, asteroid.Resource, asteroid.ResourceAmount);
                }
                OnAsteroidDestroyed(destroyed, miner, resourceMsg);
            }
            else if (destroyed.Faction == Faction.Player)
            {
                var deadPlayer = FindPlayerByEntity(destroyed.Entity);
                // Only handle local player death here, remote player deaths are handled by their own clients
                // and synchronized via ApplyNetPlayerState()
                if (deadPlayer != null && deadPlayer.Type == PlayerType.Local)
                {
                    // Report player death by NPC to server in multiplayer
                    if (IsMultiplayerClient && destroyed.KillerFaction != Faction.Player
                        && EcsWorld.IsAlive(destroyed.KillerEntity)
                        && EcsWorld.Has<NetNpcId>(destroyed.KillerEntity))
                    {
                        var npcId = EcsWorld.Get<NetNpcId>(destroyed.KillerEntity).Id;
                        _game.Network!.SendPlayerKilledByNpc(new C_PlayerKilledByNpcMessage
                        {
                            NpcId = npcId
                        });
                    }

                    HandlePlayerDeath(deadPlayer);
                }
            }
            else
            {
                // Report NPC kill to server in multiplayer
                if (IsMultiplayerClient && destroyed.KillerFaction == Faction.Player
                    && FindLocalPlayerByEntity(destroyed.KillerEntity) != null
                    && EcsWorld.IsAlive(destroyed.Entity) && EcsWorld.Has<NetNpcId>(destroyed.Entity))
                {
                    var npcId = EcsWorld.Get<NetNpcId>(destroyed.Entity).Id;
                    _game.Network!.SendNpcHit(new C_NpcHitMessage
                    {
                        NpcId = npcId,
                        Damage = 0,
                        RemainingHull = 0,
                        RemainingShield = 0,
                        Killed = true
                    });
                }

                if (destroyed.KillerFaction == Faction.Player && destroyed.Loot.HasValue)
                {
                    var killer = FindPlayerByEntity(destroyed.KillerEntity);
                    if (killer != null && _combatStates.TryGetValue(killer, out var killerState))
                    {
                        killerState.CombatMessage = ProcessEnemyLoot(killer, destroyed.Loot.Value, combatRng);
                        killerState.CombatMessageTimer = 3f;
                    }
                }
                OnEnemyDestroyed(destroyed);
            }
        }
    }

    /// <summary>Tick death timer and auto-respawn for all players.</summary>
    protected void UpdateDeathTimer(float dt)
    {
        foreach (var (player, state) in _combatStates)
        {
            if (state.Dead)
            {
                state.RespawnTimer -= dt;
                // Auto-respawn when timer expires (only for local player, remote players are respawned by their own clients and synchronized via ApplyNetPlayerState())
                if (state.RespawnTimer <= 0 && player.Type == PlayerType.Local)
                    HandlePlayerRespawn(player);
            }
        }
    }

    /// <summary>Tick combat message and music timers for all players. Call at the end of Update.</summary>
    protected void UpdateCombatTimers(float dt)
    {
        foreach (var (_, state) in _combatStates)
        {
            if (state.CombatMessageTimer > 0)
            {
                state.CombatMessageTimer -= dt;
                if (state.CombatMessageTimer <= 0) state.CombatMessage = null;
            }

            if (state.CombatMusicTimer > 0)
                state.CombatMusicTimer -= dt;
        }
    }

    /// <summary>Sync each player's health from their ECS entity to PlayerData.</summary>
    protected void SyncPlayersHealth()
    {
        foreach (var player in Players)
        {
            if (EcsWorld.IsAlive(player.Entity) && EcsWorld.Has<Health>(player.Entity))
            {
                ref var health = ref EcsWorld.Get<Health>(player.Entity);
                SyncPlayerHealth(player, health.Hull);
            }
        }
    }

    // ── Core death handling ─────────────────────────────────────────

    /// <summary>
    /// Shared death handling: sets dead state, destroys entity, applies penalties via virtual hook.
    /// </summary>
    protected void HandlePlayerDeath(SimulationPlayer player)
    {
        if (!_combatStates.TryGetValue(player, out var state)) return;
        state.Dead = true;
        state.RespawnTimer = RespawnDelay;

        if (EcsWorld.IsAlive(player.Entity))
        {
            EcsWorld.Destroy(player.Entity);
            player.Entity = Entity.Null;
        }

        state.CombatMessage = ApplyDeathPenalties(player);
        state.CombatMessageTimer = RespawnDelay;
    }

    // ── Resource collection helper ──────────────────────────────────

    /// <summary>Collect resources into player cargo, returning a HUD message string.</summary>
    protected static string CollectResource(PlayerData playerData, ResourceType resource, int amount)
    {
        int added = playerData.AddCargo(resource, amount);
        var resInfo = ResourceCatalog.Get(resource);
        if (added > 0)
        {
            playerData.Missions.NotifyResourceMined(resource, added);
            return $"+{added} {resInfo.Name.ToUpper()}";
        }
        return "CARGO FULL!";
    }

    // ── Virtual hooks for subclass customisation ────────────────────

    /// <summary>RNG seed for combat loot rolls. Override to vary per simulation.</summary>
    protected virtual ulong CombatRngSeed => 0xDEADBEEF;

    /// <summary>Called for each damage event (e.g. asteroid mining HUD tracking).</summary>
    protected virtual void OnProjectileDamageEvent(DamageEvent evt) { }

    /// <summary>
    /// Called when an asteroid/rock is destroyed. <paramref name="miner"/> is the player who
    /// destroyed it (null if non-player). <paramref name="resourceMsg"/> is non-null if the
    /// miner collected resources from it.
    /// </summary>
    protected virtual void OnAsteroidDestroyed(DestroyedEntity destroyed, SimulationPlayer? miner, string? resourceMsg)
    {
        if (miner != null && resourceMsg != null)
        {
            var minerState = GetCombatState(miner);
            minerState.CombatMessage = resourceMsg;
            minerState.CombatMessageTimer = 2.5f;
        }

        if (EcsWorld.IsAlive(destroyed.Entity))
            EcsWorld.Destroy(destroyed.Entity);
    }

    /// <summary>Apply death penalties and return a death message string (shown until respawn).</summary>
    protected virtual string? ApplyDeathPenalties(SimulationPlayer player) => null;

    /// <summary>Process enemy loot drop and return a HUD message string.</summary>
    protected virtual string? ProcessEnemyLoot(SimulationPlayer killer, LootDrop loot, SeededRandom rng)
        => CombatHelper.ProcessLootDrop(_game, killer.Data, loot, rng);

    /// <summary>Called when an enemy entity is destroyed (cleanup lists, notify missions, etc.).</summary>
    protected virtual void OnEnemyDestroyed(DestroyedEntity destroyed)
    {
        if (EcsWorld.IsAlive(destroyed.Entity))
            EcsWorld.Destroy(destroyed.Entity);
    }

    // ── Network NPC synchronization (multiplayer client) ──────────

    /// <summary>
    /// Apply NPC states received from the server. Creates, updates, or destroys
    /// NPC entities on the client side to match the server's authoritative state.
    /// Called from the game loop when connected to a multiplayer server.
    /// </summary>
    public void SyncNpcStates(ClientNetworkManager net)
    {
        var states = net.LatestNpcStates;
        var notSentStates = net.LatestNotSentNpcStates;

        if (states == null || notSentStates == null) return;

        var receivedIds = new HashSet<int>();

        foreach (var npcState in states)
        {
            receivedIds.Add(npcState.NpcId);

            if (npcState.Dead)
            {
                // NPC was destroyed on the server
                if (_netNpcEntities.TryGetValue(npcState.NpcId, out var deadEntity))
                {
                    _netNpcEntities.Remove(npcState.NpcId);
                    DestroyNPCFromNetState(deadEntity); // TODO
                }
                continue;
            }

            if (!_netNpcEntities.TryGetValue(npcState.NpcId, out var entity) || !EcsWorld.IsAlive(entity))
            {
                // New NPC — create it
                entity = CreateNpcFromNetState(npcState);
                if (entity == Entity.Null) continue;
                _netNpcEntities[npcState.NpcId] = entity;

                // Attach interpolation component so NPCs move smoothly on the client
                if (!EcsWorld.Has<NetInterpolation>(entity))
                    EcsWorld.Add(entity, new NetInterpolation());
            }

            // Update existing NPC entity
            UpdateNpcFromNetState(entity, npcState);
        }

        foreach (var notReceivedNpcState in notSentStates)
        {
            receivedIds.Add(notReceivedNpcState.NpcId);
        }

        // Destroy NPCs no longer present in the server snapshot
        List<int>? toRemove = null;
        foreach (var (npcId, entity) in _netNpcEntities)
        {
            if (!receivedIds.Contains(npcId))
            {
                if (EcsWorld.IsAlive(entity))
                    EcsWorld.Destroy(entity);
                (toRemove ??= []).Add(npcId);
            }
        }
        if (toRemove != null)
            foreach (var id in toRemove)
                _netNpcEntities.Remove(id);
    }

    /// <summary>Create a local NPC entity from a server state snapshot. Override in subclasses.</summary>
    protected virtual Entity CreateNpcFromNetState(NetNpcState state) => Entity.Null;

    /// <summary>Destroy a local NPC entity based on server state (e.g. when the server reports it as dead).</summary>
    protected virtual void DestroyNPCFromNetState(Entity entity)
    {
        if (EcsWorld.IsAlive(entity))
            EcsWorld.Destroy(entity);
    }

    /// <summary>Update a local NPC entity with server state. Override in subclasses.</summary>
    protected virtual void UpdateNpcFromNetState(Entity entity, NetNpcState state)
    {
        if (EcsWorld.Has<NetInterpolation>(entity))
        {
            ref var interp = ref EcsWorld.Get<NetInterpolation>(entity);
            interp.TargetPosition = state.Position;
            interp.TargetRotation = state.Rotation;
            interp.TargetVelocity = state.Velocity;
            interp.TimeSinceUpdate = 0f;
            interp.HasTarget = true;
        }
        else
        {
            ref var transform = ref EcsWorld.Get<Transform>(entity);
            transform.Position = state.Position;
            transform.Rotation = state.Rotation;
        }

        if (EcsWorld.Has<Velocity>(entity))
        {
            ref var vel = ref EcsWorld.Get<Velocity>(entity);
            vel.Linear = state.Velocity;
        }

        if (EcsWorld.Has<Health>(entity))
        {
            ref var health = ref EcsWorld.Get<Health>(entity);
            health.Hull = state.Hull;
            health.Shield = state.Shield;
        }
    }

    /// <summary>Find the NPC ID for a given entity, or -1 if not a networked NPC.</summary>
    protected int GetNetNpcId(Entity entity)
    {
        if (EcsWorld.Has<NetNpcId>(entity))
            return EcsWorld.Get<NetNpcId>(entity).Id;
        return -1;
    }

    /// <summary>
    /// Collect all NPC states from this simulation for network broadcasting.
    /// Called by the server to build the S_NpcStates message.
    /// </summary>
    public virtual NetNpcState[] CollectNpcStates() => [];

    public sealed override void ApplyNetPlayerState(SimulationPlayer player, NetPlayerState netState)
    {
        var world = EcsWorld;
        var entity = player.Entity;
        var solarState = GetCombatState(player);

        if (solarState.Dead && !netState.Alive)
        {
            // Dead locally, dead on remote, nothing to do
            return;
        }

        if (!netState.Alive)
        {
            // Alive locally, dead on remote, mark as dead and trigger death handling
            ref var healthRef = ref world.TryGetRef<Health>(entity, out var healthFound);
            if (healthFound)
            {
                healthRef.Hull = 0;
                healthRef.Shield = 0;
            }
            HandlePlayerDeath(player);
            return;
        }

        if (solarState.Dead && netState.Alive)
        {
            // Dead locally but alive on remote, trigger respawn
            HandlePlayerRespawn(player);
            // Update reference to new entity after respawn
            entity = player.Entity;

            // Re-attach interpolation component for remote players after respawn
            if (player.Type == PlayerType.Remote && world.IsAlive(entity) && !world.Has<NetInterpolation>(entity))
                world.Add(entity, new NetInterpolation());
        }

        if (!world.IsAlive(entity))
        {
            // This should not happen
            Console.WriteLine($"Warning: Entity for remote player {player.RemotePlayerId} is not alive during ApplyNetPlayerState.");
            return;
        }

        ApplyCombatNetPlayerState(player, netState);
    }

    protected abstract void ApplyCombatNetPlayerState(SimulationPlayer player, NetPlayerState netState);
}
