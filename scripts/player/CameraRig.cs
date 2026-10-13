using Godot;

namespace DillControl.Player;

/// <summary>
/// The camera rig, split deliberately across two transforms.
///
/// THIS node (the pivot) carries raw look input only: pitch, set directly from
/// the mouse, clamped, never smoothed and never touched by anything else.
///
/// Its child <see cref="Offset"/> carries every procedural motion -- headbob,
/// look sway, strafe roll, landing punch and weapon recoil -- accumulated each
/// frame from zero and written as a complete local transform.
///
/// The split is the whole point: procedural motion cannot drift the aim point
/// because it never touches the pivot, and mouse input cannot be smeared by a
/// spring because it never touches the offset. They compose, they do not fight.
///
/// Recoil deliberately lives on the offset rather than on the pivot. Because
/// the Camera3D is a descendant of the offset, recoil still moves where shots
/// go -- which is what makes it a real cost -- but it decays back to zero on
/// its own instead of permanently stealing the player's aim.
/// </summary>
public partial class CameraRig : Node3D
{
    [Export]
    public Node3D Offset { get; set; }

    [Export]
    public Camera3D Camera { get; set; }

    private float _pitch;

    // Procedural accumulators, all in the offset's local space.
    private float _bobPhase;
    private Vector3 _swayOffset;
    private float _strafeRoll;
    private float _swingRoll;
    private float _landingPunch;

    /// <summary>
    /// Blast shake, 0 to 1, decaying on its own. Named "trauma" after the shape
    /// of it: what accumulates is a single scalar and every axis of the shake is
    /// read off it, rather than each axis carrying its own spring to fall out of
    /// step with the others.
    /// </summary>
    private float _shakeTrauma;

    /// <summary>
    /// Advances only while there is trauma to read. Independent of the bob phase
    /// so a shake is not silently locked to the player's stride.
    /// </summary>
    private float _shakePhase;

    private Vector2 _recoilCurrent;
    private Vector2 _recoilTarget;
    private float _recoilRecoverSpeed = 8.0f;
    private float _recoilSnappiness = 22.0f;

    private float _lookVelocityX;
    private float _lookVelocityY;

    public Camera3D ActiveCamera => Camera;

    /// <summary>True while the weapon in hand has asked the rig to aim.</summary>
    public bool IsAiming => _aiming;

    /// <summary>Current recoil offset in radians. Debug readout only.</summary>
    public Vector2 RecoilOffset => _recoilCurrent;

    private static CameraRig _live;

    public override void _Ready()
    {
        _live = this;
        if (Offset == null || Camera == null)
        {
            GD.PushError("CameraRig needs both Offset and Camera assigned in the scene.");
            SetProcess(false);
        }
    }

    public override void _ExitTree()
    {
        if (_live == this)
        {
            _live = null;
        }
    }

    /// <summary>A small shake when a building starts to give, falling off with
    /// distance. The voxel code does not know about blasts; this is the one
    /// thing it is allowed to ask the rig.</summary>
    public static void Feel(Vector3 world, float trauma, float radius)
    {
        CameraRig rig = _live;
        if (rig == null || trauma <= 0.0f || radius <= 0.0f || !rig.IsInsideTree())
        {
            return;
        }
        float distance = rig.GlobalPosition.DistanceTo(world);
        if (distance >= radius)
        {
            return;
        }
        rig.AddShake(trauma * (1.0f - distance / radius));
    }

    /// <summary>
    /// Raw look input. Yaw is applied by the player body; this consumes pitch
    /// and records both so sway has something to react to.
    /// </summary>
    public void ApplyLookInput(float yawDelta, float pitchDelta, MovementTuning tuning)
    {
        _pitch = Mathf.Clamp(
            _pitch + pitchDelta,
            Mathf.DegToRad(tuning.PitchMinDegrees),
            Mathf.DegToRad(tuning.PitchMaxDegrees));

        Rotation = new Vector3(_pitch, 0.0f, 0.0f);

        _lookVelocityX += yawDelta;
        _lookVelocityY += pitchDelta;
    }

    /// <summary>
    /// Sets the pitch outright, clamped like mouse look. For aiming the view
    /// from a script (the bench); nothing in play calls it.
    /// </summary>
    public void SetPitch(float radians, MovementTuning tuning)
    {
        _pitch = Mathf.Clamp(
            radians,
            Mathf.DegToRad(tuning.PitchMinDegrees),
            Mathf.DegToRad(tuning.PitchMaxDegrees));

        Rotation = new Vector3(_pitch, 0.0f, 0.0f);
    }

    /// <summary>Called on touchdown with the speed the player was falling at.</summary>
    public void AddLandingPunch(float fallSpeed, MovementTuning tuning)
    {
        if (fallSpeed < tuning.LandingPunchMinSpeed)
        {
            return;
        }

        float punch = Mathf.Min(fallSpeed * tuning.LandingPunchPerSpeed, tuning.LandingPunchMax);
        _landingPunch = Mathf.Max(_landingPunch, punch);
    }

    /// <summary>
    /// Adds one shot's worth of recoil. Called from the weapon's Fired signal,
    /// wired up by the player scene -- the weapon does not know this exists.
    /// </summary>
    public void AddRecoil(Vector2 radians, float recoverSpeed, float snappiness)
    {
        _recoilTarget += radians;
        _recoilRecoverSpeed = recoverSpeed;
        _recoilSnappiness = snappiness;
    }

    /// <summary>
    /// Adds one explosion's worth of shake. Called by the player, which hears
    /// about blasts on the event bus -- the rig never learns that explosions
    /// exist, on the same terms as recoil above.
    ///
    /// Accumulates rather than taking the maximum, so an airstrike salvo builds
    /// where a single grenade does not, and clamps at 1 so a stress test cannot
    /// shake the camera out of the level.
    /// </summary>
    public void AddShake(float trauma)
    {
        if (trauma <= 0.0f)
        {
            return;
        }

        _shakeTrauma = Mathf.Min(_shakeTrauma + trauma, 1.0f);
    }

    /// <summary>
    /// Rebuilds the offset transform from scratch every frame. Nothing here
    /// accumulates into the transform itself, only into named terms, so any one
    /// effect can be zeroed in the tuning panel without leaving residue.
    /// </summary>
    public void UpdateProcedural(float dt, MovementTuning tuning, ProceduralInput input)
    {
        UpdateHeadbob(dt, tuning, input);
        UpdateSway(dt, tuning);
        UpdateStrafeRoll(dt, tuning, input);
        UpdateSwingRoll(dt, tuning, input);
        UpdateLandingPunch(dt, tuning);
        UpdateRecoil(dt);
        UpdateShake(dt, tuning);
        UpdateFov(dt, tuning, input);

        Vector3 bob = CurrentBobOffset(tuning, input);
        ShakeTerms(tuning, out Vector3 shakeOffset, out Vector3 shakeRotation);

        Offset.Position = bob + _swayOffset + new Vector3(0.0f, -_landingPunch, 0.0f) + shakeOffset;
        Offset.Rotation = new Vector3(
            _recoilCurrent.Y + shakeRotation.X,
            _recoilCurrent.X + shakeRotation.Y,
            _strafeRoll + _swingRoll + CurrentBobRoll(tuning, input) + shakeRotation.Z);
    }

    private void UpdateShake(float dt, MovementTuning tuning)
    {
        if (_shakeTrauma <= 0.0f)
        {
            return;
        }

        _shakePhase += dt * Mathf.Tau * tuning.BlastShakeFrequency;
        _shakeTrauma = Mathf.Max(0.0f, _shakeTrauma - (tuning.BlastShakeRecoverSpeed * dt));
    }

    /// <summary>
    /// Reads the current shake off the single trauma scalar.
    ///
    /// Trauma is squared, which is the whole reason this reads as an impact
    /// rather than as a wobble: the shake collapses fast out of its peak and
    /// then tails off, instead of decaying at one rate the whole way down.
    ///
    /// The five terms are plain sines at frequencies that do not divide into one
    /// another, so they never line up into a single visible oscillation. A noise
    /// resource would look much the same and would be one more thing to load.
    /// </summary>
    private void ShakeTerms(MovementTuning tuning, out Vector3 offset, out Vector3 rotation)
    {
        offset = Vector3.Zero;
        rotation = Vector3.Zero;

        if (_shakeTrauma <= 0.0f || !tuning.BlastShakeEnabled)
        {
            return;
        }

        float amount = _shakeTrauma * _shakeTrauma;
        float p = _shakePhase;

        offset = new Vector3(
            Mathf.Sin(p * 1.00f) * tuning.BlastShakeMaxOffset * amount,
            Mathf.Sin((p * 1.37f) + 1.7f) * tuning.BlastShakeMaxOffset * amount,
            0.0f);

        float aim = Mathf.DegToRad(tuning.BlastShakeMaxAimDegrees) * amount;
        rotation = new Vector3(
            Mathf.Sin((p * 0.83f) + 0.6f) * aim,
            Mathf.Sin((p * 1.19f) + 2.9f) * aim,
            Mathf.Sin((p * 0.61f) + 4.1f) * Mathf.DegToRad(tuning.BlastShakeMaxRollDegrees) * amount);
    }

    /// <summary>
    /// Advances the stride phase. Deliberately NOT gated on HeadbobEnabled --
    /// only the offset it drives is, in CurrentBobOffset. The phase is also
    /// what the <see cref="Footstep"/> signal counts, and turning the camera
    /// bob off in the tuning panel should change how the camera moves, not
    /// whether the player has feet.
    /// </summary>
    private void UpdateHeadbob(float dt, MovementTuning tuning, ProceduralInput input)
    {
        if (!input.OnFloor)
        {
            return;
        }

        _bobPhase += dt * Mathf.Tau * tuning.HeadbobFrequency * StrideRate(tuning, input);

        EmitFootfalls();
    }

    /// <summary>
    /// Raised once per footfall, at the bottom of each bob dip.
    ///
    /// A local signal, to the one node that owns this rig. Deriving footsteps
    /// from the stride phase rather than from a distance counter of their own
    /// is what makes the sound land on the frame the camera dips, and it gets
    /// the square-root stride rule above for free -- a second cadence
    /// calculation could only ever drift out of step with the first.
    /// </summary>
    [Signal]
    public delegate void FootstepEventHandler();

    /// <summary>
    /// The vertical bob is sin(2 * phase), so its minima -- the footfalls --
    /// are at 3*pi/4 plus any multiple of pi.
    /// </summary>
    private const float FootfallPhaseOffset = 3.0f * Mathf.Pi / 4.0f;

    private int _footfallIndex = int.MinValue;

    private void EmitFootfalls()
    {
        int index = Mathf.FloorToInt((_bobPhase - FootfallPhaseOffset) / Mathf.Pi);

        if (_footfallIndex == int.MinValue)
        {
            // First sample of a stride. Nothing has been stepped over yet.
            _footfallIndex = index;
            return;
        }

        if (index == _footfallIndex)
        {
            return;
        }

        _footfallIndex = index;
        EmitSignal(SignalName.Footstep);
    }

    private Vector3 CurrentBobOffset(MovementTuning tuning, ProceduralInput input)
    {
        if (!tuning.HeadbobEnabled)
        {
            return Vector3.Zero;
        }

        float amplitude = BobAmplitudeScale(tuning, input) * AimScale(tuning);
        // The vertical term runs at double frequency: one dip per footfall,
        // two per full stride.
        return new Vector3(
            Mathf.Cos(_bobPhase) * tuning.HeadbobHorizontalAmplitude * amplitude,
            Mathf.Sin(_bobPhase * 2.0f) * tuning.HeadbobVerticalAmplitude * amplitude,
            0.0f);
    }

    private float CurrentBobRoll(MovementTuning tuning, ProceduralInput input)
    {
        if (!tuning.HeadbobEnabled)
        {
            return 0.0f;
        }
        return Mathf.Cos(_bobPhase)
               * Mathf.DegToRad(tuning.HeadbobRollDegrees)
               * BobAmplitudeScale(tuning, input)
               * AimScale(tuning);
    }

    /// <summary>Aiming damps the procedural motion, or the zoom is unusable.</summary>
    private float AimScale(MovementTuning tuning) => _aiming ? tuning.AimBobScale : 1.0f;

    /// <summary>
    /// Bob cycles per second as a multiple of the walk-speed rate.
    ///
    /// Below walk speed this is just the speed ratio, so walking and
    /// crouch-walking are untouched. Above it, the extra speed is split between
    /// cadence and stride length by HeadbobSpeedExponent -- which is the whole
    /// reason sprinting stops looking like hopping.
    /// </summary>
    private static float StrideRate(MovementTuning tuning, ProceduralInput input)
    {
        if (!input.OnFloor || tuning.WalkSpeed <= 0.0f)
        {
            return 0.0f;
        }

        float speedRatio = input.HorizontalSpeed / tuning.WalkSpeed;
        return speedRatio <= 1.0f
            ? speedRatio
            : Mathf.Pow(speedRatio, tuning.HeadbobSpeedExponent);
    }

    private static float BobAmplitudeScale(MovementTuning tuning, ProceduralInput input)
    {
        // The stride rate, not the raw speed: a longer stride does not throw the
        // head further just because it covers more ground.
        float scale = Mathf.Min(StrideRate(tuning, input), 2.0f);
        if (input.Sprinting)
        {
            scale *= tuning.HeadbobSprintAmplitude;
        }
        return scale;
    }

    /// <summary>
    /// Told by the player, which hears it from the loadout, which hears it from
    /// whichever weapon is in hand. The rig never learns what a WeaponStats is;
    /// it is handed the two numbers it needs.
    /// </summary>
    public void SetAim(bool aiming, float fovScale, float transitionSpeed)
    {
        _aiming = aiming;
        _aimFovScale = fovScale;
        _aimTransitionSpeed = transitionSpeed;
    }

    private bool _aiming;
    private float _aimFovScale = 1.0f;
    private float _aimTransitionSpeed = 13.0f;

    private void UpdateSway(float dt, MovementTuning tuning)
    {
        float scale = _aiming ? tuning.AimSwayScale : 1.0f;

        // Look input pushes the camera opposite the turn, then it springs back.
        Vector3 push = new(
            -_lookVelocityX * tuning.SwayAmount * scale,
            -_lookVelocityY * tuning.SwayAmount * scale,
            0.0f);

        _swayOffset += push;
        _swayOffset = _swayOffset.LimitLength(tuning.SwayMaxOffset);
        _swayOffset = _swayOffset.Lerp(Vector3.Zero, Mathf.Clamp(tuning.SwayRecoverSpeed * dt, 0.0f, 1.0f));

        // Look deltas are consumed once; they arrive per input event, not per frame.
        _lookVelocityX = 0.0f;
        _lookVelocityY = 0.0f;
    }

    private void UpdateStrafeRoll(float dt, MovementTuning tuning, ProceduralInput input)
    {
        float target = -input.StrafeAxis * Mathf.DegToRad(tuning.StrafeRollDegrees);
        _strafeRoll = Mathf.Lerp(_strafeRoll, target, Mathf.Clamp(tuning.StrafeRollSpeed * dt, 0.0f, 1.0f));
    }

    /// <summary>
    /// The lean into a swing. Its own term rather than a bigger strafe roll:
    /// the player computes it from velocity on the line, where strafe roll is
    /// computed from a key, and the two settle at the same rate so a swing
    /// released into a strafe does not jerk between them.
    /// </summary>
    private void UpdateSwingRoll(float dt, MovementTuning tuning, ProceduralInput input)
    {
        _swingRoll = Mathf.Lerp(_swingRoll, input.SwingRoll, Mathf.Clamp(tuning.StrafeRollSpeed * dt, 0.0f, 1.0f));
    }

    private void UpdateLandingPunch(float dt, MovementTuning tuning)
    {
        _landingPunch = Mathf.MoveToward(_landingPunch, 0.0f, tuning.LandingPunchRecoverSpeed * dt * 0.2f);
    }

    private void UpdateRecoil(float dt)
    {
        // Two-stage: the target decays toward zero (recovery) while the visible
        // value chases the target (snap). One shot kicks hard and settles; a
        // burst stacks because the target outruns the recovery.
        _recoilTarget = _recoilTarget.Lerp(Vector2.Zero, Mathf.Clamp(_recoilRecoverSpeed * dt, 0.0f, 1.0f));
        _recoilCurrent = _recoilCurrent.Lerp(_recoilTarget, Mathf.Clamp(_recoilSnappiness * dt, 0.0f, 1.0f));
    }

    private void UpdateFov(float dt, MovementTuning tuning, ProceduralInput input)
    {
        float speedRatio = tuning.SprintSpeed > 0.0f
            ? Mathf.Clamp(input.HorizontalSpeed / tuning.SprintSpeed, 0.0f, 1.0f)
            : 0.0f;

        // Aiming overrides the sprint widening rather than adding to it: you
        // cannot be zoomed in and wind-blasted at the same time.
        float target = _aiming
            ? tuning.BaseFov * _aimFovScale
            : tuning.BaseFov + (input.Sprinting ? tuning.SprintFovAdd * speedRatio : 0.0f) + input.SpeedFovAdd;

        float speed = _aiming ? _aimTransitionSpeed : tuning.FovLerpSpeed;
        Camera.Fov = Mathf.Lerp(Camera.Fov, target, Mathf.Clamp(speed * dt, 0.0f, 1.0f));
    }

    /// <summary>
    /// Per-frame locomotion state the rig needs. Passed in rather than fetched
    /// so the rig never looks up the hierarchy for the player.
    /// </summary>
    public readonly struct ProceduralInput
    {
        public ProceduralInput(
            float horizontalSpeed,
            bool onFloor,
            bool sprinting,
            float strafeAxis,
            float swingRoll = 0.0f,
            float speedFovAdd = 0.0f)
        {
            HorizontalSpeed = horizontalSpeed;
            OnFloor = onFloor;
            Sprinting = sprinting;
            StrafeAxis = strafeAxis;
            SwingRoll = swingRoll;
            SpeedFovAdd = speedFovAdd;
        }

        public float HorizontalSpeed { get; }
        public bool OnFloor { get; }
        public bool Sprinting { get; }

        /// <summary>-1 strafing left, +1 strafing right.</summary>
        public float StrafeAxis { get; }

        /// <summary>Target lean into a swing, in radians. Zero off the line.</summary>
        public float SwingRoll { get; }

        /// <summary>FOV widening from grapple speed, in degrees. Zero off the line.</summary>
        public float SpeedFovAdd { get; }
    }
}
