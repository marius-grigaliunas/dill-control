using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// Everything tunable about a <see cref="FlightRig"/>. Part of the portable
/// module -- see this folder's README -- so nothing here names a game, a
/// player, a level or a weapon.
///
/// Flat properties rather than a nested resource per mode, on purpose: a
/// tuning panel that reflects over a resource's own property list (this
/// project's does) only walks the top level, and [ExportGroup] here becomes
/// its section headings for free.
/// </summary>
[GlobalClass]
public partial class FlightRigStats : Resource
{
    /// <summary>
    /// Acceleration and speed while hovering: omnidirectional, and -- with
    /// <see cref="HoverHoldEnabled"/> on -- height-assisted, so idling and
    /// close manoeuvring never fight gravity by hand.
    /// </summary>
    [ExportGroup("Hover Mode")]
    [Export(PropertyHint.Range, "1,200,0.5,suffix:m/s2")]
    public float HoverMainThrust { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "1,200,0.5,suffix:m/s2")]
    public float HoverStrafeThrust { get; set; } = 10.0f;

    /// <summary>
    /// Fallback climb/dive acceleration used only when the height hold has no
    /// ground to reference -- see <see cref="FlightRig"/>'s hover-hold notes.
    /// Must exceed gravity * (1 - <see cref="GravityCompensation"/>), or "up"
    /// silently loses to "down" and holding climb never actually climbs. At the
    /// default compensation of 1.0 that threshold is zero and this is pure
    /// climb authority; at 0 it is the full well, which is the case the warning
    /// was written for.
    /// </summary>
    [Export(PropertyHint.Range, "1,200,0.5,suffix:m/s2")]
    public float HoverVerticalThrust { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "5,300,1,suffix:m/s")]
    public float HoverCruiseSpeed { get; set; } = 22.0f;

    /// <summary>
    /// Acceleration and speed while throttled up: forward-biased, fast, and
    /// deliberately clumsy sideways -- the trade a vehicle with one real
    /// drive axis makes. This is the mode that covers ground.
    /// </summary>
    [ExportGroup("Cruise Mode")]
    [Export(PropertyHint.Range, "1,200,0.5,suffix:m/s2")]
    public float CruiseMainThrust { get; set; } = 22.0f;

    [Export(PropertyHint.Range, "1,200,0.5,suffix:m/s2")]
    public float CruiseStrafeThrust { get; set; } = 2.5f;

    /// <summary>
    /// Must exceed gravity * (1 - <see cref="GravityCompensation"/>), for the
    /// same reason <see cref="HoverVerticalThrust"/> does. It was 2.5 against a
    /// planet pulling 9.8 -- holding climb could never do more than fall
    /// slightly slower, which reads as broken because it was: net acceleration
    /// was negative the entire time.
    /// </summary>
    [Export(PropertyHint.Range, "1,200,0.5,suffix:m/s2")]
    public float CruiseVerticalThrust { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "5,300,1,suffix:m/s")]
    public float CruiseSpeed { get; set; } = 42.0f;

    /// <summary>
    /// Share of local gravity the drive cancels while <see cref="FlightRig.Active"/>.
    ///
    /// At 1.0 the vehicle goes where its nose points and level flight costs
    /// nothing. At 0 it flies as it did before this field existed, which
    /// against a 9.8 m/s2 well meant one of two things: hold the climb key
    /// forever, or fly at a permanent asin(9.8 / MainThrust) of nose-up -- 26
    /// degrees at the old Cruise thrust. Let go of everything and the rig sank
    /// at gravity / LinearDamping, 11.5 m/s. That is what "you point at the sky
    /// for a while, turn back, and you are still near the ground" was.
    ///
    /// This is NOT the orientation auto-levelling this module refuses to do.
    /// It never touches the hull's basis; it is one term in the translational
    /// integration. Landing is still aiming the belly at the ground and killing
    /// your speed, because the belly still points wherever you pointed it.
    ///
    /// Applied only while Active, deliberately: a parked rig, or one whose
    /// pilot got off, takes the full well and falls out of the sky.
    /// </summary>
    [ExportGroup("Assist")]
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GravityCompensation { get; set; } = 1.0f;

    [ExportGroup("Boost")]
    [Export(PropertyHint.Range, "1,4,0.05")]
    public float BoostMultiplier { get; set; } = 1.8f;

    [ExportGroup("Handling")]
    [Export(PropertyHint.Range, "0.0002,0.02,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0020f;

    [Export(PropertyHint.Range, "0.1,6,0.05,suffix:rad/s")]
    public float RollSpeed { get; set; } = 1.4f;

    /// <summary>
    /// How fast the rig sheds velocity with the throttle off, per second. Not
    /// zero, and not high -- see <see cref="FlightRig"/>'s equilibrium note.
    /// This is the one number to move if flight feels wrong.
    /// </summary>
    [Export(PropertyHint.Range, "0,4,0.01")]
    public float LinearDamping { get; set; } = 0.85f;

    /// <summary>
    /// How fast the rig sheds velocity while <b>not</b> Active, per second.
    /// Much higher than <see cref="LinearDamping"/>, deliberately: a pilot who
    /// gets off at speed leaves behind a vehicle still carrying that speed, and
    /// at flight damping it coasts the same speed / LinearDamping metres a
    /// throttled-off flight does -- dozens of metres, which is a parked vehicle
    /// drifting out of reach of the person who just parked it. Nothing else
    /// about a parked rig changes: it still takes the full gravity well (see
    /// <see cref="GravityCompensation"/>) and still falls.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,20,0.1")]
    public float ParkedDamping { get; set; } = 6.0f;

    /// <summary>
    /// Speed below which a parked rig in contact with something is simply
    /// stopped. Floating motion mode has no floor and therefore no friction,
    /// so a landed rig slides along the ground forever on whatever tangential
    /// velocity the slide response left it -- <see cref="ParkedDamping"/> only
    /// ever makes that asymptotically small, never zero.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:m/s")]
    public float ParkedRestSpeed { get; set; } = 2.5f;

    /// <summary>
    /// Velocity retained per physics frame while a collision is in contact.
    /// Godot's slide response removes only the into-the-surface component and
    /// lets the tangential part carry on unchanged, which reads as a pinball
    /// table: graze one obstacle and you carry full speed into the next one.
    /// This bleeds a scrape down over a handful of frames instead.
    /// </summary>
    [Export(PropertyHint.Range, "0.3,1.0,0.01")]
    public float CollisionSpeedRetention { get; set; } = 0.8f;

    [ExportGroup("Camera")]
    [Export(PropertyHint.Range, "50,130,0.5,suffix:°")]
    public float Fov { get; set; } = 88.0f;

    /// <summary>
    /// Degrees added to <see cref="Fov"/> at <see cref="FovSpeedReference"/>.
    /// A speed with no reference frame is a number on a label; widening the
    /// lens is the reference frame.
    /// </summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:°")]
    public float FovSpeedWiden { get; set; } = 18.0f;

    /// <summary>Speed at which the widening is fully applied. Set it to the
    /// fastest the rig actually goes -- a mode's cruise speed times
    /// <see cref="BoostMultiplier"/> -- so the lens reads the whole range.</summary>
    [Export(PropertyHint.Range, "5,400,1,suffix:m/s")]
    public float FovSpeedReference { get; set; } = 104.0f;

    [Export(PropertyHint.Range, "0.5,20,0.1")]
    public float FovLerpSpeed { get; set; } = 4.0f;

    /// <summary>
    /// DECISION -- Hover mode holds a height above the ground instead of
    /// leaving vertical to raw thrust. Reported as "hover is very slow" and,
    /// underneath that, literally unable to hold altitude: the old
    /// HoverVerticalThrust (8) was still less than a 9.8 gravity well, so
    /// even full climb input lost to gravity and the rig sank regardless.
    /// This is not the auto-levelling CLAUDE.md rules out elsewhere -- that
    /// is about the hull's *orientation* snapping upright over a planet, and
    /// still never happens. This is a height servo on one axis, the same
    /// category of assist a hovercraft's ground effect gives for free, and it
    /// only runs in Hover mode with a ground within <see cref="HoverHoldMaxRange"/>
    /// below -- lose the ground and control reverts to plain thrust.
    /// </summary>
    [ExportGroup("Hover Hold")]
    [Export]
    public bool HoverHoldEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0.5,40,0.5,suffix:m")]
    public float HoverHoldDefaultHeight { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "0.5,10,0.5,suffix:m")]
    public float HoverHoldMinHeight { get; set; } = 1.5f;

    [Export(PropertyHint.Range, "5,100,1,suffix:m")]
    public float HoverHoldMaxHeight { get; set; } = 40.0f;

    /// <summary>How fast holding climb/dive raises or lowers the target
    /// height. This is what "you control higher or lower with space and
    /// control" actually adjusts -- not a raw thrust, a target the servo
    /// then holds.</summary>
    [Export(PropertyHint.Range, "0.5,20,0.5,suffix:m/s")]
    public float HoverHoldAdjustRate { get; set; } = 4.0f;

    /// <summary>Vertical speed commanded per metre of height error. Higher
    /// snaps to the target height faster; too high oscillates.</summary>
    [Export(PropertyHint.Range, "0.2,10,0.1")]
    public float HoverHoldGain { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "1,30,0.5,suffix:m/s")]
    public float HoverHoldMaxSpeed { get; set; } = 6.0f;

    /// <summary>How far down the hold's own ground ray reaches. Beyond this
    /// there is no ground to hold against, and Hover mode falls back to
    /// plain <see cref="HoverVerticalThrust"/> control.</summary>
    [Export(PropertyHint.Range, "5,500,1,suffix:m")]
    public float HoverHoldMaxRange { get; set; } = 120.0f;
}
