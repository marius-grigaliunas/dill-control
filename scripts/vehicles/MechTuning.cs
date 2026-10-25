using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// Every number a walking mech reads: how it moves, how its legs step, how its
/// body rides on them, and how a companion follows and carries the player.
/// Read by <see cref="MechWalker"/> and <see cref="MechCompanion"/>.
///
/// Leg lengths are not here: they come from the model, measured between the
/// pivot voxels at hip, knee and ankle.
/// </summary>
[GlobalClass, Tool]
public partial class MechTuning : Resource
{
    [ExportGroup("Movement")]
    [Export(PropertyHint.Range, "0.5,20,0.1,suffix:m/s")]
    public float MaxSpeed { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "0.5,60,0.5,suffix:m/s²")]
    public float Acceleration { get; set; } = 9.0f;

    [Export(PropertyHint.Range, "0.5,60,0.5,suffix:m/s²")]
    public float Deceleration { get; set; } = 14.0f;

    /// <summary>How fast the body turns toward where it is told to face. The
    /// feet keep up by stepping; there is no turn animation.</summary>
    [Export(PropertyHint.Range, "10,1080,5,suffix:°/s")]
    public float TurnRateDegrees { get; set; } = 300.0f;

    /// <summary>How fast the turret swings toward its aim, independent of
    /// the body. Fast enough to track a mouse flick.</summary>
    [Export(PropertyHint.Range, "10,2000,10,suffix:°/s")]
    public float TurretTurnRateDegrees { get; set; } = 720.0f;

    /// <summary>Multiplies the level's gravity. A heavy machine falls harder
    /// than a person does.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.05")]
    public float GravityScale { get; set; } = 1.8f;

    [ExportGroup("Jump and jets")]
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float JumpSpeed { get; set; } = 11.0f;

    /// <summary>The crouch before take-off. The jump leaves at its end, so
    /// the body visibly loads before it goes.</summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:s")]
    public float JumpWindupSeconds { get; set; } = 0.16f;

    /// <summary>Upward thrust while jump is held in the air, as a fraction of
    /// gravity. Above 1 climbs; below 1 only slows the fall.</summary>
    [Export(PropertyHint.Range, "0,4,0.05")]
    public float JetThrustOfGravity { get; set; } = 1.35f;

    [Export(PropertyHint.Range, "0,20,0.1,suffix:s")]
    public float JetFuelSeconds { get; set; } = 2.0f;

    /// <summary>Fuel refilled per second while standing on the ground.</summary>
    [Export(PropertyHint.Range, "0,10,0.05,suffix:s/s")]
    public float JetRefillPerSecond { get; set; } = 1.0f;

    /// <summary>Steering in the air, as a fraction of ground acceleration.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float AirControl { get; set; } = 0.35f;

    [ExportGroup("Sprint")]
    /// <summary>Top speed while sprinting. 0, or no higher than MaxSpeed, means
    /// this mech cannot sprint (the tank, the storage bot).</summary>
    [Export(PropertyHint.Range, "0,40,0.1,suffix:m/s")]
    public float SprintSpeed { get; set; }

    /// <summary>How long one step takes at sprint speed. Unused when it cannot
    /// sprint.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01,suffix:s")]
    public float SprintStepSeconds { get; set; } = 0.14f;

    /// <summary>How far the body sinks at sprint, on top of the walk crouch
    /// curve, so the longer strides still reach.</summary>
    [Export(PropertyHint.Range, "0,1.5,0.01,suffix:m")]
    public float SprintCrouch { get; set; } = 0.35f;

    /// <summary>Steady nose-down pitch at full sprint, on top of the accel lean.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:°")]
    public float SprintLeanDegrees { get; set; } = 12.0f;

    /// <summary>On its own, it sprints while further than this from its follow
    /// spot. 0 never sprints of its own accord.</summary>
    [Export(PropertyHint.Range, "0,80,0.5,suffix:m")]
    public float FollowSprintDistance { get; set; }

    [ExportGroup("Gait")]
    /// <summary>How far a planted foot may fall behind at full walk speed
    /// before it steps. This is the running stride.</summary>
    [Export(PropertyHint.Range, "0.05,3,0.01,suffix:m")]
    public float StepThreshold { get; set; } = 0.45f;

    /// <summary>The same lag at a creep or a turn in place, so a small
    /// correction is a small step. 0 keeps <see cref="StepThreshold"/> at
    /// every speed.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:m")]
    public float StepThresholdSlow { get; set; }

    /// <summary>How far behind a foot may sit at full sprint, longer than the
    /// run. 0 sprints on <see cref="StepThreshold"/>.</summary>
    [Export(PropertyHint.Range, "0,4,0.01,suffix:m")]
    public float SprintStepThreshold { get; set; }

    /// <summary>Standing still, feet further off than this from rest are
    /// tidied in with a step, so a stop ends square.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.01,suffix:m")]
    public float IdleTidyThreshold { get; set; } = 0.12f;

    /// <summary>How long one step takes at a walk.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01,suffix:s")]
    public float StepSeconds { get; set; } = 0.3f;

    /// <summary>How long one step takes at full speed.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01,suffix:s")]
    public float StepSecondsFast { get; set; } = 0.18f;

    [Export(PropertyHint.Range, "0,2,0.01,suffix:m")]
    public float StepHeight { get; set; } = 0.35f;

    /// <summary>How high a full sprint stride lifts. 0 uses <see cref="StepHeight"/>.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:m")]
    public float SprintStepHeight { get; set; }

    /// <summary>How far ahead of the moving body a foot lands, in seconds of
    /// travel. Landing ahead is what lets the body walk over the foot, not
    /// drag it. <see cref="StepReach"/> caps the distance this aims.</summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:s")]
    public float StepLeadSeconds { get; set; } = 0.2f;

    /// <summary>Furthest ahead of the body a foot may aim at full walk speed.
    /// 0 means no cap. A creep aims a much shorter distance, so small steps
    /// stay small.</summary>
    [Export(PropertyHint.Range, "0,4,0.01,suffix:m")]
    public float StepReach { get; set; }

    /// <summary>Furthest ahead at full sprint. Longer than <see cref="StepReach"/>.
    /// 0 uses the walk reach.</summary>
    [Export(PropertyHint.Range, "0,5,0.01,suffix:m")]
    public float SprintStepReach { get; set; }

    /// <summary>Which way knees bend, in the model's space (+Z is its
    /// front), added to the bend the model was built with. Zero keeps the
    /// modelled bend; a leg modelled straight needs one.</summary>
    [Export]
    public Vector3 KneeBias { get; set; } = Vector3.Zero;

    [ExportGroup("Arms")]
    /// <summary>How far an arm swings, in degrees per metre its opposite foot
    /// is ahead of or behind where it rests.</summary>
    [Export(PropertyHint.Range, "0,90,0.5")]
    public float ArmSwingDegreesPerMetre { get; set; } = 25.0f;

    [Export(PropertyHint.Range, "0,90,1,suffix:°")]
    public float MaxArmSwingDegrees { get; set; } = 30.0f;

    /// <summary>How long the shoulder takes to catch up with the foot it
    /// swings against. 0 locks the arm to the foot. A short follow lets the
    /// swing ease through a plant instead of stopping dead.</summary>
    [Export(PropertyHint.Range, "0,0.4,0.005,suffix:s")]
    public float ArmSwingFollow { get; set; }

    /// <summary>How long an arm takes to raise toward a target, and to drop
    /// once the hold runs out.</summary>
    [Export(PropertyHint.Range, "0.02,1,0.01,suffix:s")]
    public float ArmRaiseSeconds { get; set; } = 0.15f;

    /// <summary>How long an arm stays up after the last AimArm, before it
    /// starts to drop.</summary>
    [Export(PropertyHint.Range, "0,3,0.05,suffix:s")]
    public float ArmAimHoldSeconds { get; set; } = 0.6f;

    /// <summary>Share of the raise blend at which a gun on this arm may fire.
    /// Below it the arm is still coming up.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ArmReadyFraction { get; set; } = 0.8f;

    /// <summary>How far an aiming arm may yaw off the body's nose. Stops a
    /// shoulder from twisting the gun behind its back.</summary>
    [Export(PropertyHint.Range, "0,180,1,suffix:°")]
    public float ArmAimMaxYawDegrees { get; set; } = 70.0f;

    [ExportGroup("Damage")]
    /// <summary>The mech is destroyed when its body is down to this share of
    /// its voxels: everything left falls apart.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float BodyDeathFraction { get; set; } = 0.45f;

    /// <summary>Share of a pickle's bite or spit that reaches the voxels:
    /// armour. A mech is many bites, not one.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float HordeDamageScale { get; set; } = 0.4f;

    /// <summary>Velocity a pickle's strike adds, per unit of its impulse. A
    /// heavy machine barely rocks.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float HordeShove { get; set; } = 0.05f;

    /// <summary>A foot coming down hurts the pickles under it: this much,
    /// over this radius.</summary>
    [Export(PropertyHint.Range, "0,500,1")]
    public float StompDamage { get; set; } = 70.0f;

    [Export(PropertyHint.Range, "0,5,0.05,suffix:m")]
    public float StompRadius { get; set; } = 1.0f;

    [ExportGroup("Broken")]
    /// <summary>Top speed is multiplied by this for every leg lost while some
    /// are left: it limps, one leg at a time (docs/decisions/0046).</summary>
    [Export(PropertyHint.Range, "0.1,1,0.01")]
    public float LimpSpeedPerLostLeg { get; set; } = 0.7f;

    /// <summary>Share of top speed on one leg, with an arm as a crutch.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01")]
    public float HopSpeedScale { get; set; } = 0.35f;

    /// <summary>Share of top speed with no legs, hauled along on its arms.</summary>
    [Export(PropertyHint.Range, "0.02,1,0.01")]
    public float CrawlSpeedScale { get; set; } = 0.2f;

    /// <summary>How long one step of a crutch or a hauling arm takes.</summary>
    [Export(PropertyHint.Range, "0.1,3,0.05,suffix:s")]
    public float CrutchStepSeconds { get; set; } = 0.7f;

    /// <summary>How far out from the shoulder a crutch is planted, sideways,
    /// as a share of the arm's length.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CrutchSpread { get; set; } = 0.35f;

    /// <summary>The body leans toward each leg it has lost: the weight hangs
    /// over the corner nothing holds.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:°")]
    public float MissingLegSagDegrees { get; set; } = 6.0f;

    /// <summary>How far above the ground its belly rests once nothing holds it up.</summary>
    [Export(PropertyHint.Range, "0,2,0.05,suffix:m")]
    public float BellyClearance { get; set; } = 0.1f;

    [ExportGroup("Body")]
    /// <summary>How far the body sinks at full speed, to give the legs more
    /// reach. The voxel legs are short; without it a stride is half a metre.</summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:m")]
    public float WalkCrouch { get; set; } = 0.18f;

    /// <summary>The dip each stepping leg puts into the body.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.005,suffix:m")]
    public float StepBob { get; set; } = 0.05f;

    /// <summary>Lean into acceleration, degrees per m/s².</summary>
    [Export(PropertyHint.Range, "0,5,0.05")]
    public float LeanPerAcceleration { get; set; } = 0.6f;

    [Export(PropertyHint.Range, "0,30,0.5,suffix:°")]
    public float MaxLeanDegrees { get; set; } = 9.0f;

    /// <summary>How hard the body's spring pulls it back to its pose. Higher
    /// is stiffer.</summary>
    [Export(PropertyHint.Range, "1,400,1")]
    public float BodySpring { get; set; } = 140.0f;

    /// <summary>Fraction of critical damping on the body's spring. Below 1 it
    /// bounces after a landing.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.01")]
    public float BodyDamping { get; set; } = 0.55f;

    /// <summary>How fast the body is sent sinking on landing, per m/s it
    /// landed at. The spring brings it back up.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float LandingKick { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "0,2,0.01,suffix:m")]
    public float JumpCrouch { get; set; } = 0.35f;

    [ExportGroup("Companion")]
    /// <summary>Where the mech keeps itself while following: behind the
    /// player by this much...</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")]
    public float FollowBehind { get; set; } = 7.0f;

    /// <summary>...and off to the side by this much.</summary>
    [Export(PropertyHint.Range, "-20,20,0.5,suffix:m")]
    public float FollowSide { get; set; } = 4.0f;

    /// <summary>Within this of its spot it stops walking.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:m")]
    public float FollowArriveRadius { get; set; } = 1.5f;

    /// <summary>Distance over which it slows to a stop.</summary>
    [Export(PropertyHint.Range, "0.1,20,0.1,suffix:m")]
    public float FollowSlowRadius { get; set; } = 5.0f;

    /// <summary>How close to the player it lets itself come. Inside this it
    /// steers around them and backs off, rather than walking through.</summary>
    [Export(PropertyHint.Range, "0,20,0.5,suffix:m")]
    public float PersonalSpace { get; set; } = 6.0f;

    /// <summary>A threat within this of the player draws the mech out to
    /// stand between them and it.</summary>
    [Export(PropertyHint.Range, "0,100,1,suffix:m")]
    public float GuardRadius { get; set; } = 30.0f;

    /// <summary>How far toward the threat from the player it takes its stand.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")]
    public float GuardDistance { get; set; } = 8.0f;

    /// <summary>However it fights, it never goes further than this from the
    /// player.</summary>
    [Export(PropertyHint.Range, "5,200,1,suffix:m")]
    public float LeashRange { get; set; } = 25.0f;

    /// <summary>How close the player has to be to climb on.</summary>
    [Export(PropertyHint.Range, "1,20,0.5,suffix:m")]
    public float MountRange { get; set; } = 9.0f;

    /// <summary>Ridden, the body turns to the camera even standing still.
    /// For a mech with no turret, whose guns point where its body does.</summary>
    [Export]
    public bool FaceCameraWhenIdle { get; set; }

    [ExportGroup("Ride camera")]
    [Export(PropertyHint.Range, "2,40,0.5,suffix:m")]
    public float CameraDistance { get; set; } = 11.0f;

    /// <summary>Height of the point the camera orbits, above the mech's feet.</summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:m")]
    public float CameraHeight { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "0.0001,0.02,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0025f;

    /// <summary>Radians per second at full right-stick deflection.</summary>
    [Export(PropertyHint.Range, "0.5,8,0.05,suffix:rad/s")]
    public float StickLookSpeed { get; set; } = 2.6f;

    /// <summary>Power curve on the right stick. 1 is linear.</summary>
    [Export(PropertyHint.Range, "1,3,0.05")]
    public float StickLookCurve { get; set; } = 1.5f;
}
