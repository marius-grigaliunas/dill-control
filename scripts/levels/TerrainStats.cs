using Godot;

namespace DillControl.Levels;

/// <summary>
/// The numbers of a <see cref="SculptedTerrain"/> that are not its shape:
/// how its chunks are cut and how the horde walks it. The shape itself is
/// the heights sculpted into it (docs/decisions/0044).
/// </summary>
[Tool]
[GlobalClass]
public partial class TerrainStats : Resource
{
    public const string DefaultPath = "res://resources/levels/terrain_default.tres";

    [ExportGroup("Chunks")]
    /// <summary>Height samples' cells along one side of a chunk: what is drawn, culled and redrawn under a brush together.</summary>
    [Export(PropertyHint.Range, "4,128,1")]
    public int ChunkCells { get; set; } = 25;

    [ExportGroup("Horde")]
    /// <summary>Width of one of the horde's flow cells.</summary>
    [Export(PropertyHint.Range, "2,20,0.5,suffix:m")]
    public float FlowCellSize { get; set; } = 6.0f;

    /// <summary>Steeper than this and the horde will not walk it: a mesa's cliff is a wall.</summary>
    [Export(PropertyHint.Range, "10,80,1,suffix:deg")]
    public float MaxSlopeDegrees { get; set; } = 42.0f;
}
