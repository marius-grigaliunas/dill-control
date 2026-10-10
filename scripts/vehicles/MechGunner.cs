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

    /// <summary>Ridden: the turret trigger works the flamer instead of the gun.</summary>
    public bool FlamerSelected { get; set; }

    public bool HasFlamer => Stats?.Flamer != null;

    /// <summary>The flamer's fuel left, 0 to 1.</summary>
    public float FlamerFuel01 => HasFlamer ? _fuel / Mathf.Max(Stats.Flamer.FuelSeconds, 0.01f) : 0.0f;

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

    // The flamer: fuel, the damage tick, the ignite timer, the jet drawn.
    private float _fuel;
    private float _flameTick;
    private float _igniteTimer;
    private float _flameQuiet;
    private bool _flaming;
    private GpuParticles3D _jet;
    private ParticleProcessMaterial _jetProcess;
    private SoundLoop _flameLoop;

    public override void _Ready()
    {
        if (Stats == null || Walker == null)
        {
            GD.PushError($"MechGunner '{Name}' needs Stats and Walker.");
            SetPhysicsProcess(false);
            return;
        }
        if (HasFlamer && TurretMuzzle != null)
        {
            _fuel = Stats.Flamer.FuelSeconds;
            _jet = new GpuParticles3D { Name = "FlamerJet" };
            TurretMuzzle.AddChild(_jet);
            _jetProcess = FlameWeapon.BuildJet(_jet, Stats.Flamer.Range * Stats.Flamer.FlameVisualScale, Stats.Flamer.ConeDegrees);
        }
    }

    public override void _ExitTree()
    {
        _flameLoop?.Stop();
        _flameLoop = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _turretCooldown -= dt;
        _cannonCooldown -= dt;
        bool flamed = false;

        if (Manual)
        {
            Target = null;
            _targetHandle = 0;
            if (TurretTrigger && FlamerSelected && HasFlamer)
            {
                flamed = Flame(AimPoint(Stats.Flamer.Range), dt);
            }
            else if (TurretTrigger && _turretCooldown <= 0.0f)
            {
                FireTurret(AimPoint(Stats.TurretRange));
            }
            if (CannonTrigger && _cannonCooldown <= 0.0f)
            {
                FireCannon(AimPoint(Stats.TurretRange * 2.0f));
            }
            FlamerRest(flamed, dt);
            return;
        }

        Track(dt);
        if (Target is Vector3 target)
        {
            bool close = HasFlamer && TurretMuzzle != null
                && TurretMuzzle.GlobalPosition.DistanceTo(target) < Stats.FlamerAutoRange;
            bool aligned = TurretMuzzle != null
                && Aligned(Walker.TurretForward, target - TurretMuzzle.GlobalPosition, Stats.AutoAimToleranceDegrees);
            if (close && aligned)
            {
                flamed = Flame(target, dt);
            }
            else if (_turretCooldown <= 0.0f && aligned)
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
        FlamerRest(flamed, dt);
    }

    // ---------------- Flamer ----------------

    /// <summary>
    /// One frame of flame toward <paramref name="target"/>. The damage is the
    /// hand flamethrower's horde half (<see cref="FlameWeapon"/>'s wash):
    /// spheres down the cone, sized to its width, as sustained blasts on the
    /// bus, so the whole crowd in the cone burns and nothing on this side of
    /// the fight does. Every so often it lights the ground where the jet
    /// lands, through the same blast field the incendiary round uses. False
    /// when it could not fire (no fuel, the arm shot off).
    /// </summary>
    private bool Flame(Vector3 target, float dt)
    {
        FlamethrowerStats flamer = Stats.Flamer;
        RaiseMount(TurretMuzzle, target);
        if (!CanFireFrom(TurretMuzzle) || _fuel <= 0.0f)
        {
            return false;
        }

        Vector3 from = TurretMuzzle.GlobalPosition;
        Vector3 forward = (target - from).Normalized();
        if (forward.LengthSquared() < 1e-6f)
        {
            return false;
        }

        _fuel = Mathf.Max(0.0f, _fuel - dt);
        if (_jet != null)
        {
            // The jet runs down its local -Z: point it where the flame goes.
            Vector3 up = Mathf.Abs(forward.Dot(Walker.Up)) > 0.99f ? Walker.GlobalBasis.Z : Walker.Up;
            _jet.GlobalTransform = new Transform3D(Basis.LookingAt(forward, up), from);
            _jetProcess.Gravity = Walker.Up * FlameWeapon.FlameLift;
        }

        _flameTick += dt;
        _igniteTimer -= dt;
        if (_flameTick < flamer.TickInterval)
        {
            return true;
        }
        float slice = _flameTick;
        _flameTick = 0.0f;

        int count = Mathf.Max(1, flamer.HordeWashSpheres);
        float tangent = Mathf.Tan(Mathf.DegToRad(flamer.ConeDegrees));
        for (int i = 0; i < count; i++)
        {
            float along = flamer.Range * (i + 0.6f) / count;
            float radius = Mathf.Max(1.0f, along * tangent);
            EventBus.EmitAreaBlast(BlastInfo.From(new Blast.Settings
            {
                Centre = from + forward * along,
                InnerRadius = radius,
                Radius = radius,
                Damage = flamer.DamagePerSecond * slice * flamer.FalloffMultiplierAt(along),
                MinMultiplier = 1.0f,
                Up = Walker.Up,
                Source = Walker,
                SourceRid = Walker.GetRid(),
                SelfImpulseScale = 0.0f,
                Sustained = true,
                NoCrater = true,
            }));
        }

        if (Stats.FlamerFireSeconds > 0.0f && _igniteTimer <= 0.0f)
        {
            _igniteTimer = Stats.FlamerIgniteInterval;
            var query = PhysicsRayQueryParameters3D.Create(from, from + forward * flamer.Range,
                GameLayers.Mask(GameLayers.World), new Godot.Collections.Array<Rid> { Walker.GetRid() });
            Godot.Collections.Dictionary hit = Walker.GetWorld3D().DirectSpaceState.IntersectRay(query);
            Vector3 lands = hit.Count > 0 ? (Vector3)hit["position"] : from + forward * flamer.Range;
            EventBus.EmitAreaBlast(new BlastInfo
            {
                Centre = lands,
                Radius = 0.1f,
                Damage = 0.0f,
                Up = Walker.Up,
                Source = Walker,
                Sustained = true,
                NoCrater = true,
                FireSeconds = Stats.FlamerFireSeconds,
            });
        }
        return true;
    }

    /// <summary>Every frame: the jet and its sound on while it flamed, and
    /// fuel coming back after a rest, as the hand flamethrower's does.</summary>
    private void FlamerRest(bool flamed, float dt)
    {
        if (!HasFlamer)
        {
            return;
        }
        if (flamed != _flaming)
        {
            _flaming = flamed;
            if (_jet != null)
            {
                _jet.Emitting = flamed;
            }
            if (flamed)
            {
                _flameLoop ??= AudioDirector.Loop(Sfx.FlameLoop, TurretMuzzle);
            }
            else
            {
                _flameLoop?.Stop();
                _flameLoop = null;
                _flameTick = 0.0f;
            }
        }

        _flameQuiet = flamed ? 0.0f : _flameQuiet + dt;
        if (_flameQuiet >= Stats.Flamer.RefillDelay)
        {
            _fuel = Mathf.Min(Stats.Flamer.FuelSeconds, _fuel + Stats.Flamer.FuelRefillPerSecond * dt);
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

        if (followed is Vector3 at && NearFriend(at))
        {
            followed = null;
        }

        // Aim at the middle of a pickle, not its feet.
        Target = followed is Vector3 p ? p + Walker.Up * 0.6f : null;
    }

    /// <summary>
    /// Whether a target is too close to the player or another ally to shoot
    /// at: everything on this side breaks like everything else, so a round
    /// into a pickle at a friend's feet is a round into the friend.
    /// </summary>
    private bool NearFriend(Vector3 at)
    {
        float r2 = Stats.PlayerSafetyRadius * Stats.PlayerSafetyRadius;
        if (Friendly != null && at.DistanceSquaredTo(Friendly.GlobalPosition) < r2)
        {
            return true;
        }
        foreach (Node node in GetTree().GetNodesInGroup(MechWalker.AllyGroup))
        {
            if (node != Walker && node is MechWalker ally && !ally.IsDestroyed
                && at.DistanceSquaredTo(ally.GlobalPosition) < r2 * 1.5f)
            {
                return true;
            }
        }
        return false;
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
        RaiseMount(TurretMuzzle, target);
        if (!CanFireFrom(TurretMuzzle))
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
        RaiseMount(CannonMuzzle, target);
        if (CannonMuzzle == null || ShellScene == null || Stats.CannonShell == null || !CanFireFrom(CannonMuzzle))
        {
            return;
        }
        _cannonCooldown = Stats.CannonFireInterval;

        Vector3 from = CannonMuzzle.GlobalPosition;
        Vector3 direction = (target - from).Normalized();
        // A hull gun cannot depress, so a shot aimed past its traverse goes
        // out along the nose. An arm can point down at the crowd: flattening
        // that shot sends it over the crosshair, which from the air is upward.
        if (!OnArm(CannonMuzzle))
        {
            Vector3 nose = Walker.GlobalBasis.Z.Normalized();
            if (Mathf.RadToDeg(nose.AngleTo(direction)) > Stats.CannonTraverseDegrees)
            {
                direction = nose;
            }
        }

        Rocket shell = ShellScene.Instantiate<Rocket>();
        Scene().AddChild(shell);
        // Only the part of the mech's speed already along the shot. The jump
        // and the fall are perpendicular to a level aim, and adding them
        // pitches the shell off the crosshair. The motor then holds that heading.
        float along = Walker.Velocity.Dot(direction);
        Vector3 carried = along > 0.0f ? direction * along : Vector3.Zero;
        shell.Launch(Stats.CannonShell, Walker, from, direction, Walker.Up, Walker.GetGravity(),
            Walker.GetRid(), carried);
        Walker.Kick(-direction * 2.5f);
    }

    private Node Scene() => GetTree().CurrentScene ?? (Node)this;

    /// <summary>A gun on a part that has been shot off does not fire.</summary>
    private bool MountAlive(Node3D muzzle) =>
        !Walker.IsDestroyed && (muzzle.GetParent() is not Node3D mount || Walker.PartAlive(mount));

    /// <summary>The part a muzzle rides on, if it rides on one.</summary>
    private static Node3D MountOf(Node3D muzzle) => muzzle?.GetParent() as Node3D;

    /// <summary>Whether this muzzle is mounted on one of the walker's arms.</summary>
    private bool OnArm(Node3D muzzle)
    {
        Node3D mount = MountOf(muzzle);
        if (mount == null)
        {
            return false;
        }
        foreach (Node3D arm in Walker.Arms)
        {
            if (arm == mount)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Raises the arm that carries this muzzle, if it is an arm.</summary>
    private void RaiseMount(Node3D muzzle, Vector3 target)
    {
        Node3D mount = MountOf(muzzle);
        if (mount != null)
        {
            Walker.AimArm(mount, target);
        }
    }

    /// <summary>Alive, raised if it is an arm, and not mid-slash.</summary>
    private bool CanFireFrom(Node3D muzzle)
    {
        if (muzzle == null || !MountAlive(muzzle))
        {
            return false;
        }
        Node3D mount = MountOf(muzzle);
        return !Walker.ArmStroking(mount) && Walker.ArmReady(mount);
    }
}
