using DillControl.Audio;
using DillControl.Core;
using DillControl.Horde;
using DillControl.Weapons;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A mech's guns: the turret's rapid explosive rounds and the hull cannon's
/// shells. Two ways to fire them:
///
/// - **On its own** (<see cref="Manual"/> off): it finds the nearest pickle
///   through the horde's spatial hash, reports it as <see cref="Target"/> for
///   its brain to turn the turret and body toward, and fires the turret once it
///   points close enough, the cannon once the target is off the nose.
/// - **Ridden**: the rider's camera aims and the triggers fire, the way every
///   hand weapon aims: from the camera, so the crosshair is where it lands.
///
/// Every round and shell goes off through <see cref="Blast"/>, so it reaches the
/// whole horde over the bus, at any range, with no branch for who fired it.
///
/// The horde arrives as an <c>[Export]</c> wired in the level, as the level
/// itself does for <see cref="MechCompanion"/>.
/// </summary>
public partial class MechGunner : Node
{
    [ExportGroup("Data")]
    [Export]
    public MechGunStats Stats { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export]
    public MechWalker Walker { get; set; }

    [Export]
    public Node3D TurretMuzzle { get; set; }

    [Export]
    public Node3D CannonMuzzle { get; set; }

    [Export]
    public HordeDirector Horde { get; set; }

    [Export]
    public PackedScene TracerScene { get; set; }

    [Export]
    public PackedScene ImpactScene { get; set; }

    [Export]
    public PackedScene ExplosionScene { get; set; }

    [Export]
    public PackedScene ShellScene { get; set; }

    /// <summary>On, the rider aims and fires; off, it picks its own targets.</summary>
    public bool Manual { get; set; }

    /// <summary>Ridden: the camera the rider aims with.</summary>
    public Camera3D AimCamera { get; set; }

    public bool TurretTrigger { get; set; }
    public bool CannonTrigger { get; set; }

    /// <summary>Where it means to shoot on its own, if it has found anything.</summary>
    public Vector3? Target { get; private set; }

    /// <summary>Kept clear when choosing targets, so it never fires on the
    /// player's own feet.</summary>
    public Node3D Friendly { get; set; }

    private float _turretCooldown;
    private float _cannonCooldown;
    private float _scanTimer;
    private long _targetHandle;
    private ulong _lastExplosionMs;

    public override void _Ready()
    {
        if (Stats == null || Walker == null)
        {
            GD.PushError($"MechGunner '{Name}' needs Stats and Walker.");
            SetPhysicsProcess(false);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _turretCooldown -= dt;
        _cannonCooldown -= dt;

        if (Manual)
        {
            Target = null;
            _targetHandle = 0;
            if (TurretTrigger && _turretCooldown <= 0.0f)
            {
                FireTurret(AimPoint(Stats.TurretRange));
            }
            if (CannonTrigger && _cannonCooldown <= 0.0f)
            {
                FireCannon(AimPoint(Stats.TurretRange * 2.0f));
            }
            return;
        }

        Track(dt);
        if (Target is not Vector3 target)
        {
            return;
        }

        if (_turretCooldown <= 0.0f && TurretMuzzle != null
            && Aligned(Walker.TurretForward, target - TurretMuzzle.GlobalPosition, Stats.AutoAimToleranceDegrees))
        {
            FireTurret(target);
        }

        if (_cannonCooldown <= 0.0f && CannonMuzzle != null
            && CannonMuzzle.GlobalPosition.DistanceTo(target) > Stats.CannonMinRange
            && Aligned(Walker.GlobalBasis.Z, target - CannonMuzzle.GlobalPosition, Stats.AutoCannonConeDegrees))
        {
            FireCannon(target);
        }
    }

    // ---------------- Targeting ----------------

    private void Track(float dt)
    {
        HordeManager horde = Horde?.Manager;
        if (horde == null)
        {
            Target = null;
            return;
        }

        // Between scans the chosen agent is followed by its handle; a dead or
        // vanished one forces a scan now.
        Vector3? followed = _targetHandle != 0 ? horde.AgentPosition(_targetHandle) : null;
        _scanTimer -= dt;
        if (followed == null || _scanTimer <= 0.0f)
        {
            _scanTimer = Stats.AutoScanInterval;
            // The crowd on the ground only: a few cells up and down, not a
            // cube of sky as tall as the range is wide.
            Vector3 from = Walker.GlobalPosition;
            followed = horde.NearestAgent(from, Stats.AutoRange, out Vector3 position, out long handle, 8.0f) ? position : null;
            _targetHandle = followed != null ? handle : 0;
        }

        if (followed is Vector3 at && Friendly != null
            && at.DistanceTo(Friendly.GlobalPosition) < Stats.PlayerSafetyRadius)
        {
            followed = null;
        }

        // Aim at the middle of a pickle, not its feet.
        Target = followed is Vector3 p ? p + Walker.Up * 0.6f : null;
    }

    private static bool Aligned(Vector3 forward, Vector3 toTarget, float degrees)
    {
        Vector3 up = Vector3.Up;
        Vector3 a = forward.Slide(up), b = toTarget.Slide(up);
        if (a.LengthSquared() < 1e-6f || b.LengthSquared() < 1e-6f)
        {
            return true;
        }
        return Mathf.RadToDeg(a.AngleTo(b)) <= degrees;
    }

    /// <summary>What the centre of the rider's view lands on, or a point at range.</summary>
    private Vector3 AimPoint(float range)
    {
        Camera3D camera = AimCamera;
        if (camera == null)
        {
            return Walker.GlobalPosition + Walker.GlobalBasis.Z * range;
        }

        Vector3 origin = camera.GlobalPosition;
        Vector3 end = origin - camera.GlobalBasis.Z * range;
        var query = PhysicsRayQueryParameters3D.Create(origin, end, GameLayers.HitscanMask,
            new Godot.Collections.Array<Rid> { Walker.GetRid() });
        query.CollideWithAreas = true;
        Godot.Collections.Dictionary hit = Walker.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? (Vector3)hit["position"] : end;
    }

    // ---------------- Turret gun ----------------

    private void FireTurret(Vector3 target)
    {
        if (TurretMuzzle == null)
        {
            return;
        }
        _turretCooldown = Stats.TurretFireInterval;

        Vector3 from = TurretMuzzle.GlobalPosition;
        Vector3 direction = (target - from).Normalized();
        float spread = Mathf.DegToRad(Stats.TurretSpreadDegrees);
        if (spread > 0.0f)
        {
            Vector3 axis = direction.Cross(Mathf.Abs(direction.Y) > 0.99f ? Vector3.Right : Vector3.Up).Normalized()
                .Rotated(direction, (float)GD.RandRange(0.0, Mathf.Tau));
            direction = direction.Rotated(axis, (float)GD.RandRange(0.0, spread));
        }

        // A round stops on the first thing in its way, pickle or wall.
        Vector3 end = from + direction * Stats.TurretRange;
        var query = PhysicsRayQueryParameters3D.Create(from, end, GameLayers.HitscanMask,
            new Godot.Collections.Array<Rid> { Walker.GetRid() });
        query.CollideWithAreas = true;
        Godot.Collections.Dictionary hit = Walker.GetWorld3D().DirectSpaceState.IntersectRay(query);

        Vector3 point = end;
        Vector3 normal = -direction;
        bool landed = hit.Count > 0;
        if (landed)
        {
            point = (Vector3)hit["position"];
            normal = (Vector3)hit["normal"];
        }
        else if (!Manual && target.DistanceTo(from) < Stats.TurretRange)
        {
            // An agent with no borrowed collider is still there: the round
            // goes off where it was aimed.
            point = target;
            landed = true;
        }

        if (TracerScene != null)
        {
            Tracer tracer = TracerScene.Instantiate<Tracer>();
            Scene().AddChild(tracer);
            tracer.Launch(from, point, Stats.TracerSpeed);
        }
        AudioDirector.PlayAt(Sfx.RifleFire, from);

        if (landed)
        {
            Detonate(point, normal);
        }
    }

    private void Detonate(Vector3 point, Vector3 normal)
    {
        Vector3 centre = point + normal * 0.1f;
        var blast = new Blast.Settings
        {
            Centre = centre,
            InnerRadius = Stats.RoundInnerRadius,
            Radius = Stats.RoundBlastRadius,
            Damage = Stats.RoundDamage,
            MinMultiplier = 0.35f,
            Impulse = Stats.RoundImpulse,
            ImpulseUpBias = 0.3f,
            Up = Walker.Up,
            Source = Walker,
            SourceRid = Walker.GetRid(),
            SelfImpulseScale = 0.0f,
            NoCrater = true,
        };
        PhysicsDirectSpaceState3D space = Walker.GetWorld3D().DirectSpaceState;
        Blast.ApplyDamage(space, blast);
        Blast.ApplyToRigidBodies(space, blast, 1.0f);

        ulong now = Time.GetTicksMsec();
        if (ExplosionScene == null || now - _lastExplosionMs < (ulong)(Stats.ExplosionEffectInterval * 1000.0f))
        {
            ImpactEffect.Spawn(ImpactScene, Scene(), point, normal);
            return;
        }

        _lastExplosionMs = now;
        Node3D explosion = ExplosionScene.Instantiate<Node3D>();
        if (explosion is ExplosionEffect effect)
        {
            effect.Configure(centre, Stats.ExplosionEffectRadius, Walker.Up, normal);
        }
        Scene().AddChild(explosion);
        explosion.GlobalPosition = centre;
    }

    // ---------------- Hull cannon ----------------

    private void FireCannon(Vector3 target)
    {
        if (CannonMuzzle == null || ShellScene == null || Stats.CannonShell == null)
        {
            return;
        }
        _cannonCooldown = Stats.CannonFireInterval;

        Vector3 from = CannonMuzzle.GlobalPosition;
        Vector3 direction = (target - from).Normalized();
        Vector3 nose = Walker.GlobalBasis.Z.Normalized();
        if (Mathf.RadToDeg(nose.AngleTo(direction)) > Stats.CannonTraverseDegrees)
        {
            direction = nose;
        }

        Rocket shell = ShellScene.Instantiate<Rocket>();
        Scene().AddChild(shell);
        shell.Launch(Stats.CannonShell, Walker, from, direction, Walker.Up, Walker.GetGravity(),
            Walker.GetRid(), Walker.Velocity);
        Walker.Kick(-direction * 2.5f);
    }

    private Node Scene() => GetTree().CurrentScene ?? (Node)this;
}
