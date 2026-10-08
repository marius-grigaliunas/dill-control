using Godot;

namespace DillControl.Horde;

/// <summary>
/// The cells a <see cref="FlowField"/> searches over, and how they connect.
///
/// The lab's field was written against the planet's cube-sphere grid. All the
/// search ever asked of that grid was a neighbour, the step length to it and
/// the direction toward it, so that is the whole contract. The flats implement
/// it with a square grid (<see cref="FlatGround"/>); the planet's grid
/// implements it again when the planet arrives (M4), and the field does not
/// change.
/// </summary>
public interface IFlowGraph
{
    int CellCount { get; }

    /// <summary>Neighbour slots per cell. A slot may be empty at an edge.</summary>
    int Directions { get; }

    /// <summary>The neighbour in one direction, or -1 where there is none.</summary>
    int Neighbour(int cell, int direction);

    /// <summary>World-space unit tangent from this cell's centre toward that neighbour.</summary>
    Vector3 NeighbourTangent(int cell, int direction);

    /// <summary>Metres from this cell's centre to that neighbour's, along the ground.</summary>
    float NeighbourArc(int cell, int direction);

    /// <summary>Where a cell's centre is, in world space.</summary>
    Vector3 CellCentre(int cell);

    /// <summary>The longest route the search could ever walk, in metres. Sizes its bucket queue.</summary>
    float LongestPath { get; }
}
