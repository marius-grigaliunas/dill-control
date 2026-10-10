using Godot;
using DillControl.Enemies;

namespace DillControl.Horde;

/// <summary>
/// How bad an infestation is: how many pickles, of what mix, and whether the
/// dead come back. A contract picks one; the <see cref="HordeStats"/> it
/// rides on keep everything about how a crowd moves and fights.
/// </summary>
[GlobalClass]
public partial class HordeTier : Resource
{
    /// <summary>What the contract screen calls it.</summary>
    [Export]
    public string DisplayName { get; set; } = "Infested";

    [Export(PropertyHint.Range, "0,60000,100")]
    public int AgentCount { get; set; } = 25000;

    /// <summary>The mix of kinds. Empty keeps the director's own.</summary>
    [Export]
    public HordeComposition Composition { get; set; }

    /// <summary>
    /// Off for a contract: the population falls as you kill it, which is what
    /// a clear threshold measures. On is the endless crowd of the test levels.
    /// </summary>
    [Export]
    public bool RespawnKilledAgents { get; set; }

    /// <summary>How close the player comes to the compound before the horde
    /// comes for them (HordeStats.AggroRange). Zero: it hunts from the start.</summary>
    [Export(PropertyHint.Range, "0,3000,10,suffix:m")]
    public float AggroRange { get; set; } = 250.0f;

    /// <summary>How far the player gets before a hunting horde goes home.</summary>
    [Export(PropertyHint.Range, "10,5000,10,suffix:m")]
    public float LeashRange { get; set; } = 600.0f;

    /// <summary>How far round the compound's middle the horde roams while it
    /// is at home (HordeStats.RoamRadius).</summary>
    [Export(PropertyHint.Range, "0,1000,5,suffix:m")]
    public float RoamRadius { get; set; } = 150.0f;
}
