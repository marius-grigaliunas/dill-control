using Godot;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// A thrown explosive. Bounces off the world under Jolt, counts a fuse down,
/// and resolves its blast as an overlap query plus one line-of-sight ray per
/// candidate.
///
/// The two-query shape is the whole point. A sphere overlap alone would damage
/// through a wall, which is what makes area damage feel arbitrary; the overlap
/// finds who is near, and the ray decides whether the blast could actually
/// reach them.
/// </summary>
public partial class Grenade : RigidBody3D
{
    [ExportGroup("Scene Wiring")]
    [Export]
    public Node3D Model { get; set; }

    [ExportGroup("Effect Scenes")]
    [Export]
    public PackedScene ExplosionScene { get; set; }

    private GrenadeStats _stats;
    private Node3D _source;
    private float _fuseRemaining;
    private float _lifeRemaining;
    private bool _detonated;
    private bool _isBomblet;

    /// <summary>
    /// Up where the grenade was thrown from. Only used as a fallback for
    /// <see cref="BlastUp"/> on a cook-off, where the body detonates on the
    /// same frame it entered the tree and the space has not yet reported it a
    /// gravity vector. On that path the grenade is at the thrower's feet, so
    /// the thrower's up is the right answer anyway.
    /// </summary>
    private Vector3 _throwerUp = Vector3.Up;

    /// <summary>
    /// Which way is up where this grenade is, not where it was thrown. A
    /// grenade thrown 20 m on a small planet has travelled ten degrees of arc,
    /// and biasing the shove along the thrower's up would visibly tilt it.
    /// </summary>
    private Vector3 BlastUp()
    {
        Vector3 gravity = GetGravity();
        return gravity.LengthSquared() > 0.0001f ? -gravity.Normalized() : _throwerUp;
    }

    /// <summary>How many overlapping colliders one blast will consider.</summary>

    /// <summary>
    /// Nudges the line-of-sight ray short of the target so it does not stop on
    /// the target's own collider before it gets there.
    /// </summary>

    /// <summary>
    /// Arms the grenade and throws it. Called after the node is in the tree, so
    /// the physics state it sets is the state the solver picks up.
    /// </summary>
    public void Launch(
        GrenadeStats stats,
        Vector3 position,
        Vector3 velocity,
        float fuseRemaining,
        Node3D source,
        Vector3 throwerUp)
    {
        _stats = stats;
        _source = source;
        _throwerUp = throwerUp;
        _fuseRemaining = fuseRemaining;
        _lifeRemaining = stats.MaxLifetime;

        CollisionLayer = GameLayers.Mask(GameLayers.Projectile);
        CollisionMask = GameLayers.ProjectileMask;

        GravityScale = stats.GravityScale;
        AngularDamp = stats.AngularDamp;
        // Built here rather than in the scene so the tuning panel can retune bounce
        // between two throws without a reimport.
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Bounce = stats.Bounce,
            Friction = stats.Friction,
        };
        // A grenade thrown hard at a wall is exactly the case that tunnels.
        ContinuousCd = true;

        GlobalPosition = position;
        LinearVelocity = velocity;
        AngularVelocity = new Vector3(
            GD.Randf() - 0.5f,
            GD.Randf() - 0.5f,
            GD.Randf() - 0.5f).Normalized() * stats.ThrowSpin;

        if (_fuseRemaining <= 0.0f)
        {
            CallDeferred(MethodName.Detonate);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_detonated || _stats == null)
        {
            return;
        }

        float dt = (float)delta;
        _fuseRemaining -= dt;
        _lifeRemaining -= dt;

        if (_fuseRemaining <= 0.0f || _lifeRemaining <= 0.0f)
        {
            Detonate();
        }
    }

    /// <summary>
    /// Detonates where the grenade is. Also used by the thrower for a cook-off,
    /// which spawns a grenade already out of fuse rather than duplicating the
    /// blast logic in a second place.
    /// </summary>
    public void Detonate()
    {
        if (_detonated || _stats == null)
        {
            return;
        }
        _detonated = true;

        Vector3 centre = GlobalPosition;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;

        Blast.Settings blast = BlastSettings(centre);

        Blast.ApplyDamage(space, blast);
        Blast.ApplyImpulses(space, blast, GameLayers.Mask(GameLayers.Player));

        // Scattering the rubble is most of what makes a blast read as powerful,
        // and it costs one more overlap query against bodies already awake.
        Blast.ApplyToRigidBodies(space, blast, _stats.DebrisImpulseScale);

        SpawnExplosion(centre);
        ScatterBomblets(centre);

        QueueFree();
    }

    /// <summary>
    /// Throws this grenade's bomblets, if it carries any, as new instances of
    /// its own scene on a ring, each on its own short fuse. A bomblet never
    /// scatters again.
    /// </summary>
    private void ScatterBomblets(Vector3 centre)
    {
        if (_isBomblet || _stats.BombletCount <= 0 || _stats.Bomblet == null || string.IsNullOrEmpty(SceneFilePath))
        {
            return;
        }

        PackedScene scene = GD.Load<PackedScene>(SceneFilePath);
        Vector3 up = BlastUp();
        Vector3 side = Mathf.Abs(up.Dot(Vector3.Right)) > 0.9f ? Vector3.Forward : Vector3.Right;
        Vector3 x = up.Cross(side).Normalized();
        Vector3 z = up.Cross(x).Normalized();
        Node parent = GetTree().CurrentScene;
        float offset = GD.Randf() * Mathf.Tau;

        for (int i = 0; i < _stats.BombletCount; i++)
        {
            float angle = offset + (Mathf.Tau * i / _stats.BombletCount);
            Vector3 outward = (x * Mathf.Cos(angle)) + (z * Mathf.Sin(angle));
            float speed = _stats.BombletSpeed * (float)GD.RandRange(0.75, 1.15);
            Vector3 velocity = ((outward + (up * _stats.BombletUpBias)) * speed);

            Grenade bomblet = scene.Instantiate<Grenade>();
            bomblet._isBomblet = true;
            parent.AddChild(bomblet);
            bomblet.Launch(
                _stats.Bomblet,
                centre + (up * 0.4f),
                velocity,
                (float)GD.RandRange(_stats.BombletFuseMin, _stats.BombletFuseMax),
                _source,
                up);
        }
    }

    // ---------------- Blast ----------------

    /// <summary>
    /// Describes this grenade to the shared solver.
    ///
    /// The two-query shape and the per-target DamageInfo live in
    /// <see cref="Blast"/> now, because the rocket and the airstrike want
    /// exactly the same rules and three copies of them would drift apart the
    /// first time one was retuned.
    /// </summary>
    private Blast.Settings BlastSettings(Vector3 centre) => new()
    {
        Centre = centre,
        InnerRadius = _stats.InnerRadius,
        Radius = _stats.BlastRadius,
        Damage = _stats.Damage,
        MinMultiplier = _stats.MinMultiplier,
        Impulse = _stats.Impulse,
        ImpulseUpBias = _stats.ImpulseUpBias,
        Up = BlastUp(),
        Source = _source,
        SelfImpulseScale = _stats.SelfImpulseScale,
        CraterRadius = _stats.CraterRadius,
        CraterDepth = _stats.CraterDepth,
    };

    private void SpawnExplosion(Vector3 centre)
    {
        if (ExplosionScene == null)
        {
            return;
        }

        Node3D explosion = ExplosionScene.Instantiate<Node3D>();

        // Configured before it enters the tree. AddChild runs _Ready
        // synchronously, so anything handed over afterwards arrives after the
        // effect has already sized itself -- which is why every explosion in
        // the project was previously built at a one-metre radius whatever its
        // warhead said.
        //
        // No surface normal is passed: a grenade has rolled to wherever it
        // stopped, so the effect's own downward probe is a better answer than
        // whatever this body last happened to touch.
        (explosion as ExplosionEffect)?.Configure(centre, _stats.BlastRadius, BlastUp());

        // Parented to the level: the grenade is about to free itself.
        GetTree().CurrentScene.AddChild(explosion);
        explosion.GlobalPosition = centre;
    }

}
