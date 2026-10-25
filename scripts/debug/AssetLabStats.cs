using Godot;

namespace DillControl.Debug;

/// <summary>
/// The asset lab's tools in play (docs/decisions/0047): how hard each one
/// hits, and the slow-motion steps. Development tooling, not the game.
/// </summary>
[GlobalClass]
public partial class AssetLabStats : Resource
{
    [ExportGroup("Tools")]
    /// <summary>A bite: one direct hit at the aim, as a heavy round lands.</summary>
    [Export(PropertyHint.Range, "1,5000,1")]
    public float BiteDamage { get; set; } = 300.0f;

    [Export(PropertyHint.Range, "0.5,40,0.5,suffix:m")]
    public float BlastRadius { get; set; } = 6.0f;

    /// <summary>Full damage at the centre of a blast, as a rocket's (260).</summary>
    [Export(PropertyHint.Range, "1,5000,1")]
    public float BlastDamage { get; set; } = 260.0f;

    [Export(PropertyHint.Range, "0,60,0.5")]
    public float BlastImpulse { get; set; } = 12.0f;

    /// <summary>How thick a cut is: a slit right through, at the aim's height.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.1,suffix:m")]
    public float CutThickness { get; set; } = 0.6f;

    /// <summary>How fast a part broken away is thrown, along the aim.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:m/s")]
    public float BreakPush { get; set; } = 3.0f;

    /// <summary>What - and = scale the chosen tool's strength by.</summary>
    [Export(PropertyHint.Range, "1.05,4,0.05")]
    public float StrengthStep { get; set; } = 1.5f;

    [ExportGroup("Time")]
    /// <summary>The slow-motion steps T cycles through.</summary>
    [Export]
    public float[] SlowScales { get; set; } = { 1.0f, 0.5f, 0.25f, 0.1f };

    [ExportGroup("Placing")]
    /// <summary>How far in front of the player the asset stands at the start.</summary>
    [Export(PropertyHint.Range, "2,200,1,suffix:m")]
    public float SpawnDistance { get; set; } = 14.0f;
}
