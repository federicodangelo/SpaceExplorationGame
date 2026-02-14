using SDL3;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Input snapshot captured each frame. Provides current and previous state for edge detection.
/// </summary>
public class InputManager
{
    private readonly HashSet<SDL.Scancode> _keysDown = [];
    private readonly HashSet<SDL.Scancode> _keysPressed = [];  // just pressed this frame
    private readonly HashSet<SDL.Scancode> _keysReleased = []; // just released this frame

    private readonly HashSet<int> _mouseDown = [];
    private readonly HashSet<int> _mousePressed = [];
    private readonly HashSet<int> _mouseReleased = [];

    public float MouseX { get; private set; }
    public float MouseY { get; private set; }
    public float MouseWheelY { get; private set; }
    public bool QuitRequested { get; private set; }

    /// <summary>Call at the start of each frame before processing events.</summary>
    public void BeginFrame()
    {
        _keysPressed.Clear();
        _keysReleased.Clear();
        _mousePressed.Clear();
        _mouseReleased.Clear();
        MouseWheelY = 0;
        QuitRequested = false;
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
                if (!e.Key.Repeat)
                {
                    _keysDown.Add(e.Key.Scancode);
                    _keysPressed.Add(e.Key.Scancode);
                }
                break;

            case SDL.EventType.KeyUp:
                _keysDown.Remove(e.Key.Scancode);
                _keysReleased.Add(e.Key.Scancode);
                break;

            case SDL.EventType.MouseMotion:
                MouseX = e.Motion.X;
                MouseY = e.Motion.Y;
                break;

            case SDL.EventType.MouseButtonDown:
                _mouseDown.Add((int)e.Button.Button);
                _mousePressed.Add((int)e.Button.Button);
                break;

            case SDL.EventType.MouseButtonUp:
                _mouseDown.Remove((int)e.Button.Button);
                _mouseReleased.Add((int)e.Button.Button);
                break;

            case SDL.EventType.MouseWheel:
                MouseWheelY = e.Wheel.Y;
                break;
        }
    }

    // Key queries
    public bool IsKeyDown(SDL.Scancode key) => _keysDown.Contains(key);
    public bool IsKeyPressed(SDL.Scancode key) => _keysPressed.Contains(key);
    public bool IsKeyReleased(SDL.Scancode key) => _keysReleased.Contains(key);

    // Mouse queries (1 = left, 2 = middle, 3 = right)
    public bool IsMouseDown(int button) => _mouseDown.Contains(button);
    public bool IsMousePressed(int button) => _mousePressed.Contains(button);
    public bool IsMouseReleased(int button) => _mouseReleased.Contains(button);
}
