using Godot;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// A self-propelled warhead that marches itself and raycasts ahead.
///
/// Not a RigidBody3D, unlike the grenade, and the difference is the point. A
/// grenade is thrown at walking speed and wants to bounce, roll and settle, all
/// of which the solver does far better than hand-written code. A rocket travels
/// at eighty metres a second in a straight line and wants exactly one thing
/// from physics: to know what it hit first. At that speed a rigid body is also
/// the classic tunnelling case, and continuous collision detection on every
/// rocket in flight is a cost paid to get an answer a single ray already gives.
///
/// So this integrates itself and sweeps a ray from where it was to where it is
/// each physics frame. No tunnelling at any speed, one query per frame, and no
/// body in the broadphase.
/// </summary>
public partial class Rocket : Node3D
{
    [ExportGroup("Scene Wiring")]
    [Export]
    public Node3D Model { get; set; }

    /// <summary>The exhaust trail. Freed outright on a round past the trail budget.</summary>
    [Export]
    public GpuParticles3D Trail { get; set; }

    /// <summary>The motor's glow. Freed outright on a round past the light budget.</summary>
    [Export]
    public OmniLight3D MotorLight { get; set; }

    /// <summary>
    /// Trails and motor lights alive at once, across every rocket in the game.
    ///
    /// Every round carried both, and an autocannon call puts a hundred rounds
    /// in the air: a hundred particle systems and a hundred moving omni lights,
    /// each pushed to the renderer every physics tick. Measured at 1080p,
    /// physics time followed the count of rounds in flight almost exactly --
    /// 4 ms with under twenty up, 10 ms with eighty to a hundred, and nearly
    /// every frame over budget in that range. A salvo's fiftieth trail is not
    /// one anybody picks out of the first ten, so past the budget a round
    /// flies without either, and the node is freed rather than hidden so it
    /// costs nothing to move.
    /// </summary>
    public static int MaxLiveTrails { get; set; } = 10;

    public static int MaxLiveMotorLights { get; set; } = 3;

    private static int _liveTrails;
    private static int _liveMotorLights;
    private bool _ownsTrail;
    private bool _ownsMotorLight;

    [ExportGroup("Effect Scenes")]
    [Export]
    public PackedScene ExplosionScene { get; set; }

    /// <summary>
    /// Set by the level's particle warm-up shot, and by nothing else. See
    /// <see cref="ExplosionEffect.Silent"/> for why that shot has to be quiet.
    /// </summary>
    public bool SilentExplosion { get; set; }

    private RocketStats _stats;
    private Node3D _source;
    private Vector3 _velocity;
    private Vector3 _up = Vector3.Up;
    private Vector3 _gravity = Vector3.Down * 9.8f;
    private float _speed;
    private float _lifeRemaining;
    private bool _detonated;

    /// <summary>
    /// The shooter's own collider, excluded from the flight ray. Without it a
    /// rocket spawned a metre in front of the camera can still clip the
    /// shooter's capsule on its first step and detonate in their face.
    /// </summary>
    private Rid _ignore;
    private PhysicsRayQueryParameters3D _query;

    /// <summary>
    /// Arms the rocket and sends it. Called by the launcher immediately after
    /// the scene is added to the tree, so the transform below is final.
    /// </summary>
    /// <param name="inheritedVelocity">
    /// The launcher's own motion, carried into the shot. A hand-held launcher
    /// passes nothing and gets the zero it always had; a vehicle at 58 m/s has
    /// to pass its velocity, or a 26 m/s muzzle speed puts the warhead behind
    /// the thing that fired it, which then flies into its own rocket.
    /// </param>
    public void Launch(
        RocketStats stats, Node3D source, Vector3 origin, Vector3 direction,
        Vector3 up, Vector3 gravity, Rid ignore, Vector3 inheritedVelocity = default)
    {
        _gravity = gravity;
        _stats = stats;
        _source = source;
        _up = up.LengthSquared() > 0.0001f ? up.Normalized() : Vector3.Up;
        _ignore = ignore;
        _lifeRemaining = stats.MaxLifetime;

        _velocity = (direction.Normalized() * stats.MuzzleSpeed) + inheritedVelocity;
        _speed = _velocity.Length();

        GlobalPosition = origin;
        FaceTravel();
        ClaimTrimmings();
    }

    private void ClaimTrimmings()
    {
        if (Trail != null)
        {
            if (_liveTrails < MaxLiveTrails)
            {
                _ownsTrail = true;
                _liveTrails++;
            }
            else
            {
                Trail.QueueFree();
                Trail = null;
            }
        }

        if (MotorLight != null)
        {
            if (_liveMotorLights < MaxLiveMotorLights)
            {
                _ownsMotorLight = true;
                _liveMotorLights++;
            }
            else
            {
                MotorLight.QueueFree();
                MotorLight = null;
            }
        }
    }

    private void ReleaseTrimmings()
    {
        if (_ownsTrail)
        {
            _ownsTrail = false;
            _liveTrails = System.Math.Max(0, _liveTrails - 1);
        }

        if (_ownsMotorLight)
        {
            _ownsMotorLight = false;
            _liveMotorLights = System.Math.Max(0, _liveMotorLights - 1);
        }
    }

    /// <summary>Rounds in the tree right now, across every launcher. For the perf recorder.</summary>
    public static int LiveCount { get; private set; }

    public override void _EnterTree() => LiveCount++;

    public override void _ExitTree()
    {
        LiveCount = System.Math.Max(0, LiveCount - 1);
        ReleaseTrimmings();

        // Disposed here rather than left to the garbage collector, which can
        // finalize it after the engine has shut down.
        _query?.Dispose();
        _query = null;
    }

    // Detonations run inside the physics step, so without these their cost
    // shows up only as unexplained engine physics time -- which is how an
    // autocannon salvo read in the recordings that found it.
    private static readonly int SectionBlastDamage = FrameProfiler.Section("blast damage");
    private static readonly int SectionBlastPush = FrameProfiler.Section("blast push");
    private static readonly int SectionExplosionFx = FrameProfiler.Section("explosion fx");
    private static readonly int SectionFlight = FrameProfiler.Section("rocket flight");

    public override void _PhysicsProcess(double delta)
    {
        if (_detonated || _stats == null)
        {
            return;
        }

        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionFlight);

        // Held at its impact point by the detonation budget last tick. It has
        // already arrived; it is only waiting for its turn to go off.
        if (_waitingToDetonate)
        {
            Detonate(_waitPoint, IsInstanceValid(_waitStruck) ? _waitStruck : null, _waitNormal);
            return;
        }

        float dt = (float)delta;

        _lifeRemaining -= dt;
        if (_lifeRemaining <= 0.0f)
        {
            Detonate(GlobalPosition, null);
            return;
        }

        // The motor: accelerate along the current heading up to cruise, then
        // hold. Applying acceleration along the heading rather than along the
        // original launch direction is what lets gravity bend the path without
        // the motor fighting the bend.
        Vector3 heading = _velocity.LengthSquared() > 0.0001f
            ? _velocity.Normalized()
            : -GlobalBasis.Z;

        _speed = Mathf.Min(_speed + (_stats.Acceleration * dt), _stats.CruiseSpeed);
        _velocity = heading * _speed;

        // Gravity comes from the shooter's own frame, which the player reads
        // from the physics server. A rocket fired on a planet therefore falls
        // toward that planet and one fired in open space flies straight, with
        // no branch here and no planet reference.
        _velocity += _gravity * _stats.GravityScale * dt;

        Vector3 from = GlobalPosition;
        Vector3 to = from + (_velocity * dt);
        float step = (to - from).Length();

        // One ray reaching several frames ahead, then none until the rocket
        // has flown through the stretch it proved clear. A salvo is a hundred
        // rounds in the air at once, and a ray and a transform each per round
        // per physics frame was most of what an autocannon call cost -- 7 ms
        // of physics on frames where nothing was even landing. Gravity bends
        // the path by about a centimetre over the lookahead, and anything that
        // moves into the stretch meets the next sweep, which is never more
        // than LookaheadFrames away.
        if (_clearAhead < step)
        {
            Vector3 reach = from + ((to - from) * LookaheadFrames);
            if (Sweep(from, reach, out Vector3 point, out Vector3 normal, out GodotObject collider))
            {
                float distance = from.DistanceTo(point);
                if (distance <= step)
                {
                    // Step back along the normal so the detonation sits just
                    // outside the surface rather than inside it, where the
                    // blast's own line-of-sight ray would be starting in solid
                    // geometry.
                    Detonate(point + (normal * 0.15f), collider, normal);
                    return;
                }

                _clearAhead = Mathf.Max(0.0f, distance - ClearMargin);
            }
            else
            {
                _clearAhead = step * LookaheadFrames;
            }
        }

        _clearAhead -= step;
        GlobalPosition = to;
        FaceTravel();
    }

    /// <summary>How many frames of flight one sweep covers.</summary>
    private const float LookaheadFrames = 8.0f;

    /// <summary>Kept back from a surface found ahead, so the sweep that reaches it starts outside it.</summary>
    private const float ClearMargin = 0.5f;

    private float _clearAhead;
    private Vector3 _faced;

    /// <summary>Rounds that may go off in one physics tick, across every rocket in the game.</summary>
    public static int MaxDetonationsPerTick { get; set; } = 2;

    /// <summary>Ticks a round will wait for its turn before it goes off regardless.</summary>
    private const int MaxWaitTicks = 6;

    private static ulong _detonationTick;
    private static int _detonationsThisTick;

    private bool _waitingToDetonate;
    private bool _waitedOnce;
    private int _waitCount;
    private Vector3 _waitPoint;
    private GodotObject _waitStruck;
    private Vector3 _waitNormal;

    /// <summary>One ray from where it was to where it is going.</summary>
    private bool Sweep(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal, out GodotObject collider)
    {
        point = to;
        normal = Vector3.Up;
        collider = null;

        // One query per rocket, reused every frame. Building a fresh one --
        // and a fresh exclude array -- per rocket per physics frame was a
        // steady stream of garbage under an autocannon salvo, and it is what
        // fed the gen-2 collection behind a 121 ms frame at 1080p.
        if (_query == null)
        {
            _query = new PhysicsRayQueryParameters3D
            {
                CollisionMask = GameLayers.HitscanMask,
                CollideWithAreas = true,   // Hitboxes are areas.
                CollideWithBodies = true,
            };

            if (_ignore != default)
            {
                _query.Exclude = new Godot.Collections.Array<Rid> { _ignore };
            }
        }

        PhysicsRayQueryParameters3D query = _query;
        query.From = from;
        query.To = to;

        Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            return false;
        }

        point = result["position"].AsVector3();
        normal = result["normal"].AsVector3();
        collider = result.TryGetValue("collider", out Variant hit) ? hit.AsGodotObject() : null;
        return true;
    }

    private void FaceTravel()
    {
        if (_velocity.LengthSquared() < 0.0001f)
        {
            return;
        }

        Vector3 forward = _velocity.Normalized();

        // A bomb falling or a rocket at cruise barely turns, and setting the
        // transform is a notification to the renderer every time.
        if (forward.Dot(_faced) > 0.99998f)
        {
            return;
        }
        _faced = forward;

        Vector3 reference = Mathf.Abs(forward.Dot(_up)) > 0.98f ? Vector3.Right : _up;
        Vector3 right = reference.Cross(forward).Normalized();
        LookAt(GlobalPosition + forward, right.Cross(forward));
    }

    /// <param name="surfaceNormal">
    /// What the flight sweep hit, carried through to the effect so the fireball
    /// squashes against that surface and the shockwave ring lies on it. Left at
    /// zero on the fuse-expiry path, where nothing was struck and the effect
    /// probes for the ground itself.
    /// </param>
    private void Detonate(Vector3 centre, GodotObject struck, Vector3 surfaceNormal = default)
    {
        if (_detonated)
        {
            return;
        }

        // A detonation is a sphere overlap, a line-of-sight ray per candidate,
        // the bus and everything listening on it (craters, the horde, the
        // camera), and a whole effect scene instanced -- one to two
        // milliseconds. A salvo lands two and three of them in one physics
        // tick, and the long frame that made then doubled the next frame's
        // physics as the engine caught up. Past the budget a round waits where
        // it struck for the next tick: sixteen milliseconds nobody can see.
        ulong tick = Engine.GetPhysicsFrames();
        if (tick != _detonationTick)
        {
            _detonationTick = tick;
            _detonationsThisTick = 0;
        }

        if (_detonationsThisTick >= MaxDetonationsPerTick && !_waitedOnce)
        {
            _waitingToDetonate = true;
            _waitedOnce = _waitCount++ >= MaxWaitTicks;
            _waitPoint = centre;
            _waitStruck = struck;
            _waitNormal = surfaceNormal;
            GlobalPosition = centre;
            return;
        }

        _detonationsThisTick++;
        _waitingToDetonate = false;
        _detonated = true;

        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;

        Blast.Settings blast = new()
        {
            Centre = centre,
            InnerRadius = _stats.InnerRadius,
            Radius = _stats.BlastRadius,
            Damage = _stats.Damage,
            MinMultiplier = _stats.MinMultiplier,
            Impulse = _stats.Impulse,
            ImpulseUpBias = _stats.ImpulseUpBias,
            Up = _up,
            Source = _source,
            SelfImpulseScale = _stats.SelfImpulseScale,

            // Carried, not acted on. Blast broadcasts these and whoever owns
            // the terrain decides what to do with them,
            // so a rocket stays a thing that goes off rather than a thing that
            // knows it is standing on a heightfield.
            CraterRadius = _stats.CraterRadius,
            CraterDepth = _stats.CraterDepth,
            FireSeconds = _stats.FireSeconds,
        };

        // The direct hit lands first and separately. A rocket that struck a
        // target head-on should beat one that merely went off nearby, and the
        // blast's own falloff cannot express that -- at zero distance both are
        // simply at full multiplier.
        ApplyDirectHit(struck, centre);

        using (FrameProfiler.Begin(SectionBlastDamage))
        {
            Blast.ApplyDamage(space, blast);
        }

        using (FrameProfiler.Begin(SectionBlastPush))
        {
            Blast.ApplyImpulses(space, blast, GameLayers.Mask(GameLayers.Player));
            Blast.ApplyToRigidBodies(space, blast, _stats.DebrisImpulseScale);
        }

        using (FrameProfiler.Begin(SectionExplosionFx))
        {
            SpawnExplosion(centre, surfaceNormal);
        }
        QueueFree();
    }

    private void ApplyDirectHit(GodotObject struck, Vector3 point)
    {
        if (_stats.DirectHitDamage <= 0.0f || struck == null)
        {
            return;
        }

        DamageInfo info = new()
        {
            Amount = _stats.DirectHitDamage,
            BaseAmount = _stats.DirectHitDamage,
            Position = point,
            Normal = -_velocity.Normalized(),
            Direction = _velocity.Normalized(),
            Distance = 0.0f,
            Source = _source,
        };

        switch (struck)
        {
            case Hitbox box:
                box.TakeHit(info);
                break;
            case IDamageable direct:
                direct.ApplyDamage(info);
                break;
        }
    }

    private void SpawnExplosion(Vector3 centre, Vector3 surfaceNormal)
    {
        if (ExplosionScene == null)
        {
            return;
        }

        Node3D explosion = ExplosionScene.Instantiate<Node3D>();

        // Configured before it enters the tree. AddChild runs _Ready
        // synchronously, so the radius and the silence flag used to be handed
        // over after the effect had already sized itself and already played its
        // sound -- which is why the warm-up shot every level fires on spawn was
        // never actually silent.
        (explosion as ExplosionEffect)?.Configure(
            centre, _stats.BlastRadius, _up, surfaceNormal, SilentExplosion);

        // Parented to the level: the rocket is about to free itself.
        GetTree().CurrentScene.AddChild(explosion);
        explosion.GlobalPosition = centre;
    }
}
