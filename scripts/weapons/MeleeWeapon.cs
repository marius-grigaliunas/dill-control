using System.Collections.Generic;
using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// The machete: one horizontal swing, alternating. The first swing goes left
/// to right; a click inside the combo window swings back right to left, and
/// so on. Let the window lapse and the next swing starts left to right again.
/// Everything it does is driven by its <see cref="MeleeStats"/>.
///
/// The swing is sampled as a fan of rays across the arc rather than as an
/// overlap volume. That reuses the query the guns already make, needs no shape
/// resource, and answers the honest question: did the blade pass through this
/// on its way across. The fan turns the same way the viewmodel does, from one
/// number, so what you see is what the blade did.
///
/// Ported from the lab's machete, which chained strokes in random directions
/// and had a stab and a momentum cut. Those wait in the lab; this keeps its hit
/// test, its timing, its chain buffering and its blade pose.
///
/// Alt fire, or fire when the stats name a <see cref="MeleeStroke"/>, plays
/// that stroke instead: posed, with the head lagging the grip, and a hit that
/// stops on the surface instead of following through.
/// </summary>
public partial class MeleeWeapon : WeaponBase
{
    [ExportGroup("Data")]
    [Export]
    public MeleeStats Stats { get; set; }

    [ExportGroup("Effect Scenes")]
    [Export]
    public PackedScene ImpactScene { get; set; }

    [ExportGroup("Stroke")]
    /// <summary>Where the stroke aims its contact pose. The blade tip or the hammer face.</summary>
    [Export]
    public Marker3D Head { get; set; }

    /// <summary>
    /// The heavy part, pivoted at the grip. Its rotation lags the viewmodel
    /// by <see cref="MeleeStats.Heft"/>, so the head trails the hands. Empty
    /// on a weapon light enough that the whole mesh is the swing.
    /// </summary>
    [Export]
    public Node3D HeadPivot { get; set; }

    /// <summary>Loops the stroke while equipped, for tuning the poses. Fire is ignored.</summary>
    [Export]
    public bool Preview { get; set; }

    /// <summary>Local signal: a swing began, and which way it is going.</summary>
    [Signal]
    public delegate void SwingStartedEventHandler(bool leftToRight);

    /// <summary>Local signal: true when the swing landed on a weak point.</summary>
    [Signal]
    public delegate void HitConfirmedEventHandler(bool weakPoint, Vector3 position);

    private enum SwingPhase
    {
        Ready,
        Windup,
        Strike,
        Recovery,
    }

    public override bool UsesAmmo => false;

    public override string DisplayName => Stats?.DisplayName ?? Name;

    protected override float ViewModelRecoverSpeed => Stats?.ViewmodelRecoverSpeed ?? 14.0f;

    /// <summary>True while a swing is anywhere between the click and the reset.</summary>
    public bool IsSwinging => _phase != SwingPhase.Ready;

    /// <summary>True while a posed stroke is between the click and the reset.</summary>
    public bool IsStroking => _strokePhase != StrokePhase.None;

    private bool IsAttacking => IsSwinging || IsStroking;

    /// <summary>Which way the current (or last) swing went.</summary>
    public bool LeftToRight { get; private set; } = true;

    private SwingPhase _phase = SwingPhase.Ready;
    private float _phaseRemaining;
    private float _swingElapsed;
    private float _comboRemaining;

    // The timings this swing is running on, cached at its start: a chained
    // swing scales them, and the hit phase and the viewmodel curve have to
    // agree on the same numbers.
    private float _windup;
    private float _strike;
    private float _recovery;

    /// <summary>
    /// Where the viewmodel was when this swing began. The windup blends from
    /// here rather than from rest, so a chained stroke starts where the last
    /// one's blade actually is and the pair reads as one motion.
    /// </summary>
    private Transform3D _swingFrom;

    /// <summary>
    /// One ray query for every ray of every swing. A query is a ref-counted
    /// engine object, and nine new ones a swing left for the garbage collector
    /// were finalized after the engine had shut down -- a crash on quit.
    /// </summary>
    private PhysicsRayQueryParameters3D _query;

    private enum StrokePhase
    {
        None,
        Raise,
        Anticipation,
        Strike,
        ContactHold,
        Recover,
        MissRecover,
    }

    private enum AttackQueue
    {
        None,
        Primary,
        Alt,
    }

    private StrokePhase _strokePhase = StrokePhase.None;
    private MeleeStroke _stroke;
    private float _segLeft;
    private float _segLength;
    private float _strokeElapsed;
    private bool _strokeResolved;
    private bool _strokeHit;
    private bool _strokeTraced;
    private StrokeContact _tracedContact;
    private Transform3D _segFrom;
    private Transform3D _contactPose;
    private Transform3D _recoverPose;
    private Transform3D _heldPose;
    private Vector3 _headLocal = new(0.0f, 0.0f, -0.45f);
    private ulong _holdUntilMs;
    private AttackQueue _queue;
    private bool _lagReady;
    private Basis _laggedBasis;

    private PhysicsShapeQueryParameters3D _shapeQuery;
    private SphereShape3D _sweepSphere;

    /// <summary>One connection from a stroke's trace, before it is sorted into a hitbox or a blast.</summary>
    private readonly struct StrokeContact(
        bool hit,
        GodotObject collider,
        Vector3 point,
        Vector3 normal,
        Vector3 direction,
        float distance)
    {
        public bool Hit { get; } = hit;
        public GodotObject Collider { get; } = collider;
        public Vector3 Point { get; } = point;
        public Vector3 Normal { get; } = normal;
        public Vector3 Direction { get; } = direction;
        public float Distance { get; } = distance;
    }

    /// <summary>Screen-space direction the blade travels: 0 is right, Pi is left.</summary>
    private float SwingAngle => LeftToRight ? 0.0f : Mathf.Pi;

    public override void _Ready()
    {
        base._Ready();

        if (Stats == null)
        {
            GD.PushError("MeleeWeapon has no MeleeStats assigned.");
            IsBroken = true;
            SetProcess(false);
        }
    }

    public override void _ExitTree()
    {
        _query?.Dispose();
        _query = null;
        _shapeQuery?.Dispose();
        _shapeQuery = null;
        _sweepSphere?.Dispose();
        _sweepSphere = null;
    }

    protected override void HandleWeaponInput()
    {
        // Preview is a loop for tuning. A click on top of it would queue a
        // second stroke into every cycle.
        if (Preview)
        {
            return;
        }

        bool fire = Input.IsActionJustPressed("fire");
        bool alt = Input.IsActionJustPressed("alt_fire");
        if (!fire && !alt)
        {
            return;
        }

        bool altStroke = alt && Stats.AltStroke != null;
        if (!IsAttacking)
        {
            if (altStroke)
            {
                StartStroke(Stats.AltStroke);
            }
            else if (fire)
            {
                StartPrimary();
            }
            return;
        }

        // Before the attack has done anything, a click is dropped. That is
        // what stops mashing from being the best input, on a stroke as on a swing.
        if (_phase == SwingPhase.Windup
            || _strokePhase == StrokePhase.Raise
            || _strokePhase == StrokePhase.Anticipation)
        {
            return;
        }

        _queue = altStroke ? AttackQueue.Alt : AttackQueue.Primary;
    }

    protected override void OnHolstered()
    {
        // Putting the weapon away cancels the swing outright, and the chain
        // with it: a swing that resolved after the weapon left the hand would
        // be a free hit.
        _phase = SwingPhase.Ready;
        _phaseRemaining = 0.0f;
        _swingElapsed = 0.0f;
        _comboRemaining = 0.0f;
        _queue = AttackQueue.None;
        LeftToRight = true;
        _strokePhase = StrokePhase.None;
        _stroke = null;
        _strokeElapsed = 0.0f;
        _lagReady = false;
        if (HeadPivot != null)
        {
            HeadPivot.Transform = Transform3D.Identity;
        }
    }

    // ---------------- Swing state machine ----------------

    private void StartSwing()
    {
        bool chained = _comboRemaining > 0.0f;

        // Back the other way inside the window; from the left again outside it.
        LeftToRight = !chained || !LeftToRight;

        _comboRemaining = 0.0f;
        _queue = AttackQueue.None;
        _swingElapsed = 0.0f;

        float scale = chained ? Stats.ChainTimingScale : 1.0f;
        _windup = Stats.Windup * scale;
        _strike = Stats.Strike;
        _recovery = Stats.Recovery * scale;

        _swingFrom = ViewModel?.Transform ?? Transform3D.Identity;

        _phase = SwingPhase.Windup;
        _phaseRemaining = _windup;

        AudioDirector.Play(Sfx.MeleeSwingLight);
        EmitSignal(SignalName.SwingStarted, LeftToRight);

        // A zero windup still has to reach the strike, through the same path.
        if (_phaseRemaining <= 0.0f)
        {
            AdvancePhase();
        }
    }

    protected override void TickWeapon(float dt)
    {
        if (_strokePhase != StrokePhase.None)
        {
            TickStroke(dt);
            return;
        }

        if (_phase == SwingPhase.Ready)
        {
            if (Preview)
            {
                MeleeStroke preview = Stats.AltStroke ?? Stats.PrimaryStroke;
                if (preview != null)
                {
                    StartStroke(preview);
                }
                return;
            }

            TickCombo(dt);
            return;
        }

        _swingElapsed += dt;
        _phaseRemaining -= dt;

        // A buffered click does not wait for the blade to get back to rest:
        // once enough of the recovery has passed, the return stroke starts
        // from wherever the blade is now.
        if (_phase == SwingPhase.Recovery && _queue != AttackQueue.None
            && _swingElapsed - _windup - _strike >= _recovery * Stats.RecoveryCancelFraction)
        {
            _phase = SwingPhase.Ready;
            _phaseRemaining = 0.0f;
            _comboRemaining = Stats.ComboWindow;
            ReleaseQueue();
            return;
        }

        while (_phase != SwingPhase.Ready && _phaseRemaining <= 0.0f)
        {
            AdvancePhase();
        }
    }

    /// <summary>Between swings: spends the combo window, and starts a buffered swing the frame it can.</summary>
    private void TickCombo(float dt)
    {
        if (_comboRemaining > 0.0f)
        {
            _comboRemaining = Mathf.Max(0.0f, _comboRemaining - dt);
        }

        if (_queue != AttackQueue.None)
        {
            ReleaseQueue();
        }
    }

    private void AdvancePhase()
    {
        switch (_phase)
        {
            case SwingPhase.Windup:
                _phase = SwingPhase.Strike;
                _phaseRemaining += _strike;
                // Hits resolve once, on the frame the blade arrives. A window
                // that tested every frame would let a held swing sweep a room.
                ResolveSwing();
                break;

            case SwingPhase.Strike:
                _phase = SwingPhase.Recovery;
                _phaseRemaining += _recovery;
                break;

            default:
                _phase = SwingPhase.Ready;
                _phaseRemaining = 0.0f;
                _swingElapsed = 0.0f;
                _comboRemaining = Stats.ComboWindow;
                break;
        }
    }

    /// <summary>Fire's attack: the primary stroke when one is set, otherwise the horizontal swing.</summary>
    private void StartPrimary()
    {
        if (Stats.PrimaryStroke != null)
        {
            StartStroke(Stats.PrimaryStroke);
            return;
        }

        StartSwing();
    }

    /// <summary>Starts whatever was buffered. The combo window is already open when a swing chains.</summary>
    private void ReleaseQueue()
    {
        AttackQueue queued = _queue;
        _queue = AttackQueue.None;

        if (queued == AttackQueue.Alt && Stats.AltStroke != null)
        {
            StartStroke(Stats.AltStroke);
            return;
        }

        if (queued == AttackQueue.None)
        {
            return;
        }

        StartPrimary();
    }

    // ---------------- Hit resolution ----------------

    private void ResolveSwing()
    {
        ApplyCameraKick();

        if (AimCamera == null)
        {
            return;
        }

        float range = Stats.Range * Bonus.MeleeReachScale;
        float arc = Mathf.DegToRad(Mathf.Min(170.0f, Stats.ArcDegrees * Bonus.MeleeArcScale));

        // More rays for a wider fan, so an upgraded arc is as dense as the
        // authored one rather than the same rays spread thinner.
        int rayCount = Mathf.Max(1, Mathf.CeilToInt(Stats.ArcRayCount * Mathf.Max(1.0f, Bonus.MeleeArcScale)));

        Vector3 origin = AimCamera.GlobalPosition;
        Vector3 forward = -AimCamera.GlobalBasis.Z.Normalized();
        Vector3 right = AimCamera.GlobalBasis.X.Normalized();
        Vector3 up = AimCamera.GlobalBasis.Y.Normalized();

        // Rotating forward about (forward x travel) moves it along travel, so
        // sweeping the angle from negative to positive carries the fan the way
        // the blade goes.
        Vector3 travel = right * Mathf.Cos(SwingAngle) + (up * Mathf.Sin(SwingAngle));
        Vector3 axis = forward.Cross(travel).Normalized();

        // One entry per receiver, not per hitbox. A target carries several
        // hitboxes over one health pool, and a swing that crosses two of them
        // is one hit on the better of the two -- never two hits.
        Dictionary<IDamageable, SwingHit> best = new();
        Vector3? worldHit = null;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;

        for (int i = 0; i < rayCount; i++)
        {
            float t = rayCount == 1 ? 0.5f : (float)i / (rayCount - 1);
            float angle = Mathf.Lerp(-arc * 0.5f, arc * 0.5f, t);
            Vector3 direction = forward.Rotated(axis, angle).Normalized();

            _query ??= new PhysicsRayQueryParameters3D
            {
                CollisionMask = GameLayers.HitscanMask,
                CollideWithAreas = true,   // Hitboxes are Area3D.
                CollideWithBodies = true,  // World and props are bodies.
            };
            _query.From = origin;
            _query.To = origin + (direction * range);

            Godot.Collections.Dictionary result = space.IntersectRay(_query);
            if (result.Count == 0)
            {
                continue;
            }

            Vector3 point = (Vector3)result["position"];
            Vector3 normal = (Vector3)result["normal"];
            GodotObject collider = (GodotObject)result["collider"];

            IDamageable receiver = DamageReceiver.Of(collider);

            if (receiver == null)
            {
                worldHit ??= point;
                continue;
            }

            SwingHit candidate = new(collider as Hitbox, receiver, point, normal, direction, origin.DistanceTo(point));
            if (!best.TryGetValue(receiver, out SwingHit existing) || candidate.Weight > existing.Weight)
            {
                best[receiver] = candidate;
            }
        }

        if (best.Count == 0)
        {
            // Nothing to hurt, but hitting a wall should still read as contact.
            if (worldHit.HasValue)
            {
                SpawnImpact(worldHit.Value);
            }
            return;
        }

        List<SwingHit> hits = new(best.Values);
        hits.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        int limit = Mathf.Min(hits.Count, Mathf.Max(1, Stats.MaxTargetsPerSwing + Bonus.MeleeExtraTargets));
        for (int i = 0; i < limit; i++)
        {
            ApplyHit(hits[i]);
        }
    }

    private void ApplyHit(SwingHit hit)
    {
        // A fresh DamageInfo per target: Hitbox.TakeHit mutates what it is
        // handed, so one shared instance would compound multipliers.
        DamageInfo info = new()
        {
            Amount = Stats.Damage * Bonus.MeleeDamageScale,
            Position = hit.Point,
            Normal = hit.Normal,
            Direction = hit.Direction,
            Distance = hit.Distance,
            Source = this,
        };

        if (hit.Box != null)
        {
            hit.Box.TakeHit(info);
            EmitSignal(SignalName.HitConfirmed, hit.Box.IsWeakPoint, hit.Point);
        }
        else
        {
            hit.Receiver.ApplyDamage(info);
            EmitSignal(SignalName.HitConfirmed, false, hit.Point);
        }

        ApplyKnockback(hit);
        SpawnImpact(hit.Point);
    }

    /// <summary>
    /// Shoves what was struck: a RigidBody3D takes a real impulse, anything
    /// implementing IBlastReceiver folds it into its own velocity.
    /// </summary>
    private void ApplyKnockback(SwingHit hit)
    {
        if (Stats.Knockback <= 0.0f)
        {
            return;
        }

        Node target = hit.Box != null ? hit.Box.Receiver as Node : hit.Receiver as Node;
        Vector3 direction = hit.Direction.Lerp(OwnerUp, Stats.KnockbackUpBias).Normalized();

        switch (target)
        {
            case RigidBody3D body when !body.Freeze:
                body.Sleeping = false;
                body.ApplyImpulse(direction * Stats.Knockback * body.Mass, hit.Point - body.GlobalPosition);
                break;

            case IBlastReceiver receiver:
                receiver.ApplyBlastImpulse(direction * Stats.Knockback);
                break;
        }
    }

    /// <summary>
    /// The camera follows the blade: a left-to-right swing kicks the view
    /// right, and the return kicks it back. A positive yaw turns the view left.
    /// </summary>
    private void ApplyCameraKick()
    {
        float yawSign = LeftToRight ? -1.0f : 1.0f;
        float yaw = Mathf.DegToRad(Stats.KickYawDegrees) * yawSign;
        float pitch = Mathf.DegToRad(Stats.KickPitchDegrees) * Rng.RandfRange(0.8f, 1.0f);

        EmitSignal(SignalName.Fired, new Vector2(yaw, pitch), Stats.KickRecoverSpeed, Stats.KickSnappiness);
    }

    private void SpawnImpact(Vector3 point)
    {
        // Parented to the level, not the weapon, so it does not ride the view;
        // capped with every other impact in the game -- see ImpactEffect.MaxLive.
        ImpactEffect.Spawn(ImpactScene, GetTree().CurrentScene, point);

        // Positional rather than in-hand, unlike the swing: the swing happens
        // at the player and the connection at arm's length, and the difference
        // between the two is what tells you the blade landed.
        AudioDirector.PlayAt(Sfx.MeleeImpact, point);
    }

    // ---------------- Viewmodel ----------------

    protected override void TickViewModel(float dt)
    {
        if (ViewModel == null)
        {
            return;
        }

        if (_strokePhase != StrokePhase.None)
        {
            if (_strokePhase != StrokePhase.ContactHold)
            {
                AdvanceViewModelSpring(dt);
            }

            ViewModel.Transform = CurrentStrokePose();
            TickHeadLag(dt);
            return;
        }

        if (_phase == SwingPhase.Ready)
        {
            base.TickViewModel(dt);
            TickHeadLag(dt);
            return;
        }

        ViewModel.Transform = SwingTransform();
        TickHeadLag(dt);
    }

    /// <summary>
    /// The viewmodel's pose for this moment of the swing: from wherever the
    /// blade was to cocked back on the start side, whipped across the view
    /// (eased at both ends, so it is fastest through the middle where the hit
    /// is tested), then back to rest.
    /// </summary>
    private Transform3D SwingTransform()
    {
        float windup = Mathf.Max(0.0001f, _windup);
        float strike = Mathf.Max(0.0001f, _strike);
        float recovery = Mathf.Max(0.0001f, _recovery);
        float e = _swingElapsed;

        if (e < windup)
        {
            float t = Mathf.Sin(e / windup * Mathf.Pi * 0.5f);
            return _swingFrom.InterpolateWith(StrokePose(-1.0f), t);
        }

        if (e < windup + strike)
        {
            float t = (e - windup) / strike;
            t = t * t * (3.0f - (2.0f * t));
            return StrokePose(Mathf.Lerp(-1.0f, 1.0f, t));
        }

        float back = Mathf.Clamp((e - windup - strike) / recovery, 0.0f, 1.0f);
        back = back * back * (3.0f - (2.0f * back));
        Transform3D rest = new(Basis.FromScale(Vector3.One * ViewModelScale), ViewModelRestPosition);
        return StrokePose(1.0f).InterpolateWith(rest, back);
    }

    /// <summary>
    /// Pose at a point along this stroke: -1 cocked back on the side it starts
    /// from, 0 the blade on the sightline, +1 followed through.
    ///
    /// The blade turns about the axis perpendicular to both the sightline and
    /// the stroke -- the same axis the hit fan turns about -- so the tip sweeps
    /// across the view ahead of the hand, and it is rolled about its own length
    /// so the edge, not the flat or the spine, leads. The lab learned that the
    /// hard way: turned any other way, the handle led and the metal trailed.
    /// </summary>
    private Transform3D StrokePose(float swing)
    {
        float across = Mathf.Cos(SwingAngle);
        float along = Mathf.Sin(SwingAngle);
        Vector3 travel = new(across, along, 0.0f);

        // Forward (-Z) crossed with travel. Turning the blade about this by a
        // positive angle carries its tip toward the travel direction.
        Vector3 axis = new(along, -across, 0.0f);

        // Roll about the blade's own length so its edge (local -Y) points
        // along the travel: (0,-1) turned by psi is (sin psi, -cos psi).
        float roll = Mathf.Atan2(across, -along);

        // The hand travels below the sightline. `side` is the screen direction
        // perpendicular to the travel; the hand stays on whichever side of the
        // sightline it rests on, at least SwingSideOffset from it.
        Vector3 side = new(-along, across, 0.0f);
        float rest = ViewModelRestPosition.Dot(side);
        float sign = rest < 0.0f ? -1.0f : 1.0f;
        float offset = sign * Mathf.Max(Mathf.Abs(rest), Stats.SwingSideOffset);
        float tilt = -sign * Mathf.DegToRad(Stats.SwingTiltDegrees);

        Basis basis = new Basis(axis, Mathf.DegToRad(Stats.SwingArcDegrees) * swing)
            * new Basis(travel, tilt)
            * new Basis(Vector3.Back, roll);

        Vector3 centre = ViewModelRestPosition.Lerp(
            new Vector3(side.X * offset, side.Y * offset, ViewModelRestPosition.Z),
            Stats.SwingCentering);

        Vector3 position = centre
            + (travel * (swing * Stats.SwingLateral))
            + new Vector3(0.0f, 0.0f, -(1.0f - (swing * swing)) * Stats.SwingForward);

        return new Transform3D(basis.Scaled(Vector3.One * ViewModelScale), position);
    }

    // ---------------- Posed stroke ----------------

    /// <summary>Heft as a time multiplier. Clamped so a zero export cannot freeze the stroke.</summary>
    private float Heft => Mathf.Max(0.35f, Stats.Heft);

    private float Scaled(float seconds) => seconds * Heft;

    private void StartStroke(MeleeStroke stroke)
    {
        if (stroke == null)
        {
            return;
        }

        _stroke = stroke;
        _strokePhase = StrokePhase.Raise;
        _segLength = Mathf.Max(0.0001f, Scaled(stroke.Raise));
        _segLeft = Scaled(stroke.Raise);
        _strokeElapsed = 0.0f;
        _strokeResolved = false;
        _strokeTraced = false;
        _strokeHit = false;
        _queue = AttackQueue.None;
        _comboRemaining = 0.0f;
        _segFrom = ViewModel?.Transform ?? Transform3D.Identity;

        if (HeadPivot != null)
        {
            HeadPivot.Transform = Transform3D.Identity;
        }
        _lagReady = false;
        _headLocal = HeadInViewModel();

        AudioDirector.Play(Sfx.MeleeSwingHeavy);
        EmitSignal(SignalName.SwingStarted, true);

        // ResolveAt 0 is the click itself: the trace and the hurt happen
        // before the raise, and the whole stroke aims at what it found.
        if (stroke.ResolveAt <= 0.0f)
        {
            ResolveStroke();
        }

        if (_segLeft <= 0.0f)
        {
            AdvanceStroke();
        }
    }

    private void TickStroke(float dt)
    {
        _strokeElapsed += dt;
        TryTraceAndResolve();

        if (_strokePhase == StrokePhase.ContactHold)
        {
            if (Time.GetTicksMsec() >= _holdUntilMs)
            {
                BeginBounce();
                BeginSegment(StrokePhase.Recover, Scaled(_stroke.Recover), _recoverPose);
            }
            return;
        }

        _segLeft -= dt;
        while (_strokePhase != StrokePhase.None && _strokePhase != StrokePhase.ContactHold && _segLeft <= 0.0f)
        {
            AdvanceStroke();
        }

        if ((_strokePhase == StrokePhase.Recover || _strokePhase == StrokePhase.MissRecover)
            && _queue != AttackQueue.None
            && _segLength - _segLeft >= _segLength * Stats.RecoveryCancelFraction)
        {
            _strokePhase = StrokePhase.None;
            _stroke = null;
            ReleaseQueue();
        }
    }

    private void AdvanceStroke()
    {
        float leftover = -_segLeft;
        switch (_strokePhase)
        {
            case StrokePhase.Raise:
                BeginSegment(StrokePhase.Anticipation, Scaled(_stroke.Hold), StrokeTransform(_stroke.CockedPosition, _stroke.CockedEulerDegrees));
                break;

            case StrokePhase.Anticipation:
                BeginSegment(StrokePhase.Strike, Scaled(_stroke.Strike), StrokeTransform(_stroke.CockedPosition, _stroke.CockedEulerDegrees));
                break;

            case StrokePhase.Strike:
                if (_strokeHit)
                {
                    _heldPose = _contactPose;
                    _strokePhase = StrokePhase.ContactHold;
                    _segLeft = 0.0f;
                    float hold = Scaled(_stroke.ContactHold);
                    _holdUntilMs = Time.GetTicksMsec() + (ulong)(hold * 1000.0f);
                    if (hold <= 0.0f)
                    {
                        BeginBounce();
                        BeginSegment(StrokePhase.Recover, Scaled(_stroke.Recover), _recoverPose);
                        _segLeft -= leftover;
                    }
                }
            else
            {
                BeginSegment(StrokePhase.MissRecover, Scaled(_stroke.MissRecover), StrokeTransform(_stroke.FollowPosition, _stroke.FollowEulerDegrees));
                _segLeft -= leftover;
            }
            return;

            default:
                _strokePhase = StrokePhase.None;
                _stroke = null;
                _segLeft = 0.0f;
                if (_queue != AttackQueue.None)
                {
                    ReleaseQueue();
                }
                return;
        }

        _segLeft -= leftover;
    }

    private void BeginSegment(StrokePhase phase, float duration, Transform3D from)
    {
        _strokePhase = phase;
        _segFrom = from;
        _segLength = Mathf.Max(0.0001f, duration);
        _segLeft = duration;
    }

    /// <summary>
    /// The trace happens no later than the start of the drop, so the drop can
    /// aim at the surface, and no later than <see cref="MeleeStroke.ResolveAt"/>.
    /// The hurt waits until ResolveAt, which is the click when that is 0 and
    /// the arrival when that is 1.
    /// </summary>
    private void TryTraceAndResolve()
    {
        if (_stroke == null)
        {
            return;
        }

        float pre = Scaled(_stroke.PreContactSeconds);
        float resolveAt = _stroke.ResolveAt * pre;
        float traceAt = Mathf.Min(resolveAt, Scaled(_stroke.WindupSeconds));

        if (!_strokeTraced && _strokeElapsed >= traceAt)
        {
            TraceStroke();
        }

        if (!_strokeResolved && _strokeElapsed >= resolveAt)
        {
            ResolveStroke();
        }
    }

    private void TraceStroke()
    {
        // Sample the pose first. Setting the hit changes where the drop aims,
        // and retargeting has to start from the pose already on screen.
        bool retarget = _strokePhase == StrokePhase.Strike;
        Transform3D showing = retarget ? CurrentStrokePose() : Transform3D.Identity;

        _strokeTraced = true;
        StrokeContact contact;
        bool found = _stroke.Shape == MeleeStrokeShape.Fan
            ? TraceFan(out contact)
            : TraceRaySweep(out contact);
        _tracedContact = contact;
        _strokeHit = found && contact.Hit;

        if (_strokeHit)
        {
            Vector3 slide = ContactSlide(contact.Point);
            _contactPose = Slide(StrokeTransform(_stroke.ContactPosition, _stroke.ContactEulerDegrees), slide);
            _recoverPose = Slide(StrokeTransform(_stroke.RecoverPosition, _stroke.RecoverEulerDegrees), slide);
        }

        if (retarget)
        {
            _segFrom = showing;
            _segLength = Mathf.Max(0.0001f, _segLeft);
        }
    }

    private void ResolveStroke()
    {
        if (!_strokeTraced)
        {
            TraceStroke();
        }

        _strokeResolved = true;
        if (_strokeHit)
        {
            ApplyStrokeHit();
        }
    }

    private void ApplyStrokeHit()
    {
        StrokeContact hit = _tracedContact;
        if (!hit.Hit || _stroke == null)
        {
            return;
        }

        // Negative pitch looks down, the same sign as mouse-down. The view
        // drops into the blow instead of climbing the way a gun's recoil does.
        float pitch = -Mathf.DegToRad(_stroke.KickPitchDegrees);
        EmitSignal(SignalName.Fired, new Vector2(0.0f, pitch), Stats.KickRecoverSpeed, Stats.KickSnappiness);
        EmitSignal(SignalName.Impacted, _stroke.ImpactTrauma);

        if (hit.Collider is Hitbox box)
        {
            DamageInfo info = new()
            {
                Amount = Stats.Damage * Bonus.MeleeDamageScale,
                Position = hit.Point,
                Normal = hit.Normal,
                Direction = hit.Direction,
                Distance = hit.Distance,
                Source = this,
            };
            box.TakeHit(info);
            EmitSignal(SignalName.HitConfirmed, box.IsWeakPoint, hit.Point);
            ApplyKnockback(new SwingHit(box, box.Receiver, hit.Point, hit.Normal, hit.Direction, hit.Distance));
            ApplyBlast(hit, _stroke.SplashScale);
        }
        else
        {
            ApplyBlast(hit, 1.0f);
            EmitSignal(SignalName.HitConfirmed, false, hit.Point);
        }

        SpawnImpact(hit.Point);
    }

    /// <summary>
    /// The shockwave. A direct hitbox hit passes a share below 1 so the body
    /// that was aimed at took the swing and its neighbours take the splash.
    /// The world, and a voxel body, take the whole thing: they have no swing
    /// of their own, and <see cref="Blast"/> is how both the crowd and a
    /// building already get hurt.
    /// </summary>
    private void ApplyBlast(StrokeContact hit, float share)
    {
        if (_stroke == null || share <= 0.0f || _stroke.BlastRadius <= 0.0f)
        {
            return;
        }

        PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return;
        }

        float radius = _stroke.BlastRadius * (share >= 1.0f ? 1.0f : 0.55f + (0.45f * share));
        var blast = new Blast.Settings
        {
            Centre = hit.Point + (hit.Normal * 0.12f),
            InnerRadius = radius * 0.35f,
            Radius = radius,
            Damage = _stroke.BlastDamage * Heft * Bonus.MeleeDamageScale * share,
            MinMultiplier = 0.15f,
            Impulse = _stroke.BlastImpulse * Heft * share,
            ImpulseUpBias = _stroke.BlastUpBias,
            Up = OwnerUp,
            Source = this,
            SelfImpulseScale = 0.0f,
            NoCrater = _stroke.NoCrater,
        };

        Blast.ApplyDamage(space, blast);
        Blast.ApplyToRigidBodies(space, blast, 1.0f);
    }

    /// <summary>The spring after the freeze. Heft takes the bounce out, so a sledge thuds and a machete rebounds.</summary>
    private void BeginBounce()
    {
        if (_stroke == null)
        {
            return;
        }

        // Positive pitch lifts the head: a gun's kick uses the same sign to climb.
        AddViewModelKick(_stroke.BounceBack / Heft, _stroke.BouncePitch / Heft);
    }

    private bool TraceRaySweep(out StrokeContact contact)
    {
        contact = default;
        if (AimCamera == null || _stroke == null)
        {
            return false;
        }

        float range = _stroke.Range * Mathf.Max(0.2f, Bonus.MeleeReachScale);
        Vector3 origin = AimCamera.GlobalPosition;
        Vector3 forward = -AimCamera.GlobalBasis.Z.Normalized();

        if (ReadRay(origin, origin + (forward * range), forward, out contact))
        {
            return true;
        }

        PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return false;
        }

        float radius = Mathf.Max(0.05f, _stroke.SweepRadius);
        _sweepSphere ??= new SphereShape3D();
        _sweepSphere.Radius = radius;
        _shapeQuery ??= new PhysicsShapeQueryParameters3D
        {
            CollisionMask = GameLayers.HitscanMask,
            CollideWithAreas = true,
            CollideWithBodies = true,
        };
        _shapeQuery.Shape = _sweepSphere;
        _shapeQuery.Transform = new Transform3D(Basis.Identity, origin);
        _shapeQuery.Motion = forward * range;

        float[] fractions = space.CastMotion(_shapeQuery);
        if (fractions == null || fractions.Length < 2 || fractions[0] >= 1.0f)
        {
            return false;
        }

        // The fraction is how far the sphere's centre travelled before its
        // surface touched. The contact is on the front of that sphere.
        Vector3 centre = origin + (forward * (range * Mathf.Clamp(fractions[0], 0.0f, 1.0f)));
        if (ReadRay(centre, centre + (forward * (radius + 0.25f)), forward, out contact))
        {
            return true;
        }

        _shapeQuery.Transform = new Transform3D(Basis.Identity, centre + (forward * radius * 0.85f));
        _shapeQuery.Motion = Vector3.Zero;
        foreach (Godot.Collections.Dictionary result in space.IntersectShape(_shapeQuery, 4))
        {
            if (!result.TryGetValue("collider", out Variant value)
                || value.AsGodotObject() is not GodotObject collider)
            {
                continue;
            }

            Vector3 point = centre + (forward * radius);
            contact = new StrokeContact(true, collider, point, -forward, forward, origin.DistanceTo(point));
            return true;
        }

        return false;
    }

    private bool TraceFan(out StrokeContact contact)
    {
        contact = default;
        if (AimCamera == null || _stroke == null)
        {
            return false;
        }

        float range = _stroke.Range * Mathf.Max(0.2f, Bonus.MeleeReachScale);
        float arc = Mathf.DegToRad(Mathf.Min(170.0f, _stroke.ArcDegrees * Bonus.MeleeArcScale));
        int rayCount = Mathf.Max(1, Mathf.CeilToInt(_stroke.ArcRayCount * Mathf.Max(1.0f, Bonus.MeleeArcScale)));

        Vector3 origin = AimCamera.GlobalPosition;
        Vector3 forward = -AimCamera.GlobalBasis.Z.Normalized();
        Vector3 right = AimCamera.GlobalBasis.X.Normalized();
        Vector3 up = AimCamera.GlobalBasis.Y.Normalized();
        Vector3 axis = forward.Cross(right).Normalized();
        if (axis.LengthSquared() < 0.0001f)
        {
            axis = up;
        }

        bool any = false;
        float best = float.MaxValue;
        for (int i = 0; i < rayCount; i++)
        {
            float t = rayCount == 1 ? 0.5f : (float)i / (rayCount - 1);
            float angle = Mathf.Lerp(-arc * 0.5f, arc * 0.5f, t);
            Vector3 direction = forward.Rotated(axis, angle).Normalized();
            if (!ReadRay(origin, origin + (direction * range), direction, out StrokeContact candidate))
            {
                continue;
            }

            if (candidate.Distance < best)
            {
                best = candidate.Distance;
                contact = candidate;
                any = true;
            }
        }

        return any;
    }

    private bool ReadRay(Vector3 from, Vector3 to, Vector3 direction, out StrokeContact contact)
    {
        contact = default;
        PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return false;
        }

        _query ??= new PhysicsRayQueryParameters3D
        {
            CollisionMask = GameLayers.HitscanMask,
            CollideWithAreas = true,
            CollideWithBodies = true,
        };
        _query.From = from;
        _query.To = to;

        Godot.Collections.Dictionary result = space.IntersectRay(_query);
        if (result.Count == 0)
        {
            return false;
        }

        Vector3 point = (Vector3)result["position"];
        Vector3 normal = (Vector3)result["normal"];
        var collider = (GodotObject)result["collider"];
        float distance = AimCamera != null ? AimCamera.GlobalPosition.DistanceTo(point) : from.DistanceTo(point);
        contact = new StrokeContact(true, collider, point, normal, direction, distance);
        return true;
    }

    /// <summary>
    /// How far to move a pose, in weapon space, so the head marker lands on
    /// the hit. Clamped, so a short blade reaches and a long one is not dragged
    /// off the screen.
    /// </summary>
    private Vector3 ContactSlide(Vector3 hitPoint)
    {
        Transform3D pose = StrokeTransform(_stroke.ContactPosition, _stroke.ContactEulerDegrees);
        Vector3 headWorld = GlobalTransform * (pose * _headLocal);
        Vector3 error = GlobalTransform.Basis.Inverse() * (hitPoint - headWorld);
        float max = Stats.ContactSlide;
        if (max <= 0.0f)
        {
            return Vector3.Zero;
        }

        return error.Length() > max ? error.Normalized() * max : error;
    }

    private static Transform3D Slide(Transform3D pose, Vector3 localError)
    {
        pose.Origin += localError;
        return pose;
    }

    private Vector3 HeadInViewModel()
    {
        if (Head == null || ViewModel == null)
        {
            return new Vector3(0.0f, 0.0f, -0.45f);
        }

        return ViewModel.GlobalTransform.AffineInverse() * Head.GlobalPosition;
    }

    private Transform3D CurrentStrokePose()
    {
        if (_stroke == null)
        {
            return RestPose();
        }

        float t = 1.0f - Mathf.Clamp(_segLeft / Mathf.Max(0.0001f, _segLength), 0.0f, 1.0f);
        Transform3D pose = _strokePhase switch
        {
            StrokePhase.Raise => _segFrom.InterpolateWith(
                StrokeTransform(_stroke.CockedPosition, _stroke.CockedEulerDegrees),
                MeleeStroke.EaseOut(t, _stroke.RaiseEase)),
            StrokePhase.Anticipation => StrokeTransform(_stroke.CockedPosition, _stroke.CockedEulerDegrees),
            StrokePhase.Strike => _segFrom.InterpolateWith(StrikeEnd(), MeleeStroke.EaseIn(t, _stroke.StrikeEase)),
            StrokePhase.ContactHold => _heldPose,
            StrokePhase.Recover => _segFrom.InterpolateWith(RestPose(), MeleeStroke.EaseOut(t, _stroke.RecoverEase)),
            StrokePhase.MissRecover => _segFrom.InterpolateWith(RestPose(), MeleeStroke.EaseOut(t, _stroke.RecoverEase)),
            _ => RestPose(),
        };

        if (_strokePhase == StrokePhase.Recover || _strokePhase == StrokePhase.MissRecover)
        {
            Basis rotation = pose.Basis.Orthonormalized() * new Basis(Vector3.Right, ViewModelPitchKick);
            pose = new Transform3D(rotation.Scaled(Vector3.One * ViewModelScale), pose.Origin + ViewModelKick);
        }

        return pose;
    }

    /// <summary>Before the trace, the drop aims at the authored contact. After it, at the surface, or through on a miss.</summary>
    private Transform3D StrikeEnd()
    {
        if (!_strokeTraced)
        {
            return StrokeTransform(_stroke.ContactPosition, _stroke.ContactEulerDegrees);
        }

        return _strokeHit
            ? _contactPose
            : StrokeTransform(_stroke.FollowPosition, _stroke.FollowEulerDegrees);
    }

    private Transform3D StrokeTransform(Vector3 position, Vector3 eulerDegrees) =>
        MeleeStroke.Scaled(position, eulerDegrees, ViewModelScale);

    private Transform3D RestPose() =>
        new(Basis.FromScale(Vector3.One * ViewModelScale), ViewModelRestPosition);

    /// <summary>
    /// The head's rotation chases the grip. During the freeze the grip has
    /// stopped and the head is still arriving, which is the weight.
    /// </summary>
    private void TickHeadLag(float dt)
    {
        if (HeadPivot == null || ViewModel == null || Stats == null)
        {
            return;
        }

        Basis current = ViewModel.GlobalBasis.Orthonormalized();
        if (!_lagReady)
        {
            _laggedBasis = current;
            _lagReady = true;
        }

        float omega = Stats.HeadLag / Heft;
        if (_strokePhase is StrokePhase.Strike or StrokePhase.ContactHold or StrokePhase.MissRecover)
        {
            // The raise is where the weight shows. The drop accelerates, and a
            // spring left at the raise rate is still at the top when the grip
            // has stopped, so the head is allowed to catch the strike.
            omega *= 3.2f;
        }
        else if (_strokePhase == StrokePhase.None)
        {
            omega *= 2.2f;
        }

        float blend = 1.0f - Mathf.Exp(-omega * dt);
        _laggedBasis = _laggedBasis.Slerp(current, blend).Orthonormalized();
        HeadPivot.Basis = current.Inverse() * _laggedBasis;
    }

    private readonly struct SwingHit(
        Hitbox box,
        IDamageable receiver,
        Vector3 point,
        Vector3 normal,
        Vector3 direction,
        float distance)
    {
        public Hitbox Box { get; } = box;
        public IDamageable Receiver { get; } = receiver;
        public Vector3 Point { get; } = point;
        public Vector3 Normal { get; } = normal;
        public Vector3 Direction { get; } = direction;
        public float Distance { get; } = distance;

        /// <summary>The higher multiplier wins, so clipping a weak point still rewards the aim.</summary>
        public float Weight => Box?.DamageMultiplier ?? 1.0f;
    }
}
