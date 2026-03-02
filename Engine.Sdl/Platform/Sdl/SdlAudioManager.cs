using SDL3;

namespace Engine.Platform.Sdl;

/// <summary>
/// SDL3 implementation of the audio manager.
/// Uses a push-based model: the base class calls <see cref="PushSamples"/> each frame
/// to keep the SDL audio stream fed with mixed music + SFX samples.
/// </summary>
public sealed class SdlAudioManager : BaseAudioManager
{
    // SDL audio stream (opened via OpenAudioDeviceStream — owns the device)
    private nint _stream;

    // Pre-allocated byte buffer for SDL stream push (avoids per-frame allocation)
    private readonly byte[] _pushBuf = new byte[GenerateFrames * ChannelCount * sizeof(float)];

    public SdlAudioManager(IMusicProvider music, ISfxProvider sfx,
        float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
        : base(music, sfx, masterVolume, musicVolume, sfxVolume)
    {
    }

    /// <summary>
    /// Initialises the SDL audio device and stream.
    /// Must be called after <c>SDL.Init(SDL.InitFlags.Audio)</c>.
    /// Returns false (with a console warning) if the device cannot be opened — the game continues silently.
    /// </summary>
    public override bool Initialize()
    {
        var spec = new SDL.AudioSpec
        {
            Format = SDL.AudioFormat.AudioF32LE,
            Channels = ChannelCount,
            Freq = SampleRate,
        };

        _stream = SDL.OpenAudioDeviceStream(
            SDL.AudioDeviceDefaultPlayback, in spec, null, nint.Zero);

        if (_stream == nint.Zero)
        {
            Console.WriteLine($"Audio: could not open device – {SDL.GetError()}");
            return false;
        }

        SDL.ResumeAudioStreamDevice(_stream);
        _initialized = true;
        return true;
    }

    protected override double GetBufferedSeconds()
    {
        int available = SDL.GetAudioStreamAvailable(_stream);
        return available / (double)(SampleRate * ChannelCount * sizeof(float));
    }

    protected override void PushSamples(float[] genBuf, int frames)
    {
        Buffer.BlockCopy(genBuf, 0, _pushBuf, 0, _pushBuf.Length);
        SDL.PutAudioStreamData(_stream, _pushBuf, _pushBuf.Length);
    }

    public override void Dispose()
    {
        if (_initialized)
        {
            SDL.DestroyAudioStream(_stream);
            _initialized = false;
        }
    }
}
