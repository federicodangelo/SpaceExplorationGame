using SpaceExplorationGame.Core;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

public enum StarshipMenuOption
{
    TakeOff,
    DisembarkOnFoot,
    DisembarkOnVehicle
}

/// <summary>
/// Overlay displayed when the player is inside the starship on a planet surface.
/// Provides options to fly to space, or disembark on foot or in a vehicle.
/// </summary>
public class StarshipMenuOverlay : MenuPanelOverlayBase<StarshipMenuOption>
{
    protected override string Title => "STARSHIP";
    protected override Color3 TitleColor => new(100, 200, 255);
    protected override float PanelWidth => 500;
    protected override bool CloseOnClickOutside => false;

    /// <summary>The last confirmed menu choice, or null if none.</summary>
    public StarshipMenuOption? LastChoice { get; private set; }

    /// <summary>Whether the player has a vehicle available for the vehicle option.</summary>
    public bool HasVehicle { get; set; } = true;

    /// <summary>Whether the player can deploy the vehicle on the planet surface (if they have one).</summary>
    public bool VehicleCanBeDeployed { get; set; } = true;

    /// <summary>Whether the vehicle is already deployed on the planet surface.</summary>
    public bool VehicleDeployed { get; set; }

    public override void Open()
    {
        bool vehicleEnabled = HasVehicle && !VehicleDeployed && VehicleCanBeDeployed;
        string? vehicleHint =
            !HasVehicle ? "(NO VEHICLE)" :
            VehicleDeployed ? "(ALREADY DEPLOYED)" :
            !VehicleCanBeDeployed ? "(CAN'T BE DEPLOYED HERE)" :
            null;

        Menu = new MenuWidget<StarshipMenuOption>([
            new(StarshipMenuOption.DisembarkOnFoot, "DISEMBARK (ON FOOT)"),
            new(StarshipMenuOption.DisembarkOnVehicle, "DISEMBARK (ON VEHICLE)",
                Enabled: vehicleEnabled, DisabledHint: vehicleHint),
            new(StarshipMenuOption.TakeOff, "TAKE OFF"),
        ])
        {
            CenterAlign = true,
            ItemHeight = 50f,
            SelectedScale = 2.5f,
            NormalScale = 2f,
            SelectedColor = new Color3(100, 255, 200),
            NormalColor = new Color3(160, 160, 180),
            HighlightBg = new Color3(40, 60, 120),
            HighlightAlpha = 200,
        };

        LastChoice = null;
        base.Open();
    }

    /// <summary>Player must select an option — Escape does nothing.</summary>
    protected override void OnEscapePressed() { }

    protected override void OnOptionSelected(Game game, StarshipMenuOption option)
    {
        LastChoice = option;
        Close();
    }
}
