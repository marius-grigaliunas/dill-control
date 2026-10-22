using Godot;

namespace DillControl.Foliage;

/// <summary>
/// How a plant's leaves behave (<see cref="VineLeaves"/>): how much they take
/// before they tear off, what a torn one weighs and how long it lasts, how
/// thick a leaf is to stand on, and how far off leaves are drawn. One resource
/// for every vine's leaves; their shapes come from the kind
/// (<see cref="VineKind"/>) and their colour from <see cref="VinePalette"/>.
/// </summary>
[GlobalClass]
public partial class LeafStats : Resource
{
    public const string DefaultPath = "res://resources/foliage/leaf_default.tres";

    [ExportGroup("Damage")]
    /// <summary>What a leaf takes before it tears off, badly holed.</summary>
    [Export(PropertyHint.Range, "1,2000,1")] public float Health { get; set; } = 250.0f;
    /// <summary>A hit this near where the stem joins the blade, a share of
    /// its half-width, shoots it through: it falls at once.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float StemZoneShare { get; set; } = 0.3f;
    /// <summary>A hit's hole, for <see cref="HoleReferenceDamage"/>; more
    /// damage, a bigger hole (as its square root).</summary>
    [Export(PropertyHint.Range, "0.02,3,0.01,suffix:m")] public float HoleRadiusM { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "1,1000,1")] public float HoleReferenceDamage { get; set; } = 32.0f;
    /// <summary>Holes a leaf shows at once; past it the oldest close.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int HolesPerLeaf { get; set; } = 12;
    /// <summary>Area damage reaches leaves this far from where it went off.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:m")] public float SplashRadiusM { get; set; } = 6.0f;
    /// <summary>How often the leaves check they are still attached after their vine is hit.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.05,suffix:s")] public float RecheckSeconds { get; set; } = 0.25f;

    [ExportGroup("Torn off")]
    /// <summary>Light: it lands on what it falls on without shoving it.</summary>
    [Export(PropertyHint.Range, "1,2000,1,suffix:kg")] public float MassKg { get; set; } = 12.0f;
    /// <summary>Air drag on a torn leaf: it sails down, it does not drop like a stone.</summary>
    [Export(PropertyHint.Range, "0,10,0.05")] public float LooseDrag { get; set; } = 1.2f;
    [Export(PropertyHint.Range, "0,10,0.05")] public float LooseSpinDrag { get; set; } = 2.0f;
    /// <summary>A torn leaf puffs into dust after this long.</summary>
    [Export(PropertyHint.Range, "0.5,60,0.5,suffix:s")] public float LooseSeconds { get; set; } = 8.0f;
    /// <summary>Its dust cloud's width against the leaf's.</summary>
    [Export(PropertyHint.Range, "0,2,0.05")] public float DustScale { get; set; } = 0.5f;

    [ExportGroup("Shape")]
    /// <summary>The collision under a blade, to stand on.</summary>
    [Export(PropertyHint.Range, "0.1,2,0.05,suffix:m")] public float ThicknessM { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0.05,2,0.05,suffix:m")] public float StemWidthM { get; set; } = 0.35f;
    /// <summary>The beam under a stem, to walk out along to its blade: wider than it is drawn.</summary>
    [Export(PropertyHint.Range, "0.1,3,0.05,suffix:m")] public float StemWalkWidthM { get; set; } = 0.9f;
    /// <summary>Leaves further than this from the camera are not drawn.</summary>
    [Export(PropertyHint.Range, "50,5000,10,suffix:m")] public float DrawMetres { get; set; } = 1200.0f;
}
