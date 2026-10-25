using DillControl.Audio;
using DillControl.Core;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A mech's downward arm slam, for clearing the crowd at its feet. Two ways
/// to fire it:
///
/// - <b>Ridden</b> (<see cref="Manual"/> on): the rider's trigger starts a
///   stroke; holding it chains, alternating arms.
/// - <b>On its own</b>: it slams when <see cref="MechGunner.Target"/> is
///   inside the cone and range, the crowd chewing its legs.
///
/// Each stroke is a shoulder arc taken from <see cref="MechSlashStats"/>:
/// cock up and back, swing through to the follow-through, return. The four
/// angles are degrees in that resource. <see cref="Preview"/> loops the arc.
/// Three slices of <see cref="Blast"/> land along the club as it arrives.
/// No clips.
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

    /// <summary>Loops the swing with no trigger, so the arc can be watched
    /// and the stats edited between strokes.</summary>
    [Export]
    public bool Preview { get; set; }

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
        bool want = Preview || (Manual ? Trigger : AutoWants());

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
            // The fist is still coming down through the first half. The hits
            // land as it arrives and while it stays down.
            float u = Mathf.Clamp((_elapsed - _windup) / Mathf.Max(_strike, 1e-4f), 0.0f, 1.0f);
            if (u >= 0.45f)
            {
                float window = (u - 0.45f) / 0.55f;
                int slice = Mathf.Clamp(Mathf.FloorToInt(window * 3.0f), 0, 2);
                while (_sliced <= slice)
                {
                    Slice();
                    _sliced++;
                }
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
        _moving = Walker.PlayMove(_arm == 0 ? Stats.RightMove : Stats.LeftMove, 0.0f);
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
                // Nose down into the blow. The sink sells the impact.
                Walker.Kick(Walker.GlobalBasis.Z * 2.6f);
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

    // The stroke is a Blender move the walker plays (Stats.RightMove/LeftMove).
    private bool _moving;

    private void PoseStroke()
    {
        if (_moving)
        {
            return;
        }
        ArmAngles(_elapsed, out float pitch, out float yaw);
        Walker.StrokeArm(_arm, pitch, yaw, 1.0f);
    }

    /// <summary>0 is hanging. The windup lifts to the cock, the strike travels
    /// to the follow-through, the recovery returns along the same arc.
    /// Angles are read from <see cref="Stats"/> every frame.</summary>
    private void ArmAngles(float elapsed, out float pitch, out float yaw)
    {
        float windup = Mathf.Max(_windup, 1e-4f);
        float strike = Mathf.Max(_strike, 1e-4f);
        float recovery = Mathf.Max(_recovery, 1e-4f);
        // Arm 0 is the model's right, on -X. Positive yaw in the stats is
        // out to the left; mirror it so each fist cocks to its own side.
        float side = _arm == 0 ? -1.0f : 1.0f;
        float cockPitch = Mathf.DegToRad(Stats.CockPitch);
        float strikePitch = -Mathf.DegToRad(Stats.StrikePitch);
        float cockYaw = side * Mathf.DegToRad(Stats.CockYaw);
        float strikeYaw = -side * Mathf.DegToRad(Stats.StrikeYaw);

        if (elapsed < windup)
        {
            float t = Smooth(elapsed / windup);
            pitch = Mathf.Lerp(0.0f, cockPitch, t);
            yaw = Mathf.Lerp(0.0f, cockYaw, t);
            return;
        }

        if (elapsed < windup + strike)
        {
            float t = Smooth((elapsed - windup) / strike);
            pitch = Mathf.Lerp(cockPitch, strikePitch, t);
            yaw = Mathf.Lerp(cockYaw, strikeYaw, t);
            return;
        }

        float back = Smooth(Mathf.Clamp((elapsed - windup - strike) / recovery, 0.0f, 1.0f));
        pitch = Mathf.Lerp(strikePitch, 0.0f, back);
        yaw = Mathf.Lerp(strikeYaw, 0.0f, back);
    }

    private static float Smooth(float t) => t * t * (3.0f - 2.0f * t);

    private void Slice()
    {
        Node3D limb = Walker.Arms[_arm];
        if (limb == null || !Walker.PartAlive(limb))
        {
            return;
        }

        Vector3 shoulder = limb.GlobalPosition;
        Vector3 tip = Walker.StrokeTip(_arm);
        int spheres = Mathf.Max(1, Stats.Spheres);
        PhysicsDirectSpaceState3D space = Walker.GetWorld3D().DirectSpaceState;

        for (int i = 0; i < spheres; i++)
        {
            // Along the club, toward the fist, which is where the swing is.
            float along = Mathf.Lerp(0.45f, 1.0f, (i + 0.5f) / spheres);
            var blast = new Blast.Settings
            {
                Centre = shoulder.Lerp(tip, along),
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
            Walker.StrokeArm(i, 0.0f, 0.0f, 0.0f);
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
