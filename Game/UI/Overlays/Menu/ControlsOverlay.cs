using SpaceExplorationGame.Core;
using Engine.Platform;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay that displays context-appropriate controls for the active input method.
/// Any key press or click outside dismisses it.
/// </summary>
public class ControlsOverlay : PanelOverlayBase
{
    private string[] _controls = [];

    /// <summary>The current game state type, determines which controls are shown.</summary>
    public GameStateType StateType { get; set; }

    protected override string Title => "CONTROLS";
    protected override Color3 TitleColor => new(200, 210, 255);
    protected override float PanelWidth => 380;
    protected override float PanelHeight => 55 + _controls.Length * 22f + 35;
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            return $"{input.GetActionHelpText(InputAction.MenuConfirm)}/{input.GetActionHelpText(InputAction.Interact)}/{input.GetActionHelpText(InputAction.MenuBack)}: CLOSE";
        }
    }

    public override void Open()
    {
        _controls = [];
        base.Open();
    }

    protected override void ProcessInput(Game game, IInputManager input)
    {
        // Any key dismisses
        if (input.IsActionPressed(InputAction.MenuConfirm) || input.IsActionPressed(InputAction.Interact))
            Close();
    }

    protected override void RenderPanelContent(Game game, ISpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        _controls = GetControlsForState(StateType, game.Input);

        float cy = contentY;
        float centerX = panelX + panelW / 2f;
        foreach (var line in _controls)
        {
            float lw = renderer.MeasureText(line, 1.5f);
            renderer.DrawTextScreen(centerX - lw / 2f, cy, line,
                new Color3(180, 180, 200), 1.5f);
            cy += 22f;
        }
    }

    private static string[] GetControlsForState(GameStateType state, IInputManager input)
    {
        bool usingGamepad = input.ActiveInputMethod == InputMethod.Gamepad;

        string moveText = string.Join('/',
            input.GetActionHelpTextFull(InputAction.MoveUp),
            input.GetActionHelpTextFull(InputAction.MoveDown),
            input.GetActionHelpTextFull(InputAction.MoveLeft),
            input.GetActionHelpTextFull(InputAction.MoveRight));
        string interactText = input.GetActionHelpTextFull(InputAction.Interact);
        string fireText = input.GetActionHelpTextFull(InputAction.FireWeapon);
        string backText = input.GetActionHelpTextFull(InputAction.MenuBack);
        string mapText = input.GetActionHelpTextFull(InputAction.ToggleMap);

        return state switch
        {
            GameStateType.SolarSystem =>
                usingGamepad
                    ?
                    [
                        "RIGHT STICK ......... HEADING",
                        "LEFT STICK .......... MOVE",
                        $"{interactText} .................. INTERACT",
                        $"{mapText} .................. GALAXY MAP",
                        $"{fireText} .............. SHOOT",
                        $"{backText} ................ MENU"
                    ]
                    :
                    [
                        "MOUSE .............. HEADING",
                        $"{input.GetActionHelpTextFull(InputAction.MoveUp)} ............. THRUST",
                        $"{input.GetActionHelpTextFull(InputAction.MoveDown)} ........... BRAKE",
                        $"{input.GetActionHelpTextFull(InputAction.MoveLeft)}/{input.GetActionHelpTextFull(InputAction.MoveRight)} .............. STRAFE",
                        "SCROLL ............. ZOOM",
                        $"{interactText} .................. INTERACT",
                        $"{mapText} .................. GALAXY MAP",
                        $"{fireText} .............. SHOOT",
                        $"{backText} ................ MENU"
                    ],
            GameStateType.PlanetSurface =>
                usingGamepad
                    ?
                    [
                        "LEFT STICK .......... MOVE",
                        $"{interactText} .................. INTERACT",
                        $"{fireText} ........ SHOOT",
                        $"{backText} ................ MENU"
                    ]
                    :
                    [
                        $"{moveText} ...... MOVE",
                        "SCROLL ............. ZOOM",
                        $"{interactText} .................. INTERACT",
                        $"{fireText} ........ SHOOT",
                        $"{backText} ................ MENU"
                    ],
            GameStateType.Interior =>
                usingGamepad
                    ?
                    [
                        "LEFT STICK .......... MOVE",
                        $"{interactText} .................. INTERACT",
                        $"{backText} ................ MENU"
                    ]
                    :
                    [
                        $"{moveText} ...... MOVE",
                        "SCROLL ............. ZOOM",
                        $"{interactText} .................. INTERACT",
                        $"{backText} ................ MENU"
                    ],
            _ =>
                [$"{backText} ................ MENU"]
        };
    }
}
