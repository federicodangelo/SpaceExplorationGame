using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.UI;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

public enum DebugMenuAction
{
    None = -1,
    StarType,
    StartingShip,
    StarTypeShowcase,
    PlanetTypeShowcase,
    AsteroidShowcase,
    SurfaceMiningShowcase,
    Back,
}

/// <summary>
/// Debug overlay accessible from the main menu.
/// </summary>
public class DebugMenuOverlay : MenuPanelOverlayBase<DebugMenuAction>
{
    private const int StarTypeIdx = 0;
    private const int StartingShipIdx = 1;

    private static readonly StarClass[] StarTypes = Enum.GetValues<StarClass>();
    private static readonly ShipType[] ShipTypes = ShipTypeCatalog.AllTypes;

    private int _starTypeIndex;
    private int _shipTypeIndex;

    public StarClass SelectedStarType => StarTypes[_starTypeIndex];
    public ShipType SelectedStartingShip => ShipTypes[_shipTypeIndex];

    /// <summary>When set, start a dedicated solar system showcasing the selected star type.</summary>
    public bool StartStarTypeShowcaseRequested { get; set; }

    /// <summary>When set, start a dedicated solar system showcasing all planet types.</summary>
    public bool StartPlanetTypeShowcaseRequested { get; set; }

    /// <summary>When set, start a dedicated solar system focused on asteroid mining in space.</summary>
    public bool StartAsteroidShowcaseRequested { get; set; }

    /// <summary>When set, start already landed on a rock-dense planet surface for mining tests.</summary>
    public bool StartSurfaceMiningShowcaseRequested { get; set; }

    protected override string Title => "DEBUG";
    protected override Color3 TitleColor => new(255, 210, 120);
    protected override float PanelWidth => 620;
    protected override float BottomPadding => base.BottomPadding + 40;
    protected override bool CloseOnClickOutside => false;

    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            return $"{input.GetActionHelpText(InputAction.MenuUp)}/{input.GetActionHelpText(InputAction.MenuDown)}: NAVIGATE  " +
                     $"{input.GetActionHelpText(InputAction.MenuConfirm)}/{input.GetActionHelpText(InputAction.MenuLeft)}/{input.GetActionHelpText(InputAction.MenuRight)}: CHANGE  " +
                   $"{input.GetActionHelpText(InputAction.MenuBack)}: BACK";
        }
    }

    public DebugMenuOverlay()
    {
        Menu = new MenuWidget<DebugMenuAction>(
        [
            new(DebugMenuAction.StarType, "STAR TYPE: < G >", "Select the star type used by debug showcase scenarios"),
            new(DebugMenuAction.StartingShip, "STARTING SHIP: < SCOUT >", "Choose which ship you start with when launching from the main menu"),
            new(DebugMenuAction.StarTypeShowcase, "STAR TYPE SHOWCASE", "Start a debug system focused on the selected star type"),
            new(DebugMenuAction.PlanetTypeShowcase, "PLANET TYPE SHOWCASE", "Start in a debug solar system containing all planet types"),
            new(DebugMenuAction.AsteroidShowcase, "ASTEROID MINING SHOWCASE", "Start in space with dense asteroid belts for mining tests"),
            new(DebugMenuAction.SurfaceMiningShowcase, "SURFACE MINING SHOWCASE", "Start already landed on a planet surface full of rocks"),
            new(DebugMenuAction.Back, "BACK", "Return to main menu"),
        ])
        {
            CenterAlign = true,
            ItemHeight = 50f,
            SelectedScale = 2.3f,
            NormalScale = 1.9f,
            SelectedColor = new Color3(255, 230, 180),
            NormalColor = new Color3(170, 150, 120),
            HighlightBg = new Color3(90, 70, 30),
            HighlightAlpha = 180,
            DescriptionScale = 1.4f,
            DescriptionColor = new Color3(200, 180, 140)
        };
    }

    public override void Open()
    {
        base.Open();

        var (savedStarTypeIndex, savedShipTypeIndex, savedSelectedIndex) = MenuOptionsPersistence.GetDebugSelections();
        _starTypeIndex = Math.Clamp(savedStarTypeIndex, 0, StarTypes.Length - 1);
        _shipTypeIndex = Math.Clamp(savedShipTypeIndex, 0, ShipTypes.Length - 1);
        Menu.SelectedIndex = Math.Clamp(savedSelectedIndex, 0, Menu.ItemCount - 1);

        StartStarTypeShowcaseRequested = false;
        StartPlanetTypeShowcaseRequested = false;
        StartAsteroidShowcaseRequested = false;
        StartSurfaceMiningShowcaseRequested = false;
        UpdateCyclingLabels();
    }

    public override void Close()
    {
        SaveSelectionState();
        base.Close();
    }

    protected override void OnEscapePressed()
    {
        Close();
    }

    protected override void OnOptionSelected(Game game, DebugMenuAction option)
    {
        switch (option)
        {
            case DebugMenuAction.StarType:
                CycleStarType(1);
                break;
            case DebugMenuAction.StartingShip:
                CycleStartingShip(1);
                break;
            case DebugMenuAction.StarTypeShowcase:
                StartStarTypeShowcaseRequested = true;
                break;
            case DebugMenuAction.PlanetTypeShowcase:
                StartPlanetTypeShowcaseRequested = true;
                break;
            case DebugMenuAction.AsteroidShowcase:
                StartAsteroidShowcaseRequested = true;
                break;
            case DebugMenuAction.SurfaceMiningShowcase:
                StartSurfaceMiningShowcaseRequested = true;
                break;
            case DebugMenuAction.Back:
                Close();
                break;
        }
    }

    protected override void ProcessInput(Game game, InputManager input)
    {
        UpdateCyclingLabels();

        if (Menu.SelectedValue == DebugMenuAction.StarType)
        {
            if (input.IsActionPressed(InputAction.MenuLeft))
            {
                CycleStarType(-1);
                game.Audio.PlaySfx(Audio.SfxType.MenuSelect);
                return;
            }
            if (input.IsActionPressed(InputAction.MenuRight))
            {
                CycleStarType(1);
                game.Audio.PlaySfx(Audio.SfxType.MenuSelect);
                return;
            }
        }
        else if (Menu.SelectedValue == DebugMenuAction.StartingShip)
        {
            if (input.IsActionPressed(InputAction.MenuLeft))
            {
                CycleStartingShip(-1);
                game.Audio.PlaySfx(Audio.SfxType.MenuSelect);
                return;
            }
            if (input.IsActionPressed(InputAction.MenuRight))
            {
                CycleStartingShip(1);
                game.Audio.PlaySfx(Audio.SfxType.MenuSelect);
                return;
            }
        }

        base.ProcessInput(game, input);
        SaveSelectionState();
    }

    private void CycleStarType(int direction)
    {
        _starTypeIndex = (_starTypeIndex + direction + StarTypes.Length) % StarTypes.Length;
        SaveSelectionState();
        UpdateCyclingLabels();
    }

    private void CycleStartingShip(int direction)
    {
        _shipTypeIndex = (_shipTypeIndex + direction + ShipTypes.Length) % ShipTypes.Length;
        SaveSelectionState();
        UpdateCyclingLabels();
    }

    private void SaveSelectionState()
    {
        MenuOptionsPersistence.SetDebugSelections(_starTypeIndex, _shipTypeIndex, Menu.SelectedIndex);
    }

    private void UpdateCyclingLabels()
    {
        string confirm = CurrentInput?.GetActionHelpText(InputAction.MenuConfirm).ToUpper() ?? "CONFIRM";
        string left = CurrentInput?.GetActionHelpText(InputAction.MenuLeft).ToUpper() ?? "LEFT";
        string right = CurrentInput?.GetActionHelpText(InputAction.MenuRight).ToUpper() ?? "RIGHT";

        Menu.SetOption(StarTypeIdx, new MenuOption<DebugMenuAction>(DebugMenuAction.StarType,
            $"STAR TYPE: < {SelectedStarType} >",
            $"Press {confirm} or {left}/{right} to change star type"));
        Menu.SetOption(StartingShipIdx, new MenuOption<DebugMenuAction>(DebugMenuAction.StartingShip,
            $"STARTING SHIP: < {SelectedStartingShip.Name.ToUpper()} >",
            $"Press {confirm} or {left}/{right} to change starting ship"));
    }

    protected override void RenderAdditionalContent(Game game, SpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        float y = MenuY + Menu.TotalHeight + 8;
        renderer.DrawLineScreen(panelX + 15, y - 4, panelX + panelW - 15, y - 4, new Color3(90, 70, 30));
        renderer.DrawTextScreen(panelX + 15, y + 6,
            "Use this menu to launch debug test scenarios.",
            new Color3(200, 180, 140), 1.4f);
    }
}
