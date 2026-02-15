using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame.UI.Overlays;

public enum StationMenuOption
{
    Repair,
    Missions,
    ShipCustomization,
    ShipDealer,
    AvatarCustomization,
    VehicleCustomization,
    WalkStation,
    ExitStation
}

/// <summary>
/// Overlay displayed atop SolarSystemState when the player docks at a space station.
/// Provides repair, missions, customization, ship dealer, walk-interior, and exit options.
/// </summary>
public class SpaceStationOverlay
{
    public bool IsOpen { get; private set; }

    private StarSystemData _starSystem = null!;
    private SpaceStationData _station = null!;
    private readonly ServiceOverlays _overlays = new();
    private readonly ShipCustomizationOverlay _shipCustomization = new();
    private readonly AvatarCustomizationOverlay _avatarCustomization = new();
    private readonly VehicleCustomizationOverlay _vehicleCustomization = new();
    private readonly ShipDealerOverlay _shipDealer = new();

    private static readonly MenuOption<StationMenuOption>[] StationMenuOptions =
    [
        new(StationMenuOption.Repair, "REPAIR"),
        new(StationMenuOption.Missions, "MISSIONS"),
        new(StationMenuOption.ShipCustomization, "SHIP CUSTOMIZATION"),
        new(StationMenuOption.ShipDealer, "SHIP DEALER"),
        new(StationMenuOption.AvatarCustomization, "AVATAR CUSTOMIZATION"),
        new(StationMenuOption.VehicleCustomization, "VEHICLE CUSTOMIZATION"),
        new(StationMenuOption.WalkStation, "WALK STATION"),
        new(StationMenuOption.ExitStation, "EXIT SPACE STATION")
    ];

    private readonly MenuWidget<StationMenuOption> _menu = new(StationMenuOptions)
    {
        ItemHeight = 50f,
        SelectedScale = 2.5f,
        NormalScale = 2f,
        SelectedColor = (100, 255, 200),
        NormalColor = (160, 160, 180),
        HighlightBg = (40, 40, 80),
        HighlightAlpha = 255,
    };

    public void Open(StarSystemData starSystem, SpaceStationData station, Game game)
    {
        _starSystem = starSystem;
        _station = station;
        _menu.SelectedIndex = 0;
        IsOpen = true;

        // Refuel when docking
        game.Player.Refuel(GameConfig.StationRefuelAmount);
    }

    public void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Update overlay logic. Returns true if the overlay consumed input (blocks solar system controls).
    /// </summary>
    public bool Update(Game game, float dt)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        // Sub-overlays take priority
        if (_overlays.Active != ServiceOverlays.OverlayType.None)
        {
            _overlays.Update(game, input);
            return true;
        }

        if (_shipCustomization.IsOpen)
        {
            _shipCustomization.Update(game, input, dt);
            return true;
        }

        if (_avatarCustomization.IsOpen)
        {
            _avatarCustomization.Update(game, input, dt);
            return true;
        }

        if (_vehicleCustomization.IsOpen)
        {
            _vehicleCustomization.Update(game, input, dt);
            return true;
        }

        if (_shipDealer.IsOpen)
        {
            _shipDealer.Update(game, input, dt);
            return true;
        }

        var confirmed = _menu.Update(input);
        if (confirmed is { } menuOption)
        {
            switch (menuOption)
            {
                case StationMenuOption.Repair:
                    _overlays.Open(ServiceOverlays.OverlayType.Repair);
                    break;
                case StationMenuOption.Missions:
                    _overlays.Open(ServiceOverlays.OverlayType.Mission);
                    break;
                case StationMenuOption.ShipCustomization:
                    _shipCustomization.Open(game.Player);
                    break;
                case StationMenuOption.ShipDealer:
                    _shipDealer.Open();
                    break;
                case StationMenuOption.AvatarCustomization:
                    _avatarCustomization.Open();
                    break;
                case StationMenuOption.VehicleCustomization:
                    _vehicleCustomization.Open();
                    break;
                case StationMenuOption.WalkStation:
                    game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromStation;
                    game.Player.ReturnStationIndex = _station.Index;
                    Close();
                    game.ChangeState(new InteriorState(
                        InteriorOrigin.Station, _starSystem, station: _station));
                    break;
                case StationMenuOption.ExitStation:
                    Close();
                    break;
            }
        }

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close();
        }

        return true;
    }

    public void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Semi-transparent dark overlay so the solar system is visible behind
        renderer.DrawRectScreen(0, 0, w, h, 0, 0, 0, 180);

        // Station frame with gradient-like border
        int frameX = w / 2 - 300;
        int frameY = 80;
        int frameW = 600;
        int menuHeight = (int)_menu.TotalHeight;
        int frameH = 130 + menuHeight + 20 + 110 + 40; // header + menu + gap + status + controls
        renderer.DrawRectScreen(frameX - 2, frameY - 2, frameW + 4, frameH + 4, 60, 60, 100, 150);
        renderer.DrawRectScreen(frameX, frameY, frameW, frameH, 15, 15, 35, 240);

        // Corner accents
        int accentLen = 30;
        renderer.DrawLineScreen(frameX, frameY, frameX + accentLen, frameY, 100, 180, 255);
        renderer.DrawLineScreen(frameX, frameY, frameX, frameY + accentLen, 100, 180, 255);
        renderer.DrawLineScreen(frameX + frameW, frameY, frameX + frameW - accentLen, frameY, 100, 180, 255);
        renderer.DrawLineScreen(frameX + frameW, frameY, frameX + frameW, frameY + accentLen, 100, 180, 255);
        renderer.DrawLineScreen(frameX, frameY + frameH, frameX + accentLen, frameY + frameH, 100, 180, 255);
        renderer.DrawLineScreen(frameX, frameY + frameH, frameX, frameY + frameH - accentLen, 100, 180, 255);
        renderer.DrawLineScreen(frameX + frameW, frameY + frameH, frameX + frameW - accentLen, frameY + frameH, 100, 180, 255);
        renderer.DrawLineScreen(frameX + frameW, frameY + frameH, frameX + frameW, frameY + frameH - accentLen, 100, 180, 255);

        // Title
        renderer.DrawTextScreen(frameX + 20, frameY + 20, "SPACE STATION", 100, 200, 255, 3f);
        renderer.DrawTextScreen(frameX + 20, frameY + 55, _station.Name.ToUpper(), 200, 200, 200, 2f);
        renderer.DrawTextScreen(frameX + 20, frameY + 80, $"IN SYSTEM: {_starSystem.Name}", 120, 120, 150, 1.5f);

        // Separator
        renderer.DrawLineScreen(frameX + 20, frameY + 105, frameX + frameW - 20, frameY + 105, 60, 60, 100);

        // Credits
        renderer.DrawTextScreen(frameX + frameW - 200, frameY + 20, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);

        // Menu options
        _menu.Render(renderer, frameX + 10, frameY + 130, frameW - 20);

        // Ship status
        float statusY = frameY + 130 + menuHeight + 20;
        renderer.DrawLineScreen(frameX + 20, statusY, frameX + frameW - 20, statusY, 60, 60, 100);
        renderer.DrawTextScreen(frameX + 20, statusY + 10, $"SHIP: {game.Player.CurrentShipType.Name.ToUpper()}", 150, 150, 200, 2f);
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

        // Sub-overlays drawn on top
        _overlays.Render(game, renderer);
        _shipCustomization.Render(game, renderer);
        _avatarCustomization.Render(game, renderer);
        _vehicleCustomization.Render(game, renderer);
        _shipDealer.Render(game, renderer);
    }
}
