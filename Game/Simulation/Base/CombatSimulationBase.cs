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
    // ── Combat state ────────────────────────────────────────────────
    public bool PlayerDead { get; protected set; }
    public float RespawnTimer { get; protected set; }

    // Combat messages (loot, kill, resource pickup)
    public string? CombatMessage { get; protected set; }
    public float CombatMessageTimer { get; protected set; }

    // Combat music tracking (exposed for states to set music theme)
    public float CombatMusicTimer { get; protected set; }

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
    protected abstract void HandlePlayerRespawn();

    /// <summary>Sync a player's health from the ECS entity to PlayerData.</summary>
    protected abstract void SyncPlayerHealth(SimulationPlayer player, float health);

    protected CombatSimulationBase(Game game, ISimulation? parent = null)
        : base(game, parent)
    {
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
        PlayerDead = false;
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
            bool localPlayerInvolved =
                (evt.OwnerFaction == Faction.Player && IsLocalPlayerEntity(evt.OwnerEntity))
                || (EcsWorld.IsAlive(evt.Target) && EcsWorld.Has<PlayerControlled>(evt.Target)
                    && IsLocalPlayerEntity(evt.Target));
            if (localPlayerInvolved)
                CombatMusicTimer = AudioConfig.CombatMusicDelay;
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
                if (destroyed.KillerFaction == Faction.Player
                    && FindLocalPlayerByEntity(destroyed.KillerEntity) is { } miner)
                {
                    resourceMsg = CollectResource(miner.Data, asteroid.Resource, asteroid.ResourceAmount);
                }
                OnAsteroidDestroyed(destroyed, resourceMsg);
            }
            else if (destroyed.Faction == Faction.Player)
            {
                if (IsLocalPlayerEntity(destroyed.Entity))
                    HandlePlayerDeathCore();
            }
            else
            {
                if (destroyed.KillerFaction == Faction.Player && destroyed.Loot.HasValue
                    && IsLocalPlayerEntity(destroyed.KillerEntity))
                {
                    CombatMessage = ProcessEnemyLoot(destroyed.Loot.Value, combatRng);
                    CombatMessageTimer = 3f;
                }
                OnEnemyDestroyed(destroyed);
            }
        }
    }

    /// <summary>Tick death timer and auto-respawn.</summary>
    protected void UpdateDeathTimer(float dt)
    {
        if (PlayerDead)
        {
            RespawnTimer -= dt;
            if (RespawnTimer <= 0)
                HandlePlayerRespawn();
        }
    }

    /// <summary>Tick combat message and music timers. Call at the end of Update.</summary>
    protected void UpdateCombatTimers(float dt)
    {
        if (CombatMessageTimer > 0)
        {
            CombatMessageTimer -= dt;
            if (CombatMessageTimer <= 0) CombatMessage = null;
        }

        if (CombatMusicTimer > 0)
            CombatMusicTimer -= dt;
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
    protected void HandlePlayerDeathCore()
    {
        if (LocalPlayer is not { } player) return;
        PlayerDead = true;
        RespawnTimer = RespawnDelay;

        if (EcsWorld.IsAlive(player.Entity))
            EcsWorld.Destroy(player.Entity);

        CombatMessage = ApplyDeathPenalties(player);
        CombatMessageTimer = RespawnDelay;
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
    /// Called when an asteroid/rock is destroyed. <paramref name="resourceMsg"/> is non-null
    /// if the local player collected resources from it.
    /// </summary>
    protected virtual void OnAsteroidDestroyed(DestroyedEntity destroyed, string? resourceMsg)
    {
        if (EcsWorld.IsAlive(destroyed.Entity))
            EcsWorld.Destroy(destroyed.Entity);
    }

    /// <summary>Apply death penalties and return a death message string (shown until respawn).</summary>
    protected virtual string? ApplyDeathPenalties(SimulationPlayer player) => null;

    /// <summary>Process enemy loot drop and return a HUD message string.</summary>
    protected virtual string? ProcessEnemyLoot(LootDrop loot, SeededRandom rng)
        => CombatHelper.ProcessLootDrop(_game, loot, rng);

    /// <summary>Called when an enemy entity is destroyed (cleanup lists, notify missions, etc.).</summary>
    protected virtual void OnEnemyDestroyed(DestroyedEntity destroyed)
    {
        if (EcsWorld.IsAlive(destroyed.Entity))
            EcsWorld.Destroy(destroyed.Entity);
    }
}
