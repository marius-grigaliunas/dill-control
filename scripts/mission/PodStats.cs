using Godot;

namespace DillControl.Mission;

/// <summary>
/// How a nest's pods behave (<see cref="PodField"/>, docs/decisions/0034):
/// how a pod knows its vine still holds it. Regrowth's numbers join it when
/// pods regrow. One resource for every pod in a nest.
/// </summary>
[GlobalClass]
public partial class PodStats : Resource
{
    public const string DefaultPath = "res://resources/mission/pod_default.tres";

    [ExportGroup("Held")]
    /// <summary>How often a vine's pods check they are still held after the vine has changed.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.05,suffix:s")] public float RecheckSeconds { get; set; } = 0.25f;
    /// <summary>How far from its marker, in voxels, a pod looks for the voxel
    /// it hangs from. Found none, it hangs from nothing and goes only with its
    /// vine.</summary>
    [Export(PropertyHint.Range, "1,8,1")] public int AnchorSearchVoxels { get; set; } = 3;
}
