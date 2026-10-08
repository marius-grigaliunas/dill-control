using Godot;

namespace DillControl.Core;

/// <summary>
/// Implemented by anything an explosion should shove. Deliberately separate
/// from <see cref="IDamageable"/>: being moved by a blast and being hurt by one
/// are different capabilities, and the player currently has the first without
/// the second.
///
/// It exists so an explosion never has to name a player type. The blast queries
/// for this interface exactly the way a bullet queries for
/// <see cref="IDamageable"/>.
/// </summary>
public interface IBlastReceiver
{
    /// <summary>
    /// Applies one blast impulse in world space, in metres per second. The
    /// receiver decides what to do with it -- a character controller folds it
    /// into its own velocity, a rigid body would hand it to the solver.
    /// </summary>
    void ApplyBlastImpulse(Vector3 impulse);
}
