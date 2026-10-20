using Godot;

namespace DillControl.Player;

/// <summary>
/// Every number the grapple line reads: the throw, the rope, the reel, the
/// zip, the swing, the release float and the orbit. Read by
/// <see cref="GrappleHook"/> and, for everything that changes velocity, by
/// <see cref="PlayerController"/>, which reaches it through the hook.
///
/// Its own resource rather than a group on <see cref="MovementTuning"/>: the
/// line is the first focus in the pitch and gets tuned on its own, and one
/// resource per system is the rule.
/// </summary>
[GlobalClass]
public partial class GrappleTuning : Resource
{
    // ---------------- Grapple ----------------

    [ExportGroup("Line")]
    /// <summary>
    /// Whether the line exists at all. Off, the hook never fires and every
    /// term below is inert, so the controller is exactly what it was.
    /// </summary>
    [Export]
    public bool GrappleEnabled { get; set; } = true;

    /// <summary>
    /// Furthest the hook will fly before it gives up and comes back. Long on
    /// purpose: a line thrown at the far edge of what you can see is how you
    /// cross ground faster than the horde can.
    /// </summary>
    [Export(PropertyHint.Range, "5,200,1,suffix:m")]
    public float GrappleRange { get; set; } = 140.0f;

    /// <summary>
    /// How fast the hook flies. Finite rather than instant, so a throw at full
    /// range reads as a throw and the attach has a moment to land on. Zero is
    /// instant.
    /// </summary>
    [Export(PropertyHint.Range, "0,800,5,suffix:m/s")]
    public float GrappleHookSpeed { get; set; } = 500.0f;

    /// <summary>Shortest the line can be reeled to.</summary>
    [Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")]
    public float GrappleMinLength { get; set; } = 1.5f;

    /// <summary>How fast holding jump shortens the line.</summary>
    [Export(PropertyHint.Range, "1,60,0.5,suffix:m/s")]
    public float GrappleReelSpeed { get; set; } = 22.0f;

    /// <summary>
    /// Extra pull toward the anchor while reeling, added on top of the swing.
    /// It never replaces the speed across the line: holding jump hauls you in
    /// and leaves the arc you already have.
    /// </summary>
    [Export(PropertyHint.Range, "1,150,0.5,suffix:m/s²")]
    public float GrappleReelAcceleration { get; set; } = 90.0f;

    /// <summary>
    /// Speed toward the anchor past which reeling stops adding to it. The
    /// swing across the line keeps its own speed up to <see cref="GrappleMaxSpeed"/>;
    /// this cap is only the reel.
    /// </summary>
    [Export(PropertyHint.Range, "1,80,0.5,suffix:m/s")]
    public float GrappleReelMaxSpeed { get; set; } = 32.0f;

    /// <summary>
    /// Within this of the hang length, inward speed is capped so arriving at
    /// the top of the line eases out rather than stopping in one frame.
    /// Zero leaves the hang's hard stop as it is.
    /// </summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:m")]
    public float GrappleReelArriveDistance { get; set; } = 5.0f;

    /// <summary>
    /// How much of the speed heading away from the anchor is kept as swing
    /// when the line first goes taut. One turns it into the swing direction
    /// at the same size; zero is a rope that eats the outward part.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GrappleAttachCarry { get; set; } = 1.0f;

    /// <summary>How fast holding crouch lets line out.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float GrapplePayoutSpeed { get; set; } = 8.0f;

    /// <summary>
    /// How hard an over-stretched line pulls back to its length, per metre of
    /// stretch. The velocity constraint does almost all the work; this only
    /// mops up the drift that integration leaves.
    /// </summary>
    [Export(PropertyHint.Range, "0,60,0.5")]
    public float GrappleRopeStiffness { get; set; } = 12.0f;

    /// <summary>Cap on that correction, so a line that snaps taut over a long
    /// stretch does not fire you at the anchor.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float GrappleMaxCorrection { get; set; } = 8.0f;

    /// <summary>
    /// Push from the movement keys while hanging, applied only across the line.
    /// This is pumping a swing, not walking in the air.
    /// </summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m/s²")]
    public float GrappleSwingAcceleration { get; set; } = 14.0f;

    /// <summary>
    /// Fraction of speed lost per second while swinging. Low: the swing is
    /// supposed to keep what gravity gives it.
    /// </summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float GrappleSwingDrag { get; set; } = 0.08f;

    /// <summary>
    /// Hard ceiling on speed across the line. While reeling, speed toward the
    /// anchor is extra, up to <see cref="GrappleReelMaxSpeed"/>, and is only
    /// trimmed once the two together pass this.
    /// </summary>
    [Export(PropertyHint.Range, "5,120,1,suffix:m/s")]
    public float GrappleMaxSpeed { get; set; } = 45.0f;

    /// <summary>
    /// Air drag after letting go at speed, in place of the ordinary air
    /// friction. The ordinary one bleeds a swing down to walking pace in a few
    /// seconds, which is the difference between a swing and a hop.
    /// </summary>
    [Export(PropertyHint.Range, "0,10,0.05,suffix:m/s²")]
    public float GrappleCarryDrag { get; set; } = 0.4f;

    /// <summary>
    /// Reeling to within this of a surface that faces up lets go and hops you
    /// onto it, so perching on top of something is one held key.
    /// </summary>
    [Export(PropertyHint.Range, "0,8,0.1,suffix:m")]
    public float GrapplePerchDistance { get; set; } = 2.5f;

    /// <summary>
    /// Fastest a moving anchor is allowed to drag the line. Guards the rope
    /// against a teleport or a respawn reading as a 900 m/s anchor.
    /// </summary>
    [Export(PropertyHint.Range, "0,120,1,suffix:m/s")]
    public float GrappleAnchorMaxSpeed { get; set; } = 40.0f;

    /// <summary>Camera roll into a swing at full sideways speed.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:°")]
    public float GrappleRollDegrees { get; set; } = 9.0f;

    /// <summary>Sideways speed at which the roll reaches its full angle.</summary>
    [Export(PropertyHint.Range, "1,80,0.5,suffix:m/s")]
    public float GrappleRollFullSpeed { get; set; } = 25.0f;

    /// <summary>FOV widening at full grapple speed. Aiming overrides it.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:°")]
    public float GrappleFovAdd { get; set; } = 12.0f;

    /// <summary>Speed at which the FOV widening reaches its full amount.</summary>
    [Export(PropertyHint.Range, "1,120,1,suffix:m/s")]
    public float GrappleFovFullSpeed { get; set; } = 35.0f;

    /// <summary>
    /// Releasing the hook within this of the press latches a zip: the line
    /// stays attached without the button held. Holding past it is a swing.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,0.8,0.01,suffix:s")]
    public float GrappleTapSeconds { get; set; } = 0.2f;

    /// <summary>How fast a latched zip pulls you toward the anchor.</summary>
    [Export(PropertyHint.Range, "5,120,1,suffix:m/s")]
    public float GrappleZipSpeed { get; set; } = 38.0f;

    /// <summary>Seconds the zip takes to reach full speed, so it eases in rather than snapping.</summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:s")]
    public float GrappleZipEaseIn { get; set; } = 0.18f;

    /// <summary>Within this of the anchor the zip eases out, so arrival is a hang not a slam.</summary>
    [Export(PropertyHint.Range, "0.5,20,0.1,suffix:m")]
    public float GrappleZipEaseOutDistance { get; set; } = 6.0f;

    /// <summary>Sideways nudge from WASD during a zip, in metres per second.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:m/s")]
    public float GrappleZipSteer { get; set; } = 8.0f;

    /// <summary>How long after letting go gravity stays reduced, for a shot at the apex.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:s")]
    public float GrappleApexFloatSeconds { get; set; } = 0.45f;

    /// <summary>Gravity scale at the start of the apex float, easing back to full.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GrappleApexGravityScale { get; set; } = 0.25f;

    /// <summary>
    /// Extra radius past her shell an orbit will not go inside. The player
    /// capsule plus a little air, so the camera stays outside the mesh.
    /// </summary>
    [Export(PropertyHint.Range, "0,8,0.1,suffix:m")]
    public float GrappleOrbitClearance { get; set; } = 1.8f;

    /// <summary>Tangential speed of A/D while orbiting a giant.</summary>
    [Export(PropertyHint.Range, "1,60,0.5,suffix:m/s")]
    public float GrappleOrbitStrafe { get; set; } = 14.0f;

    /// <summary>Climb and descend speed of W/S while orbiting a giant.</summary>
    [Export(PropertyHint.Range, "1,40,0.5,suffix:m/s")]
    public float GrappleOrbitClimb { get; set; } = 10.0f;

    [ExportGroup("Load")]
    /// <summary>
    /// What the player weighs on the line, for what it hangs from: a vine or
    /// a tree bends under this, times the swing (docs/decisions/0032).
    /// </summary>
    [Export(PropertyHint.Range, "0,1000,5,suffix:kg")]
    public float GrappleLoadKg { get; set; } = 90.0f;

    /// <summary>The most the swing multiplies that weight by, at the bottom of a hard arc.</summary>
    [Export(PropertyHint.Range, "1,10,0.1")]
    public float GrappleLoadMaxG { get; set; } = 4.0f;
}
