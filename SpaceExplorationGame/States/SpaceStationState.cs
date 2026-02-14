using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.States;

/// <summary>
/// Space station state: Menu-based UI when docked at a space station.
/// </summary>
public class SpaceStationState : GameState
{
    public override GameStateType Type => GameStateType.SpaceStation;

    private readonly StarSystemData _starSystem;
    private readonly SpaceStationData _station;
    private int _selectedOption = 0;

    private readonly string[] _menuOptions =
    [
        "SHIP CUSTOMIZATION",
        "MISSIONS",
        "EXIT SHIP (WALK STATION)",
        "EXIT SPACE STATION"
    ];

    public SpaceStationState(StarSystemData starSystem, SpaceStationData station)
    {
        _starSystem = starSystem;
        _station = station;
    }

    public override void Enter(Game game)
    {
        // Refuel when docking at a station
        game.Player.Refuel(GameConfig.StationRefuelAmount);
    }

    public override void Exit(Game game)
    {
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void Update(Game game, float dt)
    {
        var input = game.Input;

        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
        {
            _selectedOption--;
            if (_selectedOption < 0) _selectedOption = _menuOptions.Length - 1;
        }
        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
        {
            _selectedOption++;
            if (_selectedOption >= _menuOptions.Length) _selectedOption = 0;
        }

        if (input.IsKeyPressed(SDL.Scancode.Return))
        {
            switch (_selectedOption)
            {
                case 0: // Ship customization - TODO
                    break;
                case 1: // Missions - TODO
                    break;
                case 2: // Walk station - TODO (basic 2D map)
                    break;
                case 3: // Exit station
                    game.ChangeState(new SolarSystemState(_starSystem));
                    break;
            }
        }

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            game.ChangeState(new SolarSystemState(_starSystem));
        }
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Dark background with station interior feel
        renderer.DrawRectScreen(0, 0, w, h, 8, 8, 20);

        // Station frame
        int frameX = w / 2 - 300;
        int frameY = 80;
        int frameW = 600;
        int frameH = 500;
        renderer.DrawRectScreen(frameX, frameY, frameW, frameH, 15, 15, 35, 240);

        // Border
        renderer.DrawLineScreen(frameX, frameY, frameX + frameW, frameY, 80, 80, 120);
        renderer.DrawLineScreen(frameX, frameY + frameH, frameX + frameW, frameY + frameH, 80, 80, 120);
        renderer.DrawLineScreen(frameX, frameY, frameX, frameY + frameH, 80, 80, 120);
        renderer.DrawLineScreen(frameX + frameW, frameY, frameX + frameW, frameY + frameH, 80, 80, 120);

        // Title
        renderer.DrawTextScreen(frameX + 20, frameY + 20, "SPACE STATION", 100, 200, 255, 3f);
        renderer.DrawTextScreen(frameX + 20, frameY + 55, _station.Name.ToUpper(), 200, 200, 200, 2f);
        renderer.DrawTextScreen(frameX + 20, frameY + 80, $"IN SYSTEM: {_starSystem.Name}", 120, 120, 150, 1.5f);

        // Separator
        renderer.DrawLineScreen(frameX + 20, frameY + 105, frameX + frameW - 20, frameY + 105, 60, 60, 100);

        // Credits
        renderer.DrawTextScreen(frameX + frameW - 200, frameY + 20, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);

        // Menu options
        for (int i = 0; i < _menuOptions.Length; i++)
        {
            float optY = frameY + 130 + i * 50;
            bool selected = i == _selectedOption;

            if (selected)
            {
                renderer.DrawRectScreen(frameX + 10, optY - 5, frameW - 20, 40, 40, 40, 80);
                renderer.DrawTextScreen(frameX + 30, optY, $"> {_menuOptions[i]}", 100, 255, 200, 2.5f);
            }
            else
            {
                renderer.DrawTextScreen(frameX + 40, optY, _menuOptions[i], 160, 160, 180, 2f);
            }
        }

        // Ship status
        float statusY = frameY + 350;
        renderer.DrawLineScreen(frameX + 20, statusY, frameX + frameW - 20, statusY, 60, 60, 100);
        renderer.DrawTextScreen(frameX + 20, statusY + 10, "SHIP STATUS", 150, 150, 200, 2f);
        renderer.DrawTextScreen(frameX + 20, statusY + 40, $"HULL: {game.Player.ShipHealth:F0}/{game.Player.ShipMaxHealth:F0}", 100, 255, 100, 1.5f);
        renderer.DrawTextScreen(frameX + 20, statusY + 60, $"FUEL: {game.Player.ShipFuel:F0}/{game.Player.ShipMaxFuel:F0}", 100, 200, 255, 1.5f);
        renderer.DrawTextScreen(frameX + 20, statusY + 80, $"[REFUELED +{GameConfig.StationRefuelAmount:F0}]", 80, 200, 120, 1.5f);

        // Health bar
        float barX = frameX + 250;
        float barW = 200;
        renderer.DrawRectScreen(barX, statusY + 40, barW, 12, 40, 40, 40);
        renderer.DrawRectScreen(barX, statusY + 40, barW * (game.Player.ShipHealth / game.Player.ShipMaxHealth), 12, 100, 255, 100);

        // Fuel bar
        renderer.DrawRectScreen(barX, statusY + 60, barW, 12, 40, 40, 40);
        renderer.DrawRectScreen(barX, statusY + 60, barW * (game.Player.ShipFuel / game.Player.ShipMaxFuel), 12, 100, 200, 255);

        // Controls
        renderer.DrawTextScreen(frameX + 20, frameY + frameH - 30, "UP/DOWN: SELECT  ENTER: CONFIRM  ESC: EXIT", 100, 100, 130, 1.5f);
    }
}
