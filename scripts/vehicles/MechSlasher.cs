using DillControl.Audio;
using DillControl.Core;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A mech's arm slash. Two ways to fire it:
///
/// - <b>Ridden</b> (<see cref="Manual"/> on): the rider's trigger starts a
///   stroke; holding it chains, alternating arms.
/// - <b>On its own</b>: it slashes when <see cref="MechGunner.Target"/> is
///   inside the cone and range, the crowd chewing its legs.
///
/// Each stroke is a pose on <see cref="MechWalker"/> and three slices of
/// <see cref="Blast"/> along the arm, so it reaches the horde over the bus
/// and breaks voxel things like any other hit. No clips.
/// </summary>
public partial class MechSlasher : Node
{
    [ExportGroup("Data")]
    [Export]
    public MechSlashStats Stats { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export]
    public MechWalker Walker { get; set; }

    [Export]
    public MechGunner Gunner { get; set; }

    /// <summary>On, the rider's trigger fires; off, it slashes of its own.</summary>
    public bool Manual { get; set; }

    /// <summary>Ridden: the slash is the selected arm tool and the trigger is down.</summary>
    public bool Trigger { get; set; }

    private enum Phase
    {
        Ready,
        Windup,
        Strike,
        Recovery,
    }

    private Phase _phase = Phase.Ready;
    private float _remaining;
    private float _elapsed;
    private float _combo;
    private float _windup;
    private float _strike;
    private float _recovery;
    private int _arm;
    private int _nextArm;
    private int _sliced;
    private bool _hitThisStrike;

    public override void _Ready()
    {
        if (Stats == null || Walker == null)
        {
            GD.PushError($"MechSlasher '{Name}' needs Stats and Walker.");
            SetPhysicsProcess(false);
            return;
        }

        // Pose the stroke before the walker applies it this frame.
        ProcessPhysicsPriority = -1;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Walker.IsDestroyed || Walker.Arms.Count == 0)
        {
            return;
        }

        float dt = (float)delta;
        bool want = Manual ? Trigger : AutoWants();

        if (_phase == Phase.Ready)
        {
            ClearStrokes();
            if (_combo > 0.0f)
            {
                _combo = Mathf.Max(0.0f, _combo - dt);
            }
            if (want)
            {
                StartStroke();
            }
            return;
        }

        _elapsed += dt;
        _remaining -= dt;
        PoseStroke();

        if (_phase == Phase.Strike)
        {
            float t = Mathf.Clamp(_elapsed - _windup, 0.0f, _strike);
            int slice = Mathf.Clamp(Mathf.FloorToInt(t / Mathf.Max(_strike, 1e-4f) * 3.0f), 0, 2);
            while (_sliced <= slice)
            {
                Slice();
                _sliced++;
            }
        }

        if (_phase == Phase.Recovery && want
            && _elapsed - _windup - _strike >= _recovery * 0.25f)
        {
            StartStroke();
            return;
        }

        while (_phase != Phase.Ready && _remaining <= 0.0f)
        {
            Advance();
        }
    }

    private void StartStroke()
    {
        int arm = PickArm();
        if (arm < 0)
        {
            return;
        }

        _arm = arm;
        _nextArm = Stats.Alternate ? 1 - arm : arm;
        ClearStrokes();
        _combo = 0.0f;
        _elapsed = 0.0f;
        _sliced = 0;
        _hitThisStrike = false;
        _windup = Stats.Windup;
        _strike = Stats.Strike;
        _recovery = Stats.Recovery;
        _phase = Phase.Windup;
        _remaining = _windup;

        Vector3 shoulder = Walker.Arms[_arm].GlobalPosition;
        AudioDirector.PlayAt(Sfx.MeleeSwingLight, shoulder);
        float side = _arm == 0 ? 1.0f : -1.0f;
        Walker.Kick(-Walker.GlobalBasis.X * side * 1.8f + Walker.Up * 0.4f);
        PoseStroke();

        if (_remaining <= 0.0f)
        {
            Advance();
        }
    }

    private void Advance()
    {
        switch (_phase)
        {
            case Phase.Windup:
                _phase = Phase.Strike;
                _remaining += _strike;
                break;
            case Phase.Strike:
                _phase = Phase.Recovery;
                _remaining += _recovery;
                break;
            default:
                _phase = Phase.Ready;
                _remaining = 0.0f;
                _elapsed = 0.0f;
                _combo = Stats.ComboWindow;
                break;
        }
    }

    private void PoseStroke()
    {
        float weight;
        float swing;
        StrokeCurve(_elapsed, out swing, out weight);
        Walker.StrokeArm(_arm, StrokeDirection(_arm, swing), weight);
    }

    /// <summary>Swing is -1 cocked, 0 on the nose, +1 followed through.
    /// Weight eases in through the windup and out through the recovery.</summary>
    private void StrokeCurve(float elapsed, out float swing, out float weight)
    {
        float windup = Mathf.Max(_windup, 1e-4f);
        float strike = Mathf.Max(_strike, 1e-4f);
        float recovery = Mathf.Max(_recovery, 1e-4f);

        if (elapsed < windup)
        {
            float t = elapsed / windup;
            t = t * t * (3.0f - 2.0f * t);
            swing = -1.0f;
            weight = t;
            return;
        }

        if (elapsed < windup + strike)
        {
            float t = (elapsed - windup) / strike;
            t = t * t * (3.0f - 2.0f * t);
            swing = Mathf.Lerp(-1.0f, 1.0f, t);
            weight = 1.0f;
            return;
        }

        float back = Mathf.Clamp((elapsed - windup - strike) / recovery, 0.0f, 1.0f);
        back = back * back * (3.0f - 2.0f * back);
        swing = 1.0f;
        weight = 1.0f - back;
    }

    private Vector3 StrokeDirection(int arm, float swing)
    {
        float side = arm == 0 ? 1.0f : -1.0f;
        float yaw = -side * swing * Mathf.DegToRad(Stats.ArcDegrees * 0.5f);
        const float pitch = -0.32f;
        float cp = Mathf.Cos(pitch);
        var local = new Vector3(Mathf.Sin(yaw) * cp, Mathf.Sin(pitch), Mathf.Cos(yaw) * cp);
        return Walker.GlobalBasis * local;
    }

    private void Slice()
    {
        Node3D limb = Walker.Arms[_arm];
        if (limb == null || !Walker.PartAlive(limb))
        {
            return;
        }

        Vector3 shoulder = limb.GlobalPosition;
        StrokeCurve(_elapsed, out float swing, out _);
        Vector3 dir = StrokeDirection(_arm, swing);
        int spheres = Mathf.Max(1, Stats.Spheres);
        PhysicsDirectSpaceState3D space = Walker.GetWorld3D().DirectSpaceState;

        for (int i = 0; i < spheres; i++)
        {
            float along = Stats.Reach * (i + 0.7f) / spheres;
            var blast = new Blast.Settings
            {
                Centre = shoulder + dir * along,
                InnerRadius = Stats.SphereRadius * 0.45f,
                Radius = Stats.SphereRadius,
                Damage = Stats.Damage,
                MinMultiplier = 0.4f,
                Impulse = Stats.Knockback,
                ImpulseUpBias = 0.35f,
                Up = Walker.Up,
                Source = Walker,
                SourceRid = Walker.GetRid(),
                SelfImpulseScale = 0.0f,
                NoCrater = true,
            };
            int hits = Blast.ApplyDamage(space, blast);
            Blast.ApplyToRigidBodies(space, blast, 1.0f);
            if (hits > 0 && !_hitThisStrike)
            {
                _hitThisStrike = true;
                AudioDirector.PlayAt(Sfx.MeleeImpact, blast.Centre);
            }
        }
    }

    private void ClearStrokes()
    {
        for (int i = 0; i < Walker.Arms.Count; i++)
        {
            Walker.StrokeArm(i, Walker.GlobalBasis.Z, 0.0f);
        }
    }

    private int PickArm()
    {
        int count = Walker.Arms.Count;
        if (count == 0)
        {
            return -1;
        }

        int preferred = Stats.Alternate ? _nextArm : 0;
        preferred = Mathf.Clamp(preferred, 0, count - 1);
        if (ArmAlive(preferred))
        {
            return preferred;
        }
        for (int i = 0; i < count; i++)
        {
            if (ArmAlive(i))
            {
                return i;
            }
        }
        return -1;
    }

    private bool ArmAlive(int i) =>
        i >= 0 && i < Walker.Arms.Count && Walker.Arms[i] != null && Walker.PartAlive(Walker.Arms[i]);

    private bool AutoWants()
    {
        if (Gunner?.Target is not Vector3 target || Stats.AutoRange <= 0.0f)
        {
            return false;
        }

        Vector3 up = Walker.Up;
        Vector3 to = (target - Walker.GlobalPosition).Slide(up);
        if (to.Length() > Stats.AutoRange)
        {
            return false;
        }

        Vector3 nose = Walker.GlobalBasis.Z.Slide(up);
        if (nose.LengthSquared() < 1e-6f || to.LengthSquared() < 1e-6f)
        {
            return true;
        }
        return Mathf.RadToDeg(nose.AngleTo(to)) <= Stats.AutoConeDegrees;
    }
}
