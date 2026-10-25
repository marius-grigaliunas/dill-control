using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// Anything a <see cref="MechWalker"/> steps with: a leg, or an arm planted
/// as a crutch when legs are gone (docs/decisions/0046). The walker plans
/// when each one steps and where it lands; each poses its own parts.
/// </summary>
public abstract class MechStepper
{
    /// <summary>Steppers in the same group step together; groups take turns.</summary>
    public int Group;

    /// <summary>Where it meets the ground at rest, in the walker's own space
    /// (origin at the feet).</summary>
    public Vector3 RestFoot;

    /// <summary>False once shot away.</summary>
    public bool Alive = true;

    // Gait state, world space.
    /// <summary>Where it was last posed, stepping or not.</summary>
    public Vector3 Sole;

    public Vector3 Planted;
    public bool Stepping;
    public float StepT;
    public Vector3 StepFrom;
    public Vector3 StepTo;
}

/// <summary>
/// An arm planted on the ground as a support: one rigid part turned about its
/// shoulder to point at the spot. With one leg left it is a crutch; with none,
/// the arms haul the body along.
/// </summary>
public sealed class MechCrutch : MechStepper
{
    public readonly int Arm;

    public MechCrutch(int arm, Vector3 restFoot, int group)
    {
        Arm = arm;
        RestFoot = restFoot;
        Group = group;
    }
}
