using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame.UI.Overlays.Customization;

public enum StationMenuOption
{
    Repair,
    RestoreHealth,
    Missions,
    SellCargo,
    ShipCustomization,
    ShipDealer,
    AvatarCustomization,
    VehicleCustomization,
    Disembark,
    ExitStation
}

/// <summary>
/// Overlay displayed atop SolarSystemState when the player docks at a space station.
/// Provides repair, missions, customization, ship dealer, walk-interior, and exit options.
/// </summary>
public class SpaceStationOverlay : OverlayBase
{
    private StarSystemData _starSystem = null!;
    private SpaceStationData _station = null!;
    private readonly RepairOverlay _repairOverlay = new();
    private readonly HealthStationOverlay _healthStationOverlay = new();
    private readonly MissionOverlay _missionOverlay = new();
    private readonly ShipCustomizationOverlay _shipCustomization = new();
    private readonly AvatarCustomizationOverlay _avatarCustomization = new();
    private readonly VehicleCustomizationOverlay _vehicleCustomization = new();
    private readonly ShipDealerOverlay _shipDealer = new();
    private readonly SellCargoOverlay _sellCargo = new();

    private static MenuOption<StationMenuOption>[] CreateStationMenuOptions() =>
    [
        new(StationMenuOption.Repair, "REPAIR SHIP"),
        new(StationMenuOption.RestoreHealth, "RESTORE HEALTH"),
        new(StationMenuOption.Missions, "MISSIONS"),
        new(StationMenuOption.SellCargo, "SELL CARGO"),
        new(StationMenuOption.ShipCustomization, "SHIP CUSTOMIZATION"),
        new(StationMenuOption.ShipDealer, "SHIP DEALER"),
        new(StationMenuOption.AvatarCustomization, "AVATAR CUSTOMIZATION"),
        new(StationMenuOption.VehicleCustomization, "VEHICLE CUSTOMIZATION"),
        new(StationMenuOption.Disembark, "DISEMBARK"),
        new(StationMenuOption.ExitStation, "EXIT SPACE STATION")
    ];

    private readonly MenuWidget<StationMenuOption> _menu = new(CreateStationMenuOptions())
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

    public override void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Handle input once per frame. Returns true if the overlay consumed input.
    /// </summary>
    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        // Sub-overlays take priority
        if (_repairOverlay.UpdateInput(game))
            return true;
        if (_healthStationOverlay.UpdateInput(game))
            return true;
        if (_missionOverlay.UpdateInput(game))
            return true;
        if (_shipCustomization.UpdateInput(game))
            return true;
        if (_avatarCustomization.UpdateInput(game))
            return true;
        if (_vehicleCustomization.UpdateInput(game))
            return true;
        if (_shipDealer.UpdateInput(game))
            return true;
        if (_sellCargo.UpdateInput(game))
            return true;

        var confirmed = _menu.Update(input);
        if (confirmed is { } menuOption)
        {
            switch (menuOption)
            {
                case StationMenuOption.Repair:
                    _repairOverlay.Open();
                    break;
                case StationMenuOption.RestoreHealth:
                    _healthStationOverlay.Open();
                    break;
                case StationMenuOption.Missions:
                    _missionOverlay.Open();
                    break;
                case StationMenuOption.SellCargo:
                    _sellCargo.Open();
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
                case StationMenuOption.Disembark:
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

    private void UpdateMenuOptionStates(Game game)
    {
        var options = _menu.Options;
        for (int i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            switch (opt.Value)
            {
                case StationMenuOption.Repair:
                    bool shipFull = game.Player.ShipHealth >= game.Player.ShipMaxHealth;
                    _menu.SetOption(i, opt with { Enabled = !shipFull, DisabledHint = shipFull ? "HULL AT FULL INTEGRITY" : null });
                    break;
                case StationMenuOption.RestoreHealth:
                    bool healthFull = game.Player.AvatarHealth >= game.Player.AvatarMaxHealth;
                    _menu.SetOption(i, opt with { Enabled = !healthFull, DisabledHint = healthFull ? "HEALTH IS FULL" : null });
                    break;
            }
        }
    }

    /// <summary>
    /// Fixed timestep update — delegates to sub-overlays that need dt.
    /// </summary>
    public override void Update(Game game, float dt)
    {
        if (!IsOpen) return;

        // Update menu option enabled state based on player status
        UpdateMenuOptionStates(game);

        // Sub-overlays that need dt for status message timers
        _shipCustomization.Update(game, dt);
        _avatarCustomization.Update(game, dt);
        _vehicleCustomization.Update(game, dt);
        _shipDealer.Update(game, dt);
        _sellCargo.Update(game, dt);
    }

    public override void Render(Game game)
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
        _repairOverlay.Render(game);
        _healthStationOverlay.Render(game);
        _missionOverlay.Render(game);
        _shipCustomization.Render(game);
        _avatarCustomization.Render(game);
        _vehicleCustomization.Render(game);
        _shipDealer.Render(game);
        _sellCargo.Render(game);
    }
}
