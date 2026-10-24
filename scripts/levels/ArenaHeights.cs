using Godot;

namespace DillControl.Levels;

/// <summary>
/// An arena's ground as pure data: a square grid of heights, sampled
/// bilinearly. Copied from a <see cref="SculptedTerrain"/>'s heights, read from the
/// horde's parallel passes through <see cref="ArenaGround"/>, so nothing here
/// touches a node or a resource.
///
/// World space, up is +Y: the arena is not on the planet (decision 0026).
/// </summary>
public sealed class ArenaHeights
{
    public ArenaHeights(Vector3 centre, float size, float spacing)
    {
        Spacing = Mathf.Max(spacing, 0.5f);
        Count = Mathf.Max(2, Mathf.CeilToInt(size / Spacing)) + 1;
        Size = (Count - 1) * Spacing;
        Min = new Vector2(centre.X - (Size * 0.5f), centre.Z - (Size * 0.5f));
        Base = centre.Y;
        Heights = new float[Count * Count];
    }

    /// <summary>Samples along one side.</summary>
    public int Count { get; }

    /// <summary>Metres between samples.</summary>
    public float Spacing { get; }

    /// <summary>Metres along one side.</summary>
    public float Size { get; }

    /// <summary>The corner at the lowest X and Z.</summary>
    public Vector2 Min { get; }

    /// <summary>World Y of a height of zero: the terrain node's own.</summary>
    public float Base { get; }

    /// <summary>Heights over <see cref="Base"/>, row by row along X: index (z * Count) + x.</summary>
    public float[] Heights { get; }

    public float Lowest { get; private set; }

    public float Highest { get; private set; }

    public Vector3 SamplePoint(int x, int z) =>
        new(Min.X + (x * Spacing), Base + Heights[(z * Count) + x], Min.Y + (z * Spacing));

    public bool Contains(float x, float z) =>
        x >= Min.X && z >= Min.Y && x <= Min.X + Size && z <= Min.Y + Size;

    /// <summary>World Y of the ground at a point; clamped to the edge outside.</summary>
    public float HeightAt(float x, float z)
    {
        float u = Mathf.Clamp((x - Min.X) / Spacing, 0.0f, Count - 1.001f);
        float v = Mathf.Clamp((z - Min.Y) / Spacing, 0.0f, Count - 1.001f);
        int iu = (int)u;
        int iv = (int)v;
        float fu = u - iu;
        float fv = v - iv;
        int i = (iv * Count) + iu;
        float a = Mathf.Lerp(Heights[i], Heights[i + 1], fu);
        float b = Mathf.Lerp(Heights[i + Count], Heights[i + Count + 1], fu);
        return Base + Mathf.Lerp(a, b, fv);
    }

    /// <summary>The ground's normal, from differences <paramref name="step"/> metres apart.</summary>
    public Vector3 NormalAt(float x, float z, float step)
    {
        float dx = HeightAt(x + step, z) - HeightAt(x - step, z);
        float dz = HeightAt(x, z + step) - HeightAt(x, z - step);
        return new Vector3(-dx, 2.0f * step, -dz).Normalized();
    }

    /// <summary>Radians from level.</summary>
    public float SlopeAt(float x, float z, float step) => Mathf.Acos(Mathf.Clamp(NormalAt(x, z, step).Y, -1.0f, 1.0f));

    /// <summary>Refreshes <see cref="Lowest"/> and <see cref="Highest"/> after the heights are written.</summary>
    public void Measure()
    {
        float low = float.MaxValue;
        float high = float.MinValue;
        foreach (float h in Heights)
        {
            low = Mathf.Min(low, h);
            high = Mathf.Max(high, h);
        }
        Lowest = Base + low;
        Highest = Base + high;
    }
}
