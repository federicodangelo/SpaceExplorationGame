using System.Numerics;
using Engine.Platform.Base;

namespace Engine.Platform.Web;

/// <summary>
/// Input manager that polls browser keyboard, mouse, and gamepad state via JavaScript interop.
/// Events are accumulated in JS between frames and flushed per-frame as a packed string.
/// Gamepad state is polled each frame via the browser Gamepad API.
/// </summary>
public class WebInputManager : BaseInputManager
{
    private const float GamepadTriggerThreshold = 0.35f;

    // ── Key state (browser key codes: "KeyW", "ArrowUp", etc.) ───
    private readonly HashSet<string> _keysDown = [];
    private readonly HashSet<string> _keysPressed = [];
    private readonly HashSet<string> _keysReleased = [];

    // ── Gamepad state ────────────────────────────────────────────
    // Standard Gamepad button indices (W3C standard mapping)
    private const int GpBtnSouth = 0;       // A
    private const int GpBtnEast = 1;        // B
    private const int GpBtnWest = 2;        // X
    private const int GpBtnNorth = 3;       // Y
    private const int GpBtnLShoulder = 4;   // LB
    private const int GpBtnRShoulder = 5;   // RB
    private const int GpBtnLTrigger = 6;    // LT (analog as button)
    private const int GpBtnRTrigger = 7;    // RT (analog as button)
    private const int GpBtnBack = 8;        // Back / Select
    private const int GpBtnStart = 9;       // Start
    private const int GpBtnLStick = 10;     // Left stick click
    private const int GpBtnRStick = 11;     // Right stick click
    private const int GpBtnDPadUp = 12;
    private const int GpBtnDPadDown = 13;
    private const int GpBtnDPadLeft = 14;
    private const int GpBtnDPadRight = 15;
    private const int GpBtnHome = 16;
    private const int GpMaxButtons = 17;

    // Standard Gamepad axis indices
    private const int GpAxisLeftX = 0;
    private const int GpAxisLeftY = 1;
    private const int GpAxisRightX = 2;
    private const int GpAxisRightY = 3;

    private readonly HashSet<int> _gpDown = [];
    private readonly HashSet<int> _gpPressed = [];
    private readonly HashSet<int> _gpReleased = [];
    private bool _gamepadConnected;
    // After Reset(), the first PollGamepad() must not generate "pressed" events for
    // still-held buttons — the physical state hasn't changed, only our tracking was wiped.
    private bool _suppressGpPressedOnce;

    // ── Text input ───────────────────────────────────────────────
    private string _textInputBuffer = "";

    // ── Action bindings ──────────────────────────────────────────
    private readonly Dictionary<InputAction, List<InputBinding>> _bindings = new()
    {
        [InputAction.DebugToggle] = [InputBinding.Key("Digit1")],
        [InputAction.MenuConfirm] = [InputBinding.Key("Enter"), InputBinding.Key("Space"), InputBinding.Key("NumpadEnter"), InputBinding.Btn(GpBtnSouth)],
        [InputAction.MenuUp] = [InputBinding.Key("ArrowUp"), InputBinding.Key("KeyW"), InputBinding.Btn(GpBtnDPadUp)],
        [InputAction.MenuDown] = [InputBinding.Key("ArrowDown"), InputBinding.Key("KeyS"), InputBinding.Btn(GpBtnDPadDown)],
        [InputAction.MenuLeft] = [InputBinding.Key("ArrowLeft"), InputBinding.Key("KeyA"), InputBinding.Btn(GpBtnDPadLeft)],
        [InputAction.MenuRight] = [InputBinding.Key("ArrowRight"), InputBinding.Key("KeyD"), InputBinding.Btn(GpBtnDPadRight)],
        [InputAction.MenuBack] = [InputBinding.Key("Escape"), InputBinding.Btn(GpBtnEast), InputBinding.Btn(GpBtnStart)],
        [InputAction.MenuSecondaryAction] = [InputBinding.Key("KeyX"), InputBinding.Key("Delete")],

        [InputAction.MoveUp] = [InputBinding.Key("KeyW"), InputBinding.Key("ArrowUp")],
        [InputAction.MoveDown] = [InputBinding.Key("KeyS"), InputBinding.Key("ArrowDown")],
        [InputAction.MoveLeft] = [InputBinding.Key("KeyA"), InputBinding.Key("ArrowLeft")],
        [InputAction.MoveRight] = [InputBinding.Key("KeyD"), InputBinding.Key("ArrowRight")],
        [InputAction.FireWeapon] = [InputBinding.Key("Space"), InputBinding.Mouse(0), InputBinding.Btn(GpBtnWest), InputBinding.Btn(GpBtnLTrigger), InputBinding.Btn(GpBtnRTrigger)],
        [InputAction.MapZoomOut] = [InputBinding.Btn(GpBtnLTrigger)],
        [InputAction.MapZoomIn] = [InputBinding.Btn(GpBtnRTrigger)],
        [InputAction.PreviousPanelView] = [InputBinding.Btn(GpBtnLShoulder)],
        [InputAction.NextPanelView] = [InputBinding.Btn(GpBtnRShoulder)],
        [InputAction.Interact] = [InputBinding.Key("KeyE"), InputBinding.Btn(GpBtnSouth)],
        [InputAction.ToggleMap] = [InputBinding.Key("KeyM"), InputBinding.Btn(GpBtnBack)],
        [InputAction.Screenshot] = [InputBinding.Key("F12")],
    };

    private readonly record struct InputBinding(string? KeyCode, int? MouseBtn, int? GamepadBtn)
    {
        public static InputBinding Key(string code) => new(code, null, null);
        public static InputBinding Mouse(int button) => new(null, button, null);
        public static InputBinding Btn(int gpButton) => new(null, null, gpButton);
    }

    public override bool QuitRequested => false; // Browser never quits
    public override string TextInput => _textInputBuffer;

    public WebInputManager(Func<(int Width, int Height)> getCanvasSize) : base(getCanvasSize)
    {
    }

    public override void BeginFrame()
    {
        MouseX = JsInput.GetMouseX();
        MouseY = JsInput.GetMouseY();

        _textInputBuffer = "";
        _textInputBackspaceCount = 0;
        _textInputReturnCount = 0;
    }

    public override void EndFrame()
    {
        base.EndFrame(); // clears _mousePressed, _mouseReleased, MouseWheelY
        _keysPressed.Clear();
        _keysReleased.Clear();
        _gpPressed.Clear();
        _gpReleased.Clear();
    }

    public override void Reset()
    {
        base.Reset(); // clears mouse, sticks, MouseWheelY, ActiveInputMethod
        _keysDown.Clear();
        _keysPressed.Clear();
        _keysReleased.Clear();
        _gpDown.Clear();
        _gpPressed.Clear();
        _gpReleased.Clear();
        _textInputBuffer = "";
        // Suppress the spurious "pressed" events that would fire on the next PollGamepad()
        // call for any buttons that are still physically held after the state transition.
        // (The browser Gamepad API is polled, not event-driven, so clearing _gpDown while a
        // button is held makes the next poll see pressed=true / wasDown=false — a false edge.)
        _suppressGpPressedOnce = true;
    }

    public override void ProcessEvents()
    {
        MouseWheelY = JsInput.GetMouseWheel();

        // Get text input from JS
        string textInput = JsInput.GetTextInput();
        if (textInput.Length > 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in textInput)
            {
                if (c == '\b')
                    _textInputBackspaceCount++;
                else if (c == '\n')
                    _textInputReturnCount++;
                else
                    sb.Append(c);
            }
            _textInputBuffer = sb.ToString();
        }

        // Parse packed keyboard/mouse events: "KD:KeyW|KU:KeyA|MD:0|MU:2|..."
        string events = JsInput.FlushEvents();
        if (!string.IsNullOrEmpty(events))
        {
            foreach (var part in events.Split('|'))
            {
                if (part.Length < 3) continue;

                var type = part[..2];
                var value = part[3..];

                switch (type)
                {
                    case "KD": // Key down
                        ActiveInputMethod = InputMethod.MouseKeyboard;
                        if (_keysDown.Add(value))
                            _keysPressed.Add(value);
                        break;
                    case "KU": // Key up
                        ActiveInputMethod = InputMethod.MouseKeyboard;
                        _keysDown.Remove(value);
                        _keysReleased.Add(value);
                        break;
                    case "MD": // Mouse down
                        if (int.TryParse(value, out int mb))
                        {
                            if (_mouseDown.Add(mb))
                                _mousePressed.Add(mb);
                        }
                        break;
                    case "MU": // Mouse up
                        if (int.TryParse(value, out int mu))
                        {
                            _mouseDown.Remove(mu);
                            _mouseReleased.Add(mu);
                        }
                        break;
                }
            }
        }

        // Poll gamepad
        PollGamepad();
    }

    // ── Gamepad polling ──────────────────────────────────────────

    private void PollGamepad()
    {
        string gpData = JsInput.PollGamepad();
        if (string.IsNullOrEmpty(gpData))
        {
            // No gamepad connected — clear all state if we had one
            if (_gamepadConnected)
            {
                _gamepadConnected = false;
                _gpDown.Clear();
                _leftStickX = _leftStickY = 0;
                _rightStickX = _rightStickY = 0;
            }
            // Still consume the suppress flag so it doesn't bleed into a future reconnect.
            _suppressGpPressedOnce = false;
            return;
        }

        _gamepadConnected = true;

        // Format: "1|b0,b1,...|a0,a1,..."
        var sections = gpData.Split('|');
        if (sections.Length < 3) return;

        // Parse buttons
        var buttonParts = sections[1].Split(',');
        for (int i = 0; i < buttonParts.Length && i < GpMaxButtons; i++)
        {
            bool pressed = buttonParts[i] == "1";
            bool wasDown = _gpDown.Contains(i);

            if (pressed && !wasDown)
            {
                _gpDown.Add(i);
                // Skip generating a "pressed" event when suppressed after a Reset() —
                // the button was already held before the state transition.
                if (!_suppressGpPressedOnce)
                {
                    _gpPressed.Add(i);
                    ActiveInputMethod = InputMethod.Gamepad;
                }
            }
            else if (!pressed && wasDown)
            {
                _gpDown.Remove(i);
                _gpReleased.Add(i);
                ActiveInputMethod = InputMethod.Gamepad;
            }
        }

        _suppressGpPressedOnce = false;

        // Parse axes
        var axisParts = sections[2].Split(',');
        float lx = axisParts.Length > GpAxisLeftX ? ParseAxis(axisParts[GpAxisLeftX]) : 0;
        float ly = axisParts.Length > GpAxisLeftY ? ParseAxis(axisParts[GpAxisLeftY]) : 0;
        float rx = axisParts.Length > GpAxisRightX ? ParseAxis(axisParts[GpAxisRightX]) : 0;
        float ry = axisParts.Length > GpAxisRightY ? ParseAxis(axisParts[GpAxisRightY]) : 0;

        // Apply dead zone
        _leftStickX = ApplyDeadZone(lx);
        _leftStickY = ApplyDeadZone(ly);
        _rightStickX = ApplyDeadZone(rx);
        _rightStickY = ApplyDeadZone(ry);

        // Switch to gamepad input if any stick is active
        if (MathF.Abs(_leftStickX) > 0 || MathF.Abs(_leftStickY) > 0 ||
            MathF.Abs(_rightStickX) > 0 || MathF.Abs(_rightStickY) > 0)
        {
            ActiveInputMethod = InputMethod.Gamepad;
        }
    }

    private static float ParseAxis(string s) =>
        float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0;

    // ── Action queries ───────────────────────────────────────────

    public override bool IsActionDown(InputAction action) => IsAnyBindingDown(action, _keysDown, _mouseDown, _gpDown);
    public override bool IsActionPressed(InputAction action) => IsAnyBindingDown(action, _keysPressed, _mousePressed, _gpPressed);
    public override bool IsActionReleased(InputAction action) => IsAnyBindingDown(action, _keysReleased, _mouseReleased, _gpReleased);

    public override string GetActionHelpText(InputAction action, bool includeSecondary = false)
    {
        if (!_bindings.TryGetValue(action, out var bindingList) || bindingList.Count == 0)
            return string.Empty;

        List<string> labels = [];
        foreach (var binding in bindingList)
        {
            if (!ShouldIncludeBindingForActiveInput(binding)) continue;
            string label = GetBindingLabel(binding);
            if (!string.IsNullOrWhiteSpace(label) && !labels.Contains(label))
                labels.Add(label);
        }

        if (labels.Count == 0) return string.Empty;
        if (!includeSecondary && labels.Count > 1) return labels[0];
        return string.Join("/", labels);
    }

    public override string GetMouseButtonHelpText(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => "LMB",
            MouseButton.Right => "RMB",
            MouseButton.Middle => "MMB",
            _ => $"Mouse{(int)button}",
        };
    }

    public override bool IsMouseDown(MouseButton button) => _mouseDown.Contains((int)button - 1);
    public override bool IsMousePressed(MouseButton button) => _mousePressed.Contains((int)button - 1);
    public override bool IsMouseReleased(MouseButton button) => _mouseReleased.Contains((int)button - 1);

    // ── Binding checks ───────────────────────────────────────────

    private bool IsAnyBindingDown(InputAction action, HashSet<string> keySet, HashSet<int> mouseSet, HashSet<int> gpSet)
    {
        if (!_bindings.TryGetValue(action, out var bindingList)) return false;

        foreach (var binding in bindingList)
        {
            if (binding.KeyCode != null && keySet.Contains(binding.KeyCode))
                return true;
            if (binding.MouseBtn.HasValue && mouseSet.Contains(binding.MouseBtn.Value))
                return true;
            if (binding.GamepadBtn.HasValue && gpSet.Contains(binding.GamepadBtn.Value))
                return true;
        }
        return false;
    }

    private bool ShouldIncludeBindingForActiveInput(InputBinding binding)
    {
        return ActiveInputMethod switch
        {
            InputMethod.Gamepad => binding.GamepadBtn.HasValue,
            _ => binding.KeyCode != null || binding.MouseBtn.HasValue,
        };
    }

    // ── Help text labels ─────────────────────────────────────────

    private static string GetBindingLabel(InputBinding binding)
    {
        if (binding.KeyCode != null)
        {
            return binding.KeyCode switch
            {
                "Enter" or "NumpadEnter" => "Enter",
                "Space" => "Space",
                "Escape" => "Esc",
                "Backspace" => "Backspace",
                "ArrowUp" => "Up",
                "ArrowDown" => "Down",
                "ArrowLeft" => "Left",
                "ArrowRight" => "Right",
                "Delete" => "Delete",
                _ when binding.KeyCode.StartsWith("Key") => binding.KeyCode[3..],
                _ when binding.KeyCode.StartsWith("Digit") => binding.KeyCode[5..],
                _ => binding.KeyCode,
            };
        }

        if (binding.MouseBtn.HasValue)
        {
            return binding.MouseBtn.Value switch
            {
                0 => "LMB",
                1 => "MMB",
                2 => "RMB",
                _ => $"Mouse{binding.MouseBtn.Value}",
            };
        }

        if (binding.GamepadBtn.HasValue)
        {
            return binding.GamepadBtn.Value switch
            {
                GpBtnSouth => "A",
                GpBtnEast => "B",
                GpBtnWest => "X",
                GpBtnNorth => "Y",
                GpBtnLShoulder => "LB",
                GpBtnRShoulder => "RB",
                GpBtnLTrigger => "LT",
                GpBtnRTrigger => "RT",
                GpBtnStart => "Start",
                GpBtnBack => "Back",
                GpBtnDPadUp => "D-Up",
                GpBtnDPadDown => "D-Down",
                GpBtnDPadLeft => "D-Left",
                GpBtnDPadRight => "D-Right",
                _ => $"Btn{binding.GamepadBtn.Value}",
            };
        }

        return string.Empty;
    }
}
