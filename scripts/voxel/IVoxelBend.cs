using Godot;

namespace DillControl.Voxel;

/// <summary>
/// A voxel thing drawn bent away from where its voxels were built: a plant
/// (docs/decisions/0032). Its collision is moved with the bend, so a hit, a
/// blast or a tear lands where the thing is seen, and is taken back to where
/// its voxels are before it bites; a piece that breaks off starts where and
/// how it was drawn, moving as it was.
/// </summary>
public interface IVoxelBend
{
    /// <summary>A point where the thing is drawn, taken back to where its voxels are.</summary>
    Vector3 ToBuilt(Vector3 drawn);

    /// <summary>A point of its voxels, where it is drawn now.</summary>
    Vector3 ToDrawn(Vector3 built);

    /// <summary>How far the collision round a built point is moved now, in world space.</summary>
    Vector3 OffsetAt(Vector3 built);

    /// <summary>
    /// A piece is breaking off: its transform as built, and the speed and
    /// spin the split gave it, turned into the pose it was drawn in and the
    /// motion it had. <paramref name="bounds"/> is its solid box in its own
    /// space. <paramref name="angularDamp"/> is its own damping, or negative
    /// for the usual. A piece that gave way hangs on a hinge at the edge it
    /// goes over, <paramref name="hingePivot"/> (drawn) about
    /// <paramref name="hingeAxis"/>; a zero axis for none.
    /// </summary>
    void Release(Aabb bounds, ref Transform3D at, ref Vector3 velocity, ref Vector3? spin, out float angularDamp,
        out Vector3 hingePivot, out Vector3 hingeAxis);
}
