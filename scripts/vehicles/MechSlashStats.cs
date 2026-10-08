using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A walking mech's arm slash: one horizontal stroke, how strokes chain, and
/// how it decides to slash on its own. Read by <see cref="MechSlasher"/>; there
/// is no balance number there.
/// </summary>
[GlobalClass]
public partial class MechSlashStats : Resource
{
    [ExportGroup("Strike")]
    [Export(PropertyHint.Range, "1,500,1")]
    public float Damage { get; set; } = 80.0f;

    /// <summary>Time from the trigger to the blade being where it can hurt.</summary>
    [Export(PropertyHint.Range, "0,1,0.005,suffix:s")]
    public float Windup { get; set; } = 0.12f;

    /// <summary>How long the arm takes to sweep. Hits land in three slices
    /// across this window.</summary>
    [Export(PropertyHint.Range, "0.01,0.8,0.005,suffix:s")]
    public float Strike { get; set; } = 0.16f;

    [Export(PropertyHint.Range, "0,1.5,0.005,suffix:s")]
    public float Recovery { get; set; } = 0.22f;

    /// <summary>How wide the arm sweeps, from cocked out to the side across
    /// the nose to follow-through on the other side.</summary>
    [Export(PropertyHint.Range, "20,180,1,suffix:°")]
    public float ArcDegrees { get; set; } = 140.0f;

    [ExportGroup("Reach")]
    /// <summary>How far from the shoulder the furthest blast sits.</summary>
    [Export(PropertyHint.Range, "0.5,12,0.1,suffix:m")]
    public float Reach { get; set; } = 4.5f;

    /// <summary>Blast spheres laid along the arm, so the slash has a length
    /// rather than a point.</summary>
    [Export(PropertyHint.Range, "1,8,1")]
    public int Spheres { get; set; } = 3;

    [Export(PropertyHint.Range, "0.2,4,0.05,suffix:m")]
    public float SphereRadius { get; set; } = 1.6f;

    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float Knockback { get; set; } = 10.0f;

    [ExportGroup("Combo")]
    /// <summary>How long after a stroke a further trigger still swings the
    /// other arm. Past it the next stroke starts on the right again.</summary>
    [Export(PropertyHint.Range, "0,1.5,0.01,suffix:s")]
    public float ComboWindow { get; set; } = 0.4f;

    /// <summary>When set, strokes alternate right then left. Off always uses
    /// the first living arm.</summary>
    [Export]
    public bool Alternate { get; set; } = true;

    [ExportGroup("On its own")]
    /// <summary>It slashes a pickle this close instead of shooting it.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")]
    public float AutoRange { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0,180,1,suffix:°")]
    public float AutoConeDegrees { get; set; } = 50.0f;
}
