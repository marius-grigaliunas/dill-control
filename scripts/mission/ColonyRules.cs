using Godot;

namespace DillControl.Mission;

/// <summary>
/// What clears a nest (<see cref="ColonyMission"/>, docs/decisions/0034):
/// every pod destroyed, the hard rule, and few enough of its pickles left
/// alive. All of them killed earns the bonus.
/// </summary>
[GlobalClass]
public partial class ColonyRules : Resource
{
    public const string DefaultPath = "res://resources/mission/colony_rules_default.tres";

    /// <summary>With every pod destroyed, the nest is cleared once at most this
    /// share of its starting pickles is alive (0.2: from 100, 20).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float MaxAliveShare { get; set; } = 0.2f;
    /// <summary>How long the cleared card stays up before the plate counts down to the bonus.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:s")] public float CardSeconds { get; set; } = 6.0f;
}
