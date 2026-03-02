using System.Numerics;

namespace Engine.Platform.Web;

/// <summary>
/// Input manager that polls browser keyboard, mouse, and gamepad state via JavaScript interop.
/// Events are accumulated in JS between frames and flushed per-frame as a packed string.
/// </summary>
public class WebInputManager : IInputManager
{
    private readonly Func<(int Width, int Height)> _getCanvasSize;

    // Key state (using browser key codes like "KeyW", "ArrowUp", etc.)
    private readonly HashSet<string> _keysDown = [];
    private readonly HashSet<string> _keysPressed = [];
    private readonly HashSet<string> _keysReleased = [];

    // Mouse state
    private readonly HashSet<int> _mouseDown = [];
    private readonly HashSet<int> _mousePressed = [];
    private readonly HashSet<int> _mouseReleased = [];

    // Text input
    private string _textInputBuffer = "";
    private int _textInputBackspaceCount;
    private int _textInputReturnCount;

    // Action bindings: map InputAction → browser key codes / mouse buttons
    private readonly Dictionary<InputAction, List<InputBinding>> _bindings = new()
    {
        [InputAction.DebugToggle] = [InputBinding.Key("Digit1")],
        [InputAction.MenuConfirm] = [InputBinding.Key("Enter"), InputBinding.Key("Space"), InputBinding.Key("NumpadEnter")],
        [InputAction.MenuUp] = [InputBinding.Key("ArrowUp"), InputBinding.Key("KeyW")],
        [InputAction.MenuDown] = [InputBinding.Key("ArrowDown"), InputBinding.Key("KeyS")],
        [InputAction.MenuLeft] = [InputBinding.Key("ArrowLeft"), InputBinding.Key("KeyA")],
        [InputAction.MenuRight] = [InputBinding.Key("ArrowRight"), InputBinding.Key("KeyD")],
        [InputAction.MenuBack] = [InputBinding.Key("Escape")],
        [InputAction.MenuSecondaryAction] = [InputBinding.Key("KeyX"), InputBinding.Key("Delete")],

        [InputAction.MoveUp] = [InputBinding.Key("KeyW"), InputBinding.Key("ArrowUp")],
        [InputAction.MoveDown] = [InputBinding.Key("KeyS"), InputBinding.Key("ArrowDown")],
        [InputAction.MoveLeft] = [InputBinding.Key("KeyA"), InputBinding.Key("ArrowLeft")],
        [InputAction.MoveRight] = [InputBinding.Key("KeyD"), InputBinding.Key("ArrowRight")],
        [InputAction.FireWeapon] = [InputBinding.Key("Space"), InputBinding.Mouse(0)],
        [InputAction.MapZoomOut] = [],
        [InputAction.MapZoomIn] = [],
        [InputAction.MapPreviousView] = [],
        [InputAction.MapNextView] = [],
        [InputAction.Interact] = [InputBinding.Key("KeyE")],
        [InputAction.ToggleMap] = [InputBinding.Key("KeyM")],
        [InputAction.Screenshot] = [InputBinding.Key("F12")],
    };

    private readonly record struct InputBinding(string? KeyCode, int? MouseBtn)
    {
        public static InputBinding Key(string code) => new(code, null);
        public static InputBinding Mouse(int button) => new(null, button);
    }

    public float MouseX { get; private set; }
    public float MouseY { get; private set; }
    public float MouseWheelY { get; private set; }
    public bool QuitRequested => false; // Browser never quits
    public string TextInput => _textInputBuffer;
    public int TextInputBackspacesCount => _textInputBackspaceCount;
    public int TextInputReturnsCount => _textInputReturnCount;
    public InputMethod ActiveInputMethod => InputMethod.MouseKeyboard;
    public MovementInputMode MovementMode => MovementInputMode.HeadingRelative;

    public WebInputManager(Func<(int Width, int Height)> getCanvasSize)
    {
        _getCanvasSize = getCanvasSize;
    }

    public void BeginFrame()
    {
        MouseX = JsInput.GetMouseX();
        MouseY = JsInput.GetMouseY();

        _textInputBuffer = "";
        _textInputBackspaceCount = 0;
        _textInputReturnCount = 0;
    }

    public void EndFrame()
    {
        _keysPressed.Clear();
        _keysReleased.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        MouseWheelY = 0;
    }

    public void Reset()
    {
        _keysDown.Clear();
        _keysPressed.Clear();
        _keysReleased.Clear();
        _mouseDown.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        MouseWheelY = 0;
        _textInputBuffer = "";
    }

    public void ProcessEvents()
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

        // Parse packed events from JS: "KD:KeyW|KU:KeyA|MD:0|MU:2|..."
        string events = JsInput.FlushEvents();
        if (string.IsNullOrEmpty(events)) return;

        foreach (var part in events.Split('|'))
        {
            if (part.Length < 3) continue;

            var type = part[..2];
            var value = part[3..];

            switch (type)
            {
                case "KD": // Key down
                    if (_keysDown.Add(value))
                        _keysPressed.Add(value);
                    break;
                case "KU": // Key up
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

    public bool IsActionDown(InputAction action) => IsAnyBindingDown(action, _keysDown, _mouseDown);
    public bool IsActionPressed(InputAction action) => IsAnyBindingDown(action, _keysPressed, _mousePressed);
    public bool IsActionReleased(InputAction action) => IsAnyBindingDown(action, _keysReleased, _mouseReleased);

    public Vector2 GetActionAxisDirection(InputActionAxis axis)
    {
        return axis switch
        {
            InputActionAxis.Movement => GetMovementDirection(),
            InputActionAxis.Heading => GetHeadingDirection(),
            _ => Vector2.Zero,
        };
    }

    public string GetActionHelpText(InputAction action, bool includeSecondary = false)
    {
        if (!_bindings.TryGetValue(action, out var bindingList) || bindingList.Count == 0)
            return string.Empty;

        List<string> labels = [];
        foreach (var binding in bindingList)
        {
            string label = GetBindingLabel(binding);
            if (!string.IsNullOrWhiteSpace(label) && !labels.Contains(label))
                labels.Add(label);
        }

        if (labels.Count == 0) return string.Empty;
        if (!includeSecondary && labels.Count > 1) return labels[0];
        return string.Join("/", labels);
    }

    public string GetActionHelpTextFull(InputAction action) => GetActionHelpText(action, true);

    public string GetMouseButtonHelpText(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => "LMB",
            MouseButton.Right => "RMB",
            MouseButton.Middle => "MMB",
            _ => $"Mouse{(int)button}",
        };
    }

    public bool IsMouseDown(MouseButton button) => _mouseDown.Contains((int)button - 1);
    public bool IsMousePressed(MouseButton button) => _mousePressed.Contains((int)button - 1);
    public bool IsMouseReleased(MouseButton button) => _mouseReleased.Contains((int)button - 1);

    private bool IsAnyBindingDown(InputAction action, HashSet<string> keySet, HashSet<int> mouseSet)
    {
        if (!_bindings.TryGetValue(action, out var bindingList)) return false;

        foreach (var binding in bindingList)
        {
            if (binding.KeyCode != null && keySet.Contains(binding.KeyCode))
                return true;
            if (binding.MouseBtn.HasValue && mouseSet.Contains(binding.MouseBtn.Value))
                return true;
        }
        return false;
    }

    private Vector2 GetMovementDirection()
    {
        Vector2 dir = Vector2.Zero;
        if (IsActionDown(InputAction.MoveUp)) dir.Y -= 1f;
        if (IsActionDown(InputAction.MoveDown)) dir.Y += 1f;
        if (IsActionDown(InputAction.MoveLeft)) dir.X -= 1f;
        if (IsActionDown(InputAction.MoveRight)) dir.X += 1f;
        return dir == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(dir);
    }

    private Vector2 GetHeadingDirection()
    {
        var (winW, winH) = _getCanvasSize();
        Vector2 screenCenter = new(winW / 2f, winH / 2f);
        Vector2 mousePosition = new(MouseX, MouseY);
        Vector2 direction = mousePosition - screenCenter;
        return direction == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(direction);
    }

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

        return string.Empty;
    }
}
