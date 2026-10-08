using Godot;

namespace DillControl.Voxel;

/// <summary>
/// How every voxel model in the game breaks: how much a hit takes out, how
/// pieces come loose, how the loose pieces fly, break and settle, and what it
/// may cost a frame. One resource for all of them, because "everything breaks
/// the same way" is the rule (docs/decisions/0011).
///
/// Sizes are in metres, never voxels, so a rocket takes the same bite out of a
/// 0.1 m mech and a 0.5 m tower.
/// </summary>
[GlobalClass]
public partial class VoxelDestructionStats : Resource
{
    public const string DefaultPath = "res://resources/voxel/voxel_destruction_default.tres";

    [ExportGroup("Carving")]
    /// <summary>Radius every hit takes out, before damage is counted.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:m")]
    public float CarveBase { get; set; } = 0.12f;

    /// <summary>Extra radius per point of damage. A 20-damage round pits about
    /// 0.25 m, a 260-damage rocket blows out about 1.7 m.</summary>
    [Export(PropertyHint.Range, "0,0.05,0.0005,suffix:m")]
    public float CarvePerDamage { get; set; } = 0.006f;

    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:m")]
    public float CarveMax { get; set; } = 3.5f;

    /// <summary>A sustained weapon (flame, beam) carves only every this many
    /// damage points, or it would erase whatever it touched in a second.</summary>
    [Export(PropertyHint.Range, "1,500,1")]
    public float SustainedDamagePerCarve { get; set; } = 40.0f;

    [ExportGroup("Structure")]
    /// <summary>How long after a hit a piece is checked for what has come
    /// loose. The check floods the piece's whole grid, so hits in one burst
    /// share one.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:s")]
    public float IntegrityDelay { get; set; } = 0.12f;

    /// <summary>A part hangs from its hinge: the cells this close to its
    /// pivot are what it hangs from. Shoot them away and it falls off.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.05,suffix:m")]
    public float HingeRadius { get; set; } = 0.3f;

    [ExportGroup("Loose pieces")]
    /// <summary>Most loose pieces in the world at once. Past it the oldest
    /// settled one turns to dust.</summary>
    [Export(PropertyHint.Range, "0,400,1")]
    public int MaxChunks { get; set; } = 48;

    /// <summary>A piece smaller than this many voxels is dust, not a body.</summary>
    [Export(PropertyHint.Range, "1,500,1")]
    public int MinChunkVoxels { get; set; } = 6;

    /// <summary>Kilograms per cubic metre of voxel.</summary>
    [Export(PropertyHint.Range, "10,8000,10")]
    public float Density { get; set; } = 600.0f;

    /// <summary>A piece that loses this much speed in one step breaks apart.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m/s")]
    public float BreakSpeed { get; set; } = 8.0f;

    /// <summary>Only pieces at least this big break on impact; smaller ones
    /// bounce and settle.</summary>
    [Export(PropertyHint.Range, "2,5000,1")]
    public int MinBreakVoxels { get; set; } = 40;

    /// <summary>A piece landing this fast crushes what it lands on.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m/s")]
    public float CrushSpeed { get; set; } = 6.0f;

    /// <summary>Crush damage per kilogram-metre-per-second of impact.</summary>
    [Export(PropertyHint.Range, "0,1,0.001")]
    public float CrushDamagePerMomentum { get; set; } = 0.05f;

    /// <summary>How hard a piece is thrown off a hit, in m/s.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float BreakawaySpeed { get; set; } = 4.0f;

    /// <summary>Most boxes in one loose piece's collision. Past it the piece
    /// is boxed at half resolution, then as one box.</summary>
    [Export(PropertyHint.Range, "1,64,1")]
    public int MaxChunkBoxes { get; set; } = 12;

    /// <summary>A piece asleep this long stops being a body and stays as
    /// scenery: no physics, still breakable.</summary>
    [Export(PropertyHint.Range, "1,600,1,suffix:s")]
    public float SettleSeconds { get; set; } = 20.0f;

    [ExportGroup("Budget")]
    /// <summary>Edge of one re-meshed section, in voxels. A hit re-meshes the
    /// section it lands in, not the model.</summary>
    [Export(PropertyHint.Range, "8,64,1")]
    public int SectionEdge { get; set; } = 24;

    /// <summary>Most sections re-meshed in one frame, for everything in the
    /// world together. The rest wait a frame.</summary>
    [Export(PropertyHint.Range, "1,64,1")]
    public int SectionsPerFrame { get; set; } = 6;

    /// <summary>Most structural checks in one frame, world-wide.</summary>
    [Export(PropertyHint.Range, "1,32,1")]
    public int IntegrityChecksPerFrame { get; set; } = 2;
}
