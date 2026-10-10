using Godot;

namespace DillControl.Horde;

/// <summary>
/// What the horde stands on: which way is up, where the ground is under a
/// point, which flow cell that is, and where to scatter a spawn.
///
/// DECISION -- an interface, not the planet. The lab's horde read the planet
/// directly: up was the position normalised, height was the distance from the
/// centre, spawns were scattered over a sphere. Those were the only places it
/// knew the ground's shape, so they are this contract, and the horde is the
/// same code on the flats (<see cref="FlatGround"/>) as it will be on the
/// planet (M4). See <c>docs/decisions/0008-horde-ground.md</c>.
///
/// Every method here is called from the horde's parallel passes, so an
/// implementation must be pure arithmetic over data it does not change while
/// the horde ticks: no nodes, no resources, no physics queries.
/// </summary>
public interface IHordeGround
{
    /// <summary>Everything an agent could ever stand on, for the draw buffers' bounds.</summary>
    Aabb Bounds { get; }

    /// <summary>How many flow cells there are. Agents are sorted by cell.</summary>
    int CellCount { get; }

    /// <summary>Nominal width of a flow cell, in metres.</summary>
    float CellSize { get; }

    /// <summary>
    /// The sphere radius the horizon cull measures against, or zero for ground
    /// with no horizon, where distance and fog do the culling instead.
    /// </summary>
    float HorizonRadius { get; }

    /// <summary>Unit up at a point: away from the planet's centre, or straight up.</summary>
    Vector3 UpAt(Vector3 point);

    /// <summary>The ground directly under (or over) a point, and the flow cell it is in.</summary>
    Vector3 GroundBelow(Vector3 point, out int cell);

    /// <summary>
    /// A point on the ground in the ring between <paramref name="inner"/> and
    /// <paramref name="outer"/> metres of <paramref name="centre"/>, uniform
    /// over the ring's area. <paramref name="u1"/> and <paramref name="u2"/>
    /// are 0..1 samples from the caller, who owns the generator.
    /// </summary>
    Vector3 Scatter(Vector3 centre, float outer, float inner, float u1, float u2);
}
