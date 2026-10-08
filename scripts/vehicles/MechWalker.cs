using System.Collections.Generic;
using DillControl.Core;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A four-legged walker, moved and animated entirely by code. No clips: the
/// body is a CharacterBody3D sliding on a box, and the legs find their own
/// way to keep up with it.
///
/// - Each foot stays planted until the body has moved or turned far enough
///   to leave it behind, then steps to where the body will be a moment later.
///   Because steps follow the body rather than playing a cycle, walking in any
///   direction, turning on the spot and stopping all come out of one rule.
/// - Diagonal pairs step together, and a pair waits for the other to land:
///   a trot.
/// - Each leg is posed by two-bone IK from hip to sole (<see cref="MechLeg"/>).
/// - The body rides a spring: it sinks at speed for reach, dips with each
///   step, leans against acceleration, loads before a jump and squashes on
///   landing.
///
/// What it does is not decided here. Something else sets <see cref="MoveIntent"/>,
/// <see cref="FaceDirection"/>, <see cref="TurretAim"/> and the jump each
/// frame: <see cref="MechCompanion"/> for the player's ally.
///
/// The model's front is +Z, as every imported model's is.
/// </summary>
public partial class MechWalker : CharacterBody3D
{
    [ExportGroup("Data")]
    [Export]
    public MechTuning Tuning { get; set; }

    [ExportGroup("Scene Wiring")]
    /// <summary>The imported model's root. The body's bob and tilt move it;
    /// the parts below it are posed one by one.</summary>
    [Export]
    public Node3D Model { get; set; }

    [Export]
    public Node3D Turret { get; set; }

    /// <summary>Front left, front right, back left, back right.</summary>
    [Export]
    public Godot.Collections.Array<Node3D> Thighs { get; set; } = new();

    [Export]
    public Godot.Collections.Array<Node3D> Shins { get; set; } = new();

    [Export]
    public Godot.Collections.Array<Node3D> Feet { get; set; } = new();

    /// <summary>Where to walk, world space, length up to 1 for full speed.</summary>
    public Vector3 MoveIntent { get; set; }

    /// <summary>Which way the body should turn to face, world space; zero
    /// leaves it where it is.</summary>
    public Vector3 FaceDirection { get; set; }

    /// <summary>Which way the turret should point, world space; zero brings
    /// it back to the front.</summary>
    public Vector3 TurretAim { get; set; }

    /// <summary>Held jump: the jets, once in the air.</summary>
    public bool JumpHeld { get; set; }

    public bool IsGrounded { get; private set; } = true;

    public float JetFuel01 => Tuning == null || Tuning.JetFuelSeconds <= 0.0f ? 0.0f : _fuel / Tuning.JetFuelSeconds;

    public Vector3 Up { get; private set; } = Vector3.Up;

    private readonly List<MechLeg> _legs = new();
    private Transform3D _modelRest;
    private Transform3D _turretRest;
    private float _turretYaw;

    private float _fuel;
    private float _windup = -1.0f;
    private bool _jumpQueued;
    private float _yawRate;
    private Vector3 _lastHorizontal;
    private Vector3 _acceleration;

    // The body's spring: a height offset and a tilt, each with a velocity.
    private float _sink;
    private float _sinkVelocity;
    private Vector2 _tilt;
    private Vector2 _tiltVelocity;

    private float _pivotHeight;

    public override void _Ready()
    {
        if (Tuning == null || Model == null)
        {
            GD.PushError($"MechWalker '{Name}' needs Tuning and Model.");
            SetPhysicsProcess(false);
            return;
        }

        _modelRest = Model.Transform;
        if (Turret != null)
        {
            _turretRest = Turret.Transform;
        }

        int count = Mathf.Min(Thighs.Count, Mathf.Min(Shins.Count, Feet.Count));
        for (int i = 0; i < count; i++)
        {
            // FL and BR step together, FR and BL together.
            int group = i == 0 || i == 3 ? 0 : 1;
            _legs.Add(new MechLeg(Thighs[i], Shins[i], Feet[i], group, _modelRest));
        }

        // The body tilts about its hips, not about the feet.
        _pivotHeight = _legs.Count > 0 ? (_modelRest * Thighs[0].Transform.Origin).Y : 0.0f;

        _fuel = Tuning.JetFuelSeconds;
        CollisionLayer = GameLayers.Mask(GameLayers.Prop);
        // The player too, so a step that does come too close is blocked
        // rather than walking through them. Stowed on board, they collide
        // with nothing.
        CollisionMask = GameLayers.Mask(GameLayers.World, GameLayers.Prop, GameLayers.Player);
        FloorSnapLength = 0.4f;

        // Planted where they stand; the first steps correct anything off.
        // The space cannot be queried outside a physics frame.
        Transform3D self = GlobalTransform;
        foreach (MechLeg leg in _legs)
        {
            leg.Planted = self * leg.RestFoot;
        }
    }

    /// <summary>Jumps from the ground, after the wind-up. Ignored in the air.</summary>
    public void RequestJump()
    {
        _jumpQueued = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 gravity = GetGravity() * Tuning.GravityScale;
        Up = gravity.LengthSquared() > 1e-6f ? -gravity.Normalized() : Vector3.Up;
        UpDirection = Up;

        bool wasGrounded = IsGrounded;
        float fallSpeed = -Velocity.Dot(Up);

        Move(dt, gravity);
        Turn(dt);

        IsGrounded = IsOnFloor();
        if (IsGrounded && !wasGrounded)
        {
            Land(fallSpeed);
        }

        UpdateGait(dt);
        UpdateBody(dt);
        PoseLegs();
        AimTurret(dt);
    }

    // ---------------- Body motion ----------------

    private void Move(float dt, Vector3 gravity)
    {
        Vector3 velocity = Velocity;
        float vertical = velocity.Dot(Up);
        Vector3 horizontal = velocity - Up * vertical;

        Vector3 intent = MoveIntent.Slide(Up).LimitLength(1.0f);
        Vector3 target = intent * Tuning.MaxSpeed;
        if (_windup >= 0.0f)
        {
            // Planted while it loads.
            target = Vector3.Zero;
        }

        float rate = target.Length() > horizontal.Length() ? Tuning.Acceleration : Tuning.Deceleration;
        if (!IsGrounded)
        {
            rate *= Tuning.AirControl;
        }
        horizontal = horizontal.MoveToward(target, rate * dt);

        if (_jumpQueued && IsGrounded && _windup < 0.0f)
        {
            _windup = Tuning.JumpWindupSeconds;
        }
        _jumpQueued = false;

        if (_windup >= 0.0f)
        {
            _windup -= dt;
            if (_windup < 0.0f)
            {
                vertical = Tuning.JumpSpeed;
                IsGrounded = false;
                foreach (MechLeg leg in _legs)
                {
                    leg.Stepping = false;
                }
            }
        }

        if (!IsOnFloor() || vertical > 0.0f)
        {
            vertical -= gravity.Length() * dt;
            if (JumpHeld && _fuel > 0.0f && !IsOnFloor())
            {
                vertical += gravity.Length() * Tuning.JetThrustOfGravity * dt;
                _fuel -= dt;
            }
        }
        if (IsOnFloor())
        {
            _fuel = Mathf.Min(Tuning.JetFuelSeconds, _fuel + Tuning.JetRefillPerSecond * dt);
        }

        _acceleration = _acceleration.Lerp((horizontal - _lastHorizontal) / Mathf.Max(dt, 1e-4f), 1.0f - Mathf.Exp(-10.0f * dt));
        _lastHorizontal = horizontal;

        Velocity = horizontal + Up * vertical;
        MoveAndSlide();
    }

    private void Turn(float dt)
    {
        _yawRate = 0.0f;
        Vector3 face = FaceDirection.Slide(Up);
        if (face.LengthSquared() < 1e-4f)
        {
            return;
        }

        Vector3 forward = GlobalBasis.Z.Slide(Up).Normalized();
        float angle = forward.SignedAngleTo(face.Normalized(), Up);
        float step = Mathf.Clamp(angle, -Mathf.DegToRad(Tuning.TurnRateDegrees) * dt, Mathf.DegToRad(Tuning.TurnRateDegrees) * dt);
        GlobalRotate(Up, step);
        _yawRate = step / Mathf.Max(dt, 1e-4f);
    }

    private void Land(float fallSpeed)
    {
        _sinkVelocity -= Mathf.Max(0.0f, fallSpeed) * Tuning.LandingKick;
        Transform3D self = GlobalTransform;
        foreach (MechLeg leg in _legs)
        {
            leg.Stepping = false;
            leg.Planted = Ground(self * leg.RestFoot);
        }
    }

    // ---------------- Gait ----------------

    private void UpdateGait(float dt)
    {
        Transform3D self = GlobalTransform;
        Vector3 horizontal = Velocity.Slide(Up);

        if (!IsGrounded)
        {
            // Legs hang toward rest in the air, a little extended.
            float follow = 1.0f - Mathf.Exp(-12.0f * dt);
            foreach (MechLeg leg in _legs)
            {
                leg.Planted = leg.Planted.Lerp(self * leg.RestFoot - Up * 0.25f, follow);
            }
            return;
        }

        float speed01 = Mathf.Clamp(horizontal.Length() / Mathf.Max(Tuning.MaxSpeed, 0.01f), 0.0f, 1.0f);
        float stepSeconds = Mathf.Lerp(Tuning.StepSeconds, Tuning.StepSecondsFast, speed01);
        float lead = Tuning.StepLeadSeconds + stepSeconds * 0.5f;
        bool moving = horizontal.Length() > 0.2f || Mathf.Abs(_yawRate) > 0.3f;
        float threshold = moving ? Tuning.StepThreshold : Tuning.IdleTidyThreshold;

        // Where each foot would like to land: its rest spot carried forward by
        // the body's motion over the lead time, turn included.
        foreach (MechLeg leg in _legs)
        {
            if (!leg.Stepping)
            {
                continue;
            }

            leg.StepTo = Ground(Predict(self, leg.RestFoot, horizontal, lead - leg.StepT * stepSeconds));
            leg.StepT += dt / stepSeconds;
            if (leg.StepT >= 1.0f)
            {
                leg.Stepping = false;
                leg.Planted = leg.StepTo;
            }
        }

        bool anyStepping = false;
        foreach (MechLeg leg in _legs)
        {
            anyStepping |= leg.Stepping;
        }
        if (anyStepping)
        {
            return;
        }

        // Neither pair in the air: the pair that has fallen furthest behind goes.
        float[] worst = new float[2];
        foreach (MechLeg leg in _legs)
        {
            float error = (leg.Planted - self * leg.RestFoot).Slide(Up).Length();
            worst[leg.Group] = Mathf.Max(worst[leg.Group], error);
        }
        int group = worst[0] >= worst[1] ? 0 : 1;
        if (worst[group] < threshold)
        {
            return;
        }

        foreach (MechLeg leg in _legs)
        {
            if (leg.Group != group)
            {
                continue;
            }
            leg.Stepping = true;
            leg.StepT = 0.0f;
            leg.StepFrom = leg.Planted;
            leg.StepTo = Ground(Predict(self, leg.RestFoot, horizontal, lead));
        }
    }

    /// <summary>A rest foot spot, moved on by the body's velocity and turn
    /// over <paramref name="seconds"/>.</summary>
    private Vector3 Predict(Transform3D self, Vector3 restFoot, Vector3 horizontal, float seconds)
    {
        seconds = Mathf.Max(seconds, 0.0f);
        Vector3 offset = self.Basis * restFoot;
        offset = offset.Rotated(Up, _yawRate * seconds);
        return self.Origin + offset + horizontal * seconds;
    }

    /// <summary>The ground under a point, or the point on the walker's own
    /// ground plane when there is nothing to hit.</summary>
    private Vector3 Ground(Vector3 point)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(point + Up * 2.5f, point - Up * 3.0f,
            GameLayers.Mask(GameLayers.World, GameLayers.Prop), new Godot.Collections.Array<Rid> { GetRid() });
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count > 0)
        {
            return (Vector3)hit["position"];
        }
        return point - Up * (point - GlobalPosition).Dot(Up);
    }

    // ---------------- Body pose ----------------

    private void UpdateBody(float dt)
    {
        Vector3 horizontal = Velocity.Slide(Up);
        float speed01 = Mathf.Clamp(horizontal.Length() / Mathf.Max(Tuning.MaxSpeed, 0.01f), 0.0f, 1.0f);

        float target = -Tuning.WalkCrouch * speed01;
        foreach (MechLeg leg in _legs)
        {
            if (leg.Stepping)
            {
                target -= Tuning.StepBob * Mathf.Sin(Mathf.Pi * leg.StepT);
            }
        }
        if (_windup >= 0.0f && Tuning.JumpWindupSeconds > 0.0f)
        {
            target -= Tuning.JumpCrouch * (1.0f - _windup / Tuning.JumpWindupSeconds);
        }

        // Lean back against acceleration and out of a turn, in the body's own
        // frame: x is pitch, y is roll.
        Basis self = GlobalBasis;
        float forwardAccel = _acceleration.Dot(self.Z);
        float sideAccel = _acceleration.Dot(self.X);
        float maxLean = Mathf.DegToRad(Tuning.MaxLeanDegrees);
        var tiltTarget = new Vector2(
            Mathf.Clamp(-forwardAccel * Mathf.DegToRad(Tuning.LeanPerAcceleration), -maxLean, maxLean),
            Mathf.Clamp(sideAccel * Mathf.DegToRad(Tuning.LeanPerAcceleration), -maxLean, maxLean));

        float k = Tuning.BodySpring;
        float c = 2.0f * Tuning.BodyDamping * Mathf.Sqrt(k);
        _sinkVelocity += ((target - _sink) * k - _sinkVelocity * c) * dt;
        _sink += _sinkVelocity * dt;
        _tiltVelocity += ((tiltTarget - _tilt) * k - _tiltVelocity * c) * dt;
        _tilt += _tiltVelocity * dt;

        var pivot = new Vector3(0.0f, _pivotHeight, 0.0f);
        Basis tilt = new Basis(Vector3.Right, _tilt.X) * new Basis(Vector3.Back, _tilt.Y);
        Transform3D sway = new Transform3D(Basis.Identity, pivot + Vector3.Up * _sink)
                           * new Transform3D(tilt, Vector3.Zero)
                           * new Transform3D(Basis.Identity, -pivot);
        Model.Transform = sway * _modelRest;
    }

    private void PoseLegs()
    {
        Transform3D model = Model.GlobalTransform;
        Basis level = GlobalBasis * _modelRest.Basis;
        float stepHeight = Tuning.StepHeight;
        foreach (MechLeg leg in _legs)
        {
            Vector3 sole = leg.Planted;
            float toe = 0.0f;
            if (leg.Stepping)
            {
                float t = Mathf.Clamp(leg.StepT, 0.0f, 1.0f);
                float eased = t * t * (3.0f - 2.0f * t);
                float arc = Mathf.Sin(Mathf.Pi * t);
                // Short shuffles lift less than strides.
                float lift = stepHeight * Mathf.Clamp(leg.StepFrom.DistanceTo(leg.StepTo) / Mathf.Max(Tuning.StepThreshold, 0.01f), 0.3f, 1.0f);
                sole = leg.StepFrom.Lerp(leg.StepTo, eased) + Up * (arc * lift);
                toe = arc * 0.35f;
            }
            leg.Pose(model, level, Up, sole, toe);
        }
    }

    // ---------------- Turret ----------------

    private void AimTurret(float dt)
    {
        if (Turret == null)
        {
            return;
        }

        float target = 0.0f;
        if (TurretAim.LengthSquared() > 1e-4f)
        {
            Vector3 local = Model.GlobalBasis.Inverse() * TurretAim;
            target = Mathf.Atan2(local.X, local.Z);
        }

        float difference = Mathf.Wrap(target - _turretYaw, -Mathf.Pi, Mathf.Pi);
        float step = Mathf.DegToRad(Tuning.TurretTurnRateDegrees) * dt;
        _turretYaw = Mathf.Wrap(_turretYaw + Mathf.Clamp(difference, -step, step), -Mathf.Pi, Mathf.Pi);
        Turret.Transform = new Transform3D(new Basis(Vector3.Up, _turretYaw) * _turretRest.Basis, _turretRest.Origin);
    }

    /// <summary>One line of state, for <c>bench.gd</c>.</summary>
    public string BenchStatus()
    {
        int stepping = 0;
        foreach (MechLeg leg in _legs)
        {
            stepping += leg.Stepping ? 1 : 0;
        }
        Vector3 p = GlobalPosition;
        return System.FormattableString.Invariant(
            $"mech pos=({p.X:F1},{p.Y:F1},{p.Z:F1}) speed={Velocity.Slide(Up).Length():F1} up={Velocity.Dot(Up):F1} grounded={IsGrounded} stepping={stepping} fuel={JetFuel01:F2} turret={Mathf.RadToDeg(_turretYaw):F0}");
    }
}
