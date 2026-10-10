using Godot;

namespace DillControl.Planet;

/// <summary>
/// Gradient noise evaluated in C#, sampled on the 3D sphere direction.
///
/// Deliberately not FastNoiseLite. A planet is millions of samples, and every
/// FastNoiseLite call crosses the Godot interop boundary at roughly a
/// microsecond. Sampling the 3D direction rather than face coordinates makes it
/// seamless across every face edge and cube corner for free.
///
/// Ported from the lab. The lab's doc called it ridged; its code was plain
/// fractal. Both are here now, and a planet mixes them by
/// <see cref="PlanetSpec.RidgeShare"/>.
/// </summary>
public sealed class PlanetNoise
{
    private const int PermutationSize = 512;
    private readonly int[] _permutation = new int[PermutationSize];

    public PlanetNoise(int seed)
    {
        int[] source = new int[256];
        for (int i = 0; i < 256; i++)
        {
            source[i] = i;
        }

        // Deterministic shuffle, so the same seed always builds the same world.
        uint state = (uint)seed | 1u;
        for (int i = 255; i > 0; i--)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            int swap = (int)(state % (uint)(i + 1));
            (source[i], source[swap]) = (source[swap], source[i]);
        }

        for (int i = 0; i < PermutationSize; i++)
        {
            _permutation[i] = source[i & 255];
        }
    }

    /// <summary>Octave-summed gradient noise in roughly [-1, 1].</summary>
    public float Fractal(Vector3 p, float frequency, int octaves, float lacunarity, float gain)
    {
        float sum = 0.0f;
        float amplitude = 1.0f;
        float total = 0.0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            sum += Gradient(p * frequency) * amplitude;
            total += amplitude;
            frequency *= lacunarity;
            amplitude *= gain;
        }

        return total > 0.0f ? sum / total : 0.0f;
    }

    /// <summary>
    /// Ridged noise in roughly [-1, 1]: sharp crests where the gradient noise
    /// crosses zero, each octave weighted by the one before so ridges carry
    /// detail and valleys stay smooth. Gives the flow field slopes worth
    /// routing round, and the grapple something tall to hook.
    /// </summary>
    public float Ridged(Vector3 p, float frequency, int octaves, float lacunarity, float gain)
    {
        float sum = 0.0f;
        float amplitude = 1.0f;
        float total = 0.0f;
        float weight = 1.0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            float ridge = 1.0f - Mathf.Abs(Gradient(p * frequency));
            ridge *= ridge;
            ridge *= weight;
            weight = Mathf.Clamp(ridge * 2.0f, 0.0f, 1.0f);

            sum += ridge * amplitude;
            total += amplitude;
            frequency *= lacunarity;
            amplitude *= gain;
        }

        return total > 0.0f ? (sum / total * 2.0f) - 1.0f : 0.0f;
    }

    private float Gradient(Vector3 p)
    {
        int xi = FastFloor(p.X);
        int yi = FastFloor(p.Y);
        int zi = FastFloor(p.Z);

        float xf = p.X - xi;
        float yf = p.Y - yi;
        float zf = p.Z - zi;

        float u = Fade(xf);
        float v = Fade(yf);
        float w = Fade(zf);

        int x0 = xi & 255;
        int y0 = yi & 255;
        int z0 = zi & 255;
        int x1 = (x0 + 1) & 255;

        int a = _permutation[x0] + y0;
        int aa = _permutation[a & 255] + z0;
        int ab = _permutation[(a + 1) & 255] + z0;
        int b = _permutation[x1] + y0;
        int ba = _permutation[b & 255] + z0;
        int bb = _permutation[(b + 1) & 255] + z0;

        float x00 = Mathf.Lerp(Dot(_permutation[aa & 255], xf, yf, zf), Dot(_permutation[ba & 255], xf - 1, yf, zf), u);
        float x10 = Mathf.Lerp(Dot(_permutation[ab & 255], xf, yf - 1, zf), Dot(_permutation[bb & 255], xf - 1, yf - 1, zf), u);
        float x01 = Mathf.Lerp(Dot(_permutation[(aa + 1) & 255], xf, yf, zf - 1), Dot(_permutation[(ba + 1) & 255], xf - 1, yf, zf - 1), u);
        float x11 = Mathf.Lerp(Dot(_permutation[(ab + 1) & 255], xf, yf - 1, zf - 1), Dot(_permutation[(bb + 1) & 255], xf - 1, yf - 1, zf - 1), u);

        return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
    }

    private static int FastFloor(float value) => value >= 0.0f ? (int)value : (int)value - 1;

    private static float Fade(float t) => t * t * t * ((t * ((t * 6.0f) - 15.0f)) + 10.0f);

    private static float Dot(int hash, float x, float y, float z)
    {
        switch (hash & 15)
        {
            case 0: return x + y;
            case 1: return -x + y;
            case 2: return x - y;
            case 3: return -x - y;
            case 4: return x + z;
            case 5: return -x + z;
            case 6: return x - z;
            case 7: return -x - z;
            case 8: return y + z;
            case 9: return -y + z;
            case 10: return y - z;
            case 11: return -y - z;
            case 12: return x + y;
            case 13: return -y + z;
            case 14: return -x + y;
            default: return -y - z;
        }
    }
}
