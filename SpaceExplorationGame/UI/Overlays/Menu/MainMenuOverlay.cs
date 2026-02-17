using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay for the main menu start-option selection.
/// Renders inside a styled panel with the game title above and seed/controls below.
/// </summary>
public class MainMenuOverlay : MenuPanelOverlayBase<StartOption>
{
    private static readonly MenuOption<StartOption>[] Options =
    [
        new(StartOption.StarSystem, "STAR SYSTEM", "Start inside a random star system, ready to explore"),
        new(StartOption.GalaxyMap, "GALAXY MAP", "Begin at the galaxy overview and choose your destination"),
        new(StartOption.SpaceStation, "SPACE STATION", "Dock at a random space station"),
        new(StartOption.SpaceStationInside, "INSIDE SPACE STATION", "Walk around inside a random space station"),
        new(StartOption.PlanetSurface, "PLANET SURFACE", "Land directly on a random planet's surface"),
        new(StartOption.Settlement, "SETTLEMENT", "Start at a settlement on an inhabited planet"),
        new(StartOption.SettlementInside, "INSIDE SETTLEMENT", "Walk around inside a random settlement")
    ];

    /// <summary>Fired when the player confirms a start option.</summary>
    public StartOption? SelectedOption { get; set; }

    /// <summary>Top Y position of the panel, for external layout (e.g. title positioning).</summary>
    public float PanelTop => PanelY;

    // ── Panel configuration ──

    protected override string Title => "CHOOSE YOUR STARTING POINT";
    protected override Color3 TitleColor => new(180, 200, 255);
    protected override float PanelWidth => 520;
    protected override bool CloseOnClickOutside => false;
    protected override byte DimAlpha => 0; // MainMenuState draws its own background
    protected override string? ControlsHint => "UP/DOWN: SELECT   ENTER: CONFIRM";

    // ── Constructor ──

    public MainMenuOverlay()
    {
        Menu = new MenuWidget<StartOption>(Options)
        {
            CenterAlign = true,
            ItemHeight = 50f,
            SelectedScale = 2.5f,
            NormalScale = 2f,
            SelectedColor = new Color3(220, 240, 255),
            NormalColor = new Color3(140, 140, 160),
            HighlightBg = new Color3(40, 60, 120),
            HighlightAlpha = 180,
            DescriptionScale = 1.5f,
            DescriptionColor = new Color3(160, 160, 180)
        };
    }

    // ── Open ──

    public override void Open()
    {
        base.Open();
        SelectedOption = null;
    }

    // ── Escape does nothing on main menu ──

    protected override void OnEscapePressed() { }

    // ── Selection ──

    protected override void OnOptionSelected(Game game, StartOption option)
    {
        SelectedOption = option;
    }
}
