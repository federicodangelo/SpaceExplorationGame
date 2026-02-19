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
    protected override string? ControlsHint => "ENTER: CONFIRM   ESC: CANCEL   BACKSPACE: DELETE";

    public void Open(string prompt, string? defaultValue = null, bool numericOnly = false, int maxLength = 32)
    {
        _prompt = prompt;
        _defaultValue = defaultValue;
        _currentInput = defaultValue ?? "";
        _numericOnly = numericOnly;
        _maxLength = maxLength;
        ConfirmedValue = null;
        base.Open();

        // Start SDL text input
        SDL.StartTextInput(IntPtr.Zero);
    }

    public override void Close()
    {
        base.Close();
        SDL.StopTextInput(IntPtr.Zero);
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
            if (_currentInput.Length > 0)
            {
                ConfirmedValue = _currentInput;
                Close();
            }
        }

        // Handle character input
        HandleCharacterInput(input);
    }

    private void HandleCharacterInput(InputManager input)
    {
        if (_currentInput.Length >= _maxLength) return;

        // Number keys (checking by scancode value - SDL uses values 30-39 for 1-0)
        for (int i = 0; i <= 9; i++)
        {
            SDL.Scancode scancode = (SDL.Scancode)(30 + (i == 0 ? 9 : i - 1)); // 1-9 are 30-38, 0 is 39
            if (input.IsKeyPressed(scancode))
            {
                _currentInput += i.ToString();
                return;
            }
        }

        // Keypad numbers (scancodes 89-98)
        for (int i = 0; i <= 9; i++)
        {
            SDL.Scancode scancode = (SDL.Scancode)(89 + i);
            if (input.IsKeyPressed(scancode))
            {
                _currentInput += i.ToString();
                return;
            }
        }

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

        // Input text or default value hint
        string displayText = _currentInput;
        Color3 textColor = new(220, 240, 255);

        if (string.IsNullOrEmpty(displayText) && !string.IsNullOrEmpty(_defaultValue))
        {
            displayText = _defaultValue;
            textColor = new Color3(120, 120, 140);
        }

        if (!string.IsNullOrEmpty(displayText))
        {
            // Cursor blink
            float cursorBlink = MathF.Sin((float)game.GlobalTime * 3f);
            if (cursorBlink > 0 && _currentInput.Length > 0)
            {
                displayText += "|";
            }

            renderer.DrawTextScreen(boxX + 10, boxY + 10, displayText, textColor, 2f);
        }
        else
        {
            // Show blinking cursor when empty
            float cursorBlink = MathF.Sin((float)game.GlobalTime * 3f);
            if (cursorBlink > 0)
            {
                renderer.DrawTextScreen(boxX + 10, boxY + 10, "|", textColor, 2f);
            }
        }

        // Hint text
        if (_defaultValue != null && _currentInput.Length == 0)
        {
            string hint = "(leave empty for default)";
            float hintW = renderer.MeasureText(hint, 1.5f);
            renderer.DrawTextScreen(centerX - hintW / 2f, centerY + 50, hint, new Color3(120, 140, 160), 1.5f);
        }
    }
}
