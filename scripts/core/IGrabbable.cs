using Godot;

namespace DillControl.Core;

/// <summary>
/// A body that decides for itself whether the gravity gun may lift it, and is
/// told while it is held: loose rubble is concrete, far past the gun's mass
/// limit for crates, and must not settle, freeze or be recycled in the gun's
/// grip.
/// </summary>
public interface IGrabbable
{
    bool CanGrab { get; }

    /// <summary>False once it has stopped being what was grabbed (a pooled
    /// piece recycled): the gun lets go.</summary>
    bool IsHeld { get; }

    void SetHeld(bool held);
}

/// <summary>Something the gravity gun can tear a piece off and take hold of:
/// a rubble heap.</summary>
public interface IPluckable
{
    /// <summary>A body torn off at <paramref name="point"/>, or null.</summary>
    RigidBody3D Pluck(Vector3 point);
}
