using Godot;

namespace DillControl.Horde;

/// <summary>
/// Something in the level that makes the ground a horde stands on. A
/// <see cref="HordeDirector"/> with one set asks it instead of laying out a
/// square of flats; the planet is one.
///
/// The ground it returns must also be the flow field's graph
/// (<see cref="IFlowGraph"/>), as <see cref="FlatGround"/> is.
/// </summary>
public interface IHordeGroundProvider
{
    /// <summary>True once the ground exists and <see cref="CreateHordeGround"/> can answer.</summary>
    bool HordeGroundReady { get; }

    /// <summary>A ground for a horde, ready to search. Called once.</summary>
    IHordeGround CreateHordeGround();

    /// <summary>
    /// Stamps what the ground knows into a new flow field: slopes, and the
    /// authored props near <paramref name="garrison"/>. The provider may keep
    /// the field to restamp when the ground changes under it (a crater).
    /// </summary>
    /// <returns>How many cells authored things block.</returns>
    int PrepareFlow(FlowField flow, PhysicsDirectSpaceState3D space, Vector3 garrison);
}
