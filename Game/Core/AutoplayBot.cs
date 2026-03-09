using SpaceExplorationGame.Core.Bot;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Map;
using SpaceExplorationGame.UI.Overlays.Menu;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Autoplay bot that plays the game autonomously.
/// Toggled via the debug menu. Each game state calls the appropriate Update method
/// which writes directly to ECS input components and triggers state transitions.
///
/// This class is a thin coordinator — all behaviour lives in the sub-bots under Core/Bot/.
/// </summary>
public class AutoplayBot
{
    // ── Sub-bots ─────────────────────────────────────────────────────
    private readonly Random _rng = new();
    private readonly MainMenuBot _mainMenuBot;
    private readonly SolarSystemBot _solarBot;
    private readonly PlanetSurfaceBot _surfaceBot;
    private readonly InteriorBot _interiorBot;

    // Tracks the last active sub-bot so RenderStatus always shows fresh data.
    private BotBase _lastActiveBot;

    public bool Enabled
    {
        get => _mainMenuBot.Enabled;
        set
        {
            _mainMenuBot.Enabled = value;
            _solarBot.Enabled = value;
            _surfaceBot.Enabled = value;
            _interiorBot.Enabled = value;
        }
    }

    public AutoplayBot()
    {
        _mainMenuBot = new MainMenuBot(_rng);
        _solarBot = new SolarSystemBot(_rng);
        _surfaceBot = new PlanetSurfaceBot(_rng);
        _interiorBot = new InteriorBot(_rng);
        _lastActiveBot = _mainMenuBot;
    }

    /// <summary>
    /// Reset all sub-bots when starting fresh (e.g., returning to main menu).
    /// </summary>
    public void Reset()
    {
        _mainMenuBot.Reset();
        _solarBot.Reset();
        _surfaceBot.Reset();
        _interiorBot.Reset();
        _lastActiveBot = _mainMenuBot;
    }

    /// <summary>
    /// Renders the current sub-bot's goal and action in the bottom-left corner.
    /// Call from each state's RenderHud.
    /// </summary>
    public void RenderStatus(ISpriteRenderer renderer) =>
        _lastActiveBot.RenderStatus(renderer);

    // ════════════════════════════════════════════════════════════════
    //  MAIN MENU
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-starts the game after a brief delay.
    /// Returns true if the bot consumed input this frame.
    /// </summary>
    public bool UpdateMainMenu(Game game, MainMenuOverlay menuOverlay, DebugMenuOverlay debugOverlay)
    {
        _lastActiveBot = _mainMenuBot;
        bool result = _mainMenuBot.Update(game, menuOverlay, debugOverlay);
        if (_mainMenuBot.GameStartRequested)
            _solarBot.Reset();
        return result;
    }

    // ════════════════════════════════════════════════════════════════
    //  SOLAR SYSTEM
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Flies the ship around the solar system, docks at stations, lands on planets, and jumps.
    /// Returns true if the bot consumed input this frame.
    /// </summary>
    public bool UpdateSolarSystem(
        Game game,
        SolarSystemSimulation sim,
        SimulationPlayer simPlayer,
        SpaceStationOverlay stationOverlay,
        PlanetLandingOverlay landingOverlay,
        GalaxyMapOverlay galaxyMapOverlay,
        InGameMenuOverlay inGameMenuOverlay,
        StarSystemData starSystem,
        bool anyOverlayOpen,
        Action<Game, SpaceStationData> beginDocking,
        Action<Game, LandingSelectionRequest> beginLanding)
    {
        _lastActiveBot = _solarBot;
        return _solarBot.Update(
            game, sim, simPlayer,
            stationOverlay, landingOverlay, galaxyMapOverlay, inGameMenuOverlay,
            starSystem, anyOverlayOpen,
            beginDocking, beginLanding,
            onPlanetLanded: _surfaceBot.OnNewPlanetLanding);
    }

    // ════════════════════════════════════════════════════════════════
    //  PLANET SURFACE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Explores the planet surface: wanders, mines, visits settlements, returns to ship.
    /// Returns true if the bot consumed input this frame.
    /// </summary>
    public bool UpdatePlanetSurface(
        Game game,
        PlanetSurfaceSimulation sim,
        SimulationPlayer simPlayer,
        StarshipMenuOverlay starshipMenu,
        InGameMenuOverlay inGameMenu,
        bool playerInsideShip,
        bool inVehicle,
        bool anyOverlayOpen,
        out PlanetSurfaceAction action)
    {
        _lastActiveBot = _surfaceBot;
        return _surfaceBot.Update(
            game, sim, simPlayer,
            starshipMenu, inGameMenu,
            playerInsideShip, inVehicle, anyOverlayOpen,
            out action);
    }

    // ════════════════════════════════════════════════════════════════
    //  INTERIOR
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Walks around the interior, visits interactables, then exits.
    /// Returns true if the bot consumed input this frame.
    /// </summary>
    public bool UpdateInterior(
        Game game,
        InteriorSimulation sim,
        SimulationPlayer simPlayer,
        StarshipMenuOverlay starshipMenu,
        InGameMenuOverlay inGameMenu,
        bool playerInsideShip,
        bool showingDialogue,
        bool anyOverlayOpen,
        out InteriorAction action)
    {
        _lastActiveBot = _interiorBot;
        return _interiorBot.Update(
            game, sim, simPlayer,
            starshipMenu, inGameMenu,
            playerInsideShip, showingDialogue, anyOverlayOpen,
            out action);
    }
}
