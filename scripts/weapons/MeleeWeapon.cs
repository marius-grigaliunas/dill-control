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
/// </summary>
public partial class MeleeWeapon : WeaponBase
{
    [ExportGroup("Data")]
    [Export]
    public MeleeStats Stats { get; set; }

    [ExportGroup("Effect Scenes")]
    [Export]
    public PackedScene ImpactScene { get; set; }

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

    /// <summary>Which way the current (or last) swing went.</summary>
    public bool LeftToRight { get; private set; } = true;

    private SwingPhase _phase = SwingPhase.Ready;
    private float _phaseRemaining;
    private float _swingElapsed;
    private float _comboRemaining;
    private bool _queued;

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
    }

    protected override void HandleWeaponInput()
    {
        if (!Input.IsActionJustPressed("fire"))
        {
            return;
        }

        if (_phase == SwingPhase.Ready)
        {
            StartSwing();
            return;
        }

        // Buffered from the moment the blade lands. Clicking during the windup
        // -- before this swing has done anything -- is too early and dropped.
        if (_phase != SwingPhase.Windup)
        {
            _queued = true;
        }
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
        _queued = false;
        LeftToRight = true;
    }

    // ---------------- Swing state machine ----------------

    private void StartSwing()
    {
        bool chained = _comboRemaining > 0.0f;

        // Back the other way inside the window; from the left again outside it.
        LeftToRight = !chained || !LeftToRight;

        _comboRemaining = 0.0f;
        _queued = false;
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
        if (_phase == SwingPhase.Ready)
        {
            TickCombo(dt);
            return;
        }

        _swingElapsed += dt;
        _phaseRemaining -= dt;

        // A buffered click does not wait for the blade to get back to rest:
        // once enough of the recovery has passed, the return stroke starts
        // from wherever the blade is now.
        if (_phase == SwingPhase.Recovery && _queued
            && _swingElapsed - _windup - _strike >= _recovery * Stats.RecoveryCancelFraction)
        {
            _phase = SwingPhase.Ready;
            _phaseRemaining = 0.0f;
            _comboRemaining = Stats.ComboWindow;
            StartSwing();
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

        if (_queued)
        {
            StartSwing();
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

        if (_phase == SwingPhase.Ready)
        {
            base.TickViewModel(dt);
            return;
        }

        ViewModel.Transform = SwingTransform();
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

    /// <summary>One candidate hit, before de-duplication picks between them.</summary>
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
