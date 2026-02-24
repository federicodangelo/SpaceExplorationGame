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
    StarTypeShowcase,
    PlanetTypeShowcase,
    AsteroidShowcase,
    Back,
}

/// <summary>
/// Debug overlay accessible from the main menu.
/// </summary>
public class DebugMenuOverlay : MenuPanelOverlayBase<DebugMenuAction>
{
    private const int StarTypeIdx = 0;

    private static readonly StarClass[] StarTypes = Enum.GetValues<StarClass>();
    private static int s_savedStarTypeIndex;
    private static int s_savedSelectedIndex;

    private int _starTypeIndex = s_savedStarTypeIndex;

    public StarClass SelectedStarType => StarTypes[_starTypeIndex];

    /// <summary>When set, start a dedicated solar system showcasing the selected star type.</summary>
    public bool StartStarTypeShowcaseRequested { get; set; }

    /// <summary>When set, start a dedicated solar system showcasing all planet types.</summary>
    public bool StartPlanetTypeShowcaseRequested { get; set; }

    /// <summary>When set, start a dedicated solar system focused on asteroid mining in space.</summary>
    public bool StartAsteroidShowcaseRequested { get; set; }

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
                   $"{input.GetActionHelpText(InputAction.MenuConfirm)}: SELECT  " +
                   $"{input.GetActionHelpText(InputAction.MenuBack)}: BACK";
        }
    }

    public DebugMenuOverlay()
    {
        Menu = new MenuWidget<DebugMenuAction>(
        [
            new(DebugMenuAction.StarType, "STAR TYPE: < G >", "Select the star type used by debug showcase scenarios"),
            new(DebugMenuAction.StarTypeShowcase, "STAR TYPE SHOWCASE", "Start a debug system focused on the selected star type"),
            new(DebugMenuAction.PlanetTypeShowcase, "PLANET TYPE SHOWCASE", "Start in a debug solar system containing all planet types"),
            new(DebugMenuAction.AsteroidShowcase, "ASTEROID MINING SHOWCASE", "Start in space with dense asteroid belts for mining tests"),
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

        _starTypeIndex = Math.Clamp(s_savedStarTypeIndex, 0, StarTypes.Length - 1);
        Menu.SelectedIndex = Math.Clamp(s_savedSelectedIndex, 0, Menu.ItemCount - 1);

        StartStarTypeShowcaseRequested = false;
        StartPlanetTypeShowcaseRequested = false;
        StartAsteroidShowcaseRequested = false;
        UpdateStarTypeLabel();
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
            case DebugMenuAction.StarTypeShowcase:
                StartStarTypeShowcaseRequested = true;
                break;
            case DebugMenuAction.PlanetTypeShowcase:
                StartPlanetTypeShowcaseRequested = true;
                break;
            case DebugMenuAction.AsteroidShowcase:
                StartAsteroidShowcaseRequested = true;
                break;
            case DebugMenuAction.Back:
                Close();
                break;
        }
    }

    protected override void ProcessInput(Game game, InputManager input)
    {
        UpdateStarTypeLabel();

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

        base.ProcessInput(game, input);
        SaveSelectionState();
    }

    private void CycleStarType(int direction)
    {
        _starTypeIndex = (_starTypeIndex + direction + StarTypes.Length) % StarTypes.Length;
        s_savedStarTypeIndex = _starTypeIndex;
        UpdateStarTypeLabel();
    }

    private void SaveSelectionState()
    {
        s_savedStarTypeIndex = _starTypeIndex;
        s_savedSelectedIndex = Menu.SelectedIndex;
    }

    private void UpdateStarTypeLabel()
    {
        string confirm = CurrentInput?.GetActionHelpText(InputAction.MenuConfirm).ToUpper() ?? "CONFIRM";
        string left = CurrentInput?.GetActionHelpText(InputAction.MenuLeft).ToUpper() ?? "LEFT";
        string right = CurrentInput?.GetActionHelpText(InputAction.MenuRight).ToUpper() ?? "RIGHT";

        Menu.SetOption(StarTypeIdx, new MenuOption<DebugMenuAction>(DebugMenuAction.StarType,
            $"STAR TYPE: < {SelectedStarType} >",
            $"Press {confirm} or {left}/{right} to change star type"));
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
