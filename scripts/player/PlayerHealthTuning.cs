using Godot;

namespace DillControl.Player;

/// <summary>
/// The player's health pool: size, regeneration and what empty does. Read by
/// <see cref="PlayerHealth"/>.
/// </summary>
[GlobalClass]
public partial class PlayerHealthTuning : Resource
{
    // ---------------- Health ----------------

    [ExportGroup("Health")]
    /// <summary>
    /// Whether anything can drain the player at all. Off, hits are the old
    /// shove, shake and flash and the jar is hidden -- the switch for a
    /// measurement that wants nothing but load on screen.
    /// </summary>
    [Export]
    public bool HealthEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "1,1000,1")]
    public float MaxHealth { get; set; } = 100.0f;

    /// <summary>Seconds after the last hit before health starts coming back.</summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:s")]
    public float HealthRegenDelay { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0,200,0.5,suffix:/s")]
    public float HealthRegenPerSecond { get; set; } = 18.0f;

    /// <summary>
    /// What happens at zero, until clones exist (M7). The player is
    /// "pickled": health refills from empty over this long and cannot be
    /// drained while it does, and a counter goes up.
    /// </summary>
    [Export(PropertyHint.Range, "0.2,10,0.1,suffix:s")]
    public float PickledRefillSeconds { get; set; } = 2.0f;
}
