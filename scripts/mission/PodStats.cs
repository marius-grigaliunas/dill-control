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

    [ExportGroup("Drawn")]
    /// <summary>The pickle that hangs from a full pod: its size and colour.</summary>
    [Export] public DillControl.Enemies.AgentKindStats Pickle { get; set; }
    /// <summary>Pods are drawn in square cells of the ground this wide, two
    /// draws a cell: fewer, bigger cells are fewer draws, smaller ones put
    /// fewer capsules near.</summary>
    [Export(PropertyHint.Range, "40,600,10,suffix:m")] public float CellM { get; set; } = 150.0f;
    /// <summary>Within this of a cell's middle its pickles are capsules, past it five-sided columns.</summary>
    [Export(PropertyHint.Range, "20,600,10,suffix:m")] public float NearMetres { get; set; } = 160.0f;
    /// <summary>Past this from a cell's middle its pickles are not drawn: as
    /// far as the fog lets anything be seen (the nest lab's ends at 400 m).</summary>
    [Export(PropertyHint.Range, "100,3000,10,suffix:m")] public float DrawMetres { get; set; } = 450.0f;
    /// <summary>How far a hanging pickle's bottom swings each way, and how often.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:m")] public float SwayM { get; set; } = 0.15f;
    [Export(PropertyHint.Range, "0,3,0.01,suffix:Hz")] public float SwayHz { get; set; } = 0.35f;
}
