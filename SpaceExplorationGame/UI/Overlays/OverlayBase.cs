using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Base class for all overlays. Provides a consistent lifecycle with separate
/// input handling (once per frame) and simulation updates (fixed timestep).
/// </summary>
public abstract class OverlayBase
{
    public bool IsOpen { get; protected set; }

    /// <summary>
    /// Handle input once per frame. Returns true if the overlay consumed input
    /// (blocks underlying state/overlay controls).
    /// </summary>
    public virtual bool UpdateInput(Game game) => IsOpen;

    /// <summary>
    /// Fixed timestep update for simulation (can run multiple times per frame).
    /// </summary>
    public virtual void Update(Game game, float dt) { }

    /// <summary>Render the overlay.</summary>
    public abstract void Render(Game game);

    /// <summary>Close the overlay.</summary>
    public virtual void Close()
    {
        IsOpen = false;
    }
}
