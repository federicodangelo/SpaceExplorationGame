using System.Numerics;

namespace Engine.Platform.Web;

/// <summary>
/// Audio manager using the Web Audio API via JavaScript interop.
/// Generates PCM audio chunks in C# (music + SFX) and pushes them
/// to JavaScript for playback via scheduled AudioBufferSourceNodes.
/// </summary>
public sealed class WebAudioManager : IAudioManager
{
    public const int SampleRate = 44100;
    public const int ChannelCount = 2;

    private const int GenerateFrames = 512;
    private const int MaxSimultaneousSfx = 16;
    private const float CrossfadeSpeed = 2f;

    private readonly IMusicProvider _music;
    private readonly ISfxProvider _sfx;
    private readonly List<ActiveSfx> _activeSfx = new(MaxSimultaneousSfx);

    private bool _initialized;

    public float MasterVolume { get; set; }
    public float MusicVolume { get; set; }
    public float SfxVolume { get; set; }

    private float _fadeGain = 1f;
    private float _fadeTarget = 1f;
    private string? _pendingTheme;

    private readonly float[] _genBuf = new float[GenerateFrames * ChannelCount];
    private readonly double[] _pushBuf = new double[GenerateFrames * ChannelCount];

    private struct ActiveSfx
    {
        public float[] Buffer;
        public int Position;
        public float Volume;
        public float Pan;
    }

    public WebAudioManager(IMusicProvider music, ISfxProvider sfx,
        float masterVolume = 0.5f, float musicVolume = 0.4f, float sfxVolume = 0.7f)
    {
        MasterVolume = masterVolume;
        MusicVolume = musicVolume;
        SfxVolume = sfxVolume;
        _music = music;
        _sfx = sfx;
    }

    public bool Initialize()
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

    public void PlaySfxAtDistance(string sfx, Vector2 soundPos, Vector2 relativeToPos,
        float volume = 1f, float maxRange = 800f)
    {
        float dist = Vector2.Distance(soundPos, relativeToPos);
        if (dist >= maxRange) return;
        float atten = 1f - (dist / maxRange);
        atten *= atten;
        float pan = Math.Clamp((soundPos.X - relativeToPos.X) / (maxRange * 0.5f), -1f, 1f);
        PlaySfx(sfx, volume * atten, pan);
    }

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

    public void Update(float dt)
    {
        if (!_initialized) return;

        UpdateFade(dt);

        double buffered = JsAudio.GetBufferedDuration();
        double targetSeconds = 0.08; // 80ms buffer target

        while (buffered < targetSeconds)
        {
            GenerateAndPushChunk();
            buffered += GenerateFrames / (double)SampleRate;
        }
    }

    private void UpdateFade(float dt)
    {
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

        if (_fadeGain <= 0f && _pendingTheme != null)
        {
            _music.SetTheme(_pendingTheme);
            _pendingTheme = null;
            _fadeTarget = 1f;
        }
    }

    private void GenerateAndPushChunk()
    {
        Array.Clear(_genBuf);

        _music.Generate(_genBuf, GenerateFrames);

        float musicGain = MusicVolume * _fadeGain;
        for (int i = 0; i < _genBuf.Length; i++)
            _genBuf[i] *= musicGain;

        MixActiveSfx(_genBuf, GenerateFrames);

        for (int i = 0; i < _genBuf.Length; i++)
            _genBuf[i] = Math.Clamp(_genBuf[i] * MasterVolume, -1f, 1f);

        // Convert float[] to double[] for JS interop (float[] not supported by JSImport)
        for (int i = 0; i < _genBuf.Length; i++)
            _pushBuf[i] = _genBuf[i];

        JsAudio.PushChunk(_pushBuf, GenerateFrames);
    }

    private void MixActiveSfx(float[] buffer, int frames)
    {
        for (int s = _activeSfx.Count - 1; s >= 0; s--)
        {
            var sfx = _activeSfx[s];
            float vol = sfx.Volume * SfxVolume;
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
        _initialized = false;
    }
}
