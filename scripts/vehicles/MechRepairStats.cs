using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// How mechs mend (docs/decisions/0046): when one asks for help, how fast the
/// storage bot regrows it, how a lost limb comes down in a pod, when a badly
/// hurt one falls back, and what each costs. The prices are the material's:
/// a restored voxel, a part, a bot. There is no money in the game yet; the
/// contract's ledger in <c>Session</c> records them for the screens to come.
/// </summary>
[GlobalClass]
public partial class MechRepairStats : Resource
{
    [ExportGroup("Asking")]
    /// <summary>How often a mech looks at what it has lost.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.1,suffix:s")]
    public float CheckSeconds { get; set; } = 0.5f;

    /// <summary>It asks for the storage bot once this many voxels are gone
    /// from the parts it still has.</summary>
    [Export(PropertyHint.Range, "1,5000,1")]
    public int CallAtMissingVoxels { get; set; } = 40;

    /// <summary>Its core below this share of its voxels: it falls back behind
    /// the player, and keeps asking.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float FallBackBelowCore { get; set; } = 0.75f;

    /// <summary>How far behind the player a mech that has fallen back stands.</summary>
    [Export(PropertyHint.Range, "2,80,0.5,suffix:m")]
    public float FallBackDistance { get; set; } = 18.0f;

    [ExportGroup("Regrowing")]
    /// <summary>Voxels the storage bot puts back a second.</summary>
    [Export(PropertyHint.Range, "1,2000,1")]
    public float RegrowPerSecond { get; set; } = 60.0f;

    /// <summary>How near the storage bot must be to regrow a mech.</summary>
    [Export(PropertyHint.Range, "1,40,0.5,suffix:m")]
    public float RepairRange { get; set; } = 7.0f;

    [ExportGroup("Parts by pod")]
    /// <summary>How long after a limb is lost its replacement is dropped.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:s")]
    public float PodDelaySeconds { get; set; } = 4.0f;

    /// <summary>How far from the mech the pod lands, toward the player.</summary>
    [Export(PropertyHint.Range, "2,80,0.5,suffix:m")]
    public float PodDistance { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "20,2000,10,suffix:m")]
    public float PodDropHeight { get; set; } = 300.0f;

    [Export(PropertyHint.Range, "0.5,30,0.1,suffix:s")]
    public float PodDropSeconds { get; set; } = 3.0f;

    /// <summary>How near the pod the mech must come for the part to be fitted.</summary>
    [Export(PropertyHint.Range, "1,20,0.5,suffix:m")]
    public float FitRange { get; set; } = 4.5f;

    [ExportGroup("Prices")]
    /// <summary>One voxel put back, in credits.</summary>
    [Export(PropertyHint.Range, "0,1000,0.01")]
    public float VoxelPrice { get; set; } = 2.0f;

    /// <summary>A part fitted new, besides its voxels, in credits. A lost
    /// fighter leg is about 1,750 with them.</summary>
    [Export(PropertyHint.Range, "0,100000,1")]
    public float PartPrice { get; set; } = 500.0f;
}
