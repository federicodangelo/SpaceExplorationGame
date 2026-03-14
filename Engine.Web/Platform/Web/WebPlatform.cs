using Engine.Platform.Base;

namespace Engine.Platform.Web;

/// <summary>
/// Web platform implementation — owns all subsystems (renderer, input, audio, settings).
/// Window dimensions are driven by the browser canvas size reported from JavaScript.
/// </summary>
public class WebPlatform : BasePlatform
{
    public override bool CanQuit => false;

    public WebPlatform(string windowTitle, int windowWidth, int windowHeight,
        IMusicProvider musicProvider, ISfxProvider sfxProvider,
        float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
        : base(windowTitle)
    {

        var textures = new WebTextureManager();
        Textures = textures;
        SpriteRenderer = new WebSpriteRenderer(textures);
        InputManager = new WebInputManager(() => (WindowWidth, WindowHeight));
        AudioManager = new WebAudioManager(musicProvider, sfxProvider,
            masterVolume: masterVolume, musicVolume: musicVolume, sfxVolume: sfxVolume);
        AudioManager.Initialize();
        Settings = new WebSettings();
        SaveGame = new WebSaveGame();
    }


}
