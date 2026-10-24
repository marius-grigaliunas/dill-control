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

    [ExportGroup("Shot")]
    /// <summary>What a hanging pickle takes before it drops dead.</summary>
    [Export(PropertyHint.Range, "1,2000,1")] public float PickleHealth { get; set; } = 60.0f;
    /// <summary>Colliders handed each tick to the pickles nearest the aim.</summary>
    [Export(PropertyHint.Range, "0,256,1")] public int HitProxies { get; set; } = 48;
    /// <summary>No proxy goes to a pickle further off than this.</summary>
    [Export(PropertyHint.Range, "10,1000,10,suffix:m")] public float ProxyMetres { get; set; } = 350.0f;
    /// <summary>How often, in physics ticks, the proxies are handed out again.
    /// Between, the ones handed out follow their pickles.</summary>
    [Export(PropertyHint.Range, "1,30,1")] public int ProxyEveryTicks { get; set; } = 3;

    [ExportGroup("Dropped dead")]
    /// <summary>A killed pickle falls as a body, at most this many at once; past it, it puffs where it hung.</summary>
    [Export(PropertyHint.Range, "0,256,1")] public int MaxFalling { get; set; } = 32;
    [Export(PropertyHint.Range, "1,500,1,suffix:kg")] public float FallingMassKg { get; set; } = 40.0f;
    /// <summary>A fallen pickle puffs into dust after this long.</summary>
    [Export(PropertyHint.Range, "0.5,30,0.5,suffix:s")] public float FallingSeconds { get; set; } = 5.0f;
    /// <summary>Its dust cloud's width against the pickle's height.</summary>
    [Export(PropertyHint.Range, "0,4,0.05")] public float DustScale { get; set; } = 1.0f;

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
