using Engine.Network;
using Engine.Network.Client;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.States;

/// <summary>
/// Transient state that connects to a multiplayer server and transitions to
/// <see cref="SolarSystemState"/> on success, or back to <see cref="MainMenuState"/> on failure.
/// </summary>
public sealed class MultiplayerConnectState : GameState
{
    public override GameStateType Type => GameStateType.MainMenu;

    private readonly string _url;
    private readonly string _playerName;

    private ClientNetworkManager? _net;
    private Task? _connectTask;
    private float _elapsed;
    private string _statusMessage = "CONNECTING...";
    private bool _failed;

    private const float TimeoutSeconds = 10f;

    public MultiplayerConnectState(string url, string playerName)
    {
        _url = url;
        _playerName = playerName;
    }

    public override void Enter(Game game)
    {
        game.Audio.SetMusicTheme(AudioThemes.MainMenu);
        game.Player.ClearReturnContext();

        // Don't assign the network to Game.Network yet; we have to wait for the welcome message to get the starting location and galaxy seed.
        _net = new ClientNetworkManager();

        _statusMessage = "CONNECTING...";
        _failed = false;
        _elapsed = 0f;

        var player = game.Player;
        var ship = player.CurrentShipType;
        var shipStats = player.GetCombinedShipStats();

        NetPlayerInfo info = new NetPlayerInfo
        {
            ShipTypeId = game.Player.CurrentShipType.Id,
            MaxHull = (int)(ship.BaseHull + shipStats.MaxHull),
            MaxShield = (int)shipStats.ShieldStrength,
        };

        NetPlayerLocation location = NetPlayerLocation.ForSolarSystem(0);

        // Begin async connect; we'll poll IsJoined each frame
        _connectTask = _net.ConnectAsync(_url, _playerName, info, location);
    }

    public override void Exit(Game game) { }

    public override void UpdateInput(Game game)
    {
        // Allow cancelling with Escape / Back
        if (game.Input.IsActionPressed(InputAction.MenuBack) || game.Input.IsActionPressed(InputAction.MenuConfirm) && _failed)
        {
            _net?.Dispose();
            game.ChangeState(new MainMenuState());
        }
    }

    public override void Update(Game game)
    {
        if (_failed) return;

        _elapsed += game.DeltaTime;

        // Check for task-level exception (connection refused, DNS failure, etc.)
        if (_connectTask != null && _connectTask.IsFaulted)
        {
            var msg = _connectTask.Exception?.GetBaseException().Message ?? "Unknown error";
            _statusMessage = $"FAILED: {msg}";
            Console.Error.WriteLine($"[Net] Connect failed: {msg}");
            _failed = true;
            _net?.Dispose();
            return;
        }

        // Process any messages that arrived (e.g. S_Welcome)
        _net?.ProcessMessages();

        if (_net != null && _net.IsJoined)
        {
            OnJoined(game);
            return;
        }

        // Timeout
        if (_elapsed >= TimeoutSeconds)
        {
            _statusMessage = "FAILED: SERVER DID NOT RESPOND";
            Console.Error.WriteLine("[Net] Connect timeout — no welcome message received.");
            _failed = true;
            _net?.Dispose();
        }
    }

    private void OnJoined(Game game)
    {
        var net = _net!;
        var location = net.PlayerStartingLocation;
        var player = game.Player;

        Console.WriteLine($"[Net] Joined as player {net.LocalPlayerId}, {location}");

        // Assign the network manager to the game AFTER we print the welcome message
        game.Network = net;

        // Regenerate galaxy with the server's seed so both sides use identical data
        game.RegenerateGalaxy(net.ServerGalaxySeed);
        game.Audio.PlaySfx(AudioSfx.MenuSelect);

        var solarSystemData = game.GalaxyData[location.SolarSystemIndex];
        var solarSystem = game.UniverseGenerator.GenerateSolarSystem(solarSystemData);

        var spaceStationData = location.SolarSystemIndex >= 0 ? solarSystem.SpaceStations.FirstOrDefault(s => s.Index == location.SpaceStationIndex) : null;
        var planetData = location.SolarSystemIndex >= 0 && location.PlanetIndex >= 0 ? solarSystem.Planets.FirstOrDefault(p => p.Index == location.PlanetIndex) : null;
        if (location.MoonIndex >= 0 && planetData != null)
            planetData = planetData.Moons.FirstOrDefault(m => m.Index == location.MoonIndex)?.ToPlanetData(planetData.Index);

        var planet = planetData != null ? game.UniverseGenerator.GeneratePlanetSurface(solarSystemData, planetData) : null;
        var settlementData = location.SettlementIndex >= 0 && planet != null ? planet.Settlements.FirstOrDefault(s => s.Index == location.SettlementIndex) : null;

        player.CurrentStarSystemIndex = solarSystemData.Index;

        if (settlementData != null)
        {
            game.ChangeState(new InteriorState(
                InteriorOrigin.Settlement,
                solarSystemData,
                planet: planetData,
                settlement: settlementData)
            );
        }
        else if (spaceStationData != null)
        {
            game.ChangeState(new InteriorState(
                InteriorOrigin.SpaceStation,
                solarSystemData,
                spaceStation: spaceStationData)
            );
        }
        else if (planetData != null)
        {
            game.ChangeState(new PlanetSurfaceState(solarSystemData, planetData));
        }
        else
        {
            game.ChangeState(new SolarSystemState(solarSystemData));
        }
    }

    public override void RenderGame(Game game)
    {
        // Dark background
        game.SpriteRenderer.DrawRectScreen(0, 0,
            game.SpriteRenderer.WindowWidth, game.SpriteRenderer.WindowHeight,
            new Engine.Core.Color4(0, 0, 20, 255));
    }

    public override void RenderHud(Game game)
    {
        var renderer = game.SpriteRenderer;
        float cx = renderer.WindowWidth * 0.5f;
        float cy = renderer.WindowHeight * 0.5f;

        float titleScale = 3f;
        float msgScale = 2f;
        float lineH = 30f;

        string title = _failed ? "CONNECTION FAILED" : "JOINING SERVER";
        var titleColor = _failed ? new Engine.Core.Color3(255, 80, 80) : new Engine.Core.Color3(180, 200, 255);
        float titleW = renderer.MeasureText(title, titleScale);
        renderer.DrawTextScreen(cx - titleW * 0.5f, cy - lineH * 2, title, titleColor, titleScale);

        float msgW = renderer.MeasureText(_statusMessage, msgScale);
        renderer.DrawTextScreen(cx - msgW * 0.5f, cy, _statusMessage, new Engine.Core.Color3(200, 200, 200), msgScale);

        if (_failed)
        {
            string hint = "PRESS ENTER OR ESCAPE TO GO BACK";
            float hintScale = 1.5f;
            float hintW = renderer.MeasureText(hint, hintScale);
            renderer.DrawTextScreen(cx - hintW * 0.5f, cy + lineH * 2.5f, hint, new Engine.Core.Color3(140, 140, 160), hintScale);
        }
        else if (!_failed)
        {
            // Animated dots
            int dots = ((int)(_elapsed * 2f) % 4);
            string progress = _url + new string('.', dots);
            float progScale = 1.5f;
            float progW = renderer.MeasureText(progress, progScale);
            renderer.DrawTextScreen(cx - progW * 0.5f, cy + lineH * 1.5f, progress, new Engine.Core.Color3(100, 120, 180), progScale);
        }
    }
}
