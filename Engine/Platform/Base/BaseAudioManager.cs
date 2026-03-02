using System.Numerics;

namespace Engine.Platform;

/// <summary>
/// Platform-agnostic base for audio managers.
/// Handles music crossfading, SFX mixing, volume control, and PCM generation.
/// Subclasses provide platform-specific audio output via
/// <see cref="GetBufferedSeconds"/> and <see cref="PushSamples"/>.
/// </summary>
public abstract class BaseAudioManager : IAudioManager
{
    public const int SampleRate = 44100;
    public const int ChannelCount = 2; // stereo, interleaved L/R

    protected const int GenerateFrames = 512;
    protected const int MaxSimultaneousSfx = 16;
    protected const float CrossfadeSpeed = 2f;

    protected readonly IMusicProvider _music;
    protected readonly ISfxProvider _sfx;
    protected readonly List<ActiveSfx> _activeSfx = new(MaxSimultaneousSfx);

    protected bool _initialized;

    public float MasterVolume { get; set; }
    public float MusicVolume { get; set; }
    public float SfxVolume { get; set; }

    protected float _fadeGain = 1f;
    protected float _fadeTarget = 1f;
    protected string? _pendingTheme;

    protected readonly float[] _genBuf = new float[GenerateFrames * ChannelCount];

    /// <summary>Active SFX instance — tracks playback position through a mono buffer.</summary>
    protected struct ActiveSfx
    {
        public float[] Buffer;   // mono samples
        public int Position;     // current sample index
        public float Volume;     // 0 – 1
        public float Pan;        // –1 = left, 0 = center, +1 = right
    }

    protected BaseAudioManager(IMusicProvider music, ISfxProvider sfx,
        float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
    {
        MasterVolume = masterVolume;
        MusicVolume = musicVolume;
        SfxVolume = sfxVolume;
        _music = music;
        _sfx = sfx;
    }

    public abstract bool Initialize();

    // ────────────────────────────────────────────────────────────────
    // Music theme control
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Switch the background music theme. If <paramref name="instant"/> is false the current
    /// theme fades out, the new one fades in (crossfade ≈ 0.5 s each way).
    /// </summary>
    public void SetMusicTheme(string theme, bool instant = false)
    {
        if (!_initialized) return;

        if (instant || _music.CurrentTheme.Length == 0)
        {
            _music.SetTheme(theme);
            _fadeGain = 1f;
            _fadeTarget = 1f;
            _pendingTheme = null;
        }
        else if (theme != _music.CurrentTheme || _pendingTheme != null)
        {
            _pendingTheme = theme;
            _fadeTarget = 0f;
        }
    }

    // ────────────────────────────────────────────────────────────────
    // SFX playback
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Play a one-shot sound effect with volume attenuated by distance from <paramref name="relativeToPos"/>.
    /// <paramref name="maxRange"/>: distance beyond which the sound is inaudible.
    /// </summary>
    public void PlaySfxAtDistance(string sfx, Vector2 soundPos, Vector2 relativeToPos,
        float volume = 1f, float maxRange = 800f)
    {
        float dist = Vector2.Distance(soundPos, relativeToPos);
        if (dist >= maxRange) return;
        float atten = 1f - (dist / maxRange);
        atten *= atten; // quadratic falloff for more natural sound
        float pan = Math.Clamp((soundPos.X - relativeToPos.X) / (maxRange * 0.5f), -1f, 1f);
        PlaySfx(sfx, volume * atten, pan);
    }

    /// <summary>
    /// Play a one-shot sound effect. <paramref name="pan"/>: –1 left, 0 center, +1 right.
    /// </summary>
    public void PlaySfx(string sfx, float volume = 1f, float pan = 0f)
    {
        if (!_initialized) return;
        if (!_sfx.TryGetBuffer(sfx, out var buf)) return;
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

        const double targetSeconds = 0.08; // keep ~80ms of audio buffered
        double frameSeconds = GenerateFrames / (double)SampleRate;
        double buffered = GetBufferedSeconds();

        while (buffered < targetSeconds)
        {
            FillGenBuf();
            PushSamples(_genBuf, GenerateFrames);
            buffered += frameSeconds;
        }
    }

    /// <summary>
    /// Returns how many seconds of audio are currently buffered in the platform audio backend.
    /// </summary>
    protected abstract double GetBufferedSeconds();

    /// <summary>
    /// Pushes the generated PCM buffer (<paramref name="genBuf"/>) to the platform audio backend.
    /// </summary>
    protected abstract void PushSamples(float[] genBuf, int frames);

    public abstract void Dispose();

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
        if (_fadeGain <= 0f && _pendingTheme != null)
        {
            _music.SetTheme(_pendingTheme);
            _pendingTheme = null;
            _fadeTarget = 1f;
        }
    }

    private void FillGenBuf()
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
}
