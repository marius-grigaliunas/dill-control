using Godot;
using DillControl.Horde;

namespace DillControl.Mission;

/// <summary>
/// The arguable numbers of a drop-and-extract contract (decision 0026): what
/// counts as done, how many clones, how the pods come down. Read by
/// <see cref="ExtractionMission"/>.
/// </summary>
[GlobalClass]
public partial class ExtractionRules : Resource
{
    [ExportGroup("The job")]
    /// <summary>The infestation: how many, what mix, whether the dead stay dead. Set on the horde at load.</summary>
    [Export]
    public HordeTier Tier { get; set; }

    /// <summary>Share of the pickles that must die before the pod is sent.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float KillShare { get; set; } = 0.5f;

    /// <summary>The jar counts as cracked once this share of its core is gone.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.01")]
    public float CoreShare { get; set; } = 0.5f;

    [ExportGroup("Clones")]
    /// <summary>Bodies the company prints for the contract, the first included. The last one dying voids it.</summary>
    [Export(PropertyHint.Range, "1,20,1")]
    public int Clones { get; set; } = 3;

    /// <summary>Seconds between a body going down and the next one's pod falling.</summary>
    [Export(PropertyHint.Range, "0,20,0.5,suffix:s")]
    public float PrintSeconds { get; set; } = 3.0f;

    [ExportGroup("Pods")]
    /// <summary>Metres over the ground a pod starts its fall.</summary>
    [Export(PropertyHint.Range, "20,3000,10,suffix:m")]
    public float DropHeight { get; set; } = 450.0f;

    [Export(PropertyHint.Range, "0.5,30,0.1,suffix:s")]
    public float DropSeconds { get; set; } = 5.0f;

    /// <summary>What a pod's landing does to what is under it: radius and damage.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m")]
    public float LandingRadius { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "0,5000,10")]
    public float LandingDamage { get; set; } = 400.0f;

    /// <summary>Seconds from the job being done to the extraction pod falling.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:s")]
    public float ExtractionDelay { get; set; } = 4.0f;

    /// <summary>An extraction site nearer the player than this is not picked, unless none is further.</summary>
    [Export(PropertyHint.Range, "0,2000,10,suffix:m")]
    public float ExtractionMinDistance { get; set; } = 300.0f;

    /// <summary>Which far-enough site the pod goes to: a roll on the "extraction" stream.</summary>
    [Export]
    public long Seed { get; set; } = 1;
}
