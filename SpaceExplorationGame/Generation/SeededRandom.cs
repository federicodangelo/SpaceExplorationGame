namespace SpaceExplorationGame.Generation;

/// <summary>
/// Deterministic random number generator based on a seed.
/// Same seed always produces the same sequence.
/// </summary>
public class SeededRandom
{
    private ulong _state;

    public ulong Seed { get; }

    public SeededRandom(ulong seed)
    {
        Seed = seed;
        _state = seed;
    }

    /// <summary>xorshift64 - fast deterministic PRNG</summary>
    private ulong Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 7;
        _state ^= _state << 17;
        return _state;
    }

    /// <summary>Returns a random int in [min, max) range.</summary>
    public int NextInt(int min, int max)
    {
        if (min >= max) return min;
        ulong range = (ulong)(max - min);
        return (int)(Next() % range) + min;
    }

    /// <summary>Returns a random int in [0, max) range.</summary>
    public int NextInt(int max) => NextInt(0, max);

    /// <summary>Returns a random float in [0, 1).</summary>
    public float NextFloat()
    {
        return (Next() & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Returns a random float in [min, max).</summary>
    public float NextFloat(float min, float max)
    {
        return min + NextFloat() * (max - min);
    }

    /// <summary>Returns a random double in [0, 1).</summary>
    public double NextDouble()
    {
        return (Next() & 0xFFFFFFFFFFFFF) / (double)0x10000000000000;
    }

    /// <summary>Returns true with the given probability (0.0 to 1.0).</summary>
    public bool NextBool(float probability = 0.5f)
    {
        return NextFloat() < probability;
    }

    /// <summary>Derive a child seed from the current state + an index. Deterministic.</summary>
    public ulong DeriveChildSeed(int index)
    {
        // Use a hash-like combination of current seed and index
        ulong combined = Seed ^ ((ulong)index * 6364136223846793005UL + 1442695040888963407UL);
        combined ^= combined << 13;
        combined ^= combined >> 7;
        combined ^= combined << 17;
        return combined;
    }

    /// <summary>Pick a random item from a list.</summary>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        return items[NextInt(items.Count)];
    }

    /// <summary>Gaussian (normal) distribution using Box-Muller transform.</summary>
    public float NextGaussian(float mean = 0, float stdDev = 1)
    {
        float u1 = NextFloat();
        float u2 = NextFloat();
        if (u1 < 1e-10f) u1 = 1e-10f;
        float z = MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
        return mean + z * stdDev;
    }
}
