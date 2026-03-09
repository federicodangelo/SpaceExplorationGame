using SpaceExplorationGame.UI.Overlays.Menu;

namespace SpaceExplorationGame.Core.Bot;

/// <summary>
/// Autoplay sub-bot for the main menu state.
/// Starts the game after a short delay and closes any open debug overlay.
/// </summary>
internal sealed class MainMenuBot : BotBase
{
    private const float MainMenuStartDelay = 1.0f;

    private float _mainMenuTimer;

    /// <summary>Set to true for one frame when the bot triggers a game start.</summary>
    internal bool GameStartRequested { get; private set; }

    internal MainMenuBot(Random rng) : base(rng) { }

    internal void Reset()
    {
        _mainMenuTimer = 0;
        GameStartRequested = false;
        _statusGoal = "";
        _statusAction = "";
    }

    /// <summary>
    /// Auto-starts the game after a brief delay.
    /// Returns true if the bot consumed input this frame.
    /// </summary>
    internal bool Update(Game game, MainMenuOverlay menuOverlay, DebugMenuOverlay debugOverlay)
    {
        GameStartRequested = false;
        if (!Enabled) return false;

        // Close debug overlay if open
        if (debugOverlay.IsOpen)
        {
            debugOverlay.Close();
            return true;
        }

        _mainMenuTimer += game.DeltaTime;
        _statusGoal = "MAIN MENU";
        _statusAction = _mainMenuTimer < MainMenuStartDelay ? "Waiting..." : "Starting game";

        if (_mainMenuTimer >= MainMenuStartDelay)
        {
            _mainMenuTimer = 0;
            GameStartRequested = true;
            menuOverlay.StartRequested = true;
        }

        return true;
    }
}
