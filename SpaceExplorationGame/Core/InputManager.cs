using System.Numerics;
using SDL3;

namespace SpaceExplorationGame.Core;

public enum InputAction
{
    MenuConfirm,
    MenuUp,
    MenuDown,
    MenuLeft,
    MenuRight,
    MenuBack,
    MenuSecondaryAction,

    MoveUp,
    MoveDown,
    MoveLeft,
    MoveRight,
    FireWeapon,
    MapZoomOut,
    MapZoomIn,
    MapPreviousView,
    MapNextView,
    Interact,
    ToggleMap,
}

public enum InputActionAxis
{
    Movement,
    Heading,
}

public enum InputMethod
{
    MouseKeyboard,
    Gamepad,
}

public enum MovementInputMode
{
    HeadingRelative,
    Absolute,
}

/// <summary>
/// Input snapshot captured each frame. Provides current and previous state for edge detection.
/// </summary>
public class InputManager
{
    private readonly record struct InputBinding(SDL.Scancode? Scancode, int? MouseButton, SDL.GamepadButton? GamepadButton, SDL.GamepadAxis? GamepadAxis)
    {
        public static InputBinding Key(SDL.Scancode scancode) => new(scancode, null, null, null);
        public static InputBinding Mouse(int button) => new(null, button, null, null);
        public static InputBinding Gamepad(SDL.GamepadButton button) => new(null, null, button, null);
        public static InputBinding Axis(SDL.GamepadAxis axis) => new(null, null, null, axis);
    }

    private readonly HashSet<SDL.Scancode> _keysDown = [];
    private readonly HashSet<SDL.Scancode> _keysPressed = [];  // just pressed this frame
    private readonly HashSet<SDL.Scancode> _keysReleased = []; // just released this frame

    private readonly HashSet<int> _mouseDown = [];
    private readonly HashSet<int> _mousePressed = [];
    private readonly HashSet<int> _mouseReleased = [];

    private readonly HashSet<SDL.GamepadButton> _gamepadDown = [];
    private readonly HashSet<SDL.GamepadButton> _gamepadPressed = [];
    private readonly HashSet<SDL.GamepadButton> _gamepadReleased = [];

    private readonly HashSet<SDL.GamepadAxis> _gamepadAxesDown = [];
    private readonly HashSet<SDL.GamepadAxis> _gamepadAxesPressed = [];
    private readonly HashSet<SDL.GamepadAxis> _gamepadAxesReleased = [];

    private uint _activeGamepadId;
    private float _leftStickX;
    private float _leftStickY;
    private float _rightStickX;
    private float _rightStickY;

    private const float GamepadDeadZone = 0.20f;
    private const float GamepadTriggerThreshold = 0.35f;

    private readonly Dictionary<InputAction, List<InputBinding>> _bindings = new()
    {
        [InputAction.MenuConfirm] = [InputBinding.Key(SDL.Scancode.Return), InputBinding.Key(SDL.Scancode.Space), InputBinding.Gamepad(SDL.GamepadButton.South)],
        [InputAction.MenuUp] = [InputBinding.Key(SDL.Scancode.Up), InputBinding.Key(SDL.Scancode.W), InputBinding.Gamepad(SDL.GamepadButton.DPadUp)],
        [InputAction.MenuDown] = [InputBinding.Key(SDL.Scancode.Down), InputBinding.Key(SDL.Scancode.S), InputBinding.Gamepad(SDL.GamepadButton.DPadDown)],
        [InputAction.MenuLeft] = [InputBinding.Key(SDL.Scancode.Left), InputBinding.Key(SDL.Scancode.A), InputBinding.Gamepad(SDL.GamepadButton.DPadLeft)],
        [InputAction.MenuRight] = [InputBinding.Key(SDL.Scancode.Right), InputBinding.Key(SDL.Scancode.D), InputBinding.Gamepad(SDL.GamepadButton.DPadRight)],
        [InputAction.MenuBack] = [InputBinding.Key(SDL.Scancode.Escape), InputBinding.Key(SDL.Scancode.Backspace), InputBinding.Gamepad(SDL.GamepadButton.East), InputBinding.Gamepad(SDL.GamepadButton.Start)],
        [InputAction.MenuSecondaryAction] = [InputBinding.Key(SDL.Scancode.X), InputBinding.Key(SDL.Scancode.Delete)],

        [InputAction.MoveUp] = [InputBinding.Key(SDL.Scancode.W), InputBinding.Key(SDL.Scancode.Up)],
        [InputAction.MoveDown] = [InputBinding.Key(SDL.Scancode.S), InputBinding.Key(SDL.Scancode.Down)],
        [InputAction.MoveLeft] = [InputBinding.Key(SDL.Scancode.A), InputBinding.Key(SDL.Scancode.Left)],
        [InputAction.MoveRight] = [InputBinding.Key(SDL.Scancode.D), InputBinding.Key(SDL.Scancode.Right)],
        [InputAction.FireWeapon] = [
            InputBinding.Key(SDL.Scancode.Space),
            InputBinding.Mouse(SDL.ButtonLeft),
            InputBinding.Gamepad(SDL.GamepadButton.West),
            InputBinding.Axis(SDL.GamepadAxis.LeftTrigger),
            InputBinding.Axis(SDL.GamepadAxis.RightTrigger)
        ],
        [InputAction.MapZoomOut] = [InputBinding.Axis(SDL.GamepadAxis.LeftTrigger)],
        [InputAction.MapZoomIn] = [InputBinding.Axis(SDL.GamepadAxis.RightTrigger)],
        [InputAction.MapPreviousView] = [InputBinding.Gamepad(SDL.GamepadButton.LeftShoulder)],
        [InputAction.MapNextView] = [InputBinding.Gamepad(SDL.GamepadButton.RightShoulder)],
        [InputAction.Interact] = [InputBinding.Key(SDL.Scancode.E), InputBinding.Gamepad(SDL.GamepadButton.South)],
        [InputAction.ToggleMap] = [InputBinding.Key(SDL.Scancode.M), InputBinding.Gamepad(SDL.GamepadButton.Back)],
    };

    public float MouseX { get; private set; }
    public float MouseY { get; private set; }
    public float MouseWheelY { get; private set; }
    public bool QuitRequested { get; private set; }
    public InputMethod ActiveInputMethod { get; private set; } = InputMethod.MouseKeyboard;
    public MovementInputMode MovementMode =>
        ActiveInputMethod == InputMethod.Gamepad ? MovementInputMode.Absolute : MovementInputMode.HeadingRelative;

    /// <summary>Call at the start of each frame before processing events.</summary>
    public void BeginFrame()
    {
        // Only poll mouse position here.
        // Edge-detection sets (pressed/released) are NOT cleared here —
        // they persist until EndFrame() so that fixed-timestep updates always see them.
        SDL.GetMouseState(out float mx, out float my);
        MouseX = mx;
        MouseY = my;
    }

    /// <summary>
    /// Call after the fixed-timestep update loop has run at least once.
    /// Clears edge-detection state so the next frame starts fresh.
    /// </summary>
    public void EndFrame()
    {
        _keysPressed.Clear();
        _keysReleased.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        _gamepadPressed.Clear();
        _gamepadReleased.Clear();
        _gamepadAxesPressed.Clear();
        _gamepadAxesReleased.Clear();
        MouseWheelY = 0;
    }

    /// <summary>
    /// Full reset: clears ALL input state (pressed, released, down, wheel).
    /// Use on state transitions so the new state starts with a completely clean slate.
    /// </summary>
    public void Reset()
    {
        _keysDown.Clear();
        _keysPressed.Clear();
        _keysReleased.Clear();
        _mouseDown.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        _gamepadDown.Clear();
        _gamepadPressed.Clear();
        _gamepadReleased.Clear();
        _gamepadAxesDown.Clear();
        _gamepadAxesPressed.Clear();
        _gamepadAxesReleased.Clear();
        _activeGamepadId = 0;
        _leftStickX = 0;
        _leftStickY = 0;
        _rightStickX = 0;
        _rightStickY = 0;
        MouseWheelY = 0;
        ActiveInputMethod = InputMethod.MouseKeyboard;
    }

    /// <summary>Feed an SDL event into the input manager.</summary>
    public void ProcessEvent(SDL.Event e)
    {
        switch ((SDL.EventType)e.Type)
        {
            case SDL.EventType.Quit:
                QuitRequested = true;
                break;

            case SDL.EventType.KeyDown:
                ActiveInputMethod = InputMethod.MouseKeyboard;
                if (!e.Key.Repeat)
                {
                    _keysDown.Add(e.Key.Scancode);
                    _keysPressed.Add(e.Key.Scancode);
                }
                break;

            case SDL.EventType.KeyUp:
                ActiveInputMethod = InputMethod.MouseKeyboard;
                _keysDown.Remove(e.Key.Scancode);
                _keysReleased.Add(e.Key.Scancode);
                break;

            case SDL.EventType.MouseMotion:
                MouseX = e.Motion.X;
                MouseY = e.Motion.Y;
                break;

            case SDL.EventType.MouseButtonDown:
                _mouseDown.Add(e.Button.Button);
                _mousePressed.Add(e.Button.Button);
                MouseX = e.Button.X;
                MouseY = e.Button.Y;
                break;

            case SDL.EventType.MouseButtonUp:
                _mouseDown.Remove(e.Button.Button);
                _mouseReleased.Add(e.Button.Button);
                MouseX = e.Button.X;
                MouseY = e.Button.Y;
                break;

            case SDL.EventType.MouseWheel:
                MouseWheelY += e.Wheel.Y;  // accumulate across frames
                break;

            case SDL.EventType.GamepadAdded:
                ActiveInputMethod = InputMethod.Gamepad;
                SDL.OpenGamepad(e.GDevice.Which);
                if (_activeGamepadId == 0)
                    _activeGamepadId = e.GDevice.Which;
                break;

            case SDL.EventType.GamepadRemoved:
                if (_activeGamepadId == e.GDevice.Which)
                {
                    _activeGamepadId = 0;
                    _gamepadDown.Clear();
                    _gamepadPressed.Clear();
                    _gamepadReleased.Clear();
                    _gamepadAxesDown.Clear();
                    _gamepadAxesPressed.Clear();
                    _gamepadAxesReleased.Clear();
                    _leftStickX = 0;
                    _leftStickY = 0;
                    _rightStickX = 0;
                    _rightStickY = 0;
                }
                SDL.CloseGamepad((nint)e.GDevice.Which);
                break;

            case SDL.EventType.GamepadButtonDown:
                ActiveInputMethod = InputMethod.Gamepad;
                TrackGamepadSource(e.GButton.Which);
                if (IsFromActiveGamepad(e.GButton.Which))
                {
                    SDL.GamepadButton button = (SDL.GamepadButton)e.GButton.Button;
                    _gamepadDown.Add(button);
                    _gamepadPressed.Add(button);
                }
                break;

            case SDL.EventType.GamepadButtonUp:
                ActiveInputMethod = InputMethod.Gamepad;
                if (IsFromActiveGamepad(e.GButton.Which))
                {
                    SDL.GamepadButton button = (SDL.GamepadButton)e.GButton.Button;
                    _gamepadDown.Remove(button);
                    _gamepadReleased.Add(button);
                }
                break;

            case SDL.EventType.GamepadAxisMotion:
                TrackGamepadSource(e.GAxis.Which);
                if (IsFromActiveGamepad(e.GAxis.Which))
                {
                    float normalized = NormalizeGamepadAxis(e.GAxis.Value);
                    switch ((SDL.GamepadAxis)e.GAxis.Axis)
                    {
                        case SDL.GamepadAxis.LeftX:
                            _leftStickX = normalized;
                            break;
                        case SDL.GamepadAxis.LeftY:
                            _leftStickY = normalized;
                            break;
                        case SDL.GamepadAxis.RightX:
                            _rightStickX = normalized;
                            break;
                        case SDL.GamepadAxis.RightY:
                            _rightStickY = normalized;
                            break;
                    }

                    if (Math.Abs(normalized) >= GamepadDeadZone)
                    {
                        ActiveInputMethod = InputMethod.Gamepad;
                    }

                    UpdateGamepadAxisState((SDL.GamepadAxis)e.GAxis.Axis, normalized);
                }
                break;
        }
    }

    // Key queries
    public bool IsKeyDown(SDL.Scancode key) => _keysDown.Contains(key);
    public bool IsKeyPressed(SDL.Scancode key) => _keysPressed.Contains(key);
    public bool IsKeyReleased(SDL.Scancode key) => _keysReleased.Contains(key);

    public bool IsActionDown(InputAction action) => IsAnyBindingActive(action, _keysDown, _mouseDown, _gamepadDown, _gamepadAxesDown);
    public bool IsActionPressed(InputAction action) => IsAnyBindingActive(action, _keysPressed, _mousePressed, _gamepadPressed, _gamepadAxesPressed);
    public bool IsActionReleased(InputAction action) => IsAnyBindingActive(action, _keysReleased, _mouseReleased, _gamepadReleased, _gamepadAxesReleased);

    public Vector2 GetActionAxisDirection(InputActionAxis axis)
    {
        return axis switch
        {
            InputActionAxis.Movement => GetCombinedMovementDirection(),
            InputActionAxis.Heading => GetCombinedHeadingDirection(),
            _ => Vector2.Zero,
        };
    }

    public string GetActionHelpText(InputAction action, bool includeSecondary = false)
    {
        if (!_bindings.TryGetValue(action, out List<InputBinding>? bindingList) || bindingList.Count == 0)
            return string.Empty;

        List<string> labels = [];
        foreach (InputBinding binding in bindingList)
        {
            if (!ShouldIncludeBindingForActiveInput(binding))
                continue;

            string label = GetBindingLabel(binding);
            if (!string.IsNullOrWhiteSpace(label) && !labels.Contains(label))
                labels.Add(label);
        }

        if (labels.Count == 0)
            return string.Empty;

        if (!includeSecondary && labels.Count > 1)
            return labels[0];

        return string.Join("/", labels);
    }

    public string GetActionHelpTextFull(InputAction action)
    {
        return GetActionHelpText(action, includeSecondary: true);
    }

    public string GetKeyHelpText(SDL.Scancode scancode)
    {
        return ActiveInputMethod == InputMethod.MouseKeyboard
            ? GetBindingLabel(InputBinding.Key(scancode))
            : string.Empty;
    }

    public string GetMouseButtonHelpText(int button)
    {
        return ActiveInputMethod == InputMethod.MouseKeyboard
            ? GetBindingLabel(InputBinding.Mouse(button))
            : string.Empty;
    }

    // Mouse queries (SDL.ButtonLeft=1, SDL.ButtonMiddle=2, SDL.ButtonRight=3)
    public bool IsMouseDown(int button) => _mouseDown.Contains(button);
    public bool IsMousePressed(int button) => _mousePressed.Contains(button);
    public bool IsMouseReleased(int button) => _mouseReleased.Contains(button);

    private bool IsAnyBindingActive(
        InputAction action,
        HashSet<SDL.Scancode> keySet,
        HashSet<int> mouseSet,
        HashSet<SDL.GamepadButton>? gamepadSet = null,
        HashSet<SDL.GamepadAxis>? gamepadAxisSet = null)
    {
        if (!_bindings.TryGetValue(action, out List<InputBinding>? bindingList))
            return false;

        foreach (InputBinding binding in bindingList)
        {
            if (binding.Scancode.HasValue && keySet.Contains(binding.Scancode.Value))
                return true;

            if (binding.MouseButton.HasValue && mouseSet.Contains(binding.MouseButton.Value))
                return true;

            if (binding.GamepadButton.HasValue && gamepadSet != null && gamepadSet.Contains(binding.GamepadButton.Value))
                return true;

            if (binding.GamepadAxis.HasValue && gamepadAxisSet != null && gamepadAxisSet.Contains(binding.GamepadAxis.Value))
                return true;
        }

        return false;
    }

    private Vector2 GetDirectionFromActions(InputAction upAction, InputAction downAction, InputAction leftAction, InputAction rightAction)
    {
        Vector2 direction = Vector2.Zero;
        if (IsActionDown(upAction)) direction.Y -= 1f;
        if (IsActionDown(downAction)) direction.Y += 1f;
        if (IsActionDown(leftAction)) direction.X -= 1f;
        if (IsActionDown(rightAction)) direction.X += 1f;
        return direction == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(direction);
    }

    private Vector2 GetLeftStickDirection()
    {
        Vector2 direction = new(_leftStickX, _leftStickY);
        return ApplyDeadZone(direction);
    }

    private Vector2 GetRightStickDirection()
    {
        Vector2 direction = new(_rightStickX, _rightStickY);
        return ApplyDeadZone(direction);
    }

    private static Vector2 ApplyDeadZone(Vector2 direction)
    {
        float length = direction.Length();
        if (length < GamepadDeadZone)
            return Vector2.Zero;

        return Vector2.Normalize(direction);
    }

    private static float NormalizeGamepadAxis(short value)
    {
        return value < 0 ? value / 32768f : value / 32767f;
    }

    private void UpdateGamepadAxisState(SDL.GamepadAxis axis, float normalizedValue)
    {
        if (axis != SDL.GamepadAxis.LeftTrigger && axis != SDL.GamepadAxis.RightTrigger)
            return;

        bool isDown = normalizedValue >= GamepadTriggerThreshold;
        bool wasDown = _gamepadAxesDown.Contains(axis);

        if (isDown && !wasDown)
        {
            _gamepadAxesDown.Add(axis);
            _gamepadAxesPressed.Add(axis);
        }
        else if (!isDown && wasDown)
        {
            _gamepadAxesDown.Remove(axis);
            _gamepadAxesReleased.Add(axis);
        }
    }

    private void TrackGamepadSource(uint gamepadId)
    {
        if (_activeGamepadId == 0)
            _activeGamepadId = gamepadId;
    }

    private bool IsFromActiveGamepad(uint gamepadId)
    {
        return _activeGamepadId != 0 && _activeGamepadId == gamepadId;
    }

    private Vector2 GetDirectionFromScreenCenterToMouse()
    {
        Vector2 screenCenter = new(GameConfig.WindowWidth / 2f, GameConfig.WindowHeight / 2f);
        Vector2 mousePosition = new(MouseX, MouseY);
        Vector2 direction = mousePosition - screenCenter;
        return direction == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(direction);
    }

    private Vector2 GetCombinedMovementDirection()
    {
        return ActiveInputMethod == InputMethod.Gamepad
            ? GetLeftStickDirection()
            : GetDirectionFromActions(InputAction.MoveUp, InputAction.MoveDown, InputAction.MoveLeft, InputAction.MoveRight);
    }

    private Vector2 GetCombinedHeadingDirection()
    {
        return ActiveInputMethod == InputMethod.Gamepad
            ? GetRightStickDirection()
            : GetDirectionFromScreenCenterToMouse();
    }

    private bool ShouldIncludeBindingForActiveInput(InputBinding binding)
    {
        return ActiveInputMethod switch
        {
            InputMethod.Gamepad => binding.GamepadButton.HasValue || binding.GamepadAxis.HasValue,
            _ => binding.Scancode.HasValue || binding.MouseButton.HasValue,
        };
    }

    private static string GetBindingLabel(InputBinding binding)
    {
        if (binding.Scancode.HasValue)
        {
            return binding.Scancode.Value switch
            {
                SDL.Scancode.Return => "Enter",
                SDL.Scancode.Space => "Space",
                SDL.Scancode.Escape => "Esc",
                SDL.Scancode.Backspace => "Backspace",
                SDL.Scancode.Up => "Up",
                SDL.Scancode.Down => "Down",
                SDL.Scancode.Left => "Left",
                SDL.Scancode.Right => "Right",
                _ => binding.Scancode.Value.ToString(),
            };
        }

        if (binding.MouseButton.HasValue)
        {
            return binding.MouseButton.Value switch
            {
                SDL.ButtonLeft => "LMB",
                SDL.ButtonRight => "RMB",
                SDL.ButtonMiddle => "MMB",
                _ => $"Mouse{binding.MouseButton.Value}",
            };
        }

        if (binding.GamepadButton.HasValue)
        {
            return binding.GamepadButton.Value switch
            {
                SDL.GamepadButton.South => "A",
                SDL.GamepadButton.East => "B",
                SDL.GamepadButton.West => "X",
                SDL.GamepadButton.North => "Y",
                SDL.GamepadButton.LeftShoulder => "LB",
                SDL.GamepadButton.RightShoulder => "RB",
                SDL.GamepadButton.Start => "Start",
                SDL.GamepadButton.Back => "Back",
                _ => binding.GamepadButton.Value.ToString(),
            };
        }

        if (binding.GamepadAxis.HasValue)
        {
            return binding.GamepadAxis.Value switch
            {
                SDL.GamepadAxis.LeftTrigger => "LT",
                SDL.GamepadAxis.RightTrigger => "RT",
                _ => binding.GamepadAxis.Value.ToString(),
            };
        }

        return string.Empty;
    }
}
