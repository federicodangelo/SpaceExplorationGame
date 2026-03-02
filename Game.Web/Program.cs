using System.Runtime.InteropServices.JavaScript;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.States;
using Engine.Platform.Web;

namespace SpaceExplorationGame;

/// <summary>
/// WebAssembly entry point. Initializes the game and exposes a per-frame
/// step function that the browser's requestAnimationFrame loop can call.
/// </summary>
public partial class WebMain
{
    private static Game? _game;

    public static void Main()
    {
        try
        {
            Console.WriteLine("[SEG-CS] Main() starting...");

            // Create platform
            var musicProvider = new GameMusicProvider(WebAudioManager.SampleRate);
            var sfxProvider = new GameSfxProvider(WebAudioManager.SampleRate);
            Console.WriteLine("[SEG-CS] Audio providers created");

            var platform = new WebPlatform(
                GameConfig.WindowTitle,
                GameConfig.DefaultWindowWidth, GameConfig.DefaultWindowHeight,
                musicProvider, sfxProvider,
                GameConfig.AudioMasterVolume, GameConfig.AudioMusicVolume, GameConfig.AudioSfxVolume);
            Console.WriteLine("[SEG-CS] WebPlatform created");

            _game = new Game();
            _game.Initialize(platform);
            Console.WriteLine("[SEG-CS] Game initialized");

            _game.ChangeState(new MainMenuState());
            Console.WriteLine("[SEG-CS] MainMenuState set");

            _game.InitializeLoop();
            Console.WriteLine("[SEG-CS] Game loop initialized, ready for frames");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SEG-CS] FATAL: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Called by JavaScript each frame via requestAnimationFrame.
    /// </summary>
    [JSExport]
    public static void RunOneFrame()
    {
        try
        {
            _game?.RunOneFrame();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SEG-CS] Frame error: {ex}");
            throw;
        }
    }
}
