using SpaceExplorationGame.Core;
using SpaceExplorationGame.UI.Overlays.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Non-interactive panel rendered to the right of the main menu that lists
/// basic game controls, adapting to the active input method (keyboard/gamepad).
/// </summary>
public class MainMenuControlsPanel
{
    private const float PanelWidth = 360f;
    private const float Gap = 20f;
    private const float LineH = 22f;

    /// <summary>
    /// Render the controls panel.
    /// </summary>
    /// <param name="game">Current game instance.</param>
    /// <param name="mainMenuRightEdge">Right edge X of the main menu panel.</param>
    /// <param name="mainMenuCenterY">Vertical center Y of the main menu panel.</param>
    public void Render(Game game, float mainMenuRightEdge, float mainMenuCenterY)
    {
        var renderer = game.SpriteRenderer;
        var input = game.Input;

        bool usingGamepad = input.ActiveInputMethod == InputMethod.Gamepad;

        string[] spaceControls = BuildSpaceControls(input, usingGamepad);
        string[] surfaceControls = BuildSurfaceControls(input, usingGamepad);

        // Layout: title + separator (55px), two sections with headers + lines + gamepad badge
        int totalLines = 1 + spaceControls.Length + 1 + surfaceControls.Length;
        float panelH = 55f + totalLines * LineH + 20f + LineH + 10f;

        float px = mainMenuRightEdge + Gap;
        float py = mainMenuCenterY - panelH / 2f;

        // Bail out if panel doesn't fit horizontally
        if (px + PanelWidth > renderer.WindowWidth - 10f)
            return;

        // Clamp vertically
        py = Math.Clamp(py, 10f, renderer.WindowHeight - panelH - 10f);

        // Background & border
        OverlayBase.DrawFrame(renderer, px, py, PanelWidth, panelH, 200);

        // Title
        renderer.DrawTextScreen(px + 15, py + 10, "CONTROLS",
            new Color3(200, 210, 255), 2.5f, PanelWidth - 30f);
        renderer.DrawLineScreen(px + 15, py + 45, px + PanelWidth - 15, py + 45,
            new Color3(60, 80, 140));

        float cy = py + 55f;

        // ── In Space section ──
        renderer.DrawTextScreen(px + 15, cy, "IN SPACE",
            new Color3(180, 210, 255), 1.8f);
        cy += LineH;
        renderer.DrawLineScreen(px + 15, cy - 3, px + PanelWidth - 15, cy - 3,
            new Color3(40, 60, 100));

        foreach (var line in spaceControls)
        {
            renderer.DrawTextScreen(px + 20, cy, line,
                new Color3(155, 165, 195), 1.5f, PanelWidth - 35f);
            cy += LineH;
        }

        cy += 8f;

        // ── On Foot / Surface section ──
        renderer.DrawTextScreen(px + 15, cy, "ON FOOT / SURFACE",
            new Color3(180, 210, 255), 1.8f);
        cy += LineH;
        renderer.DrawLineScreen(px + 15, cy - 3, px + PanelWidth - 15, cy - 3,
            new Color3(40, 60, 100));

        foreach (var line in surfaceControls)
        {
            renderer.DrawTextScreen(px + 20, cy, line,
                new Color3(155, 165, 195), 1.5f, PanelWidth - 35f);
            cy += LineH;
        }

        // ── Gamepad badge ──
        cy += 8f;
        renderer.DrawLineScreen(px + 15, cy - 3, px + PanelWidth - 15, cy - 3,
            new Color3(40, 60, 100));
        string badge = usingGamepad ? "\u25cf GAMEPAD ACTIVE" : "\u25cf GAMEPAD SUPPORTED AND RECOMMENDED";
        var badgeColor = usingGamepad ? new Color3(100, 220, 130) : new Color3(100, 160, 100);
        float badgeW = renderer.MeasureText(badge, 1.5f);
        renderer.DrawTextScreen(px + PanelWidth / 2f - badgeW / 2f, cy, badge, badgeColor, 1.5f);
    }

    private static string[] BuildSpaceControls(IInputManager input, bool usingGamepad)
    {
        string interactText = input.GetActionHelpTextFull(InputAction.Interact);
        string fireText = input.GetActionHelpTextFull(InputAction.FireWeapon);
        string backText = input.GetActionHelpTextFull(InputAction.MenuBack);
        string mapText = input.GetActionHelpTextFull(InputAction.ToggleMap);

        if (usingGamepad)
            return
            [
                "RIGHT STICK ...... HEADING",
                "LEFT STICK ....... MOVE",
                $"{interactText} ................ INTERACT",
                $"{mapText} ................ GALAXY MAP",
                $"{fireText} ........... SHOOT",
                $"{backText} ............. MENU",
            ];

        return
        [
            "MOUSE ............ HEADING",
            $"{input.GetActionHelpTextFull(InputAction.MoveUp)} ........... THRUST",
            $"{input.GetActionHelpTextFull(InputAction.MoveDown)} ......... BRAKE",
            $"{input.GetActionHelpTextFull(InputAction.MoveLeft)}/{input.GetActionHelpTextFull(InputAction.MoveRight)} .......... STRAFE",
            "SCROLL ........... ZOOM",
            $"{interactText} ................ INTERACT",
            $"{mapText} ................ GALAXY MAP",
            $"{fireText} ........... SHOOT",
            $"{backText} ............. MENU",
        ];
    }

    private static string[] BuildSurfaceControls(IInputManager input, bool usingGamepad)
    {
        string moveText = string.Join('/',
            input.GetActionHelpTextFull(InputAction.MoveUp),
            input.GetActionHelpTextFull(InputAction.MoveDown),
            input.GetActionHelpTextFull(InputAction.MoveLeft),
            input.GetActionHelpTextFull(InputAction.MoveRight));
        string interactText = input.GetActionHelpTextFull(InputAction.Interact);
        string fireText = input.GetActionHelpTextFull(InputAction.FireWeapon);
        string backText = input.GetActionHelpTextFull(InputAction.MenuBack);

        if (usingGamepad)
            return
            [
                "LEFT STICK ....... MOVE",
                $"{interactText} ................ INTERACT",
                $"{fireText} ...... SHOOT",
                $"{backText} ............. MENU",
            ];

        return
        [
            $"{moveText} .... MOVE",
            "SCROLL ........... ZOOM",
            $"{interactText} ................ INTERACT",
            $"{fireText} ...... SHOOT",
            $"{backText} ............. MENU",
        ];
    }
}
