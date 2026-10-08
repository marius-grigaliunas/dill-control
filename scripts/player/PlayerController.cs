using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Player;

/// <summary>
/// First-person locomotion.
///
/// DECISION -- CharacterBody3D over RigidBody3D under Jolt.
/// A rigid-body controller gets physical interaction for free (getting shoved
/// by a gator, riding a moving platform, being pushed by current) but every
/// feel change becomes an argument with the solver: you tune friction
/// materials and force curves instead of speeds and accelerations, and
/// stair-stepping and slope handling need custom work either way.
/// CharacterBody3D gives direct authority over velocity, which is what a month
/// of feel iteration actually needs. Jolt's MoveAndSlide is well-behaved, and
/// its motion_queries/use_enhanced_internal_edge_removal default fixes the
/// snagging-on-seams problem that used to argue for rigid bodies.
/// Cost: physical shove-back in month 2 becomes an explicit impulse this
/// script applies to its own velocity rather than something the solver does.
/// That is a contained, known cost and it does not change this file's shape.
///
/// This node owns yaw. Pitch belongs to CameraRig. All procedural camera
/// motion belongs to the rig's offset child. Nothing here reaches outside its
/// own scene: children arrive as [Export] references wired in the scene, and
/// cross-system traffic goes through EventBus.
///
/// Ported from the lab without its wading and coolant-wet ground, which were
/// features of the cut swamp.
/// </summary>
public partial class PlayerController : CharacterBody3D, IBlastReceiver
{
    [ExportGroup("Data")]
    [Export]
    public MovementTuning Tuning { get; set; }

    [Export]
    public PlayerHealthTuning HealthTuning { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export]
    public CameraRig CameraRig { get; set; }

    [Export]
    public CollisionShape3D BodyCollision { get; set; }

    /// <summary>Cast upward to test for headroom before standing back up.</summary>
    [Export]
    public ShapeCast3D HeadroomCheck { get; set; }

    /// <summary>
    /// The line. It throws the hook and holds the anchor; everything that
    /// changes velocity -- the rope, the swing, the reel -- is here, because
    /// this is the one place with authority over velocity. Its numbers are on
    /// the hook's own <see cref="GrappleTuning"/>.
    /// </summary>
    [Export]
    public GrappleHook Grapple { get; set; }

    /// <summary>The line's tuning. Null when there is no hook or it has none.</summary>
    private GrappleTuning Line => Grapple?.Tuning;

    /// <summary>Local signal for anything inside the player scene.</summary>
    [Signal]
    public delegate void StanceChangedEventHandler(bool crouched);

    private CapsuleShape3D _capsule;
    private float _currentHeight;
    private float _targetHeight;

    private bool _crouchHeld;
    private bool _isCrouched;

    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool _wasOnFloor;
    private float _fallSpeed;

    private bool _stowed;

    /// <summary>
    /// True while the player is riding something. Note that a stowed player is
    /// hidden and not simulating but has <b>not moved</b> -- anything keying
    /// off the player's position while stowed is reading where they boarded,
    /// not where they are.
    /// </summary>
    public bool IsStowed => _stowed;

    /// <summary>
    /// True while something else wants the mouse's relative motion for its own
    /// purpose. Nothing uses it at the moment; the build radial's wedge picker
    /// did, until the contract loop was replaced by the nest compound.
    ///
    /// Kept because the problem it solves comes back with the next on-screen
    /// picker that keeps the mouse captured: the same InputEventMouseMotion
    /// that steers the picker also reaches _UnhandledInput and spins the
    /// camera, unless the picker marks every motion handled. That is what "the
    /// menu doesn't pause, so I can't really select" was -- picking and aiming
    /// fighting over one motion delta.
    /// </summary>
    private bool _lookSuppressed;

    private PlayerState _state = PlayerState.Idle;
    /// <summary>
    /// Impact speed above which a landing reads as hard, in metres per second.
    ///
    /// A feel constant with a sound attached rather than a tuning value: it
    /// picks which of two placeholders plays and nothing else, and there is
    /// nothing to argue about until the placeholders are real recordings.
    /// </summary>
    private const float HardLandingSpeed = 9.0f;

    private SoundLoop _jetpackLoop;

    private Vector3 _pendingImpulse;

    public bool IsCrouched => _isCrouched;
    public bool IsSprinting { get; private set; }
    public PlayerState State => _state;

    /// <summary>
    /// Which way is up for this player right now: the opposite of the gravity
    /// at its own position. Pushed outward to whoever needs it; nothing goes
    /// looking up the tree for it.
    /// </summary>
    public Vector3 GravityUp { get; private set; } = Vector3.Up;

    /// <summary>True while the jetpack is engaged.</summary>
    public bool IsFlying { get; private set; }

    /// <summary>Remaining jetpack fuel, 0..1. Always 1 when fuel is disabled.</summary>
    public float FlightFuel01 => Tuning == null || Tuning.FlightFuelSeconds <= 0.0f
        ? 1.0f
        : Mathf.Clamp(_flightFuel / Tuning.FlightFuelSeconds, 0.0f, 1.0f);

    private float _flightFuel = float.MaxValue;

    /// <summary>
    /// True once the jetpack has actually got the player off the ground.
    ///
    /// The landing check below cannot fire before this. Engaging from standing
    /// leaves onFloor true for the rest of that same frame -- MoveAndSlide has
    /// not run yet -- so without this the toggle switched flight on and the
    /// landing rule switched it straight back off, and the jetpack appeared to
    /// do nothing at all unless you happened to be mid-jump.
    /// </summary>
    private bool _flightLeftGround;

    /// <summary>True this physics frame while hanging from or being hauled by the line.</summary>
    private bool _swinging;

    /// <summary>Seconds of zip already spent, for the ease-in.</summary>
    private float _zipElapsed;

    /// <summary>Remaining apex-float time after letting go of the line.</summary>
    private float _apexTimer;

    private bool _orbitInit;
    private float _orbitAngle;
    private float _orbitHeight;
    private float _orbitRadius;

    private float _dashCooldown;
    private float _dashHoldTimer;

    /// <summary>
    /// Let go of the line fast and not yet landed. While set, air friction
    /// gives way to <see cref="MovementTuning.GrappleCarryDrag"/> and air
    /// steering turns the velocity instead of dragging it toward walking pace
    /// -- without this a swing is thrown away the instant it is released, and
    /// that throw is the thing the swing was for.
    /// </summary>
    private bool _momentumCarry;

    /// <summary>True while the grapple line is attached. Read by the HUD.</summary>
    public bool IsGrappling => Grapple != null && Grapple.IsAttached;

    /// <summary>Zip, swing or orbit. Swing when the line is idle.</summary>
    public GrappleLineMode GrappleMode => Grapple != null && Grapple.IsActive
        ? Grapple.Mode
        : GrappleLineMode.Swing;

    /// <summary>How many dash charges the tank currently holds.</summary>
    public int DashCharges
    {
        get
        {
            if (Tuning == null || Tuning.FlightFuelSeconds <= 0.0f || Tuning.DashFuelCost <= 0.0f)
            {
                return int.MaxValue;
            }

            return Mathf.FloorToInt((_flightFuel + 0.001f) / Tuning.DashFuelCost);
        }
    }

    /// <summary>How many dash charges a full tank holds.</summary>
    public int DashChargesMax
    {
        get
        {
            if (Tuning == null || Tuning.FlightFuelSeconds <= 0.0f || Tuning.DashFuelCost <= 0.0f)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.FloorToInt(Tuning.FlightFuelSeconds / Tuning.DashFuelCost));
        }
    }

    /// <summary>
    /// Speed across the ground plane, ignoring motion along gravity. Sliding
    /// along UpDirection rather than dropping Y is what keeps this honest when
    /// the ground plane is the side of a planet.
    /// </summary>
    public float HorizontalSpeed => Velocity.Slide(UpDirection).Length();

    public override void _Ready()
    {
        if (Tuning == null)
        {
            GD.PushError("PlayerController has no MovementTuning assigned.");
            SetPhysicsProcess(false);
            return;
        }

        if (BodyCollision?.Shape is not CapsuleShape3D capsule)
        {
            GD.PushError("PlayerController expects BodyCollision to hold a CapsuleShape3D.");
            SetPhysicsProcess(false);
            return;
        }

        // Built here rather than authored into player.tscn: it has no
        // collaborators but the tuning, and a child keeps ticking while the
        // controller's own processing is off on the bike.
        Health = new PlayerHealth { Name = "Health", Tuning = HealthTuning };
        AddChild(Health);

        // The shape resource is shared by default; a local copy stops runtime
        // crouch resizing from writing back into the scene file.
        _capsule = (CapsuleShape3D)capsule.Duplicate();
        BodyCollision.Shape = _capsule;

        CollisionLayer = GameLayers.Mask(GameLayers.Player);
        CollisionMask = GameLayers.Mask(GameLayers.World, GameLayers.Prop, GameLayers.Enemy);

        MotionMode = MotionModeEnum.Grounded;
        // Starting value only. RealignToGravity rewrites it every physics frame
        // from the gravity at the body's own position.
        UpDirection = Vector3.Up;
        SlideOnCeiling = true;

        _currentHeight = Tuning.StandHeight;
        _targetHeight = Tuning.StandHeight;
        ApplyHeight(_currentHeight);

        if (CameraRig != null)
        {
            CameraRig.Footstep += OnFootstep;
        }

        if (Grapple != null)
        {
            Grapple.Attached += OnGrappleAttached;
            Grapple.Released += OnGrappleReleased;
        }

        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast += OnAreaBlast;
        }

        Input.MouseMode = Input.MouseModeEnum.Captured;

        ClaimCamera();
    }

    public override void _ExitTree()
    {
        if (CameraRig != null)
        {
            CameraRig.Footstep -= OnFootstep;
        }

        if (Grapple != null)
        {
            Grapple.Attached -= OnGrappleAttached;
            Grapple.Released -= OnGrappleReleased;
        }

        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast -= OnAreaBlast;
        }

        _jetpackLoop?.Stop();
        _jetpackLoop = null;
    }

    /// <summary>
    /// An explosion went off somewhere. Turned into camera shake here.
    ///
    /// A new <i>listener</i> on the existing fourth bus signal rather than a
    /// fifth signal, which is exactly what BlastInfo being a RefCounted was for:
    /// nothing was added to the signal and no emitter was touched. The player
    /// genuinely cannot be named by a grenade, and the grenade already
    /// broadcasts -- so this costs one connection.
    ///
    /// The shake is computed from the blast's own radius and distance rather
    /// than from the impulse the blast may or may not have applied, because
    /// <see cref="Blast.ApplyImpulses"/> checks line of sight and being felt
    /// should not depend on cover. It is also why this is separate from
    /// <see cref="ApplyBlastImpulse"/>, which is the shove and is a different
    /// thing entirely: one moves the body, this one moves only the camera.
    /// </summary>
    private void OnAreaBlast(BlastInfo blast)
    {
        if (blast == null || blast.Sustained || CameraRig == null || Tuning == null
            || !Tuning.BlastShakeEnabled || _stowed)
        {
            return;
        }

        float radius = Mathf.Max(blast.Radius, 0.01f);
        float range = radius * Tuning.BlastShakeRangeScale;
        float distance = GlobalPosition.DistanceTo(blast.Centre);
        if (distance >= range)
        {
            return;
        }

        // Two independent scalars: how big the blast was, and how close it was.
        // The size term is clamped above 1 so a very large warhead does not need
        // a special case, and the distance term is squared so the difference
        // between one going off at your feet and one across the site is felt
        // rather than merely measured.
        float size = Mathf.Min(radius / Mathf.Max(Tuning.BlastShakeFullRadius, 0.01f), 1.0f);
        float nearness = 1.0f - (distance / range);

        CameraRig.AddShake(size * nearness * nearness);
    }

    /// <summary>
    /// A footfall, raised by the rig at the bottom of each bob dip, so the
    /// sound lands on the frame the camera dips.
    /// </summary>
    private void OnFootstep()
    {
        if (_stowed || IsFlying || _swinging)
        {
            return;
        }

        AudioDirector.PlayAt(Sfx.FootstepDry, GlobalPosition);
    }

    /// <summary>
    /// The rearm pod (and any other resupply) tops up the gas tank as a
    /// fraction of a full tank, the same way it refills each weapon's reserve.
    /// </summary>
    public bool TryRefuel(float fraction)
    {
        if (Tuning == null || Tuning.FlightFuelSeconds <= 0.0f)
        {
            return false;
        }

        if (_flightFuel >= Tuning.FlightFuelSeconds - 0.01f)
        {
            return false;
        }

        _flightFuel = Mathf.Min(
            Tuning.FlightFuelSeconds,
            _flightFuel + (Tuning.FlightFuelSeconds * Mathf.Max(fraction, 0.0f)));
        return true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Mouse capture still toggles while stowed; nothing else does. The
        // look would otherwise turn a body nobody is looking through.
        if (!_stowed
            && !_lookSuppressed
            && @event is InputEventMouseMotion motion
            && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float yawDelta = -motion.Relative.X * Tuning.MouseSensitivity;
            float pitchDelta = -motion.Relative.Y * Tuning.MouseSensitivity;
            if (Tuning.InvertY)
            {
                pitchDelta = -pitchDelta;
            }

            // Object-local, not RotateY: RotateY turns about the parent's Y
            // axis, while yaw has to turn about the body's own up -- which
            // RealignToGravity keeps pointed away from the planet.
            RotateObjectLocal(Vector3.Up, yawDelta);
            CameraRig?.ApplyLookInput(yawDelta, pitchDelta, Tuning);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("mouse_capture_toggle"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Before anything reads IsOnFloor: floor, ceiling and snap are all
        // defined relative to UpDirection, so up has to be current first.
        RealignToGravity();

        _crouchHeld = Input.IsActionPressed("crouch");
        UpdateStance(dt);

        Vector3 velocity = Velocity;
        ConsumeBlastImpulse(ref velocity);
        bool onFloor = IsOnFloor();

        UpdateTimers(dt, onFloor);
        UpdateFlight(dt, onFloor);

        // After flight, so a hook that attaches this frame has already shut the
        // hover's fuel burn off before the movement split reads the line.
        bool attached = Grapple != null && Grapple.PhysicsTick(dt, RopeOrigin, GetRid());
        GrappleLineMode mode = attached ? Grapple.Mode : GrappleLineMode.Swing;
        bool reeling = attached
                       && mode != GrappleLineMode.Zip
                       && Input.IsActionPressed("jump");
        _swinging = attached && (mode != GrappleLineMode.Swing || !onFloor || reeling);
        Grapple?.SetReeling(reeling && mode != GrappleLineMode.Zip);

        bool hoverSuspended = attached && (mode == GrappleLineMode.Zip || mode == GrappleLineMode.Orbit);
        bool hovering = IsFlying && !hoverSuspended;

        if (attached && mode == GrappleLineMode.Zip)
        {
            ApplyZip(ref velocity, dt);
        }
        else if (attached && mode == GrappleLineMode.Orbit)
        {
            ApplyOrbit(ref velocity, dt);
        }
        else if (hovering && attached)
        {
            ApplyHoverOnSwing(ref velocity, dt);
            ApplyRope(ref velocity, dt, reeling);
        }
        else if (hovering)
        {
            ApplyHover(ref velocity, dt);
        }
        else
        {
            ApplyGravity(ref velocity, dt, onFloor);

            // On the line, jump is the reel. A jump as well would spend the
            // same press twice. A latched zip already used jump to let go.
            if (!attached)
            {
                ApplyJump(ref velocity);
            }

            if (_swinging)
            {
                ApplySwing(ref velocity, dt);
            }
            else
            {
                ApplyHorizontal(ref velocity, dt, onFloor);
            }

            // Last, because it is a constraint rather than a force: whatever
            // gravity and the keys did this frame, the line gets the final say
            // on how far from the anchor the body may go.
            if (attached)
            {
                ApplyRope(ref velocity, dt, reeling);
            }
        }

        TryDash(ref velocity, dt, onFloor);

        Velocity = velocity;
        FloorMaxAngle = Mathf.DegToRad(Tuning.FloorMaxAngleDegrees);
        // Snap keeps the controller glued to downhill slopes and stairs, but it
        // must be off while rising or it eats the jump -- and off entirely
        // while flying or on the line, or the first pass near the ground yanks
        // the body down out of the arc.
        FloorSnapLength = IsFlying || _swinging || velocity.Dot(UpDirection) > 0.0f
            ? 0.0f
            : FloorSnapDistance;
        MoveAndSlide();

        DetectLanding();
        UpdateState();
    }

    /// <summary>
    /// How far below the feet the controller looks for a floor to stick to.
    /// Not in MovementTuning: this is a collision-robustness constant, not a
    /// feel value -- changing it does not make the game play differently, it
    /// makes the controller stop working.
    /// </summary>
    private const float FloorSnapDistance = 0.35f;

    /// <summary>
    /// A gentle pull into the ground held while standing on it.
    ///
    /// On flat ground the grounded speed along up is exactly 0.0f, and both
    /// this controller's snap gate and MoveAndSlide's own internal one read
    /// "not rising" and snap the body to the floor. Decomposing along an
    /// arbitrary up replaces that exact zero with a projection that lands a
    /// whisker either side of it, and a positive whisker makes MoveAndSlide
    /// refuse to snap -- which on a convex surface, where the ground falls
    /// away under the body every frame, meant skipping along the planet with
    /// IsOnFloor alternating every frame, taking sprint, gravity and
    /// PlayerState with it.
    ///
    /// Applied only inside a band this wide, so a real climb or descent on a
    /// ramp -- orders of magnitude larger -- is carried through untouched.
    /// Sub-millimetre per frame: MoveAndSlide resolves it against the floor on
    /// the same frame, so it is not observable anywhere.
    /// </summary>
    private const float GroundStickSpeed = 0.05f;

    // ---------------- Gravity orientation ----------------

    /// <summary>
    /// Points the controller's up at whatever gravity is here, and turns the
    /// body to match.
    ///
    /// DECISION -- gravity comes from an Area3D, not from a bespoke field node.
    /// GetGravity() reports the gravity at this body's own position, already
    /// combining any gravity_point area it is standing inside. A level with no
    /// gravity area at all therefore reports the project default straight down
    /// and plays exactly as it always has, and the grenade RigidBody3D gets a
    /// planet's pull from the solver for free instead of needing a custom
    /// integrator. The alternative -- a PlanetBody node every interested thing
    /// has to find and query -- buys nothing the physics server already does.
    ///
    /// The basis is snapped, not interpolated. Walking a planet of radius R at
    /// speed v tilts it by only v*dt/R radians per frame, so there is nothing
    /// worth smoothing, and smoothing would leave the capsule pointing
    /// somewhere other than UpDirection while MoveAndSlide runs.
    /// </summary>
    private void RealignToGravity()
    {
        Vector3 gravity = GetGravity();

        // A level may legitimately have no gravity at all. Keeping the last
        // known up beats tumbling because the vector had no direction.
        if (gravity.LengthSquared() > GravityEpsilon)
        {
            GravityUp = -gravity.Normalized();
        }

        UpDirection = GravityUp;

        // Rebuild the basis around the new up while keeping the yaw the player
        // has accumulated. Yaw cannot be kept as a scalar here: the frame it
        // would be measured against is itself moving.
        Basis basis = GlobalBasis;
        Vector3 forward = (-basis.Z).Slide(GravityUp);
        if (forward.LengthSquared() < GravityEpsilon)
        {
            // Facing straight along the new up. Any tangent will do, and the
            // old up is the axis that moved least.
            forward = basis.Y.Slide(GravityUp);
        }

        forward = forward.Normalized();
        GlobalBasis = new Basis(forward.Cross(GravityUp), GravityUp, -forward);
    }

    private const float GravityEpsilon = 0.000001f;

    // ---------------- Stance ----------------

    private void UpdateStance(float dt)
    {
        bool wantCrouch = _crouchHeld;

        // Refuse to stand up under a low ceiling.
        if (!wantCrouch && _isCrouched && !HasHeadroom())
        {
            wantCrouch = true;
        }

        if (wantCrouch != _isCrouched)
        {
            _isCrouched = wantCrouch;
            EmitSignal(SignalName.StanceChanged, _isCrouched);
        }

        _targetHeight = _isCrouched ? Tuning.CrouchHeight : Tuning.StandHeight;

        if (!Mathf.IsEqualApprox(_currentHeight, _targetHeight))
        {
            _currentHeight = Mathf.MoveToward(_currentHeight, _targetHeight, Tuning.CrouchTransitionSpeed * dt);
            ApplyHeight(_currentHeight);
        }
    }

    private void ApplyHeight(float height)
    {
        // The body origin is at the feet, so the capsule is centred at half
        // height and shrinking it keeps the feet planted.
        _capsule.Height = height;
        _capsule.Radius = Mathf.Min(_capsule.Radius, (height * 0.5f) - 0.01f);
        BodyCollision.Position = new Vector3(0.0f, height * 0.5f, 0.0f);

        if (CameraRig != null)
        {
            CameraRig.Position = new Vector3(0.0f, height - Tuning.EyeHeightOffset, 0.0f);
        }

        if (HeadroomCheck != null)
        {
            HeadroomCheck.Position = new Vector3(0.0f, height, 0.0f);
            HeadroomCheck.TargetPosition = new Vector3(0.0f, Tuning.StandHeight - height + 0.05f, 0.0f);
        }
    }

    private bool HasHeadroom()
    {
        if (HeadroomCheck == null)
        {
            return true;
        }
        HeadroomCheck.ForceShapecastUpdate();
        return !HeadroomCheck.IsColliding();
    }

    // ---------------- Movement ----------------

    private void UpdateTimers(float dt, bool onFloor)
    {
        _coyoteTimer = onFloor ? Tuning.CoyoteTime : Mathf.Max(0.0f, _coyoteTimer - dt);

        if (Input.IsActionJustPressed("jump"))
        {
            _jumpBufferTimer = Tuning.JumpBufferTime;
        }
        else
        {
            _jumpBufferTimer = Mathf.Max(0.0f, _jumpBufferTimer - dt);
        }
    }

    private void ApplyGravity(ref Vector3 velocity, float dt, bool onFloor)
    {
        if (onFloor)
        {
            _apexTimer = 0.0f;
            return;
        }

        float scale = Tuning.GravityScale;
        if (_apexTimer > 0.0f && Line != null && Line.GrappleApexFloatSeconds > 0.0f)
        {
            float t = 1.0f - Mathf.Clamp(_apexTimer / Line.GrappleApexFloatSeconds, 0.0f, 1.0f);
            scale *= Mathf.Lerp(Line.GrappleApexGravityScale, 1.0f, t * t);
            _apexTimer = Mathf.Max(0.0f, _apexTimer - dt);
        }

        velocity += GetGravity() * scale * dt;
    }

    private void ApplyJump(ref Vector3 velocity)
    {
        if (_jumpBufferTimer <= 0.0f || _coyoteTimer <= 0.0f)
        {
            return;
        }

        // Set the speed along up rather than add to it, so jumping out of a fall
        // is worth exactly what jumping from standing is worth.
        velocity += UpDirection * (Tuning.JumpVelocity - velocity.Dot(UpDirection));
        _jumpBufferTimer = 0.0f;
        _coyoteTimer = 0.0f;

        AudioDirector.PlayAt(Sfx.Jump, GlobalPosition);
    }

    // ---------------- Flight ----------------

    /// <summary>
    /// Engages, disengages and fuels the jetpack. Runs before the movement
    /// split so the rest of the frame only has to read <see cref="IsFlying"/>.
    /// </summary>
    private void UpdateFlight(float dt, bool onFloor)
    {
        if (!Tuning.FlightEnabled)
        {
            IsFlying = false;
            return;
        }

        bool unlimited = Tuning.FlightFuelSeconds <= 0.0f;
        if (unlimited)
        {
            _flightFuel = float.MaxValue;
        }
        else if (_flightFuel > Tuning.FlightFuelSeconds)
        {
            // First frame, or fuel was just switched away from unlimited.
            _flightFuel = Tuning.FlightFuelSeconds;
        }

        if (Input.IsActionJustPressed("fly_toggle") && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            SetFlying(!IsFlying);
        }

        if (!IsFlying)
        {
            // Refuelling needs the ground. Otherwise a tap of the jetpack every
            // few seconds is indistinguishable from unlimited flight, and the
            // tank stops being a cost at all.
            if (onFloor && !unlimited)
            {
                _flightFuel = Mathf.Min(
                    Tuning.FlightFuelSeconds,
                    _flightFuel + (Tuning.FlightFuelSeconds / Mathf.Max(Tuning.FlightRefuelSeconds, 0.01f) * dt));
            }
            return;
        }

        bool attached = Grapple != null && Grapple.IsAttached;
        GrappleLineMode mode = attached ? Grapple.Mode : GrappleLineMode.Swing;
        bool hoverSuspended = attached && (mode == GrappleLineMode.Zip || mode == GrappleLineMode.Orbit);

        if (!unlimited && !hoverSuspended)
        {
            bool boosting = Input.IsActionPressed("sprint");
            _flightFuel -= (boosting ? Tuning.FlightBoostBurnMultiplier : 1.0f) * dt;

            if (_flightFuel <= 0.0f)
            {
                _flightFuel = 0.0f;
                SetFlying(false);
                return;
            }
        }

        if (!onFloor)
        {
            _flightLeftGround = true;
        }

        // Touching down under control ends the hover, so landing is just
        // drifting into the ground rather than a second button. Only once it
        // has actually lifted off, or engaging while standing would cancel
        // itself on the same frame. Staying on the line does not count as
        // landing.
        else if (_flightLeftGround && !Input.IsActionPressed("jump") && !attached)
        {
            SetFlying(false);
        }
    }

    private void SetFlying(bool flying)
    {
        if (flying == IsFlying)
        {
            return;
        }

        if (flying && (_flightFuel <= 0.0f || !Tuning.FlightEnabled))
        {
            return;
        }

        IsFlying = flying;
        _flightLeftGround = false;

        if (!flying)
        {
            _jetpackLoop?.Stop();
            _jetpackLoop = null;
            return;
        }

        _jetpackLoop ??= AudioDirector.Loop(Sfx.JetpackLoop, this);

        // A kick on takeoff, so engaging from standing leaves the ground on the
        // same frame instead of scraping along it while thrust builds.
        Vector3 velocity = Velocity;
        float alongUp = velocity.Dot(UpDirection);
        if (alongUp < Tuning.FlightTakeoffImpulse)
        {
            Velocity = velocity + (UpDirection * (Tuning.FlightTakeoffImpulse - alongUp));
        }
    }

    private void ApplyHorizontal(ref Vector3 velocity, float dt, bool onFloor)
    {
        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        // GlobalBasis, not Transform.Basis: the player is parented under the
        // level's spawn marker, so the two part company the moment that marker
        // is rotated -- which a planet spawn does by construction.
        Vector3 wish = (GlobalBasis * new Vector3(input.X, 0.0f, input.Y))
            .Slide(UpDirection)
            .Normalized();

        IsSprinting = Input.IsActionPressed("sprint")
                      && !_isCrouched
                      && input.Y < -0.1f
                      && onFloor;

        float targetSpeed = _isCrouched
            ? Tuning.CrouchSpeed
            : IsSprinting ? Tuning.SprintSpeed : Tuning.WalkSpeed;

        // Split along gravity rather than along Y. Acceleration and friction
        // act on the tangential part only; the part along up belongs to gravity
        // and to the jump, and has to survive untouched.
        float alongUp = velocity.Dot(UpDirection);
        if (onFloor && Mathf.Abs(alongUp) < GroundStickSpeed)
        {
            alongUp = -GroundStickSpeed;
        }

        Vector3 vertical = UpDirection * alongUp;
        Vector3 horizontal = velocity.Slide(UpDirection);
        Vector3 target = wish * targetSpeed * input.Length();

        bool pushing = wish.LengthSquared() > 0.0f;

        float accel;
        if (onFloor)
        {
            accel = pushing ? Tuning.GroundAcceleration : Tuning.GroundFriction;

            if (_dashHoldTimer > 0.0f)
            {
                _dashHoldTimer -= dt;
                float current = horizontal.Length();
                if (current > target.Length())
                {
                    target = pushing ? wish * current : horizontal;
                    accel = Tuning.GroundAcceleration;
                }
            }
        }
        else
        {
            accel = pushing
                ? Tuning.GroundAcceleration * Tuning.AirControl
                : Tuning.AirFriction;

            if (_momentumCarry)
            {
                float carried = horizontal.Length();
                if (carried <= Tuning.SprintSpeed)
                {
                    // Back to a speed the ordinary air rules already handle.
                    _momentumCarry = false;
                }
                else
                {
                    // Steering aims the speed you have rather than dragging it
                    // toward what walking would give, and letting go costs
                    // almost nothing.
                    target = wish * carried * input.Length();
                    if (!pushing)
                    {
                        accel = Line?.GrappleCarryDrag ?? Tuning.AirFriction;
                    }
                }
            }
        }

        horizontal = horizontal.MoveToward(target, accel * dt);
        velocity = horizontal + vertical;
    }

    // ---------------- Grapple ----------------

    /// <summary>
    /// Where the line pulls on the body: chest height, so hanging from it
    /// swings the body under the anchor rather than from the feet.
    /// </summary>
    private Vector3 RopeOrigin => GlobalPosition + (UpDirection * (_currentHeight * RopeAttachHeight));

    /// <summary>Fraction of body height the line is attached at.</summary>
    private const float RopeAttachHeight = 0.7f;

    /// <summary>
    /// Pumping the swing. The keys push across the line only -- pushing along
    /// it would be a second reel.
    ///
    /// Relative to the body's level heading, not the camera. The camera is
    /// usually looking up the line at the anchor, so its forward lies almost
    /// along the rope and projects to nothing: the first scripted test hung
    /// beside the skyscraper holding forward for three seconds and did not
    /// move. The heading still means "that way" while you look up at what you
    /// are swinging from.
    ///
    /// No MoveToward target, which is the whole point of having this rather
    /// than the ground rules: those drag velocity toward walking pace, and a
    /// swing is supposed to keep what gravity gives it.
    /// </summary>
    private void ApplySwing(ref Vector3 velocity, float dt)
    {
        IsSprinting = false;

        Vector3 toAnchor = Grapple.AnchorPoint - RopeOrigin;
        if (toAnchor.LengthSquared() < 0.0001f)
        {
            return;
        }
        Vector3 along = toAnchor.Normalized();

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 wish = (GlobalBasis * new Vector3(input.X, 0.0f, input.Y)).Slide(UpDirection);
        Vector3 across = wish - (along * wish.Dot(along));

        if (across.LengthSquared() > 0.0001f)
        {
            velocity += across.Normalized() * Line.GrappleSwingAcceleration * Mathf.Min(input.Length(), 1.0f) * dt;
        }

        velocity *= Mathf.Max(0.0f, 1.0f - (Line.GrappleSwingDrag * dt));
    }

    /// <summary>
    /// A tap's pull: a straight line toward the anchor, no gravity, WASD only
    /// a sideways nudge. Predictable so the mouse is free to aim.
    /// </summary>
    private void ApplyZip(ref Vector3 velocity, float dt)
    {
        IsSprinting = false;
        _zipElapsed += dt;

        Vector3 origin = RopeOrigin;
        Vector3 toAnchor = Grapple.AnchorPoint - origin;
        float distance = toAnchor.Length();
        if (distance < 0.001f)
        {
            return;
        }

        Vector3 along = toAnchor / distance;
        float ease = 1.0f;
        if (Line.GrappleZipEaseIn > 0.0f)
        {
            float t = Mathf.Clamp(_zipElapsed / Line.GrappleZipEaseIn, 0.0f, 1.0f);
            ease = t * t * (3.0f - (2.0f * t));
        }

        float speed = Line.GrappleZipSpeed * ease;
        if (distance < Line.GrappleZipEaseOutDistance && Line.GrappleZipEaseOutDistance > 0.0f)
        {
            speed *= distance / Line.GrappleZipEaseOutDistance;
        }

        velocity = Grapple.AnchorVelocity + (along * speed);

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 wish = (GlobalBasis * new Vector3(input.X, 0.0f, input.Y)).Slide(UpDirection);
        Vector3 across = wish - (along * wish.Dot(along));
        if (across.LengthSquared() > 0.0001f)
        {
            velocity += across.Normalized() * Line.GrappleZipSteer * Mathf.Min(input.Length(), 1.0f);
        }

        if (TryPerch(ref velocity, distance))
        {
            return;
        }

        if (distance <= Line.GrappleMinLength + HangSlack)
        {
            velocity = Grapple.AnchorVelocity;
            Grapple.RopeLength = Line.GrappleMinLength;
            return;
        }

        Grapple.RopeLength = Mathf.Max(Line.GrappleMinLength, distance);
        velocity = velocity.LimitLength(Line.GrappleMaxSpeed);
    }

    /// <summary>
    /// Circle-strafe on a cylinder around a giant. Angle and height live in her
    /// frame so a hop or a turn carries you with her. The radius never goes
    /// inside the shell.
    /// </summary>
    private void ApplyOrbit(ref Vector3 velocity, float dt)
    {
        IsSprinting = false;

        if (!Grapple.CurrentOrbit.HasValue)
        {
            return;
        }

        GrappleOrbitAxis orbit = Grapple.CurrentOrbit.Value;
        Vector3 axis = orbit.Axis.LengthSquared() > 0.0001f ? orbit.Axis.Normalized() : UpDirection;
        Vector3 reference = axis.Cross(Vector3.Right);
        if (reference.LengthSquared() < 0.0001f)
        {
            reference = axis.Cross(Vector3.Forward);
        }

        reference = reference.Normalized();
        Vector3 bitangent = axis.Cross(reference);

        if (!_orbitInit)
        {
            Vector3 from = GlobalPosition - orbit.Origin;
            float along = from.Dot(axis);
            Vector3 radial = from - (axis * along);
            _orbitHeight = Mathf.Clamp(along, 0.0f, Mathf.Max(orbit.Height, 0.1f));
            _orbitRadius = Mathf.Max(
                radial.Length(),
                orbit.MinRadius + Line.GrappleOrbitClearance);
            _orbitAngle = Mathf.Atan2(radial.Dot(bitangent), radial.Dot(reference));
            _orbitInit = true;
        }

        if (Input.IsActionPressed("jump"))
        {
            _orbitRadius = Mathf.Max(
                orbit.MinRadius + Line.GrappleOrbitClearance,
                _orbitRadius - (Line.GrappleReelSpeed * dt));
        }
        else if (_crouchHeld)
        {
            _orbitRadius = Mathf.Min(Line.GrappleRange, _orbitRadius + (Line.GrapplePayoutSpeed * dt));
        }

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        float radius = Mathf.Max(_orbitRadius, 0.1f);
        _orbitAngle += input.X * Line.GrappleOrbitStrafe * dt / radius;
        // GetVector: negative Y is forward, which climbs.
        _orbitHeight = Mathf.Clamp(
            _orbitHeight - (input.Y * Line.GrappleOrbitClimb * dt),
            0.0f,
            Mathf.Max(orbit.Height, 0.1f));

        Vector3 radialDir = (reference * Mathf.Cos(_orbitAngle)) + (bitangent * Mathf.Sin(_orbitAngle));
        Vector3 target = orbit.Origin + (axis * _orbitHeight) + (radialDir * _orbitRadius);
        Vector3 delta = target - GlobalPosition;
        velocity = dt > 0.0001f ? delta / dt : Vector3.Zero;
        velocity = velocity.LimitLength(Line.GrappleMaxSpeed);
        Grapple.RopeLength = _orbitRadius;
    }

    /// <summary>
    /// Hover for aiming: gravity cancelled, a strong brake, a slow drift.
    /// Shift still boosts the crawl.
    /// </summary>
    private void ApplyHover(ref Vector3 velocity, float dt)
    {
        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Basis aim = CameraRig?.ActiveCamera?.GlobalBasis ?? GlobalBasis;
        Vector3 wish = CameraRelativeWish(aim, input);

        float lift = 0.0f;
        if (Input.IsActionPressed("jump"))
        {
            lift += 1.0f;
        }
        if (_crouchHeld)
        {
            lift -= 1.0f;
        }

        wish += UpDirection * lift;
        if (wish.LengthSquared() > 1.0f)
        {
            wish = wish.Normalized();
        }

        bool boosting = Input.IsActionPressed("sprint");
        float speed = Tuning.HoverDriftSpeed * (boosting ? Tuning.FlightBoostMultiplier : 1.0f);
        Vector3 target = wish * speed;
        velocity = velocity.MoveToward(target, Tuning.HoverDamping * dt);
    }

    /// <summary>
    /// Hover while on a held swing: brake relative to the anchor so you can
    /// stop mid-arc and aim, without the line letting go.
    /// </summary>
    private void ApplyHoverOnSwing(ref Vector3 velocity, float dt)
    {
        IsSprinting = false;
        velocity -= Grapple.AnchorVelocity;
        ApplyHover(ref velocity, dt);
        velocity += Grapple.AnchorVelocity;
    }

    private void TryDash(ref Vector3 velocity, float dt, bool onFloor)
    {
        _dashCooldown = Mathf.Max(0.0f, _dashCooldown - dt);
        if (!Input.IsActionJustPressed("sprint") || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }

        if (_dashCooldown > 0.0f || !TrySpendDash())
        {
            return;
        }

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Basis aim = CameraRig?.ActiveCamera?.GlobalBasis ?? GlobalBasis;
        Vector3 wish = CameraRelativeWish(aim, input);
        if (wish.LengthSquared() < 0.0001f)
        {
            wish = -aim.Z;
        }

        if (onFloor)
        {
            wish = wish.Slide(UpDirection);
        }

        if (wish.LengthSquared() < 0.0001f)
        {
            return;
        }

        wish = wish.Normalized();
        float burst = onFloor ? Tuning.DashGroundSpeed : Tuning.DashSpeed;
        // One addition. Re-applying the remainder every frame integrates it:
        // a 14 m/s air dash became thousands of metres per second and left
        // the planet.
        velocity += wish * burst;
        if (onFloor)
        {
            _dashHoldTimer = Tuning.DashGroundHold;
        }
        else
        {
            _momentumCarry = true;
        }

        _dashCooldown = Tuning.DashCooldown;
    }

    /// <summary>
    /// WASD in the camera frame. <see cref="Input.GetVector"/> puts forward on
    /// negative Y, and a camera's forward is its -Z, so forward is
    /// <c>aim.Z * input.Y</c>. Negating Z here points W backward.
    /// </summary>
    private static Vector3 CameraRelativeWish(Basis aim, Vector2 input)
    {
        return (aim.X * input.X) + (aim.Z * input.Y);
    }

    private bool TrySpendDash()
    {
        if (Tuning.FlightFuelSeconds <= 0.0f)
        {
            return true;
        }

        if (_flightFuel < Tuning.DashFuelCost)
        {
            return false;
        }

        _flightFuel -= Tuning.DashFuelCost;
        return true;
    }

    /// <summary>
    /// The line itself: an inextensible rope with a winch.
    ///
    /// Slack costs nothing -- inside the line's length the body is free, which
    /// is what lets a swing go over the top of its arc. Past it, the part of
    /// the velocity carrying the body away from the anchor is removed, relative
    /// to the anchor's own motion so a line in something that is moving tows
    /// rather than yanks. Removing it rather than reflecting it loses energy
    /// when the line snaps taut, which is correct: a rope that goes taut is a
    /// small collision, and one that bounced would feel like elastic.
    ///
    /// Drift left over by integration is taken up by asking for a small inward
    /// speed proportional to the stretch, rather than by adding an impulse
    /// every frame -- an added impulse would persist and accumulate, where a
    /// requested minimum speed does not.
    /// </summary>
    private void ApplyRope(ref Vector3 velocity, float dt, bool reeling)
    {
        Vector3 origin = RopeOrigin;
        Vector3 toAnchor = Grapple.AnchorPoint - origin;
        float distance = toAnchor.Length();
        if (distance < 0.001f)
        {
            return;
        }

        Vector3 along = toAnchor / distance;
        Vector3 relative = velocity - Grapple.AnchorVelocity;
        float length = Grapple.RopeLength;

        if (reeling)
        {
            length = Mathf.Max(Line.GrappleMinLength, Mathf.Min(length, distance) - (Line.GrappleReelSpeed * dt));

            if (TryPerch(ref velocity, distance))
            {
                return;
            }

            // Reeled all the way up: hang there, riding the anchor. Without
            // this the body arrives at the end of the line with whatever speed
            // the haul gave it and bounces round the anchor on a 1.5 m tether.
            if (distance <= Line.GrappleMinLength + HangSlack)
            {
                velocity = Grapple.AnchorVelocity;
                Grapple.RopeLength = length;
                return;
            }

            // Hauling in straightens the line out. A rope shortened under a
            // body conserves its angular momentum, so the sideways speed grows
            // as the length shrinks -- twenty-fold from thirty metres to one
            // and a half, which in the first test spun the player round the
            // anchor at 40 m/s. Correct for a stone on a string; wrong for a
            // winch whose whole purpose is taking you to what you hooked.
            Vector3 sideways = relative - (along * relative.Dot(along));
            Vector3 settle = sideways * Mathf.Min(1.0f, Line.GrappleReelSteadying * dt);
            velocity -= settle;
            relative -= settle;

            float inward = relative.Dot(along);
            if (inward < Line.GrappleReelMaxSpeed)
            {
                float add = Mathf.Min(Line.GrappleReelAcceleration * dt, Line.GrappleReelMaxSpeed - inward);
                velocity += along * add;
                relative += along * add;
            }
        }
        else if (_crouchHeld)
        {
            length = Mathf.Min(Line.GrappleRange, length + (Line.GrapplePayoutSpeed * dt));
        }

        Grapple.RopeLength = length;

        if (distance > length)
        {
            float stretch = distance - length;
            float wanted = Mathf.Min(stretch * Line.GrappleRopeStiffness, Line.GrappleMaxCorrection);
            float inward = relative.Dot(along);
            if (inward < wanted)
            {
                velocity += along * (wanted - inward);
            }
        }

        velocity = velocity.LimitLength(Line.GrappleMaxSpeed);
    }

    /// <summary>
    /// Reeled right up to a surface that faces up: let go and hop onto it.
    /// Otherwise reeling into the top of something leaves you hanging off its
    /// lip, pressed against it and unable to stand, which is the moment a
    /// perch was supposed to happen.
    ///
    /// The hop is derived rather than tuned -- the speed that lifts the feet
    /// from the line's attachment height to just over the lip under this
    /// body's own gravity -- so it is right on the moon and on the planet.
    /// </summary>
    private bool TryPerch(ref Vector3 velocity, float distance)
    {
        if (distance > Line.GrapplePerchDistance || Grapple.AnchorNormal.Dot(UpDirection) < PerchMinUpDot)
        {
            return false;
        }

        float gravity = GetGravity().Length() * Tuning.GravityScale;
        float lift = (_currentHeight * RopeAttachHeight) + PerchClearance;
        float hop = Mathf.Sqrt(2.0f * Mathf.Max(gravity, 0.01f) * lift);

        Vector3 onto = (Grapple.AnchorPoint - GlobalPosition).Slide(UpDirection);
        Vector3 horizontal = onto.LengthSquared() > 0.0001f ? onto.Normalized() * PerchForwardSpeed : Vector3.Zero;

        velocity = horizontal + (UpDirection * Mathf.Max(velocity.Dot(UpDirection), hop));
        Grapple.Release(false);
        return true;
    }

    /// <summary>
    /// How close to the end of the line counts as reeled all the way in, in
    /// metres. A band rather than an exact length, or integration overshoot
    /// would flicker the hang on and off.
    /// </summary>
    private const float HangSlack = 0.5f;

    /// <summary>How steeply the anchor face has to point up to count as a top.</summary>
    private const float PerchMinUpDot = 0.5f;

    /// <summary>Clearance the perch hop gives the feet over the lip, in metres.</summary>
    private const float PerchClearance = 0.4f;

    /// <summary>Carry onto the top once the feet clear it, in metres per second.</summary>
    private const float PerchForwardSpeed = 3.0f;

    private void OnGrappleAttached()
    {
        _momentumCarry = false;
        _apexTimer = 0.0f;
        _zipElapsed = 0.0f;
        _orbitInit = false;
    }

    private void OnGrappleReleased(bool snapped)
    {
        _orbitInit = false;
        bool airborne = !IsOnFloor();
        _momentumCarry = airborne && HorizontalSpeed > Tuning.SprintSpeed;
        if (airborne)
        {
            _apexTimer = Line.GrappleApexFloatSeconds;
        }
    }

    private void DetectLanding()
    {
        bool onFloor = IsOnFloor();

        if (onFloor && !_wasOnFloor)
        {
            _momentumCarry = false;
            _apexTimer = 0.0f;
            CameraRig?.AddLandingPunch(_fallSpeed, Tuning);

            // Split at the speed the camera punch becomes noticeable, so what
            // you hear and what you feel agree about whether that hurt.
            AudioDirector.PlayAt(
                _fallSpeed > HardLandingSpeed ? Sfx.LandHard : Sfx.LandSoft,
                GlobalPosition);

            _fallSpeed = 0.0f;
        }
        else if (!onFloor)
        {
            // MoveAndSlide zeroes the up component of Velocity on contact, so
            // the impact speed has to be remembered from the last airborne frame.
            _fallSpeed = Mathf.Max(_fallSpeed, -Velocity.Dot(UpDirection));
        }

        _wasOnFloor = onFloor;
    }

    private void UpdateState()
    {
        PlayerState next;
        if (IsGrappling && (Grapple.Mode != GrappleLineMode.Swing || !IsOnFloor()))
        {
            next = PlayerState.Grappling;
        }
        else if (IsFlying)
        {
            next = PlayerState.Flying;
        }
        else if (!IsOnFloor())
        {
            next = PlayerState.Airborne;
        }
        else if (_isCrouched)
        {
            next = HorizontalSpeed > MovingThreshold ? PlayerState.CrouchWalking : PlayerState.Crouching;
        }
        else if (IsSprinting && HorizontalSpeed > MovingThreshold)
        {
            next = PlayerState.Sprinting;
        }
        else
        {
            next = HorizontalSpeed > MovingThreshold ? PlayerState.Walking : PlayerState.Idle;
        }

        if (next != _state)
        {
            _state = next;
            EventBus.EmitPlayerStateChanged(_state);
        }
    }

    private const float MovingThreshold = 0.2f;

    // ---------------- Camera ----------------

    /// <summary>
    /// Takes the viewport, and takes it explicitly. A Camera3D makes itself
    /// current only if it is the first one to enter the viewport, and the
    /// player loses that race in any level holding a parked ship: the level
    /// scene -- ShipCamera included -- is already in the tree by the time
    /// LevelRoot._Ready instances the player. Without this the planet level
    /// renders from a shuttle sitting on the pad while the player walks
    /// around unseen. Called on spawn and again on leaving a ship, which is
    /// the other moment the view comes back.
    /// </summary>
    private void ClaimCamera()
    {
        if (CameraRig?.ActiveCamera != null)
        {
            CameraRig.ActiveCamera.Current = true;
        }
    }

    /// <summary>
    /// Drives the camera rig's procedural layer. Runs on the render frame, not
    /// the physics frame, so bob and sway stay smooth at any physics rate.
    /// The rig is handed the state it needs; it never reads the player back.
    /// </summary>
    public override void _Process(double delta)
    {
        if (HitFlash > 0.0f)
        {
            HitFlash = Mathf.Max(0.0f, HitFlash - ((float)delta * 1.4f));
        }

        for (int i = 0; i < HitMarkCount; i++)
        {
            _hitMarkAge[i] += (float)delta;
        }

        if (CameraRig == null || Tuning == null)
        {
            return;
        }

        float strafeAxis = Input.GetAxis("move_left", "move_right");
        bool onFloor = IsOnFloor();

        SwingCameraTerms(out float swingRoll, out float speedFovAdd);

        CameraRig.UpdateProcedural(
            (float)delta,
            Tuning,
            new CameraRig.ProceduralInput(HorizontalSpeed, onFloor, IsSprinting, strafeAxis, swingRoll, speedFovAdd));
    }

    /// <summary>
    /// The camera's share of the swing: a lean into sideways speed and a wider
    /// view at speed. Both are zero unless the line is attached or its speed is
    /// still being carried, so nothing about walking, sprinting or the
    /// graybox's feel moves.
    /// </summary>
    private void SwingCameraTerms(out float roll, out float fovAdd)
    {
        roll = 0.0f;
        fovAdd = 0.0f;

        if ((!(IsGrappling && !IsOnFloor()) && !_momentumCarry) || Line == null)
        {
            return;
        }

        if (Grapple != null && Grapple.Mode == GrappleLineMode.Orbit)
        {
            fovAdd = Mathf.Clamp(Velocity.Length() / Mathf.Max(Line.GrappleFovFullSpeed, 0.01f), 0.0f, 1.0f)
                     * Line.GrappleFovAdd;
            return;
        }

        Vector3 right = CameraRig.ActiveCamera?.GlobalBasis.X ?? GlobalBasis.X;
        float lateral = Velocity.Dot(right) / Mathf.Max(Line.GrappleRollFullSpeed, 0.01f);
        // Same sign as the strafe roll, so a swing to the right leans the way
        // strafing right already does.
        roll = -Mathf.Clamp(lateral, -1.0f, 1.0f) * Mathf.DegToRad(Line.GrappleRollDegrees);
        fovAdd = Mathf.Clamp(Velocity.Length() / Mathf.Max(Line.GrappleFovFullSpeed, 0.01f), 0.0f, 1.0f)
                 * Line.GrappleFovAdd;
    }

    // ---------------- Blast ----------------

    /// <summary>
    /// Queued by an explosion, folded into velocity on the next physics frame.
    ///
    /// A CharacterBody3D gets no shove from the solver, so this is the explicit
    /// impulse the controller applies to its own velocity. It came early,
    /// before the player had any health: without it a grenade at your own feet
    /// did nothing at all, and there was no way to judge whether the blast
    /// read. Your own blasts still cost no health; see PlayerHealth.
    /// </summary>
    public void ApplyBlastImpulse(Vector3 impulse)
    {
        _pendingImpulse += impulse;
    }

    /// <summary>
    /// How red the screen edge is, 0 to 0.35, decaying on its own. Read by the
    /// HUD. The shove and the shake say something hit you; this says it was a
    /// hit and not a gust, which in a crowd throwing things at you is the only
    /// way to tell the two apart. The bike has the same flash for the same
    /// reason.
    /// </summary>
    public float HitFlash { get; private set; }

    /// <summary>Something hit the player. Strength is the hit's camera trauma.</summary>
    public void FlashHit(float strength)
    {
        HitFlash = Mathf.Min(0.35f, HitFlash + (strength * 0.6f));
    }

    /// <summary>
    /// As <see cref="FlashHit(float)"/>, and remembers where the hit came
    /// from, so the HUD can point at it. In a crowd that surrounds you the
    /// red wash says you are being hurt; only a direction says by what.
    /// </summary>
    public void FlashHit(float strength, Vector3 from)
    {
        FlashHit(strength);

        // A hit from about the same direction as a live mark refreshes it
        // rather than taking a slot: a pack biting from one side is one wedge.
        Vector3 toward = (from - GlobalPosition).Slide(UpDirection);
        int slot = -1;
        for (int i = 0; i < HitMarkCount && slot < 0 && toward.LengthSquared() > 0.01f; i++)
        {
            Vector3 other = (_hitMarkFrom[i] - GlobalPosition).Slide(UpDirection);
            if (_hitMarkAge[i] < HitMarkSeconds && other.LengthSquared() > 0.01f
                && toward.Normalized().Dot(other.Normalized()) > 0.9f)
            {
                slot = i;
            }
        }

        if (slot < 0)
        {
            slot = 0;
            for (int i = 1; i < HitMarkCount; i++)
            {
                if (_hitMarkAge[i] > _hitMarkAge[slot])
                {
                    slot = i;
                }
            }
        }

        _hitMarkFrom[slot] = from;
        _hitMarkAge[slot] = 0.0f;
    }

    /// <summary>Hit directions remembered at once.</summary>
    public const int HitMarkCount = 6;

    /// <summary>How long a hit direction stays on the HUD.</summary>
    public const float HitMarkSeconds = 1.1f;

    private readonly Vector3[] _hitMarkFrom = new Vector3[HitMarkCount];
    private readonly float[] _hitMarkAge = { 99, 99, 99, 99, 99, 99 };

    /// <summary>One remembered hit: where it came from and how far through its life, 0 to 1.</summary>
    public bool TryGetHitMark(int index, out Vector3 from, out float age01)
    {
        from = _hitMarkFrom[index];
        age01 = _hitMarkAge[index] / HitMarkSeconds;
        return age01 < 1.0f;
    }

    /// <summary>
    /// The health pool. It drains and refills and never kills -- see
    /// <see cref="PlayerHealth"/>. Null only if the tuning was missing.
    /// </summary>
    public PlayerHealth Health { get; private set; }

    /// <summary>
    /// Something hurt the player. Returns true if it emptied the jar, so the
    /// caller can make the moment louder than an ordinary hit.
    /// </summary>
    public bool TakeDamage(float amount)
    {
        return Health != null && Health.TakeDamage(amount);
    }

    private void ConsumeBlastImpulse(ref Vector3 velocity)
    {
        if (_pendingImpulse == Vector3.Zero)
        {
            return;
        }

        velocity += _pendingImpulse;
        _pendingImpulse = Vector3.Zero;

        // Being thrown means leaving the ground, and the coyote timer would
        // otherwise let the player jump out of it for free.
        _coyoteTimer = 0.0f;

        if (velocity.Slide(UpDirection).Length() > Tuning.SprintSpeed)
        {
            _momentumCarry = true;
        }
    }

    // ---------------- Riding something else ----------------

    /// <summary>
    /// Parks the player while something else has the controls -- the ship, for
    /// now. The body stops simulating and leaves every collision layer, so it
    /// cannot be shoved around by the thing it is riding or block its own
    /// hatch, and the hands are emptied so a held trigger does not fire out of
    /// a cockpit.
    ///
    /// Deliberately not ProcessMode.Disabled: that would take the HUD and the
    /// debug overlay down with it, and both are wanted while flying.
    /// </summary>
    public void SetStowed(bool stowed)
    {
        _stowed = stowed;

        Visible = !stowed;
        SetPhysicsProcess(!stowed);
        SetProcess(!stowed);

        CollisionLayer = stowed ? 0u : GameLayers.Mask(GameLayers.Player);
        CollisionMask = stowed
            ? 0u
            : GameLayers.Mask(GameLayers.World, GameLayers.Prop, GameLayers.Enemy);

        if (stowed)
        {
            // A line left attached would tow a body that is no longer simulating.
            Grapple?.Release(false);
            _momentumCarry = false;
            Velocity = Vector3.Zero;
        }
        else
        {
            ClaimCamera();
        }
    }

    /// <summary>
    /// Turns the body and the camera to look at a world point. Yaw about the
    /// body's own up, pitch on the rig, exactly as mouse look would leave them.
    /// For <c>bench.gd</c>'s <c>@aim</c>; nothing in play calls it.
    /// </summary>
    public void BenchLookAt(Vector3 point)
    {
        if (CameraRig?.ActiveCamera == null || Tuning == null)
        {
            return;
        }

        Vector3 toPoint = point - CameraRig.ActiveCamera.GlobalPosition;
        Vector3 flat = toPoint.Slide(UpDirection);
        if (flat.LengthSquared() < 0.0001f)
        {
            return;
        }

        flat = flat.Normalized();
        GlobalBasis = new Basis(flat.Cross(UpDirection), UpDirection, -flat);

        float pitch = Mathf.Atan2(toPoint.Dot(UpDirection), toPoint.Slide(UpDirection).Length());
        CameraRig.SetPitch(pitch, Tuning);
    }

    /// <summary>One line of locomotion and rope state, for <c>bench.gd</c>'s <c>@status</c>.</summary>
    public string BenchStatus()
    {
        string line = Grapple == null || !Grapple.IsActive
            ? "line idle"
            : Grapple.IsAttached
                ? System.FormattableString.Invariant(
                    $"line {Grapple.Mode} len={Grapple.RopeLength:F1} to-anchor={GlobalPosition.DistanceTo(Grapple.AnchorPoint):F1}")
                : "line flying";

        return System.FormattableString.Invariant(
            $"pos=({GlobalPosition.X:F1},{GlobalPosition.Y:F1},{GlobalPosition.Z:F1}) speed={Velocity.Length():F1} state={_state} {line} gas={FlightFuel01:F2}");
    }

    /// <summary>
    /// Freezes mouse-look without releasing the mouse. See the field comment
    /// on <c>_lookSuppressed</c> for why this exists.
    /// </summary>
    public void SetLookSuppressed(bool suppressed)
    {
        _lookSuppressed = suppressed;
    }
}
