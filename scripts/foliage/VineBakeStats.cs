using Godot;

namespace DillControl.Foliage;

/// <summary>
/// How the vine dock bakes and checks a vine: the voxel size every vine shares,
/// what a vine is judged against, and what its wrapper scene is given. One
/// resource for the bake; the vine's own look is <see cref="VinePalette"/> and
/// its shape a <see cref="VineKind"/>. See docs/decisions/0033.
/// </summary>
[GlobalClass, Tool]
public partial class VineBakeStats : Resource
{
    public const string DefaultPath = "res://resources/foliage/vine_bake.tres";

    /// <summary>The seed pool seeds are drawn from (<see cref="VineKind.PoolSeed"/>).</summary>
    [Export(PropertyHint.Range, "0,9999999,1")] public int ArchetypeSeed { get; set; } = 1;

    /// <summary>Every vine's voxel, written into its .vox.import as <c>voxel/size</c>.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05,suffix:m")] public float VoxelSize { get; set; } = 0.5f;

    /// <summary>prp_tree_tall_a's voxels: collision boxes and load time scale with it.</summary>
    [ExportGroup("Checks")]
    [Export(PropertyHint.Range, "1000,200000,1")] public int VoxelBudget { get; set; } = 30352;
    /// <summary>MagicaVoxel stores each coordinate in a byte.</summary>
    [Export(PropertyHint.Range, "16,256,1")] public int AxisLimit { get; set; } = 256;
    /// <summary>Clear air a hanging pickle needs below its pod (nme_standard's height).</summary>
    [Export(PropertyHint.Range, "0.3,8,0.01,suffix:m")] public float PickleHeightM { get; set; } = 1.8f;
    /// <summary>Closer than this, two hanging pickles overlap.</summary>
    [Export(PropertyHint.Range, "0.1,4,0.05,suffix:m")] public float PodSpacingM { get; set; } = 0.9f;
    /// <summary>Jolt's body limit shared by the whole world; a vine at rest
    /// holds about one per occupied section of its destruction stats.</summary>
    [Export(PropertyHint.Range, "1,200,1")] public int BodiesMax { get; set; } = 20;

    /// <summary>A seed whose vine fails a check is grown again from the seed
    /// and an attempt number, up to this many times, so a seed still means one
    /// vine (<see cref="VineGrower"/>). Past it, the kind's ranges are broken.</summary>
    [ExportGroup("Validity")]
    [Export(PropertyHint.Range, "1,64,1")] public int MaxAttempts { get; set; } = 16;
    /// <summary>The share of its grown pods a vine may leave out (no drop
    /// below them, or crowded) and still be valid.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float MaxPodsLeftOut { get; set; } = 0.1f;
    /// <summary>The share of its leaves a vine may leave out for want of room.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float MaxLeavesLeftOut { get; set; } = 0.5f;
    /// <summary>How far from level a blade may be: leaves are platforms.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:deg")] public float MaxLeafTiltDeg { get; set; } = 15.0f;
    /// <summary>Over a lower leaf, the air a leaf leaves (to stand on the lower one).</summary>
    [Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")] public float LeafGapM { get; set; } = 2.5f;
    /// <summary>Arms and leaves stay this far above the ground.</summary>
    [Export(PropertyHint.Range, "0,20,0.5,suffix:m")] public float GroundClearanceM { get; set; } = 3.0f;
    /// <summary>The tip's lean from the foot.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:deg")] public float MaxLeanDeg { get; set; } = 25.0f;
    /// <summary>How much wider than the ring below a ring may be (the stalk tapers).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float StalkWidening { get; set; } = 0.1f;
    /// <summary>How much longer or thicker the top's arms may be than the bottom's,
    /// on the line fitted through them (arms shorten and thin upward).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float ArmGrowth { get; set; } = 0.05f;
    /// <summary>The most any joint of the vine's spine bends under the weight
    /// it carries off the stalk (<see cref="VineValidator"/>).</summary>
    [Export(PropertyHint.Range, "0,30,0.1,suffix:deg")] public float MaxKinkDeg { get; set; } = 3.0f;
    /// <summary>How far its tip would sag under that weight.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")] public float MaxSagM { get; set; } = 8.0f;

    [ExportGroup("Wrapper")]
    // Read by the editor, where game resources without [Tool] load as
    // placeholders: held as plain resources, read by property name.
    /// <summary>The wrapper's VoxelDestructionStats (voxel_destruction_vine.tres).</summary>
    [Export] public Resource Destruction { get; set; }
    /// <summary>The wrapper's FlexStats (flex_vine.tres).</summary>
    [Export] public Resource Flex { get; set; }
    [Export] public VinePalette Palette { get; set; }
    [Export(PropertyHint.Dir)] public string VoxelFolder { get; set; } = "res://art/voxels";
    [Export(PropertyHint.Dir)] public string SceneFolder { get; set; } = "res://scenes/props";

    public VineGenerator.Limits Limits() => new()
    {
        TreeVoxels = VoxelBudget,
        AxisLimit = AxisLimit,
        PickleHeightM = System.Math.Round((double)PickleHeightM, 6),
        PodSpacingM = System.Math.Round((double)PodSpacingM, 6),
        LeafGapM = System.Math.Round((double)LeafGapM, 6),
        SectionEdge = Destruction != null && Destruction.Get("SectionEdge").VariantType == Variant.Type.Int
            ? Destruction.Get("SectionEdge").AsInt32() : 64,
    };
}
