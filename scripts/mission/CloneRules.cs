using Godot;

namespace DillControl.Mission;

/// <summary>
/// The arguable numbers of death by cloning (decision 0053), read by
/// <see cref="CloneBank"/>.
/// </summary>
[GlobalClass]
public partial class CloneRules : Resource
{
    public const string DefaultPath = "res://resources/mission/clone_rules_default.tres";

    /// <summary>Bodies the company prints for a contract, the first included. Losing the last voids it.</summary>
    [Export(PropertyHint.Range, "1,20,1")]
    public int Clones { get; set; } = 3;

    /// <summary>Seconds between a body going down and the next one's pod falling.</summary>
    [Export(PropertyHint.Range, "0,20,0.5,suffix:s")]
    public float PrintSeconds { get; set; } = 3.0f;

    [ExportGroup("The lab")]
    /// <summary>
    /// How long the unexplained flashes of the cloning lab (decision 0054) cut
    /// over the view as each body begins its fall. Zero shows none.
    /// </summary>
    [Export(PropertyHint.Range, "0,6,0.1,suffix:s")]
    public float FlashSeconds { get; set; } = 1.6f;
}
