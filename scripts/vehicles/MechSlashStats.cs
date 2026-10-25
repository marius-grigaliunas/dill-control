using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A walking mech's arm swing: the arc the fist travels, how long each
/// part takes, and how it decides to swing on its own. Read by
/// <see cref="MechSlasher"/>. Edit this resource, then swing again: the
/// next stroke uses the new numbers. There is no balance number in the slasher.
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

    /// <summary>How long the fist takes to travel from the cock to the
    /// follow-through. Hits land in the second half.</summary>
    [Export(PropertyHint.Range, "0.05,1.2,0.01,suffix:s")]
    public float Strike { get; set; } = 0.36f;

    [Export(PropertyHint.Range, "0,1.5,0.01,suffix:s")]
    public float Recovery { get; set; } = 0.32f;

    [ExportGroup("Swing")]
    /// <summary>How far the fist cocks up and back before the swing, in
    /// degrees from hanging. Larger lifts the club higher.</summary>
    [Export(PropertyHint.Range, "0,120,1,suffix:°")]
    public float CockPitch { get; set; } = 75.0f;

    /// <summary>How far the fist follows through forward and down, in degrees
    /// past hanging. Larger reaches further in front of the feet.</summary>
    [Export(PropertyHint.Range, "0,100,1,suffix:°")]
    public float StrikePitch { get; set; } = 55.0f;

    /// <summary>How far out to the arm's own side the cock sits.</summary>
    [Export(PropertyHint.Range, "0,90,1,suffix:°")]
    public float CockYaw { get; set; } = 40.0f;

    /// <summary>How far past the nose the swing carries the fist.</summary>
    [Export(PropertyHint.Range, "0,90,1,suffix:°")]
    public float StrikeYaw { get; set; } = 28.0f;

    [ExportGroup("Combo")]
    /// <summary>The strokes one attack button flows through, in order, posed
    /// in Blender (docs/decisions/0046). Held or pressed again, each blends
    /// into the next once its blow has landed; let go past
    /// <see cref="ComboWindow"/> and it starts again from the first. Hits land
    /// on each move's 'hit' marker, from every arm it swings. Empty: the
    /// procedural swing above, alternating arms.</summary>
    [Export]
    public Godot.Collections.Array<MechMove> Combo { get; set; } = new();

    /// <summary>How long after a stroke's blow lands the next may start.</summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:s")]
    public float ChainAfterHit { get; set; } = 0.12f;

    /// <summary>How long one stroke takes to blend into the next.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.01,suffix:s")]
    public float ChainBlend { get; set; } = 0.12f;

    [ExportGroup("Reach")]
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
    /// <summary>It slams a pickle this close instead of shooting it.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")]
    public float AutoRange { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0,180,1,suffix:°")]
    public float AutoConeDegrees { get; set; } = 50.0f;
}
