namespace SpaceExplorationGame.Core.Config;

public static class WindowConfig
{
    // Window
    public const int DefaultWindowWidth = 1920;
    public const int DefaultWindowHeight = 1080;
    public const string WindowTitle = "Space Exploration Game";

    // Debug
    public static bool Debug = false;

    // Tiles
    public const int TileSize = 32;

    // Timing
    public const float TargetFps = 60f;
    public const float FixedTimeStep = 1f / TargetFps;
    public const int MaxFrameSkip = 5;
}
