namespace SpaceExplorationGame.Audio;

/// <summary>Identifies a one-shot sound effect.</summary>
public enum SfxType
{
    LaserFire,
    EnemyLaser,
    Explosion,
    SmallExplosion,
    ShieldHit,
    HullDamage,
    MenuSelect,
    MenuNavigate,
    FtlCharge,
    FtlJump,
    PickupCredits,
    PickupItem,
    MiningHit,
    Landing,
    Takeoff,
}

/// <summary>
/// Procedurally generates all sound effect buffers at startup.
/// Each buffer is a mono float array at the given sample rate.
/// </summary>
public static class SfxGenerator
{
    public static Dictionary<SfxType, float[]> GenerateAll(int sr) => new()
    {
        [SfxType.LaserFire]      = LaserFire(sr),
        [SfxType.EnemyLaser]     = EnemyLaser(sr),
        [SfxType.Explosion]      = Explosion(sr),
        [SfxType.SmallExplosion] = SmallExplosion(sr),
        [SfxType.ShieldHit]      = ShieldHit(sr),
        [SfxType.HullDamage]     = HullDamage(sr),
        [SfxType.MenuSelect]     = MenuSelect(sr),
        [SfxType.MenuNavigate]   = MenuNavigate(sr),
        [SfxType.FtlCharge]      = FtlCharge(sr),
        [SfxType.FtlJump]        = FtlJump(sr),
        [SfxType.PickupCredits]  = PickupCredits(sr),
        [SfxType.PickupItem]     = PickupItem(sr),
        [SfxType.MiningHit]      = MiningHit(sr),
        [SfxType.Landing]        = LandingSound(sr),
        [SfxType.Takeoff]        = TakeoffSound(sr),
    };

    // ──────────────────────────────────────────────────────────────
    //  Weapon SFX
    // ──────────────────────────────────────────────────────────────

    /// <summary>Player weapon — descending sine sweep 800→200 Hz + slight square harmonics.</summary>
    private static float[] LaserFire(int sr)
    {
        int len = (int)(sr * 0.15f);
        var buf = new float[len];
        double phase = 0;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float p = (float)i / len;
            float freq = 800f - 600f * p;
            phase += freq / sr;
            float env = MathF.Exp(-t * 20f);
            buf[i] = (MathF.Sin((float)(phase * MathF.Tau)) * 0.7f
                     + Square(phase) * 0.15f) * env;
        }
        return buf;
    }

    /// <summary>Enemy weapon — ascending sweep 400→700 Hz, harsher timbre.</summary>
    private static float[] EnemyLaser(int sr)
    {
        int len = (int)(sr * 0.12f);
        var buf = new float[len];
        double phase = 0;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float p = (float)i / len;
            float freq = 400f + 300f * p;
            phase += freq / sr;
            float env = MathF.Exp(-t * 22f);
            buf[i] = (MathF.Sin((float)(phase * MathF.Tau)) * 0.5f
                     + Square(phase) * 0.25f) * env;
        }
        return buf;
    }

    // ──────────────────────────────────────────────────────────────
    //  Explosion SFX
    // ──────────────────────────────────────────────────────────────

    /// <summary>Large explosion — filtered noise burst + 60 Hz thump.</summary>
    private static float[] Explosion(int sr)
    {
        int len = (int)(sr * 0.6f);
        var buf = new float[len];
        double phase = 0;
        float lpf = 0;
        var rng = new Random(42);

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float env = MathF.Exp(-t * 4f);

            float noise = (float)(rng.NextDouble() * 2 - 1);
            float cutoff = 2000f * MathF.Exp(-t * 3f);
            float alpha = LpfAlpha(sr, cutoff);
            lpf += alpha * (noise - lpf);

            phase += 60.0 / sr;
            float thump = MathF.Sin((float)(phase * MathF.Tau)) * MathF.Exp(-t * 8f);

            buf[i] = (lpf * 0.6f + thump * 0.4f) * env;
        }
        return buf;
    }

    /// <summary>Small explosion (asteroid / rock shatter).</summary>
    private static float[] SmallExplosion(int sr)
    {
        int len = (int)(sr * 0.3f);
        var buf = new float[len];
        float lpf = 0;
        var rng = new Random(77);

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float env = MathF.Exp(-t * 8f);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float alpha = LpfAlpha(sr, 3000f * MathF.Exp(-t * 5f));
            lpf += alpha * (noise - lpf);
            buf[i] = lpf * env * 0.7f;
        }
        return buf;
    }

    // ──────────────────────────────────────────────────────────────
    //  Impact SFX
    // ──────────────────────────────────────────────────────────────

    /// <summary>Shield absorbs damage — metallic ring at 1500 Hz.</summary>
    private static float[] ShieldHit(int sr)
    {
        int len = (int)(sr * 0.25f);
        var buf = new float[len];
        double phase = 0;
        var rng = new Random(33);

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float env = MathF.Exp(-t * 12f);
            phase += 1500.0 / sr;
            float ring = MathF.Sin((float)(phase * MathF.Tau));
            float noise = (float)(rng.NextDouble() * 2 - 1) * 0.15f;
            buf[i] = (ring * 0.7f + noise) * env * 0.6f;
        }
        return buf;
    }

    /// <summary>Hull takes damage — low thump at 100 Hz + noise.</summary>
    private static float[] HullDamage(int sr)
    {
        int len = (int)(sr * 0.15f);
        var buf = new float[len];
        double phase = 0;
        var rng = new Random(55);

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float env = MathF.Exp(-t * 18f);
            phase += 100.0 / sr;
            float thump = MathF.Sin((float)(phase * MathF.Tau));
            float noise = (float)(rng.NextDouble() * 2 - 1) * 0.3f;
            buf[i] = (thump * 0.6f + noise) * env * 0.8f;
        }
        return buf;
    }

    // ──────────────────────────────────────────────────────────────
    //  UI SFX
    // ──────────────────────────────────────────────────────────────

    /// <summary>Menu confirm — two-tone ascending beep (660 → 880 Hz).</summary>
    private static float[] MenuSelect(int sr)
    {
        int len = (int)(sr * 0.12f);
        var buf = new float[len];
        double phase = 0;

        for (int i = 0; i < len; i++)
        {
            float p = (float)i / len;
            float freq = p < 0.5f ? 660f : 880f;
            phase += freq / sr;
            float env = 1f - p;
            env *= env;
            buf[i] = MathF.Sin((float)(phase * MathF.Tau)) * env * 0.4f;
        }
        return buf;
    }

    /// <summary>Menu navigate — soft blip at 440 Hz.</summary>
    private static float[] MenuNavigate(int sr)
    {
        int len = (int)(sr * 0.06f);
        var buf = new float[len];
        double phase = 0;

        for (int i = 0; i < len; i++)
        {
            float p = (float)i / len;
            phase += 440.0 / sr;
            float env = 1f - p;
            buf[i] = MathF.Sin((float)(phase * MathF.Tau)) * env * 0.25f;
        }
        return buf;
    }

    // ──────────────────────────────────────────────────────────────
    //  FTL SFX
    // ──────────────────────────────────────────────────────────────

    /// <summary>FTL charge-up — rising sine sweep 60→1560 Hz over 2 s + building noise.</summary>
    private static float[] FtlCharge(int sr)
    {
        int len = (int)(sr * 2.0f);
        var buf = new float[len];
        double phase = 0;
        float lpf = 0;
        var rng = new Random(99);

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float p = (float)i / len;

            float freq = 60f + 1500f * p * p;
            phase += freq / sr;
            float sine = MathF.Sin((float)(phase * MathF.Tau));

            float noise = (float)(rng.NextDouble() * 2 - 1) * p;
            float alpha = LpfAlpha(sr, 200f + 2000f * p);
            lpf += alpha * (noise - lpf);

            float env = p * p;
            buf[i] = (sine * 0.5f + lpf * 0.3f) * env * 0.6f;
        }
        return buf;
    }

    /// <summary>FTL jump flash — descending burst + noise.</summary>
    private static float[] FtlJump(int sr)
    {
        int len = (int)(sr * 0.5f);
        var buf = new float[len];
        double phase = 0;
        var rng = new Random(88);

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float p = (float)i / len;
            float env = MathF.Exp(-t * 4f);

            float freq = 2000f - 1800f * p;
            phase += freq / sr;
            float sine = MathF.Sin((float)(phase * MathF.Tau));
            float noise = (float)(rng.NextDouble() * 2 - 1);

            buf[i] = (sine * 0.3f + noise * 0.5f) * env * 0.8f;
        }
        return buf;
    }

    // ──────────────────────────────────────────────────────────────
    //  Pickup SFX
    // ──────────────────────────────────────────────────────────────

    /// <summary>Credits collected — ascending two-tone ding (880 → 1320 Hz).</summary>
    private static float[] PickupCredits(int sr)
    {
        int len = (int)(sr * 0.2f);
        var buf = new float[len];
        double phase = 0;

        for (int i = 0; i < len; i++)
        {
            float p = (float)i / len;
            float freq = p < 0.4f ? 880f : 1320f;
            phase += freq / sr;
            float env = (1f - p) * MathF.Sin(p * MathF.PI);
            buf[i] = MathF.Sin((float)(phase * MathF.Tau)) * env * 0.35f;
        }
        return buf;
    }

    /// <summary>Item / resource pickup — clean tone at 1047 Hz (C5).</summary>
    private static float[] PickupItem(int sr)
    {
        int len = (int)(sr * 0.15f);
        var buf = new float[len];
        double phase = 0;

        for (int i = 0; i < len; i++)
        {
            float p = (float)i / len;
            phase += 1047.0 / sr;
            float env = 1f - p;
            env *= env;
            buf[i] = MathF.Sin((float)(phase * MathF.Tau)) * env * 0.35f;
        }
        return buf;
    }

    // ──────────────────────────────────────────────────────────────
    //  Mining SFX
    // ──────────────────────────────────────────────────────────────

    /// <summary>Mining projectile hits rock — short noise + thump.</summary>
    private static float[] MiningHit(int sr)
    {
        int len = (int)(sr * 0.1f);
        var buf = new float[len];
        double phase = 0;
        var rng = new Random(44);

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float env = MathF.Exp(-t * 25f);
            phase += 150.0 / sr;
            float thump = MathF.Sin((float)(phase * MathF.Tau));
            float noise = (float)(rng.NextDouble() * 2 - 1);
            buf[i] = (thump * 0.4f + noise * 0.4f) * env * 0.6f;
        }
        return buf;
    }

    // ──────────────────────────────────────────────────────────────
    //  Ship SFX
    // ──────────────────────────────────────────────────────────────

    /// <summary>Ship landing — descending engine rumble with low thump at end.</summary>
    private static float[] LandingSound(int sr)
    {
        int len = (int)(sr * 0.8f);
        var buf = new float[len];
        double phase = 0;
        float lpf = 0;
        var rng = new Random(66);

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float p = (float)i / len;

            float noise = (float)(rng.NextDouble() * 2 - 1);
            float alpha = LpfAlpha(sr, 800f * (1f - p * 0.7f));
            lpf += alpha * (noise - lpf);

            phase += (80.0 - 30.0 * p) / sr;
            float engine = MathF.Sin((float)(phase * MathF.Tau));

            float env = 1f - p * 0.6f;
            buf[i] = (lpf * 0.4f + engine * 0.3f) * env * 0.5f;
        }
        return buf;
    }

    /// <summary>Ship takeoff — ascending engine rumble.</summary>
    private static float[] TakeoffSound(int sr)
    {
        int len = (int)(sr * 1.0f);
        var buf = new float[len];
        double phase = 0;
        float lpf = 0;
        var rng = new Random(77);

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float p = (float)i / len;

            float noise = (float)(rng.NextDouble() * 2 - 1);
            float alpha = LpfAlpha(sr, 400f + 800f * p);
            lpf += alpha * (noise - lpf);

            phase += (50.0 + 80.0 * p) / sr;
            float engine = MathF.Sin((float)(phase * MathF.Tau));

            // Fade in, quick fade out at the end
            float env = p < 0.8f ? p / 0.8f : (1f - p) / 0.2f;
            buf[i] = (lpf * 0.5f + engine * 0.3f) * env * 0.5f;
        }
        return buf;
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────

    private static float Square(double phase)
    {
        double p = phase % 1.0;
        if (p < 0) p += 1.0;
        return p < 0.5 ? 0.5f : -0.5f;
    }

    /// <summary>Single-pole low-pass filter coefficient.</summary>
    private static float LpfAlpha(int sr, float cutoffHz)
    {
        float rc = 1f / (MathF.Tau * cutoffHz);
        return (1f / sr) / (rc + 1f / sr);
    }
}
