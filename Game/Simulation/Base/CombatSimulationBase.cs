using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
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

    /// <summary>Get the combat state for a specific player.</summary>
    public CombatPlayerState GetCombatState(SimulationPlayer player) => _combatStates[player];

    /// <summary>Try to get the combat state for a specific player.</summary>
    public bool TryGetCombatState(SimulationPlayer player, out CombatPlayerState state)
        => _combatStates.TryGetValue(player, out state!);

    // ── Convenience properties (delegate to local player) ───────────
    public bool LocalPlayerDead => LocalPlayer != null && _combatStates.TryGetValue(LocalPlayer, out var s) && s.Dead;
    public float LocalRespawnTimer => LocalPlayer != null && _combatStates.TryGetValue(LocalPlayer, out var s) ? s.RespawnTimer : 0;
    public string? LocalCombatMessage => LocalPlayer != null && _combatStates.TryGetValue(LocalPlayer, out var s) ? s.CombatMessage : null;
    public float CombatMessageTimer => LocalPlayer != null && _combatStates.TryGetValue(LocalPlayer, out var s) ? s.CombatMessageTimer : 0;
    public float CombatMusicTimer => LocalPlayer != null && _combatStates.TryGetValue(LocalPlayer, out var s) ? s.CombatMusicTimer : 0;

    // ── Shared ECS Systems ──────────────────────────────────────────
    protected DependentEntityCleanupSystem _dependentEntityCleanupSystem = null!;
    protected VelocitySystem _velocitySystem = null!;
    protected ProjectileSystem _projectileSystem = null!;

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
    protected void ProcessCombatResults(float dt)
    {
        _projectileSystem.Update(in dt);
        OnPostProjectileUpdate(dt);

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

            OnDamageEvent(evt);
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
                if (deadPlayer != null)
                    HandlePlayerDeathCore(deadPlayer);
            }
            else
            {
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
                if (state.RespawnTimer <= 0)
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
    protected void SyncAllPlayerHealth()
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
    protected void HandlePlayerDeathCore(SimulationPlayer player)
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

    /// <summary>Called after projectile system update (e.g. for shield regen).</summary>
    protected virtual void OnPostProjectileUpdate(float dt) { }

    /// <summary>Called for each damage event (e.g. asteroid mining HUD tracking).</summary>
    protected virtual void OnDamageEvent(DamageEvent evt) { }

    /// <summary>
    /// Called when an asteroid/rock is destroyed. <paramref name="miner"/> is the player who
    /// destroyed it (null if non-player). <paramref name="resourceMsg"/> is non-null if the
    /// miner collected resources from it.
    /// </summary>
    protected virtual void OnAsteroidDestroyed(DestroyedEntity destroyed, SimulationPlayer? miner, string? resourceMsg)
    {
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
}
