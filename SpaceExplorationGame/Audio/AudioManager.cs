using System.Numerics;
using SDL3;

namespace SpaceExplorationGame.Audio;

/// <summary>
/// Central audio manager — procedurally generated music and sound effects via SDL3.
/// Uses a push-based model: the game loop calls <see cref="Update"/> each frame to keep
/// the audio stream fed with mixed music + SFX samples.
/// </summary>
public sealed class AudioManager : IDisposable
{
    public const int SampleRate = 44100;
    public const int ChannelCount = 2; // stereo, interleaved L/R

    private const int GenerateFrames = 2048;     // ~46 ms per chunk at 44100 Hz
    private const int MaxSimultaneousSfx = 16;
    private const float CrossfadeSpeed = 2f;     // volume units / second

    // SDL audio stream (opened via OpenAudioDeviceStream — owns the device)
    private nint _stream;
    private bool _initialized;

    // Generators
    private readonly MusicGenerator _music;
    private readonly Dictionary<SfxType, float[]> _sfxBuffers;

    // Active one-shot SFX instances
    private readonly List<ActiveSfx> _activeSfx = new(MaxSimultaneousSfx);

    // Volume controls (0 – 1)
    public float MasterVolume { get; set; }
    public float MusicVolume { get; set; }
    public float SfxVolume { get; set; }

    // Crossfade state
    private float _fadeGain = 1f;
    private float _fadeTarget = 1f;
    private MusicTheme? _pendingTheme;

    // Pre-allocated buffers to avoid per-frame allocation
    private readonly float[] _genBuf = new float[GenerateFrames * ChannelCount];
    private readonly byte[] _pushBuf = new byte[GenerateFrames * ChannelCount * sizeof(float)];

    /// <summary>Active SFX instance — tracks playback position through a mono buffer.</summary>
    private struct ActiveSfx
    {
        public float[] Buffer;   // mono samples
        public int Position;     // current sample index
        public float Volume;     // 0 – 1
        public float Pan;        // –1 = left, 0 = center, +1 = right
    }

    public AudioManager(float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
    {
        MasterVolume = masterVolume;
        MusicVolume = musicVolume;
        SfxVolume = sfxVolume;

        _music = new MusicGenerator(SampleRate);
        _sfxBuffers = SfxGenerator.GenerateAll(SampleRate);
    }

    /// <summary>
    /// Initialise the SDL audio device and stream.
    /// Must be called after <c>SDL.Init(SDL.InitFlags.Audio)</c>.
    /// Returns false (with a console warning) if the device cannot be opened — the game continues silently.
    /// </summary>
    public bool Initialize()
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

    // ────────────────────────────────────────────────────────────────
    // Music theme control
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Switch the background music theme. If <paramref name="instant"/> is false the current
    /// theme fades out, the new one fades in (crossfade ≈ 0.5 s each way).
    /// </summary>
    public void SetMusicTheme(MusicTheme theme, bool instant = false)
    {
        if (!_initialized) return;

        if (instant || _music.CurrentTheme == MusicTheme.None)
        {
            _music.SetTheme(theme);
            _fadeGain = 1f;
            _fadeTarget = 1f;
            _pendingTheme = null;
        }
        else if (theme != _music.CurrentTheme || _pendingTheme.HasValue)
        {
            _pendingTheme = theme;
            _fadeTarget = 0f;   // start fade-out
        }
    }

    // ────────────────────────────────────────────────────────────────
    // SFX playback
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Play a one-shot sound effect with volume attenuated by distance from <paramref name="playerPos"/>.
    /// <paramref name="maxRange"/>: distance beyond which the sound is inaudible.
    /// </summary>
    public void PlaySfxAtDistance(SfxType type, Vector2 soundPos, Vector2 playerPos,
        float volume = 1f, float maxRange = 800f)
    {
        float dist = Vector2.Distance(soundPos, playerPos);
        if (dist >= maxRange) return;
        float atten = 1f - (dist / maxRange);
        atten *= atten; // quadratic falloff for more natural sound
        float pan = Math.Clamp((soundPos.X - playerPos.X) / (maxRange * 0.5f), -1f, 1f);
        PlaySfx(type, volume * atten, pan);
    }

    /// <summary>
    /// Play a one-shot sound effect. <paramref name="pan"/>: –1 left, 0 center, +1 right.
    /// </summary>
    public void PlaySfx(SfxType type, float volume = 1f, float pan = 0f)
    {
        if (!_initialized) return;
        if (!_sfxBuffers.TryGetValue(type, out var buf)) return;
        if (_activeSfx.Count >= MaxSimultaneousSfx) return;

        _activeSfx.Add(new ActiveSfx
        {
            Buffer = buf,
            Position = 0,
            Volume = volume,
            Pan = Math.Clamp(pan, -1f, 1f),
        });
    }

    // ────────────────────────────────────────────────────────────────
    // Per-frame update — generates & pushes audio chunks
    // ────────────────────────────────────────────────────────────────

    public void Update(float dt)
    {
        if (!_initialized) return;

        UpdateFade(dt);

        // Keep ≈ 0.2 s of audio buffered
        int available = SDL.GetAudioStreamAvailable(_stream);
        int targetBytes = SampleRate * ChannelCount * sizeof(float) / 5;

        while (available < targetBytes)
        {
            GenerateAndPushChunk();
            available += _pushBuf.Length;
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Internals
    // ────────────────────────────────────────────────────────────────

    private void UpdateFade(float dt)
    {
        // Interpolate fade gain toward target
        if (MathF.Abs(_fadeGain - _fadeTarget) < 0.001f)
        {
            _fadeGain = _fadeTarget;
        }
        else
        {
            float step = CrossfadeSpeed * dt;
            if (_fadeGain > _fadeTarget)
                _fadeGain = MathF.Max(_fadeGain - step, 0f);
            else
                _fadeGain = MathF.Min(_fadeGain + step, _fadeTarget);
        }

        // When fade-out is complete and a new theme is waiting, switch and start fade-in
        if (_fadeGain <= 0f && _pendingTheme.HasValue)
        {
            _music.SetTheme(_pendingTheme.Value);
            _pendingTheme = null;
            _fadeTarget = 1f;
        }
    }

    private void GenerateAndPushChunk()
    {
        Array.Clear(_genBuf);

        // 1) Music
        _music.Generate(_genBuf, GenerateFrames);

        float musicGain = MusicVolume * _fadeGain;
        for (int i = 0; i < _genBuf.Length; i++)
            _genBuf[i] *= musicGain;

        // 2) SFX (mixed on top)
        MixActiveSfx(_genBuf, GenerateFrames);

        // 3) Master volume + clamp
        for (int i = 0; i < _genBuf.Length; i++)
            _genBuf[i] = Math.Clamp(_genBuf[i] * MasterVolume, -1f, 1f);

        // 4) Push to SDL stream
        Buffer.BlockCopy(_genBuf, 0, _pushBuf, 0, _pushBuf.Length);
        SDL.PutAudioStreamData(_stream, _pushBuf, _pushBuf.Length);
    }

    private void MixActiveSfx(float[] buffer, int frames)
    {
        for (int s = _activeSfx.Count - 1; s >= 0; s--)
        {
            var sfx = _activeSfx[s];
            float vol = sfx.Volume * SfxVolume;

            // Constant-power panning
            float lGain = vol * MathF.Sqrt(0.5f * (1f - sfx.Pan));
            float rGain = vol * MathF.Sqrt(0.5f * (1f + sfx.Pan));

            int remaining = sfx.Buffer.Length - sfx.Position;
            int count = Math.Min(frames, remaining);

            for (int f = 0; f < count; f++)
            {
                float mono = sfx.Buffer[sfx.Position + f];
                buffer[f * 2] += mono * lGain;
                buffer[f * 2 + 1] += mono * rGain;
            }

            sfx.Position += count;
            if (sfx.Position >= sfx.Buffer.Length)
                _activeSfx.RemoveAt(s);
            else
                _activeSfx[s] = sfx;
        }
    }

    public void Dispose()
    {
        if (_initialized)
        {
            SDL.DestroyAudioStream(_stream);
            _initialized = false;
        }
    }
}
