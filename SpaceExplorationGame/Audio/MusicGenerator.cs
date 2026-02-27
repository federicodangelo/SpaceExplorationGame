namespace SpaceExplorationGame.Audio;

/// <summary>Identifies the current background music mood / game context.</summary>
public enum MusicTheme
{
    None,
    MainMenu,
    SolarSystem,
    PlanetSurface,
    Interior,
    FTL,
    Combat,
}

/// <summary>
/// Real-time procedural ambient music generator.
/// Produces stereo float PCM via layered synthesis:
///   Drone — detuned sine pad at the root frequency
///   Pad   — three-note chord that morphs between voicings
///   Arp   — pentatonic arpeggio with per-theme patterns
///   Bass  — sub-octave sine following the chord root
///   Atmo  — filtered stereo noise for texture
///   Reverb — ping-pong delay for spatial depth
/// </summary>
public sealed class MusicGenerator
{
    private readonly int _sr;      // sample rate
    private readonly float _dt;    // 1 / sample rate

    public MusicTheme CurrentTheme { get; private set; } = MusicTheme.None;

    // ── Oscillator phases (never reset — ensures continuity) ──
    private double _droneP1, _droneP2, _droneSub;
    private double _padP1, _padP2, _padP3;
    private double _arpP;
    private double _bassP;
    private double _lfo1P, _lfo2P;

    // ── Musical sequencer state ──
    private double _beatAccum;
    private int _chordIdx;
    private int _arpIdx;
    private float _arpEnv;        // 1 → 0 per note

    // ── Chord portamento ──
    private float _chordBlend;    // 0 → 1 over crossfade window
    private float _prevF1, _prevF2, _prevF3, _prevBass;
    private float _tgtF1, _tgtF2, _tgtF3, _tgtBass;

    // ── Atmosphere filter state (two channels for stereo width) ──
    private float _lpfL, _lpfR;

    // ── Reverb delay lines ──
    private readonly float[] _dlyL, _dlyR;
    private int _dlyW;
    private const int DlyLenL = 21_527;   // ≈ 488 ms  (prime — less resonance)
    private const int DlyLenR = 26_861;   // ≈ 609 ms  (different prime)
    private const int DlySize = DlyLenR + 1;

    // ── Noise PRNG (deterministic, avoids locking Random.Shared) ──
    private uint _noiseSeed = 0xDEAD_BEEF;

    // ── Scale & harmony ──
    private static readonly int[] Pent = [0, 3, 5, 7, 10]; // pentatonic minor intervals (semitones)

    /// Chord voicings — each entry is three scale-degree indices.
    private static readonly int[][] Chords =
    [
        [0, 2, 4],      // root  + P4  + m7
        [1, 3, 5],      // bIII  + P5  + root′
        [2, 4, 6],      // P4   + m7  + bIII′
        [0, 3, 5],      // root  + P5  + root′
    ];

    // Arp patterns (scale degrees; offset +5 = one octave above root)
    private static readonly int[] ArpDefault = [5, 6, 7, 8, 9, 8, 7, 6];
    private static readonly int[] ArpCombat = [5, 5, 7, 8, 5, 5, 8, 9];
    private static readonly int[] ArpFtl = [5, 6, 7, 8, 9, 10, 11, 12];

    public MusicGenerator(int sampleRate)
    {
        _sr = sampleRate;
        _dt = 1f / sampleRate;
        _dlyL = new float[DlySize];
        _dlyR = new float[DlySize];
    }

    /// <summary>Switch to a new theme. Resets beat/chord state but keeps phases for smooth timbre.</summary>
    public void SetTheme(MusicTheme theme)
    {
        CurrentTheme = theme;
        _chordIdx = 0;
        _arpIdx = 0;
        _arpEnv = 0f;
        _beatAccum = 0;
        _chordBlend = 1f;
        SnapChordFreqs(Params.Root);
    }

    // ──────────────────────────────────────────────────────────────
    // Per-theme tuning
    // ──────────────────────────────────────────────────────────────

    private readonly record struct ThemeParams(
        float Root, float Bpm,
        float Drone, float Pad, float Arp, float Bass, float Atmo,
        float Reverb, int[] ArpPattern);

    private ThemeParams Params => CurrentTheme switch
    {
        MusicTheme.MainMenu => new(110.00f, 50, .15f, .12f, .05f, .08f, .04f, .50f, ArpDefault),
        MusicTheme.SolarSystem => new(130.81f, 65, .12f, .10f, .08f, .10f, .06f, .40f, ArpDefault),
        MusicTheme.PlanetSurface => new(164.81f, 72, .08f, .12f, .06f, .12f, .03f, .30f, ArpDefault),
        MusicTheme.Interior => new(196.00f, 55, .10f, .08f, .03f, .06f, .05f, .35f, ArpDefault),
        MusicTheme.FTL => new(146.83f, 130, .18f, .06f, .15f, .14f, .08f, .50f, ArpFtl),
        MusicTheme.Combat => new(110.00f, 110, .14f, .06f, .12f, .16f, .05f, .30f, ArpCombat),
        _ => new(130.81f, 60, 0, 0, 0, 0, 0, 0, ArpDefault),
    };

    // ──────────────────────────────────────────────────────────────
    // Main generation entry point
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates <paramref name="frames"/> stereo frames, **adding** into <paramref name="buffer"/>
    /// (interleaved L/R float samples). Caller must clear the buffer before if a clean mix is desired.
    /// </summary>
    public void Generate(float[] buffer, int frames)
    {
        if (CurrentTheme == MusicTheme.None) return;

        var p = Params;
        float bps = p.Bpm / 60f;
        float chordBeats = CurrentTheme is MusicTheme.FTL or MusicTheme.Combat ? 4f : 8f;
        float arpBeats = CurrentTheme is MusicTheme.FTL ? 0.25f
                       : CurrentTheme is MusicTheme.Combat ? 0.5f
                       : 1f;
        float arpDecay = 6f / (arpBeats / bps); // envelope rate

        for (int i = 0; i < frames; i++)
        {
            // ── Sequencer ────────────────────────────────────
            _beatAccum += bps * _dt;
            if (_beatAccum >= chordBeats)
            {
                _beatAccum -= chordBeats;
                _chordIdx = (_chordIdx + 1) % Chords.Length;
                StartChordGlide(p.Root);
            }

            _chordBlend = MathF.Min(_chordBlend + _dt * 0.5f, 1f); // ≈ 2 s glide

            _arpEnv -= arpDecay * _dt;
            if (_arpEnv <= 0f)
            {
                _arpIdx = (_arpIdx + 1) % p.ArpPattern.Length;
                _arpEnv = 1f;
            }

            // ── LFOs ─────────────────────────────────────────
            _lfo1P += 0.2 * _dt;
            _lfo2P += 0.13 * _dt;
            float lfo1 = MathF.Sin((float)(_lfo1P * MathF.Tau));
            float lfo2 = MathF.Sin((float)(_lfo2P * MathF.Tau));

            // ── Interpolated chord frequencies ───────────────
            float pF1 = Lerp(_prevF1, _tgtF1, _chordBlend);
            float pF2 = Lerp(_prevF2, _tgtF2, _chordBlend);
            float pF3 = Lerp(_prevF3, _tgtF3, _chordBlend);
            float bF = Lerp(_prevBass, _tgtBass, _chordBlend);

            float left = 0f, right = 0f;

            // ═══════════════════════════════════════════════
            //  DRONE — two detuned sines + sub octave
            // ═══════════════════════════════════════════════
            if (p.Drone > 0f)
            {
                _droneP1 += p.Root * _dt;
                _droneP2 += p.Root * 1.003 * _dt;
                _droneSub += p.Root * 0.5 * _dt;
                float d = Sine(_droneP1) * 0.50f
                        + Sine(_droneP2) * 0.30f
                        + Sine(_droneSub) * 0.20f;
                d *= 1f + lfo1 * 0.3f;
                left += d * p.Drone;
                right += d * p.Drone;
            }

            // ═══════════════════════════════════════════════
            //  PAD — 3-note chord with stereo spread
            // ═══════════════════════════════════════════════
            if (p.Pad > 0f)
            {
                _padP1 += pF1 * _dt;
                _padP2 += pF2 * _dt;
                _padP3 += pF3 * _dt;
                float s1 = Sine(_padP1);
                float s2 = Sine(_padP2);
                float s3 = Sine(_padP3);
                float amp = 1f + lfo2 * 0.2f;
                left += (s1 * 0.40f + s2 * 0.45f + s3 * 0.15f) * amp * p.Pad;
                right += (s1 * 0.40f + s2 * 0.15f + s3 * 0.45f) * amp * p.Pad;
            }

            // ═══════════════════════════════════════════════
            //  ARPEGGIO — single triangle oscillator
            // ═══════════════════════════════════════════════
            if (p.Arp > 0f)
            {
                float aFreq = ScaleFreq(p.Root, p.ArpPattern[_arpIdx]);
                _arpP += aFreq * _dt;
                float env = MathF.Max(0f, _arpEnv);
                env *= env; // quadratic shape
                float a = Triangle(_arpP) * env;
                float pan = (_arpIdx % 2 == 0) ? 0.15f : -0.15f;
                left += a * p.Arp * (1f - pan);
                right += a * p.Arp * (1f + pan);
            }

            // ═══════════════════════════════════════════════
            //  BASS — sine at chord-root / 2
            // ═══════════════════════════════════════════════
            if (p.Bass > 0f)
            {
                _bassP += bF * _dt;
                float b = Sine(_bassP);
                left += b * p.Bass;
                right += b * p.Bass;
            }

            // ═══════════════════════════════════════════════
            //  ATMOSPHERE — stereo filtered noise
            // ═══════════════════════════════════════════════
            if (p.Atmo > 0f)
            {
                float cutoff = 400f + lfo1 * 250f;
                float rc = 1f / (MathF.Tau * cutoff);
                float alpha = _dt / (rc + _dt);
                _lpfL += alpha * (Noise() - _lpfL);
                _lpfR += alpha * (Noise() - _lpfR);
                left += _lpfL * p.Atmo;
                right += _lpfR * p.Atmo;
            }

            // ═══════════════════════════════════════════════
            //  REVERB — ping-pong delay
            // ═══════════════════════════════════════════════
            if (p.Reverb > 0f)
            {
                int rL = (_dlyW - DlyLenL + DlySize) % DlySize;
                int rR = (_dlyW - DlyLenR + DlySize) % DlySize;
                float dL = _dlyL[rL];
                float dR = _dlyR[rR];
                left += dR * p.Reverb;   // cross-feed
                right += dL * p.Reverb;
                _dlyL[_dlyW] = left * 0.35f;
                _dlyR[_dlyW] = right * 0.35f;
                _dlyW = (_dlyW + 1) % DlySize;
            }

            buffer[i * 2] += left;
            buffer[i * 2 + 1] += right;

            // Wrap phase accumulators to prevent floating-point precision loss over long sessions
            _droneP1 %= 1.0; _droneP2 %= 1.0; _droneSub %= 1.0;
            _padP1 %= 1.0; _padP2 %= 1.0; _padP3 %= 1.0;
            _arpP %= 1.0;
            _bassP %= 1.0;
            _lfo1P %= 1.0; _lfo2P %= 1.0;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Chord helpers
    // ──────────────────────────────────────────────────────────────

    private void SnapChordFreqs(float root)
    {
        var c = Chords[_chordIdx];
        _prevF1 = _tgtF1 = ScaleFreq(root, c[0]);
        _prevF2 = _tgtF2 = ScaleFreq(root, c[1]);
        _prevF3 = _tgtF3 = ScaleFreq(root, c[2]);
        _prevBass = _tgtBass = ScaleFreq(root, c[0]) * 0.5f;
    }

    private void StartChordGlide(float root)
    {
        _prevF1 = _tgtF1; _prevF2 = _tgtF2; _prevF3 = _tgtF3;
        _prevBass = _tgtBass;

        var c = Chords[_chordIdx];
        _tgtF1 = ScaleFreq(root, c[0]);
        _tgtF2 = ScaleFreq(root, c[1]);
        _tgtF3 = ScaleFreq(root, c[2]);
        _tgtBass = ScaleFreq(root, c[0]) * 0.5f;
        _chordBlend = 0f;
    }

    // ──────────────────────────────────────────────────────────────
    // Oscillator & math primitives
    // ──────────────────────────────────────────────────────────────

    private static float Sine(double phase) =>
        MathF.Sin((float)(phase * MathF.Tau));

    private static float Triangle(double phase)
    {
        double p = phase % 1.0;
        if (p < 0) p += 1.0;
        return (float)(4.0 * Math.Abs(p - 0.5) - 1.0);
    }

    /// <summary>Convert a scale-degree index to Hz. Degree 0–4 = first octave, 5–9 = second, etc.</summary>
    private static float ScaleFreq(float rootHz, int degree)
    {
        int oct = Math.DivRem(degree, Pent.Length, out int rem);
        if (rem < 0) { rem += Pent.Length; oct--; }
        int semitones = Pent[rem] + 12 * oct;
        return rootHz * MathF.Pow(2f, semitones / 12f);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Fast inline xorshift noise (–1 … +1).</summary>
    private float Noise()
    {
        _noiseSeed ^= _noiseSeed << 13;
        _noiseSeed ^= _noiseSeed >> 17;
        _noiseSeed ^= _noiseSeed << 5;
        return (_noiseSeed / (float)uint.MaxValue) * 2f - 1f;
    }
}
