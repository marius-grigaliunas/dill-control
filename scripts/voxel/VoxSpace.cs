using Godot;

namespace DillControl.Voxel;

/// <summary>
/// A MagicaVoxel rotation: a 3x3 matrix whose rows each hold one +1 or -1, so
/// it only permutes and flips axes. Stored in a .vox as one byte.
/// </summary>
public readonly struct VoxRotation
{
    /// <summary>Row-major, applied to column vectors.</summary>
    private readonly int _m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22;

    public static readonly VoxRotation Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    private VoxRotation(int m00, int m01, int m02, int m10, int m11, int m12, int m20, int m21, int m22)
    {
        _m00 = m00; _m01 = m01; _m02 = m02;
        _m10 = m10; _m11 = m11; _m12 = m12;
        _m20 = m20; _m21 = m21; _m22 = m22;
    }

    /// <summary>Bits 0-1: the column of row 0's non-zero entry. Bits 2-3: row
    /// 1's. Row 2 takes the column left over. Bits 4, 5 and 6: rows 0, 1 and 2
    /// are negative.</summary>
    public static VoxRotation FromByte(byte r)
    {
        int c0 = r & 3, c1 = (r >> 2) & 3, c2 = 3 - c0 - c1;
        int s0 = (r & 16) != 0 ? -1 : 1, s1 = (r & 32) != 0 ? -1 : 1, s2 = (r & 64) != 0 ? -1 : 1;
        int[] m = new int[9];
        m[c0] = s0;
        m[3 + c1] = s1;
        m[6 + c2] = s2;
        return new VoxRotation(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
    }

    public Vector3 Apply(Vector3 v) => new(
        _m00 * v.X + _m01 * v.Y + _m02 * v.Z,
        _m10 * v.X + _m11 * v.Y + _m12 * v.Z,
        _m20 * v.X + _m21 * v.Y + _m22 * v.Z);
}

/// <summary>
/// MagicaVoxel space to Godot space. MagicaVoxel is Z-up; its front view looks
/// along +Y at the model's -Y face. Godot is Y-up. The map is a quarter turn
/// about X, (x, y, z) to (x, z, -y): a proper rotation, so nothing is
/// mirrored, and a model's -Y front arrives facing +Z, Godot's
/// <c>Vector3.ModelFront</c>.
/// </summary>
public static class VoxSpace
{
    public static Vector3 ToGodot(Vector3 v) => new(v.X, v.Z, -v.Y);

    public static Vector3 FromGodot(Vector3 v) => new(v.X, -v.Z, v.Y);

    public static Basis ToGodot(VoxRotation r) => new(
        ToGodot(r.Apply(FromGodot(Vector3.Right))),
        ToGodot(r.Apply(FromGodot(Vector3.Up))),
        ToGodot(r.Apply(FromGodot(Vector3.Back))));

    /// <summary>The point of a model that its node's translation places:
    /// MagicaVoxel centres a model on <c>floor(size / 2)</c>, in voxel corners.</summary>
    public static Vector3 Centre(Vector3I size) => new(size.X / 2, size.Y / 2, size.Z / 2);
}
