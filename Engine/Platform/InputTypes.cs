namespace Engine.Platform;

public enum InputAction
{
    DebugToggle,
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
    Screenshot,
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

public enum MouseButton
{
    Left = 1,
    Middle = 2,
    Right = 3,
}

public enum MovementInputMode
{
    HeadingRelative,
    Absolute,
}

public enum TextureScaleMode
{
    Nearest,
    Linear,
}
