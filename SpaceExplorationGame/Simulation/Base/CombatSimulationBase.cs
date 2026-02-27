using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Simulation.Base;

/// <summary>
/// Intermediate base class for simulations that feature combat (solar system, planet surface).
/// Provides shared combat state: death/respawn tracking, combat messages, and music timer.
/// </summary>
public abstract class CombatSimulationBase : SimulationBase
{
    // ── Combat state ────────────────────────────────────────────────
    public bool PlayerDead { get; protected set; }
    public float RespawnTimer { get; protected set; }

    // Combat messages (loot, kill, resource pickup)
    public string? CombatMessage { get; private set; }
    public float CombatMessageTimer { get; private set; }
    protected string? _combatMessage;
    protected float _combatMessageTimer;

    // Combat music tracking (exposed for states to set music theme)
    public float CombatMusicTimer { get; protected set; }

    protected CombatSimulationBase(Game game, ISimulation? parent = null)
        : base(game, parent)
    {
    }

    public override void Destroy()
    {
        PlayerDead = false;
        base.Destroy();
    }

    /// <summary>
    /// Tick combat message and music timers. Call at the end of Update.
    /// </summary>
    protected void UpdateCombatTimers(float dt)
    {
        CombatHelper.UpdateCombatMessageTimer(ref _combatMessage, ref _combatMessageTimer, dt);
        CombatMessage = _combatMessage;
        CombatMessageTimer = _combatMessageTimer;

        if (CombatMusicTimer > 0)
            CombatMusicTimer -= dt;
    }
}
