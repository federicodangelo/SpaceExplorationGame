using Arch.Core;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Simulation.Base;

/// <summary>
/// Abstract base class for all simulations. Provides player management, ECS world lifecycle,
/// and common plumbing so that concrete simulations only implement domain-specific logic.
/// </summary>
public abstract class SimulationBase : ISimulation, IDebugInfoProvider
{
    // ── ECS ─────────────────────────────────────────────────────────
    public World EcsWorld { get; }

    // ── Players ─────────────────────────────────────────────────────
    private readonly List<SimulationPlayer> _players = [];
    public IReadOnlyList<SimulationPlayer> Players => _players;
    public bool HasPlayers => _players.Count > 0;
    public SimulationPlayer? LocalPlayer { get; private set; }
    public ISimulation? Parent { get; }

    // ── Game reference ──────────────────────────────────────────────
    protected readonly Game _game;

    // ── Debug ───────────────────────────────────────────────────────
    protected readonly DebugTimer _debugTimer = new();

    protected SimulationBase(Game game, ISimulation? parent = null)
    {
        EcsWorld = World.Create();
        _game = game;
        Parent = parent;
    }

    // ── ISimulation ─────────────────────────────────────────────────

    public abstract void Create();

    public virtual void Destroy()
    {
        _players.Clear();
        EcsWorld.Dispose();
    }

    public abstract void Update(UpdateContext ctx);

    /// <summary>
    /// Add a player to this simulation. Creates the player entity via
    /// <see cref="CreatePlayerEntity"/>, registers it, then calls <see cref="OnPlayerAdded"/>.
    /// </summary>
    public SimulationPlayer AddPlayer(PlayerData player, AddContext ctx = default)
    {
        var entity = CreatePlayerEntity(player, ctx);
        var simPlayer = new SimulationPlayer(player) { Entity = entity };
        _players.Add(simPlayer);
        if (player.Type == PlayerType.Local)
            LocalPlayer = simPlayer;
        return simPlayer;
    }

    /// <summary>
    /// Remove a player from this simulation. Calls <see cref="DestroyPlayerEntity"/> for
    /// subclass cleanup, then destroys the entity and updates the player list.
    /// </summary>
    public void RemovePlayer(SimulationPlayer player)
    {
        DestroyPlayerEntity(player.Entity);
        if (EcsWorld.IsAlive(player.Entity))
            EcsWorld.Destroy(player.Entity);
        _players.Remove(player);
        if (player == LocalPlayer)
            LocalPlayer = _players.FirstOrDefault(p => p.Type == PlayerType.Local);
    }

    // ── Template methods ────────────────────────────────────────────

    /// <summary>
    /// Create and return the primary entity for the player (ship, avatar, etc.).
    /// Called by <see cref="AddPlayer"/>. Do NOT register the player here.
    /// </summary>
    protected abstract Entity CreatePlayerEntity(PlayerData player, AddContext ctx);

    /// <summary>
    /// Optional hook called before the player entity is destroyed and removed.
    /// Override to persist state (e.g. health) before the entity is gone.
    /// </summary>
    protected virtual void DestroyPlayerEntity(Entity entity) { }

    // ── Shared helpers ──────────────────────────────────────────────

    /// <summary>Find the SimulationPlayer that owns the given entity, or null.</summary>
    public SimulationPlayer? FindPlayerByEntity(Entity entity)
    {
        foreach (var p in _players)
            if (p.Entity == entity)
                return p;
        return null;
    }

    /// <summary>Returns true if the given entity belongs to the local player.</summary>
    public bool IsLocalPlayerEntity(Entity entity)
        => LocalPlayer != null && entity == LocalPlayer.Entity;

    /// <summary>
    /// Returns the <see cref="SimulationPlayer"/> for the given entity only if it is the
    /// local player; otherwise returns null.
    /// </summary>
    public SimulationPlayer? FindLocalPlayerByEntity(Entity entity)
    {
        return LocalPlayer != null && LocalPlayer.Entity == entity ? LocalPlayer : null;
    }

    // ── IDebugInfoProvider ──────────────────────────────────────────

    /// <inheritdoc />
    public virtual IReadOnlyList<DebugTimingEntry>? GetDebugTimings() => _debugTimer.Entries;

    /// <inheritdoc />
    public virtual IReadOnlyList<string>? GetDebugInfo() => null;
}
