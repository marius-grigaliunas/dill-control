using DillControl.Foliage;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// How a vine nest is laid out (docs/handoffs/vine-tool.md, step 6): which
/// kinds of vine it grows (each with its <see cref="VineKind.Weight"/> and
/// <see cref="VineKind.Zone"/>), how far apart, how far out at most, the
/// avenues kept clear through it, and the pad each vine stands on. One pod
/// per pickle: vines are added ring by ring out from the heart until their
/// pods cover the contract's population (<see cref="NestLayout"/>).
/// </summary>
[GlobalClass, Tool]
public partial class NestKit : Resource
{
    [Export] public string DisplayName { get; set; } = "Vine nest";

    [ExportGroup("Vines")]
    [Export] public Godot.Collections.Array<VineKind> Kinds { get; set; } = new();
    /// <summary>How every vine is grown and judged; none, vine_bake.tres.</summary>
    [Export] public VineBakeStats Bake { get; set; }
    /// <summary>Each vine's height against its roll, least and most: no two neighbours match.</summary>
    [Export] public Vector2 SizeJitter { get; set; } = new(0.88f, 1.12f);

    [ExportGroup("Population")]
    /// <summary>A contract's population is held inside this, least and most.</summary>
    [Export] public Vector2I PopulationClamp { get; set; } = new(1000, 25000);
    /// <summary>The population when nothing else names one.</summary>
    [Export(PropertyHint.Range, "1000,25000,100")] public int DefaultPopulation { get; set; } = 5000;

    [ExportGroup("Layout")]
    /// <summary>Between neighbouring vines along the ground, rolled a ring at
    /// a time: 20 m is unplayable, 100 m swings well, the grapple reaches 140 m.</summary>
    [Export] public Vector2 SpacingM { get; set; } = new(85, 110);
    /// <summary>How far a vine strays from its slot, a share of the spacing.</summary>
    [Export(PropertyHint.Range, "0,0.45,0.01")] public float Jitter { get; set; } = 0.22f;
    /// <summary>No vine further than this from the heart, along the ground.
    /// A population that does not fit shifts to kinds with more pods.</summary>
    [Export(PropertyHint.Range, "100,2000,10,suffix:m")] public float MaxRadiusM { get; set; } = 900.0f;
    /// <summary>Clear ways in from the edge to the heart, least and most.</summary>
    [Export] public Vector2I Avenues { get; set; } = new(2, 4);
    [Export(PropertyHint.Range, "0,200,1,suffix:m")] public float AvenueWidthM { get; set; } = 60.0f;

    [ExportGroup("Placement")]
    /// <summary>Between the compound's pad and the nest's edge, along the ground.</summary>
    [Export(PropertyHint.Range, "0,1000,10,suffix:m")] public float CompoundGapM { get; set; } = 150.0f;
    /// <summary>Hearts tried, the flattest kept.</summary>
    [Export(PropertyHint.Range, "1,64,1")] public int HeartCandidates { get; set; } = 16;
    /// <summary>The ground levelled under each vine, and the blend back to the terrain.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")] public float PadRadiusM { get; set; } = 6.0f;
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")] public float PadBlendM { get; set; } = 8.0f;
    /// <summary>How far each vine's foot is sunk into its pad: rooted, never floating.</summary>
    [Export(PropertyHint.Range, "0,5,0.1,suffix:m")] public float SinkM { get; set; } = 1.0f;
    /// <summary>The player starts this far outside the nest, at an avenue's mouth.</summary>
    [Export(PropertyHint.Range, "0,500,5,suffix:m")] public float SpawnOutsideM { get; set; } = 60.0f;

    public VineBakeStats BakeOrDefault() => Bake ?? GD.Load<VineBakeStats>(VineBakeStats.DefaultPath);
}
