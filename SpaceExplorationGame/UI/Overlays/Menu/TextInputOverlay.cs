using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Simple text input overlay for entering numeric or text values.
/// Used for seed input, name entry, etc.
/// </summary>
public class TextInputOverlay : PanelOverlayBase
{
    private string _currentInput = "";
    private string _prompt = "";
    private string? _defaultValue;
    private bool _numericOnly;
    private int _maxLength;

    public string? ConfirmedValue { get; private set; }

    /// <summary>
    /// Gets and clears the confirmed value (consume once).
    /// </summary>
    public string? TakeConfirmedValue()
    {
        var value = ConfirmedValue;
        ConfirmedValue = null;
        return value;
    }

    protected override string Title => _prompt;
    protected override float PanelWidth => 600;
    protected override float PanelHeight => 200;
    protected override bool CloseOnClickOutside => false;
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            return $"{input.GetKeyHelpText(SDL.Scancode.Return)}: CONFIRM   " +
                   $"{input.GetKeyHelpText(SDL.Scancode.Escape)}: CANCEL   " +
                   $"{input.GetKeyHelpText(SDL.Scancode.Backspace)}: DELETE";
        }
    }

    public void Open(string prompt, string? defaultValue = null, bool numericOnly = false, int maxLength = 32)
    {
        _prompt = prompt;
        _defaultValue = defaultValue;
        _currentInput = ""; // Start empty; default shown as placeholder
        _numericOnly = numericOnly;
        _maxLength = maxLength;
        ConfirmedValue = null;
        base.Open();
    }

    protected override void OnEscapePressed()
    {
        ConfirmedValue = null;
        Close();
    }

    protected override void ProcessInput(Game game, InputManager input)
    {
        // Backspace
        if (input.IsKeyPressed(SDL.Scancode.Backspace) && _currentInput.Length > 0)
        {
            _currentInput = _currentInput[..^1];
        }

        // Enter - confirm
        if (input.IsKeyPressed(SDL.Scancode.Return))
        {
            // If user typed something, use that; otherwise fall back to default
            if (_currentInput.Length > 0)
            {
                ConfirmedValue = _currentInput;
                Close();
            }
            else if (_defaultValue != null)
            {
                ConfirmedValue = _defaultValue;
                Close();
            }
        }

        // Handle character input
        HandleCharacterInput(input);
    }

    private void HandleCharacterInput(InputManager input)
    {
        if (_currentInput.Length >= _maxLength) return;

        // Number row keys
        if (input.IsKeyPressed(SDL.Scancode.Alpha0)) { _currentInput += "0"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Alpha1)) { _currentInput += "1"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Alpha2)) { _currentInput += "2"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Alpha3)) { _currentInput += "3"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Alpha4)) { _currentInput += "4"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Alpha5)) { _currentInput += "5"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Alpha6)) { _currentInput += "6"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Alpha7)) { _currentInput += "7"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Alpha8)) { _currentInput += "8"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Alpha9)) { _currentInput += "9"; return; }

        // Keypad number keys
        if (input.IsKeyPressed(SDL.Scancode.Kp0)) { _currentInput += "0"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Kp1)) { _currentInput += "1"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Kp2)) { _currentInput += "2"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Kp3)) { _currentInput += "3"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Kp4)) { _currentInput += "4"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Kp5)) { _currentInput += "5"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Kp6)) { _currentInput += "6"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Kp7)) { _currentInput += "7"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Kp8)) { _currentInput += "8"; return; }
        if (input.IsKeyPressed(SDL.Scancode.Kp9)) { _currentInput += "9"; return; }

        if (!_numericOnly)
        {
            // Letters A-Z
            for (SDL.Scancode key = SDL.Scancode.A; key <= SDL.Scancode.Z; key++)
            {
                if (input.IsKeyPressed(key))
                {
                    char c = (char)('A' + ((int)key - (int)SDL.Scancode.A));
                    bool shift = input.IsKeyDown(SDL.Scancode.LShift) || input.IsKeyDown(SDL.Scancode.RShift);
                    _currentInput += shift ? c : char.ToLower(c);
                    return;
                }
            }

            // Space
            if (input.IsKeyPressed(SDL.Scancode.Space))
            {
                _currentInput += " ";
                return;
            }

            // Hyphen
            if (input.IsKeyPressed(SDL.Scancode.Minus))
            {
                _currentInput += "-";
                return;
            }
        }
    }

    protected override void RenderPanelContent(Game game, SpriteRenderer renderer, float panelX, float contentY, float panelW, float contentH)
    {
        float centerX = panelX + panelW / 2f;
        float centerY = contentY + contentH / 2f - 20;

        // Instruction
        string instruction = _numericOnly ? "Enter a number:" : "Enter text:";
        float instrW = renderer.MeasureText(instruction, 2f);
        renderer.DrawTextScreen(centerX - instrW / 2f, centerY - 40, instruction, new Color3(180, 180, 200), 2f);

        // Input box background
        float boxW = 500;
        float boxH = 40;
        float boxX = centerX - boxW / 2f;
        float boxY = centerY;

        renderer.DrawRectScreen(boxX, boxY, boxW, boxH, new Color4(20, 25, 40, 255));
        renderer.DrawRectScreen(boxX - 2, boxY - 2, boxW + 4, boxH + 4, new Color4(80, 120, 200, 150));

        // Input text or placeholder
        if (_currentInput.Length > 0)
        {
            // Show typed text with blinking cursor
            string displayText = _currentInput;
            float cursorBlink = MathF.Sin((float)game.GlobalTime * 3f);
            if (cursorBlink > 0)
                displayText += "|";
            renderer.DrawTextScreen(boxX + 10, boxY + 10, displayText, new Color3(220, 240, 255), 2f);
        }
        else if (!string.IsNullOrEmpty(_defaultValue))
        {
            // Show default value as placeholder
            renderer.DrawTextScreen(boxX + 10, boxY + 10, _defaultValue, new Color3(0, 0, 100), 2f);
            // Blinking cursor at start
            float cursorBlink = MathF.Sin((float)game.GlobalTime * 3f);
            if (cursorBlink > 0)
                renderer.DrawTextScreen(boxX + 6, boxY + 10, "|", new Color3(220, 240, 255), 2f);
        }
        else
        {
            // Show blinking cursor when empty
            float cursorBlink = MathF.Sin((float)game.GlobalTime * 3f);
            if (cursorBlink > 0)
                renderer.DrawTextScreen(boxX + 10, boxY + 10, "|", new Color3(220, 240, 255), 2f);
        }

        // Hint text below the input box
        if (_defaultValue != null && _currentInput.Length == 0)
        {
            string hint = "Type to replace, or ENTER to keep current";
            float hintW = renderer.MeasureText(hint, 1.5f);
            renderer.DrawTextScreen(centerX - hintW / 2f, centerY + 50, hint, new Color3(120, 140, 160), 1.5f);
        }
    }
}
