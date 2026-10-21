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

    /// <summary>The seed pool seeds are drawn from (kinds.js poolSeed).</summary>
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
    [Export(PropertyHint.Range, "1,200,1")] public int BodiesWarnAbove { get; set; } = 20;

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
        SectionEdge = Destruction != null && Destruction.Get("SectionEdge").VariantType == Variant.Type.Int
            ? Destruction.Get("SectionEdge").AsInt32() : 64,
    };
}
