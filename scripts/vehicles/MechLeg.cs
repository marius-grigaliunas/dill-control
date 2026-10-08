using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// One leg of a <see cref="MechWalker"/>: three rigid voxel parts (thigh, shin,
/// foot) hinged at hip, knee and ankle, where the model's pivot voxels put
/// their origins. Holds what was measured from the rest pose and where the
/// foot is planted; the walker decides when it steps.
///
/// A plain class, not a node: it has no life of its own in the tree, and the
/// parts it moves belong to the imported model.
/// </summary>
public sealed class MechLeg
{
    public readonly Node3D Thigh;
    public readonly Node3D Shin;
    public readonly Node3D Foot;

    /// <summary>0 or 1. Legs in the same group step together, diagonally
    /// opposite pairs, which is a trot.</summary>
    public readonly int Group;

    // Rest pose, in the model's space.
    private readonly Transform3D _thighRest;
    private readonly Transform3D _shinRest;
    private readonly Transform3D _footRest;
    private readonly Vector3 _thighDir;
    private readonly Vector3 _shinDir;
    private readonly Vector3 _pole;
    private readonly float _upper;
    private readonly float _lower;

    /// <summary>Where the foot meets the ground at rest, in the walker's own
    /// space (origin at the feet).</summary>
    public readonly Vector3 RestFoot;

    /// <summary>Ankle pivot height above the sole.</summary>
    public readonly float AnkleHeight;

    // Gait state, world space.
    /// <summary>Where the sole was last posed, stepping or not.</summary>
    public Vector3 Sole;

    public Vector3 Planted;
    public bool Stepping;
    public float StepT;
    public Vector3 StepFrom;
    public Vector3 StepTo;

    public MechLeg(Node3D thigh, Node3D shin, Node3D foot, int group, Transform3D modelRest, Vector3 kneeBias)
    {
        Thigh = thigh;
        Shin = shin;
        Foot = foot;
        Group = group;

        _thighRest = thigh.Transform;
        _shinRest = shin.Transform;
        _footRest = foot.Transform;

        Vector3 hip = _thighRest.Origin, knee = _shinRest.Origin, ankle = _footRest.Origin;
        _upper = hip.DistanceTo(knee);
        _lower = knee.DistanceTo(ankle);
        _thighDir = (knee - hip).Normalized();
        _shinDir = (ankle - knee).Normalized();

        // The way the knee bends: its rest offset from the hip-ankle line. A
        // leg modelled straight bends outward.
        Vector3 axis = (ankle - hip).Normalized();
        Vector3 offset = (knee - hip) - axis * (knee - hip).Dot(axis) + kneeBias;
        offset -= axis * offset.Dot(axis);
        _pole = offset.LengthSquared() > 1e-6f
            ? offset.Normalized()
            : new Vector3(hip.X >= 0.0f ? 1.0f : -1.0f, 0.0f, 0.0f);

        Vector3 ankleInWalker = modelRest * ankle;
        AnkleHeight = ankleInWalker.Y;
        RestFoot = new Vector3(ankleInWalker.X, 0.0f, ankleInWalker.Z);
    }

    /// <summary>
    /// Poses the three parts so the sole sits at <paramref name="sole"/>:
    /// two-bone IK from the hip, bending toward the rest pole, then the foot
    /// kept level with the walker rather than the tilting body.
    /// </summary>
    /// <param name="model">The model's global transform this frame (bob and
    /// tilt included); the hip rides on it.</param>
    /// <param name="level">The walker's own basis, which the foot stays flat to.</param>
    /// <param name="toeLift">Radians the toe tips up mid-step.</param>
    public void Pose(Transform3D model, Basis level, Vector3 up, Vector3 sole, float toeLift)
    {
        Sole = sole;
        Vector3 hip = model * _thighRest.Origin;
        Vector3 target = sole + up * AnkleHeight;

        Vector3 toTarget = target - hip;
        float reach = toTarget.Length();
        Vector3 dir = reach > 1e-5f ? toTarget / reach : -up;
        reach = Mathf.Clamp(reach, Mathf.Abs(_upper - _lower) + 1e-3f, _upper + _lower - 1e-3f);

        Vector3 pole = model.Basis * _pole;
        pole = (pole - dir * pole.Dot(dir)).Normalized();

        float cosHip = Mathf.Clamp((_upper * _upper + reach * reach - _lower * _lower) / (2.0f * _upper * reach), -1.0f, 1.0f);
        float sinHip = Mathf.Sqrt(1.0f - cosHip * cosHip);
        Vector3 knee = hip + dir * (cosHip * _upper) + pole * (sinHip * _upper);
        Vector3 ankle = hip + dir * reach;

        Basis restWorld = model.Basis;
        Vector3 restPole = restWorld * _pole;

        Thigh.GlobalTransform = new Transform3D(
            Turn(restWorld * _thighDir, restPole, knee - hip, pole) * restWorld * _thighRest.Basis,
            hip);
        Shin.GlobalTransform = new Transform3D(
            Turn(restWorld * _shinDir, restPole, ankle - knee, pole) * restWorld * _shinRest.Basis,
            knee);

        Basis foot = level * _footRest.Basis;
        if (toeLift != 0.0f)
        {
            foot = new Basis(level.X, -toeLift) * foot;
        }
        Foot.GlobalTransform = new Transform3D(foot, ankle);
    }

    /// <summary>The rotation taking a bone from its rest direction to a new
    /// one, keeping its bend plane turned toward the pole: frame to frame, so
    /// a bone never twists about its own length.</summary>
    private static Basis Turn(Vector3 fromDir, Vector3 fromPole, Vector3 toDir, Vector3 toPole) =>
        Frame(toDir, toPole) * Frame(fromDir, fromPole).Transposed();

    private static Basis Frame(Vector3 dir, Vector3 pole)
    {
        Vector3 x = dir.Normalized();
        Vector3 y = (pole - x * pole.Dot(x)).Normalized();
        return new Basis(x, y, x.Cross(y));
    }
}
