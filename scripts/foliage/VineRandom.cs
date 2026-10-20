namespace DillControl.Foliage;

/// <summary>
/// The vine generator's random numbers: mulberry32 seeded from a 32-bit FNV-1a
/// hash of a stream's name, bit for bit what <c>tools/vine-gen</c> uses, so a
/// seed grows the same vine here as in the browser. Bake-time only; the game
/// never rolls a vine. Everything else rolls through
/// <see cref="Core.SeededRandom"/> (docs/decisions/0033).
/// </summary>
public sealed class VineRandom
{
    private uint _a;

    public VineRandom(uint seed) => _a = seed;

    /// <summary>A named stream: a tweak to one stage cannot reshuffle another.</summary>
    public static VineRandom Stream(int seed, string name) => new((uint)seed ^ HashName(name));

    public static uint HashName(string name)
    {
        uint h = 2166136261;
        foreach (char c in name)
        {
            h ^= c;
            h *= 16777619;
        }
        return h;
    }

    /// <summary>The seed of a kind's pool vine from an archetype's seed, as
    /// <c>kinds.js poolSeed</c> makes it.</summary>
    public static int PoolSeed(int seed, string kindName, int index) =>
        (int)((((uint)seed ^ HashName(kindName + "#" + index)) * 2654435761u) % 10000000u);

    /// <summary>[0, 1), a double, as JavaScript divides it.</summary>
    public double Next()
    {
        unchecked
        {
            _a += 0x6D2B79F5;
            uint t = (_a ^ (_a >> 15)) * (1 | _a);
            t = (t + ((t ^ (t >> 7)) * (61 | t))) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
}
