using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Customization;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

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
public class SpaceStationOverlay : MenuPanelOverlayBase<StationMenuOption>
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

    // ── Panel configuration ──

    protected override string Title => "SPACE STATION";
    protected override Color3 TitleColor => new(100, 200, 255);
    protected override float PanelWidth => 600;
    protected override float PanelHeight => 110 + Menu.TotalHeight + 20 + 110 + 40;
    protected override bool ShowCredits => true;
    protected override bool CloseOnClickOutside => false;
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            return $"{input.GetActionHelpText(InputAction.MenuUp)}/{input.GetActionHelpText(InputAction.MenuDown)}: SELECT  " +
                   $"{input.GetActionHelpText(InputAction.MenuConfirm)}: CONFIRM  " +
                   $"{input.GetActionHelpText(InputAction.MenuBack)}: EXIT";
        }
    }
    protected override float PanelY => 80;

    // ── Menu layout ──

    protected override float MenuY => PanelY + 110;

    // ── Constructor ──

    public SpaceStationOverlay()
    {
        Menu = CreateMenu();

        RegisterSubOverlay(_repairOverlay);
        RegisterSubOverlay(_healthStationOverlay);
        RegisterSubOverlay(_missionOverlay);
        RegisterSubOverlay(_shipCustomization);
        RegisterSubOverlay(_avatarCustomization);
        RegisterSubOverlay(_vehicleCustomization);
        RegisterSubOverlay(_shipDealer);
        RegisterSubOverlay(_sellCargo);
    }

    private static MenuWidget<StationMenuOption> CreateMenu() => new([
        new(StationMenuOption.Repair, "REPAIR SHIP"),
        new(StationMenuOption.RestoreHealth, "RESTORE HEALTH"),
        new(StationMenuOption.Missions, "MISSIONS"),
        new(StationMenuOption.SellCargo, "SELL CARGO"),
        new(StationMenuOption.ShipCustomization, "SHIP CUSTOMIZATION"),
        new(StationMenuOption.ShipDealer, "SHIP DEALER"),
        new(StationMenuOption.AvatarCustomization, "AVATAR CUSTOMIZATION"),
        new(StationMenuOption.VehicleCustomization, "VEHICLE CUSTOMIZATION"),
        new(StationMenuOption.Disembark, "DISEMBARK"),
        new(StationMenuOption.ExitStation, "LAUNCH"),
    ])
    {
        ItemHeight = 50f,
        SelectedScale = 2.5f,
        NormalScale = 2f,
        SelectedColor = new Color3(100, 255, 200),
        NormalColor = new Color3(160, 160, 180),
        HighlightBg = new Color3(40, 40, 80),
        HighlightAlpha = 255,
    };

    // ── Open ──

    public void Open(StarSystemData starSystem, SpaceStationData station, Game game)
    {
        _starSystem = starSystem;
        _station = station;
        Menu.SelectedIndex = 0;
        base.Open();

        game.Player.Refuel(game.Player.ShipMaxFuel);
        game.Player.NotifyStationDocked(starSystem.Index);
    }

    // ── Menu actions ──

    protected override void OnOptionSelected(Game game, StationMenuOption option)
    {
        switch (option)
        {
            case StationMenuOption.Repair:
                _repairOverlay.Open();
                break;
            case StationMenuOption.RestoreHealth:
                _healthStationOverlay.Open();
                break;
            case StationMenuOption.Missions:
                var boardSeed = MissionGenerator.GetStationBoardSeed(game.Seeds, _starSystem.Index, _station.Index);
                _missionOverlay.Open(game, _starSystem, boardSeed);
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

    // ── Update ──

    protected override void OnUpdate(Game game)
    {
        UpdateMenuOptionStates(game);
    }

    private void UpdateMenuOptionStates(Game game)
    {
        var options = Menu.Options;
        for (int i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            switch (opt.Value)
            {
                case StationMenuOption.Repair:
                    bool shipFull = game.Player.ShipHealth >= game.Player.ShipMaxHealth;
                    Menu.SetOption(i, opt with { Enabled = !shipFull, DisabledHint = shipFull ? "HULL AT FULL INTEGRITY" : null });
                    break;
                case StationMenuOption.RestoreHealth:
                    bool healthFull = game.Player.AvatarHealth >= game.Player.AvatarMaxHealth;
                    Menu.SetOption(i, opt with { Enabled = !healthFull, DisabledHint = healthFull ? "HEALTH IS FULL" : null });
                    break;
            }
        }
    }

    protected override void RenderAdditionalContent(Game game, SpriteRenderer renderer, float panelX, float contentY, float panelW, float contentH)
    {
        int menuHeight = (int)Menu.TotalHeight;
        float px = PanelX, py = PanelY, pw = PanelWidth, ph = PanelHeight;

        // Station info header
        renderer.DrawTextScreen(px + 20, py + 55, _station.Name.ToUpper(), new Color3(200, 200, 200), 2f);
        renderer.DrawTextScreen(px + 20, py + 80, $"IN SYSTEM: {_starSystem.Name}", new Color3(120, 120, 150), 1.5f);
        renderer.DrawLineScreen(px + 20, py + 105, px + pw - 20, py + 105, new Color3(60, 80, 140));

        // Ship status section
        float statusY = MenuY + menuHeight + 4;
        renderer.DrawLineScreen(px + 20, statusY, px + pw - 20, statusY, new Color3(60, 60, 100));
        renderer.DrawTextScreen(px + 20, statusY + 10, $"SHIP: {game.Player.CurrentShipType.Name.ToUpper()}", new Color3(150, 150, 200), 2f);
        renderer.DrawTextScreen(px + 20, statusY + 40, $"HULL: {game.Player.ShipHealth:F0}/{game.Player.ShipMaxHealth:F0}", new Color3(100, 255, 100), 1.5f);
        renderer.DrawTextScreen(px + 20, statusY + 60, $"FUEL: {game.Player.ShipFuel:F0}/{game.Player.ShipMaxFuel:F0}", new Color3(100, 200, 255), 1.5f);
        renderer.DrawTextScreen(px + 20, statusY + 80, $"[FULLY REFUELED]", new Color3(80, 200, 120), 1.5f);

        float barX = px + 250;
        float barW = 200;
        renderer.DrawRectScreen(barX, statusY + 40, barW, 12, new Color3(40, 40, 40));
        renderer.DrawRectScreen(barX, statusY + 40, barW * (game.Player.ShipHealth / game.Player.ShipMaxHealth), 12, new Color3(100, 255, 100));
        renderer.DrawRectScreen(barX, statusY + 60, barW, 12, new Color3(40, 40, 40));
        renderer.DrawRectScreen(barX, statusY + 60, barW * (game.Player.ShipFuel / game.Player.ShipMaxFuel), 12, new Color3(100, 200, 255));
    }
}
