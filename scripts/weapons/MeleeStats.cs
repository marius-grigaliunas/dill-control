using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Everything tunable about a melee weapon: one horizontal swing, how strokes
/// chain into each other, the camera kick, the knockback and the viewmodel's
/// path. Read by <see cref="MeleeWeapon"/>; there is no balance number there.
///
/// Ported from the lab's machete without its stab, its random chain
/// directions and its momentum bonus. The slice's machete is one swing that
/// alternates left-to-right and right-to-left; the rest waits in the lab until
/// play asks for it.
/// </summary>
[GlobalClass]
public partial class MeleeStats : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "Unnamed Melee Weapon";

    [ExportGroup("Swing")]
    [Export(PropertyHint.Range, "1,300,0.5")]
    public float Damage { get; set; } = 62.0f;

    /// <summary>Time from the click to the blade being where it can hurt.</summary>
    [Export(PropertyHint.Range, "0,1,0.005,suffix:s")]
    public float Windup { get; set; } = 0.11f;

    /// <summary>How long the swing stays out. Hits resolve once, at its start.</summary>
    [Export(PropertyHint.Range, "0.01,0.6,0.005,suffix:s")]
    public float Strike { get; set; } = 0.13f;

    /// <summary>Dead time after the swing, before another attack is accepted.</summary>
    [Export(PropertyHint.Range, "0,1.5,0.005,suffix:s")]
    public float Recovery { get; set; } = 0.22f;

    /// <summary>How wide the blade's path is across the view.</summary>
    [Export(PropertyHint.Range, "5,170,1,suffix:°")]
    public float ArcDegrees { get; set; } = 70.0f;

    [Export(PropertyHint.Range, "0.5,6,0.05,suffix:m")]
    public float Range { get; set; } = 2.8f;

    /// <summary>
    /// How many rays the arc is sampled with. More is a finer blade and costs
    /// one raycast each; too few and a narrow target slips between them.
    /// </summary>
    [Export(PropertyHint.Range, "1,21,1")]
    public int ArcRayCount { get; set; } = 9;

    /// <summary>Cap on how many separate things one swing can hurt.</summary>
    [Export(PropertyHint.Range, "1,10,1")]
    public int MaxTargetsPerSwing { get; set; } = 3;

    [ExportGroup("Combo")]
    /// <summary>
    /// How long after a swing finishes a further click still swings back the
    /// other way. Past it, the next swing starts left-to-right again. A click
    /// from the blade's arrival onward is buffered; one during the windup is
    /// dropped, which is what stops mashing from being the best input.
    /// </summary>
    [Export(PropertyHint.Range, "0,1.5,0.01,suffix:s")]
    public float ComboWindow { get; set; } = 0.40f;

    /// <summary>
    /// Windup and recovery multiplier on a chained swing. Under 1 because the
    /// blade is already moving and already out where the last swing left it --
    /// that speed-up is the reward for hitting the window.
    /// </summary>
    [Export(PropertyHint.Range, "0.2,1,0.01")]
    public float ChainTimingScale { get; set; } = 0.72f;

    /// <summary>
    /// How much of a swing's recovery must pass before a buffered click cuts it
    /// short and the next stroke starts from where this one's blade is. Below 1
    /// keeps a chain flowing instead of returning to rest between strokes.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float RecoveryCancelFraction { get; set; } = 0.25f;

    [ExportGroup("Camera Kick")]
    [Export(PropertyHint.Range, "0,10,0.05,suffix:°")]
    public float KickPitchDegrees { get; set; } = 2.2f;

    /// <summary>Sideways kick, in the direction the blade travels.</summary>
    [Export(PropertyHint.Range, "0,10,0.05,suffix:°")]
    public float KickYawDegrees { get; set; } = 1.1f;

    /// <summary>
    /// Kept near the guns' recoil curve on purpose: the camera rig takes its
    /// spring constants from every kick, so wildly different values here would
    /// retune whatever gun recoil is still decaying.
    /// </summary>
    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float KickRecoverSpeed { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "1,60,0.5")]
    public float KickSnappiness { get; set; } = 20.0f;

    [ExportGroup("Impact")]
    /// <summary>
    /// Speed, in metres per second, given to what the blade connects with.
    /// Damage numbers are invisible; a body that is thrown is not.
    /// </summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float Knockback { get; set; } = 7.0f;

    /// <summary>How much of the knockback is bent toward the local up, so a struck body lifts.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float KnockbackUpBias { get; set; } = 0.3f;

    [ExportGroup("Viewmodel")]
    /// <summary>How far the hand reaches out at the middle of a stroke. Zero at both ends.</summary>
    [Export(PropertyHint.Range, "0,0.8,0.005,suffix:m")]
    public float SwingForward { get; set; } = 0.16f;

    /// <summary>How far the hand travels across the view either side of its centre.</summary>
    [Export(PropertyHint.Range, "0,0.8,0.005,suffix:m")]
    public float SwingLateral { get; set; } = 0.30f;

    /// <summary>
    /// How far the blade is turned from the sightline at each end of the
    /// stroke: cocked this far back at the windup, this far past at the finish.
    /// </summary>
    [Export(PropertyHint.Range, "0,90,0.5,suffix:°")]
    public float SwingArcDegrees { get; set; } = 72.0f;

    /// <summary>
    /// How far a stroke is pulled from the hand's rest position onto the middle
    /// of the view, 0 to 1. At 0 a swing happens off at the right edge; the
    /// blade has to cross the view to read as crossing it.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SwingCentering { get; set; } = 0.8f;

    /// <summary>
    /// Least distance below the sightline the hand passes, the way an arm
    /// swinging from the shoulder does. Straight through the eye line shows the
    /// blade end-on at exactly the moment it lands.
    /// </summary>
    [Export(PropertyHint.Range, "0,0.5,0.005,suffix:m")]
    public float SwingSideOffset { get; set; } = 0.2f;

    /// <summary>How far the blade leans back toward the sightline, so the tip passes over the crosshair.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:°")]
    public float SwingTiltDegrees { get; set; } = 28.0f;

    [Export(PropertyHint.Range, "1,40,0.1")]
    public float ViewmodelRecoverSpeed { get; set; } = 14.0f;
}
