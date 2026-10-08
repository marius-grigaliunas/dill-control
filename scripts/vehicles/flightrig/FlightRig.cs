using Godot;

namespace DillControl.Vehicles;

/// <summary>Which thrust profile is active. See FlightRigStats.Hover* and
/// FlightRigStats.Cruise* for what each one actually changes.</summary>
public enum FlightMode
{
    Hover,
    Cruise,
}

/// <summary>
/// A six-degrees-of-freedom floating flight controller: thrust in every
/// direction, no auto-levelling, gravity read from the physics server rather
/// than owned. Part of a portable module -- see this folder's README -- so
/// nothing in this file names a game, a player, a weapon or a level. A game
/// wires up boarding, HUD, weapons and cameras against this rig's small
/// public surface (<see cref="Active"/>, <see cref="Mode"/>, <see cref="SetActive"/>,
/// the inherited <c>Velocity</c>) from a separate script, rather than this
/// class knowing about any of it.
///
/// DECISION -- CharacterBody3D in floating mode, not a RigidBody3D.
/// Direct authority over velocity is what feel iteration needs; the
/// alternative is tuning force curves and inertia tensors instead of thrusts
/// and damping. Floating motion mode drops all the floor logic a walker
/// wants and this does not, so MoveAndSlide here is pure collide-and-slide.
///
/// DECISION -- no orientation auto-levelling. The rig never rotates itself
/// upright over a planet. Landing is aiming the belly at the ground and
/// killing your speed, which is the skill flying is supposed to test.
///
/// DECISION -- Hover mode holds a height above the ground, Cruise mode does
/// not. This is a *different* assist than the orientation one this class
/// still refuses to do: a height servo on one axis, active only in Hover
/// mode and only while a ground plane is within reach below. See
/// FlightRigStats.HoverHoldEnabled for the reasoning.
///
/// DECISION -- the drive cancels gravity while it is being flown. Same
/// category of assist as the height servo and the same distinction from
/// auto-levelling: it is one term in the translational integration and never
/// touches the hull's basis. Without it, holding a heading in Cruise costs
/// either a permanently held climb key or a permanent nose-up trim, which is
/// what made Cruise mode read as pointing at the sky and getting nowhere.
/// See FlightRigStats.GravityCompensation.
///
/// DECISION -- a parked rig brakes, a flying one coasts. Disarming does not
/// zero Velocity outright (a rig disarmed in mid-air should still arc, not
/// stop dead in the sky), but it switches to FlightRigStats.ParkedDamping and
/// stops entirely once it is slow and in contact with something. Without
/// that, stepping off at cruise speed left the vehicle carrying that speed
/// under flight damping -- and with no floor and therefore no friction in
/// floating motion mode, it skated away from wherever it was set down.
/// </summary>
public partial class FlightRig : CharacterBody3D
{
    [Export]
    public FlightRigStats Stats { get; set; }

    /// <summary>The rig's own camera. Made current on activation, not
    /// current otherwise, so a game can freely switch between this and
    /// whatever else is looking at the world.</summary>
    [Export]
    public Camera3D Camera { get; set; }

    /// <summary>Collision mask the hover-hold's ground ray tests against.
    /// Defaults to bit 0 -- most projects, this one included, put static
    /// world geometry there.</summary>
    [Export(PropertyHint.Layers3DPhysics)]
    public uint GroundMask { get; set; } = 1u;

    [ExportGroup("Input Actions")]
    [Export] public StringName MoveForwardAction { get; set; } = "move_forward";
    [Export] public StringName MoveBackAction { get; set; } = "move_back";
    [Export] public StringName MoveLeftAction { get; set; } = "move_left";
    [Export] public StringName MoveRightAction { get; set; } = "move_right";
    [Export] public StringName ClimbAction { get; set; } = "jump";
    [Export] public StringName DiveAction { get; set; } = "crouch";
    [Export] public StringName BoostAction { get; set; } = "sprint";
    [Export] public StringName RollLeftAction { get; set; } = "ship_roll_left";
    [Export] public StringName RollRightAction { get; set; } = "ship_roll_right";
    [Export] public StringName ModeToggleAction { get; set; } = "ship_mode_toggle";

    /// <summary>Whether the rig is currently reading input and applying
    /// thrust. A game toggles this on boarding/leaving, arming/disarming, or
    /// whatever its own concept of "in control of this" is.</summary>
    public bool Active { get; private set; }

    /// <summary>Starts in Hover on every activation, deliberately -- see
    /// SetActive. Hover is the mode safe to find yourself in without
    /// immediately rocketing off or dropping like a stone.</summary>
    public FlightMode Mode { get; private set; } = FlightMode.Hover;

    /// <summary>True this frame if Hover mode's height servo found ground to
    /// hold against. A game's HUD can read this to show "holding" vs.
    /// "falling back to manual climb" if it wants to.</summary>
    public bool HoverHolding { get; private set; }

    /// <summary>
    /// Distance to whatever is directly below along gravity's own down, or -1
    /// when nothing is within <see cref="FlightRigStats.HoverHoldMaxRange"/>.
    /// Probed every physics frame while <see cref="Active"/>: Hover mode's
    /// height servo reads it, and a game's HUD can show it as an altimeter in
    /// either mode -- which matters most in Cruise, where nothing else tells a
    /// pilot how far off the ground they are.
    /// </summary>
    public float GroundDistance { get; private set; } = -1.0f;

    private float _hoverTargetHeight;
    private bool _hoverTargetInitialised;

    public override void _Ready()
    {
        if (Stats == null)
        {
            GD.PushError("FlightRig has no FlightRigStats assigned.");
            SetPhysicsProcess(false);
            return;
        }

        // Floating: no floor, no snap, no slope logic. This is not a walker.
        MotionMode = MotionModeEnum.Floating;

        if (Camera != null)
        {
            Camera.Fov = Stats.Fov;
        }
    }

    /// <summary>
    /// Arms or disarms the rig. Resets to Hover and re-homes the hover-hold
    /// target every time it arms, so a game never has to reset either itself.
    /// </summary>
    public void SetActive(bool active)
    {
        Active = active;
        Mode = FlightMode.Hover;
        _hoverTargetInitialised = false;

        if (Camera != null)
        {
            Camera.Current = active;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Active || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }

        if (@event.IsActionPressed(ModeToggleAction))
        {
            Mode = Mode == FlightMode.Hover ? FlightMode.Cruise : FlightMode.Hover;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            // Object-local on both axes, and no pitch clamp. There is no up
            // out here to be upside down relative to.
            RotateObjectLocal(Vector3.Up, -motion.Relative.X * Stats.MouseSensitivity);
            RotateObjectLocal(Vector3.Right, -motion.Relative.Y * Stats.MouseSensitivity);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;
        bool boosting = false;

        if (Active)
        {
            boosting = Input.IsActionPressed(BoostAction);
            ApplyRoll(dt);
            velocity += ThrustAcceleration(boosting) * dt;
        }

        // Gravity, less whatever the drive cancels. Compensation applies only
        // while Active, so a parked rig stays parked and one whose pilot got
        // off falls out of the sky exactly as it always did.
        //
        // At full compensation the rig goes where its nose points. Before this,
        // holding a heading in Cruise cost either the climb key or a permanent
        // asin(gravity / MainThrust) of nose-up trim, and letting go of
        // everything sank at gravity / LinearDamping. This is a translational
        // term: GlobalBasis is untouched here and everywhere else in this
        // method, so the no-auto-levelling rule above is intact.
        float compensation = Active
            ? Mathf.Clamp(Stats.GravityCompensation, 0.0f, 1.0f)
            : 0.0f;
        velocity += GetGravity() * (1.0f - compensation) * dt;

        // Parked rigs shed speed far faster than flying ones. Flight damping on
        // a vehicle whose pilot just stepped off means it keeps coasting the
        // full speed / LinearDamping metres, which is a bike you landed and
        // left drifting away over the horizon while you watch.
        float damping = Active ? Stats.LinearDamping : Stats.ParkedDamping;
        velocity = velocity.Lerp(Vector3.Zero, Mathf.Clamp(damping * dt, 0.0f, 1.0f));

        // The cap itself rises under boost -- holding boost only reaching the
        // same ceiling faster would never actually go anywhere quicker.
        float cruiseSpeed = Mode == FlightMode.Cruise ? Stats.CruiseSpeed : Stats.HoverCruiseSpeed;
        Velocity = velocity.LimitLength(boosting ? cruiseSpeed * Stats.BoostMultiplier : cruiseSpeed);

        ProbeGround();

        if (Active && Mode == FlightMode.Hover && Stats.HoverHoldEnabled)
        {
            ApplyHoverHold(dt);
        }
        else
        {
            HoverHolding = false;
        }

        MoveAndSlide();

        // Godot's slide response removes only the into-the-surface velocity
        // component and lets the tangential part carry on at full speed,
        // which reads as bouncing off one obstacle straight into the next at
        // the same speed you hit the first with. This bleeds it down instead.
        if (GetSlideCollisionCount() > 0)
        {
            Velocity *= Stats.CollisionSpeedRetention;

            // Floating motion mode has no floor and so no friction: a parked
            // rig that has landed keeps whatever tangential velocity the slide
            // response left it and skates off across the ground. Damping alone
            // only ever makes that asymptotically small. Below walking pace and
            // touching something, a parked rig is simply parked.
            if (!Active && Velocity.LengthSquared()
                <= Stats.ParkedRestSpeed * Stats.ParkedRestSpeed)
            {
                Velocity = Vector3.Zero;
            }
        }
    }

    /// <summary>
    /// Widens the lens with speed. A number on a HUD is not a sensation; the
    /// lens is. In _Process rather than _PhysicsProcess because it is purely
    /// visual and the physics tick would stair-step it. Lerped rather than set,
    /// so a collision that halves your speed in one frame does not snap it.
    /// Disarming sets Active false, so the lens eases back on its own.
    /// </summary>
    public override void _Process(double delta)
    {
        if (Camera == null || Stats == null)
        {
            return;
        }

        float reach = Stats.FovSpeedReference > 0.01f
            ? Mathf.Clamp(Velocity.Length() / Stats.FovSpeedReference, 0.0f, 1.0f)
            : 0.0f;

        float target = Stats.Fov + (Active ? Stats.FovSpeedWiden * reach : 0.0f);

        Camera.Fov = Mathf.Lerp(
            Camera.Fov,
            target,
            Mathf.Clamp(Stats.FovLerpSpeed * (float)delta, 0.0f, 1.0f));
    }

    /// <summary>
    /// One ray straight down along gravity, every physics frame while Active.
    /// Hoisted out of ApplyHoverHold so the reading also exists in Cruise mode,
    /// where the servo never runs but a pilot still wants an altimeter.
    /// </summary>
    private void ProbeGround()
    {
        GroundDistance = -1.0f;
        if (!Active)
        {
            return;
        }

        Vector3 gravity = GetGravity();

        // Gravity's own up, not the hull's, so it keeps reading while rolled.
        // With no gravity at all -- drifting between fields -- the hull's up is
        // the only guess available, and better than refusing to look.
        Vector3 up = gravity.LengthSquared() > 1e-6f
            ? -gravity.Normalized()
            : GlobalBasis.Y;

        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            GlobalPosition, GlobalPosition - (up * Stats.HoverHoldMaxRange));
        query.CollisionMask = GroundMask;

        Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count > 0)
        {
            GroundDistance = GlobalPosition.DistanceTo(result["position"].AsVector3());
        }
    }

    private void ApplyRoll(float dt)
    {
        float roll = Input.GetAxis(RollLeftAction, RollRightAction);
        if (roll != 0.0f)
        {
            // About -Z, the direction the nose points: a positive angle is clockwise
            // seen from the seat, so E (the positive axis) banks right.
            RotateObjectLocal(Vector3.Forward, roll * Stats.RollSpeed * dt);
        }
    }

    /// <summary>
    /// Thrust in world space. The movement keys keep the meaning they have on
    /// foot -- forward is forward, climb is up -- so the hands do not learn a
    /// second vocabulary to fly. Vertical here is Cruise mode's only climb
    /// control, and Hover mode's fallback when ApplyHoverHold finds no ground.
    /// </summary>
    private Vector3 ThrustAcceleration(bool boosting)
    {
        (float main, float strafe, float vertical) = Mode == FlightMode.Cruise
            ? (Stats.CruiseMainThrust, Stats.CruiseStrafeThrust, Stats.CruiseVerticalThrust)
            : (Stats.HoverMainThrust, Stats.HoverStrafeThrust, Stats.HoverVerticalThrust);

        Vector3 local = new(
            Input.GetAxis(MoveLeftAction, MoveRightAction) * strafe,
            Input.GetAxis(DiveAction, ClimbAction) * vertical,
            Input.GetAxis(MoveForwardAction, MoveBackAction) * main);

        if (local == Vector3.Zero)
        {
            return Vector3.Zero;
        }

        if (boosting)
        {
            local *= Stats.BoostMultiplier;
        }

        return GlobalBasis * local;
    }

    /// <summary>
    /// Holds Hover mode at a target height above whatever is directly below,
    /// instead of leaving vertical to raw thrust the way Cruise mode does.
    ///
    /// Rays straight down along "up" (gravity's own direction, not the hull's,
    /// so it still works while rolled); if it finds ground within
    /// HoverHoldMaxRange, a target height -- raised or lowered by the same
    /// climb/dive input Cruise mode reads as thrust -- is held by commanding a
    /// vertical speed proportional to the height error, clamped to
    /// HoverHoldMaxSpeed. That overrides whatever gravity and damping already
    /// did to Velocity's vertical component this frame; that is the whole
    /// point of a hold. No ground in range (too high, or drifting between
    /// gravity fields) and this does nothing, leaving ThrustAcceleration's
    /// plain HoverVerticalThrust as the only climb control.
    /// </summary>
    private void ApplyHoverHold(float dt)
    {
        Vector3 gravity = GetGravity();

        // No well to hold against, or no ground within reach: fall back to
        // plain vertical thrust, which is what HoverVerticalThrust is for.
        if (gravity == Vector3.Zero || GroundDistance < 0.0f)
        {
            HoverHolding = false;
            return;
        }

        Vector3 up = -gravity.Normalized();

        if (!_hoverTargetInitialised)
        {
            _hoverTargetHeight = Stats.HoverHoldDefaultHeight;
            _hoverTargetInitialised = true;
        }

        float climb = Input.GetAxis(DiveAction, ClimbAction);
        _hoverTargetHeight = Mathf.Clamp(
            _hoverTargetHeight + (climb * Stats.HoverHoldAdjustRate * dt),
            Stats.HoverHoldMinHeight,
            Stats.HoverHoldMaxHeight);

        float heightError = _hoverTargetHeight - GroundDistance;
        float desiredVerticalSpeed = Mathf.Clamp(
            heightError * Stats.HoverHoldGain,
            -Stats.HoverHoldMaxSpeed,
            Stats.HoverHoldMaxSpeed);

        Vector3 velocity = Velocity;
        velocity -= up * velocity.Dot(up);
        velocity += up * desiredVerticalSpeed;
        Velocity = velocity;
        HoverHolding = true;
    }
}
