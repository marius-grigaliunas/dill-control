using Godot;

namespace DillControl.Player;

/// <summary>
/// Every number the first-person controller reads. Nothing in
/// <see cref="PlayerController"/> or <see cref="CameraRig"/> is a literal that
/// affects feel -- if it can be argued about, it is here.
///
/// The canonical instance is
/// <c>res://resources/player/movement_tuning_default.tres</c>. The line has its
/// own resource, <see cref="GrappleTuning"/>, and so does health,
/// <see cref="PlayerHealthTuning"/>.
///
/// Groups below drive the inspector layout.
/// </summary>
[GlobalClass]
public partial class MovementTuning : Resource
{
    // ---------------- Ground movement ----------------

    [ExportGroup("Ground Movement")]
    [Export(PropertyHint.Range, "0.5,20,0.1")]
    public float WalkSpeed { get; set; } = 5.2f;

    [Export(PropertyHint.Range, "0.5,25,0.1")]
    public float SprintSpeed { get; set; } = 8.4f;

    [Export(PropertyHint.Range, "0.5,15,0.1")]
    public float CrouchSpeed { get; set; } = 2.6f;

    /// <summary>How hard the controller drives toward target velocity, in m/s^2.</summary>
    [Export(PropertyHint.Range, "1,200,0.5")]
    public float GroundAcceleration { get; set; } = 60.0f;

    /// <summary>How hard it decelerates with no input, in m/s^2.</summary>
    [Export(PropertyHint.Range, "1,200,0.5")]
    public float GroundFriction { get; set; } = 50.0f;

    /// <summary>Maximum walkable slope. Above this the player slides.</summary>
    [Export(PropertyHint.Range, "10,80,0.5,suffix:°")]
    public float FloorMaxAngleDegrees { get; set; } = 46.0f;

    // ---------------- Air movement ----------------

    [ExportGroup("Air Movement")]
    [Export(PropertyHint.Range, "0.5,15,0.05")]
    public float JumpVelocity { get; set; } = 4.9f;

    /// <summary>Multiplies project gravity. Above 1 gives a snappier, less floaty arc.</summary>
    [Export(PropertyHint.Range, "0.1,4,0.05")]
    public float GravityScale { get; set; } = 1.75f;

    /// <summary>
    /// Fraction of ground acceleration usable in the air. 0 is a committed
    /// jump, 1 is full steering. Somewhere in between is the arcade-vs-sim dial.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float AirControl { get; set; } = 0.35f;

    /// <summary>Air drag, in m/s^2. Keeps air strafing from accumulating speed forever.</summary>
    [Export(PropertyHint.Range, "0,50,0.1")]
    public float AirFriction { get; set; } = 1.5f;

    /// <summary>Grace period after walking off a ledge during which jump still works.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.005,suffix:s")]
    public float CoyoteTime { get; set; } = 0.12f;

    /// <summary>A jump pressed this long before landing still fires on touchdown.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.005,suffix:s")]
    public float JumpBufferTime { get; set; } = 0.14f;

    // ---------------- Stance ----------------

    [ExportGroup("Stance")]
    [Export(PropertyHint.Range, "1,2.5,0.01,suffix:m")]
    public float StandHeight { get; set; } = 1.8f;

    [Export(PropertyHint.Range, "0.5,2,0.01,suffix:m")]
    public float CrouchHeight { get; set; } = 1.05f;

    /// <summary>Metres per second the capsule and eye height change while crouching.</summary>
    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float CrouchTransitionSpeed { get; set; } = 9.0f;

    /// <summary>Eye position below the top of the capsule.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.01,suffix:m")]
    public float EyeHeightOffset { get; set; } = 0.16f;

    // ---------------- Look ----------------

    [ExportGroup("Look")]
    /// <summary>Radians of rotation per pixel of mouse movement.</summary>
    [Export(PropertyHint.Range, "0.0002,0.02,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0022f;

    [Export(PropertyHint.Range, "-90,0,0.5,suffix:°")]
    public float PitchMinDegrees { get; set; } = -89.0f;

    [Export(PropertyHint.Range, "0,90,0.5,suffix:°")]
    public float PitchMaxDegrees { get; set; } = 89.0f;

    [Export]
    public bool InvertY { get; set; }

    // ---------------- Field of view ----------------

    [ExportGroup("Field Of View")]
    [Export(PropertyHint.Range, "50,130,0.5,suffix:°")]
    public float BaseFov { get; set; } = 90.0f;

    /// <summary>Degrees added to <see cref="BaseFov"/> at full sprint speed.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:°")]
    public float SprintFovAdd { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float FovLerpSpeed { get; set; } = 8.0f;

    // ---------------- Headbob ----------------

    [ExportGroup("Headbob")]
    [Export]
    public bool HeadbobEnabled { get; set; } = true;

    /// <summary>Full bob cycles per second at walk speed.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.05")]
    public float HeadbobFrequency { get; set; } = 1.9f;

    [Export(PropertyHint.Range, "0,0.15,0.001,suffix:m")]
    public float HeadbobVerticalAmplitude { get; set; } = 0.035f;

    [Export(PropertyHint.Range, "0,0.15,0.001,suffix:m")]
    public float HeadbobHorizontalAmplitude { get; set; } = 0.022f;

    /// <summary>
    /// How much of extra speed becomes extra cadence rather than a longer
    /// stride, above walk speed only.
    ///
    /// 1.0 is the naive reading -- twice the speed is twice the steps -- and it
    /// is what made sprinting look like hopping: at 8.4 m/s the bob ran at
    /// 4.45 cycles a second, which is 8.9 vertical dips, on an 8 cm throw.
    /// Running does not work that way. A sprinter covers more ground per step
    /// as well as taking more of them, so cadence grows with roughly the square
    /// root of speed and the stride absorbs the rest. At 0.5 the sprint stride
    /// is 1.74 m against the walk's 1.37 m, and the camera rises and falls 27%
    /// more often instead of 134% more often.
    ///
    /// Applied only above walk speed, so crouch-walking and walking keep the
    /// bob they already had.
    /// </summary>
    [Export(PropertyHint.Range, "0.2,1,0.01")]
    public float HeadbobSpeedExponent { get; set; } = 0.5f;

    /// <summary>
    /// Bob amplitude multiplier while sprinting, on top of the stride term.
    /// Below 1 on purpose: a sprint is a flatter, more committed gait than a
    /// jog, and the speed is already being sold by the stride rate and the FOV.
    /// </summary>
    [Export(PropertyHint.Range, "0.2,2,0.05")]
    public float HeadbobSprintAmplitude { get; set; } = 0.85f;

    /// <summary>Camera roll into the bob, in degrees.</summary>
    [Export(PropertyHint.Range, "0,5,0.05,suffix:°")]
    public float HeadbobRollDegrees { get; set; } = 0.55f;

    // ---------------- Aiming ----------------

    [ExportGroup("Aiming")]
    /// <summary>
    /// Headbob amplitude multiplier while aiming. Low, because the whole point
    /// of aiming is that the sight stops moving; leaving the bob at full makes
    /// the zoom actively worse than the hip.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float AimBobScale { get; set; } = 0.25f;

    /// <summary>Sway multiplier while aiming, for the same reason.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float AimSwayScale { get; set; } = 0.35f;

    // ---------------- Camera sway ----------------

    [ExportGroup("Camera Sway")]
    /// <summary>Metres of camera lag per radian/second of look input.</summary>
    [Export(PropertyHint.Range, "0,0.2,0.001")]
    public float SwayAmount { get; set; } = 0.02f;

    [Export(PropertyHint.Range, "0,0.2,0.001,suffix:m")]
    public float SwayMaxOffset { get; set; } = 0.045f;

    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float SwayRecoverSpeed { get; set; } = 7.0f;

    /// <summary>Degrees of roll applied when strafing, opposite the strafe direction.</summary>
    [Export(PropertyHint.Range, "0,8,0.05,suffix:°")]
    public float StrafeRollDegrees { get; set; } = 0.9f;

    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float StrafeRollSpeed { get; set; } = 6.0f;

    // ---------------- Landing punch ----------------

    [ExportGroup("Landing Punch")]
    /// <summary>Metres of camera dip per m/s of downward impact velocity.</summary>
    [Export(PropertyHint.Range, "0,0.05,0.0005")]
    public float LandingPunchPerSpeed { get; set; } = 0.008f;

    [Export(PropertyHint.Range, "0,0.5,0.005,suffix:m")]
    public float LandingPunchMax { get; set; } = 0.14f;

    /// <summary>Impacts slower than this do not register a punch.</summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:m/s")]
    public float LandingPunchMinSpeed { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "1,40,0.1")]
    public float LandingPunchRecoverSpeed { get; set; } = 11.0f;

    // ---------------- Blast shake ----------------
    //
    // Camera shake from explosions. It lives here rather than on the explosion
    // effect because it is camera feel, which is what this resource is for, and
    // because that keeps every camera number in one resource -- the landing
    // punch above is the same argument. The rig accumulates it as one more named
    // term on CameraOffset, so it composes with bob, sway and recoil and leaves
    // no residue when zeroed.
    //
    // Nothing checks line of sight. A pressure wave is not light, and a blast
    // that goes unfelt because a crate stood between you and it is a worse lie
    // than one felt through a wall.

    [ExportGroup("Blast Shake")]

    [Export]
    public bool BlastShakeEnabled { get; set; } = true;

    /// <summary>Metres of positional jitter at full trauma.</summary>
    [Export(PropertyHint.Range, "0,0.6,0.005,suffix:m")]
    public float BlastShakeMaxOffset { get; set; } = 0.12f;

    /// <summary>Degrees of roll at full trauma. The axis the eye notices most.</summary>
    [Export(PropertyHint.Range, "0,12,0.05")]
    public float BlastShakeMaxRollDegrees { get; set; } = 2.4f;

    /// <summary>
    /// Degrees of pitch and yaw at full trauma. Deliberately smaller than the
    /// roll: this term sits under the camera, so pitch and yaw move where shots
    /// go, exactly as recoil does. Shake is allowed to cost accuracy; it is not
    /// allowed to take the aim point away.
    /// </summary>
    [Export(PropertyHint.Range, "0,8,0.05")]
    public float BlastShakeMaxAimDegrees { get; set; } = 1.1f;

    /// <summary>Oscillations per second. High enough to read as a jolt rather than a sway.</summary>
    [Export(PropertyHint.Range, "1,60,0.5,suffix:Hz")]
    public float BlastShakeFrequency { get; set; } = 24.0f;

    /// <summary>How fast trauma bleeds off. One over this is roughly how long a full shake lasts.</summary>
    [Export(PropertyHint.Range, "0.2,12,0.05")]
    public float BlastShakeRecoverSpeed { get; set; } = 2.4f;

    /// <summary>
    /// Blast radius that produces full trauma at the centre. Below it a blast
    /// shakes proportionally less, so a grenade and a bunker buster are felt as
    /// different events with no table mapping one to the other.
    /// </summary>
    [Export(PropertyHint.Range, "1,40,0.5,suffix:m")]
    public float BlastShakeFullRadius { get; set; } = 11.0f;

    /// <summary>
    /// How far a blast is felt, as a multiple of its own radius. Derived from the
    /// blast rather than typed as a distance: a warhead twice the size should
    /// be felt from further away without anyone editing a second number.
    /// </summary>
    [Export(PropertyHint.Range, "1,15,0.1")]
    public float BlastShakeRangeScale { get; set; } = 6.0f;

    // ---------------- Flight ----------------

    [ExportGroup("Flight")]
    /// <summary>
    /// Whether the jetpack exists at all. A level that wants the terrain to
    /// matter turns it off and everything else here goes with it.
    /// </summary>
    [Export]
    public bool FlightEnabled { get; set; } = true;

    /// <summary>Hover drift speed multiplier while sprint is held.</summary>
    [Export(PropertyHint.Range, "1,6,0.05")]
    public float FlightBoostMultiplier { get; set; } = 2.4f;

    /// <summary>
    /// Upward kick given on engaging flight from the ground, so taking off is
    /// one press rather than a press and a scrape along the floor.
    /// </summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:m/s")]
    public float FlightTakeoffImpulse { get; set; } = 4.5f;

    /// <summary>
    /// Seconds of continuous thrust in a full tank. Zero means unlimited,
    /// which is the honest playground setting and not the default, because a
    /// jetpack with no cost makes every piece of terrain optional.
    /// </summary>
    [Export(PropertyHint.Range, "0,120,0.5,suffix:s")]
    public float FlightFuelSeconds { get; set; } = 9.0f;

    /// <summary>Fuel burned per second of boost, on top of the base burn.</summary>
    [Export(PropertyHint.Range, "0,5,0.05")]
    public float FlightBoostBurnMultiplier { get; set; } = 2.2f;

    /// <summary>Seconds on the ground to refill an empty tank.</summary>
    [Export(PropertyHint.Range, "0.5,60,0.5,suffix:s")]
    public float FlightRefuelSeconds { get; set; } = 6.0f;

    // ---------------- Gas ----------------

    [ExportGroup("Gas")]
    /// <summary>
    /// Burst added to velocity by a dash, in the air or on the line.
    /// </summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m/s")]
    public float DashSpeed { get; set; } = 20.0f;

    /// <summary>Burst added to velocity by a dash on the ground.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float DashGroundSpeed { get; set; } = 18.0f;

    /// <summary>
    /// Seconds of the jetpack tank one dash spends. With a 9 s tank this is
    /// three charges. Zero fuel seconds still means unlimited, including dashes.
    /// </summary>
    [Export(PropertyHint.Range, "0,30,0.1,suffix:s")]
    public float DashFuelCost { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0,2,0.01,suffix:s")]
    public float DashCooldown { get; set; } = 0.25f;

    /// <summary>
    /// How long a ground dash keeps its extra speed before ordinary friction
    /// eats it. Without this, ApplyHorizontal drags a ground dash back to
    /// walk speed on the same frame.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:s")]
    public float DashGroundHold { get; set; } = 0.18f;

    /// <summary>
    /// Hover drift speed. The jetpack is a hold-in-the-air for aiming, not
    /// free flight, so this is a crawl.
    /// </summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:m/s")]
    public float HoverDriftSpeed { get; set; } = 4.5f;

    /// <summary>
    /// How hard hover brakes toward its drift target. High is a hover; the
    /// old flight damping was a glide.
    /// </summary>
    [Export(PropertyHint.Range, "1,80,0.5,suffix:m/s²")]
    public float HoverDamping { get; set; } = 22.0f;
}
