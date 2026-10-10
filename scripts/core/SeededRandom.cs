using Godot;

namespace DillControl.Core;

/// <summary>
/// A deterministic random source for anything generated from a contract's
/// seed: the same seed gives the same planet, biome and horde on every
/// machine and every run. SplitMix64, which is small, fast, and has no state
/// worth worrying about beyond one integer.
///
/// Never <c>GD.Randi</c> or <c>System.Random</c> for generation: the first is
/// global and shared with everything else that rolls, the second is not
/// guaranteed the same across .NET versions.
///
/// Each stage of generation takes its own stream, <see cref="Stream"/>, named
/// by a string, so adding a roll to one stage never reshuffles another.
/// </summary>
public sealed class SeededRandom
{
    private ulong _state;

    public SeededRandom(ulong seed)
    {
        _state = seed;
    }

    /// <summary>A generator for one named stage of a seed: "terrain", "biome", "horde".</summary>
    public static SeededRandom Stream(ulong seed, string stage) => new(Hash(seed, stage));

    /// <summary>A seed and a stage name mixed into one well-spread 64-bit value.</summary>
    public static ulong Hash(ulong seed, string stage)
    {
        // FNV-1a over the name, then folded into the seed and finished with
        // SplitMix's mixer so neighbouring seeds land far apart.
        ulong hash = 14695981039346656037UL;
        foreach (char c in stage)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return Mix(seed ^ hash);
    }

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float NextFloat() => (NextULong() >> 40) / (float)(1UL << 24);

    /// <summary>Uniform in [min, max].</summary>
    public float Range(float min, float max) => min + ((max - min) * NextFloat());

    /// <summary>Uniform over min..max, both included.</summary>
    public int RangeInt(int min, int max) =>
        max <= min ? min : min + (int)(NextULong() % (ulong)(max - min + 1));

    /// <summary>A 31-bit seed for code that takes an int (the noise).</summary>
    public int NextSeed() => (int)(NextULong() >> 33);

    /// <summary>An index picked by weight; -1 when every weight is zero or there are none.</summary>
    public int PickWeighted(float[] weights)
    {
        float total = 0.0f;
        foreach (float w in weights)
        {
            total += Mathf.Max(w, 0.0f);
        }
        if (total <= 0.0f)
        {
            return -1;
        }

        float roll = NextFloat() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= Mathf.Max(weights[i], 0.0f);
            if (roll < 0.0f)
            {
                return i;
            }
        }
        return weights.Length - 1;
    }

    /// <summary>A unit vector uniform over the sphere.</summary>
    public Vector3 OnSphere()
    {
        float z = Range(-1.0f, 1.0f);
        float angle = Range(0.0f, Mathf.Tau);
        float r = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - (z * z)));
        return new Vector3(r * Mathf.Cos(angle), r * Mathf.Sin(angle), z);
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
