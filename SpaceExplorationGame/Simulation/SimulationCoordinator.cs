namespace SpaceExplorationGame.Simulation;

/// <summary>
/// Manages all active simulations. Always updated every tick — never paused by overlays or menus.
/// Handles simulation lifecycle: creation, updates, and destruction after players leave.
/// </summary>
public class SimulationCoordinator
{
    private const float DestroyDelaySeconds = 90f;

    private class ActiveSimulation
    {
        public ISimulation Simulation;
        public float EmptyTimer; // time with no players

        public ActiveSimulation(ISimulation simulation)
        {
            Simulation = simulation;
            EmptyTimer = 0f;
        }
    }

    private readonly List<ActiveSimulation> _simulations = [];

    /// <summary>All currently active simulations (read-only view).</summary>
    public IReadOnlyList<ISimulation> Simulations => _simulations.ConvertAll(s => s.Simulation);

    /// <summary>Register a simulation that has already been created.</summary>
    public void Register(ISimulation simulation)
    {
        _simulations.Add(new ActiveSimulation(simulation));
    }

    /// <summary>Unregister and destroy a specific simulation immediately.</summary>
    public void Unregister(ISimulation simulation)
    {
        for (int i = _simulations.Count - 1; i >= 0; i--)
        {
            if (_simulations[i].Simulation == simulation)
            {
                _simulations[i].Simulation.Destroy();
                _simulations.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>
    /// Update all active simulations. Destroys simulations that have had no players
    /// for longer than the timeout period.
    /// </summary>
    public void Update(UpdateContext ctx)
    {
        for (int i = _simulations.Count - 1; i >= 0; i--)
        {
            var entry = _simulations[i];
            entry.Simulation.Update(ctx);

            if (!entry.Simulation.HasPlayers)
            {
                entry.EmptyTimer += ctx.Dt;
                if (entry.EmptyTimer >= DestroyDelaySeconds)
                {
                    entry.Simulation.Destroy();
                    _simulations.RemoveAt(i);
                }
            }
            else
            {
                entry.EmptyTimer = 0f;
            }
        }
    }

    /// <summary>Find an existing simulation by predicate.</summary>
    public T? Find<T>(Func<T, bool> predicate) where T : class, ISimulation
    {
        foreach (var entry in _simulations)
        {
            if (entry.Simulation is T typed && predicate(typed))
                return typed;
        }
        return null;
    }

    /// <summary>
    /// Find an existing simulation matching <paramref name="predicate"/>, or create and register
    /// a new one using <paramref name="builder"/> if none exists.
    /// </summary>
    public T FindOrCreate<T>(Func<T, bool> predicate, Func<T> builder) where T : class, ISimulation
    {
        var existing = Find(predicate);
        if (existing != null)
            return existing;

        var simulation = builder();
        simulation.Create();
        Register(simulation);
        return simulation;
    }

    /// <summary>Destroy all active simulations immediately.</summary>
    public void DestroyAll()
    {
        foreach (var entry in _simulations)
            entry.Simulation.Destroy();
        _simulations.Clear();
    }
}
