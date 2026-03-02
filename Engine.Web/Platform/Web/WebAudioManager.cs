namespace Engine.Platform.Web;

/// <summary>
/// Web Audio API implementation of the audio manager.
/// Generates PCM audio chunks in C# (music + SFX) and pushes them
/// to JavaScript for playback via scheduled AudioBufferSourceNodes.
/// </summary>
public sealed class WebAudioManager : BaseAudioManager
{
    // Pre-allocated double buffer for JS interop (float[] not supported by JSImport)
    private readonly double[] _pushBuf = new double[GenerateFrames * ChannelCount];

    public WebAudioManager(IMusicProvider music, ISfxProvider sfx,
        float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
        : base(music, sfx, masterVolume, musicVolume, sfxVolume)
    {
    }

    public override bool Initialize()
    {
        try
        {
            _initialized = JsAudio.Init(SampleRate);
        }
        catch
        {
            _initialized = false;
        }
        return _initialized;
    }

    protected override double GetBufferedSeconds()
        => JsAudio.GetBufferedDuration();

    protected override void PushSamples(float[] genBuf, int frames)
    {
        // Convert float[] to double[] for JS interop (float[] not supported by JSImport)
        for (int i = 0; i < genBuf.Length; i++)
            _pushBuf[i] = genBuf[i];

        JsAudio.PushChunk(_pushBuf, frames);
    }

    public override void Dispose()
    {
        _initialized = false;
    }
}
