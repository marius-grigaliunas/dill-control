using Godot;

namespace DillControl.Levels;

/// <summary>
/// One square of an arena's ground: its mesh and its collision. A type of its
/// own so the horde's obstacle test can pass over the ground it stands on, as
/// it passes over a <c>PlanetChunk</c>.
/// </summary>
public partial class ArenaChunk : StaticBody3D
{
}
