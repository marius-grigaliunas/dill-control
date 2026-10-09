using System;
using System.Collections.Generic;
using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// Rides along with something the gravity manipulator threw and hurts whatever
/// it passes through.
///
/// Reported as "the gravity gun does not damage anyone, no debris damages the
/// enemy", and that was literal rather than a tuning problem: an agent is a
/// struct with no body, and the colliders standing in for the nearest ones are
/// <see cref="Hitbox"/> areas that never take part in a collision. A thrown
/// crate therefore flew straight through every agent it was aimed at -- the
/// solver had nothing to report, and nothing listened even when it did.
///
/// DECISION -- a sweep per physics frame, not contact signals. A capsule from
/// where the thing was last frame to where it is now, run through
/// <see cref="Blast.DistinctOverlaps"/>, finds hitboxes, which contact
/// monitoring never can, and it cannot tunnel at 42 m/s the way a per-frame
/// point test would. Damage goes down the existing Hitbox → IDamageable →
/// DamageInfo path, so a thrown box lights the hitmarker, honours weak points
/// and reaches the horde with no branch anywhere for what threw it.
///
/// Damage scales with speed and with the square root of mass: a brick at
/// launch speed is lethal, the same brick rolling is not, and a boulder hits
/// harder than a pebble without a one-tonne slab one-shotting a brood-mother.
/// Each receiver is hit once per throw. The tracker frees itself when the
/// thing slows below <see cref="MinSpeed"/> or its lifetime runs out.
/// </summary>
public partial class ThrownImpact : Node
{
    /// <summary>Numbers the thrower hands every tracker it starts.</summary>
    public readonly record struct Tuning(
        float DamagePerSpeed,
        float MinSpeed,
        float ReferenceMass,
        int MaxTargets,
        float Lifetime);

    public Tuning Settings { get; set; }

    /// <summary>What threw it, for <see cref="DamageInfo.Source"/>.</summary>
    public Node3D Source { get; set; }

    /// <summary>
    /// Where the thrown thing is now, or null once it is gone. A rigid body
    /// answers with its own position; a thrown agent has no node, so whoever
    /// owns the horde answers for it.
    /// </summary>
    public Func<Vector3?> Follow { get; set; }

    /// <summary>True for colliders that are the thrown thing itself.</summary>
    public Func<GodotObject, bool> IsSelf { get; set; }

    /// <summary>
    /// Called with the same hit a thrown thing deals, when it has a health pool
    /// of its own to take it -- an agent thrown into a pack is hurt by the
    /// landing too. Null for a crate.
    /// </summary>
    public Action<DamageInfo> DamageSelf { get; set; }

    public float Mass { get; set; } = 10.0f;

    /// <summary>Half the size of the thrown thing; the sweep is this fat.</summary>
    public float Radius { get; set; } = 0.4f;

    /// <summary>Slack around <see cref="Radius"/>, so a near miss on a thin
    /// agent still reads as a hit.</summary>
    private const float Padding = 0.25f;

    /// <summary>Speed kept by a rigid body after each thing it passes through,
    /// so a throw through a pack bowls rather than drills.</summary>
    private const float SpeedKeptPerHit = 0.8f;

    /// <summary>Ceiling on the speed damage is computed from, in m/s.</summary>
    private const float MaxDamageSpeed = 60.0f;

    private RigidBody3D _body;
    private Vector3 _last;
    private float _age;
    private int _hits;
    private readonly HashSet<IDamageable> _struck = new();

    /// <summary>Starts, or restarts, tracking a thrown rigid body.</summary>
    public static void Track(RigidBody3D body, Node3D source, Tuning tuning)
    {
        if (body == null || !IsInstanceValid(body))
        {
            return;
        }

        // A pooled debris body can be thrown again before its last tracker
        // expired. Restarting that one keeps it to a single sweep per body.
        ThrownImpact impact = null;
        foreach (Node child in body.GetChildren())
        {
            if (child is ThrownImpact existing)
            {
                impact = existing;
                break;
            }
        }

        if (impact == null)
        {
            impact = new ThrownImpact { Name = "ThrownImpact" };
            body.AddChild(impact);
        }

        impact._body = body;
        impact.Source = source;
        impact.Settings = tuning;
        impact.Mass = body.Mass;
        impact.Radius = RadiusOf(body);
        impact.Follow = () => IsInstanceValid(body) ? body.GlobalPosition : null;
        impact.IsSelf = obj => obj == body;
        impact.Restart(body.GlobalPosition);
    }

    /// <summary>Starts tracking something with no node of its own.</summary>
    public static void Track(Node parent, Vector3 start, Node3D source, Tuning tuning, float mass,
                             float radius, Func<Vector3?> follow, Func<GodotObject, bool> isSelf,
                             Action<DamageInfo> damageSelf)
    {
        if (parent == null || follow == null)
        {
            return;
        }

        ThrownImpact impact = new()
        {
            Name = "ThrownImpact",
            Source = source,
            Settings = tuning,
            Mass = mass,
            Radius = radius,
            Follow = follow,
            IsSelf = isSelf,
            DamageSelf = damageSelf,
        };
        parent.AddChild(impact);
        impact.Restart(start);
    }

    private void Restart(Vector3 start)
    {
        _last = start;
        _age = 0.0f;
        _hits = 0;
        _struck.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;

        Vector3? current = Follow?.Invoke();
        if (current == null || _age > Settings.Lifetime || _hits >= Settings.MaxTargets)
        {
            QueueFree();
            return;
        }

        Vector3 position = current.Value;
        Vector3 travel = position - _last;
        float speed = _body != null ? _body.LinearVelocity.Length() : travel.Length() / Mathf.Max(dt, 0.0001f);

        // A thrown agent's speed is read from displacement, and the horde steps
        // on the render frame while this runs on the physics frame -- so one
        // physics frame can hold two horde steps and read double. Clamped
        // rather than smoothed, because only the damage reads it.
        speed = Mathf.Min(speed, MaxDamageSpeed);

        // A short grace so the frame of release, before the solver has applied
        // the launch, does not read as "already stopped".
        if (speed < Settings.MinSpeed)
        {
            if (_age > 0.15f)
            {
                QueueFree();
            }
            _last = position;
            return;
        }

        Sweep(_last, position, speed);
        _last = position;
    }

    private void Sweep(Vector3 from, Vector3 to, float speed)
    {
        PhysicsDirectSpaceState3D space = GetViewport()?.World3D?.DirectSpaceState;
        if (space == null)
        {
            return;
        }

        float radius = Radius + Padding;
        Vector3 travel = to - from;
        float length = travel.Length();
        Vector3 direction = length > 0.0001f ? travel / length : Vector3.Zero;

        // A capsule along this frame's travel, so a fast throw cannot step
        // over a target between two frames.
        Shape3D shape;
        Transform3D transform;
        if (length < 0.01f)
        {
            shape = new SphereShape3D { Radius = radius };
            transform = new Transform3D(Basis.Identity, to);
        }
        else
        {
            shape = new CapsuleShape3D { Radius = radius, Height = length + (radius * 2.0f) };
            transform = new Transform3D(AlongY(direction), from + (travel * 0.5f));
        }

        PhysicsShapeQueryParameters3D query = new()
        {
            Shape = shape,
            Transform = transform,
            CollisionMask = GameLayers.DirectAreaMask,
            CollideWithAreas = true,   // Hitboxes are monitorable areas.
            CollideWithBodies = true,
        };
        if (_body != null)
        {
            query.Exclude = new Godot.Collections.Array<Rid> { _body.GetRid() };
        }

        float massScale = Mathf.Clamp(
            Mathf.Sqrt(Mass / Mathf.Max(0.01f, Settings.ReferenceMass)), 0.5f, 2.0f);
        float amount = Settings.DamagePerSpeed * speed * massScale;

        foreach (GodotObject collider in Blast.DistinctOverlaps(space, query, 16))
        {
            if (_hits >= Settings.MaxTargets)
            {
                return;
            }

            if (collider is not Node3D node || (IsSelf != null && IsSelf(collider)))
            {
                continue;
            }

            Hitbox box = node as Hitbox;
            IDamageable receiver = DamageReceiver.Of(node);
            if (receiver == null || !receiver.IsAlive || !_struck.Add(receiver))
            {
                continue;
            }

            Vector3 point = receiver is IBlastSurface surface
                ? surface.NearestBlastPoint(to)
                : node.GlobalPosition;

            DamageInfo info = new()
            {
                Amount = amount,
                Position = point,
                Normal = -direction,
                Direction = direction,
                Distance = 0.0f,
                Source = Source,
            };

            if (box != null)
            {
                box.TakeHit(info);
            }
            else
            {
                receiver.ApplyDamage(info);
            }

            DamageSelf?.Invoke(new DamageInfo
            {
                Amount = amount,
                Position = to,
                Normal = direction,
                Direction = -direction,
                Source = Source,
            });

            AudioDirector.PlayAt(Sfx.MeleeImpact, point);
            _hits++;

            if (_body != null && IsInstanceValid(_body))
            {
                _body.LinearVelocity *= SpeedKeptPerHit;
            }
        }
    }

    /// <summary>A basis whose Y is <paramref name="up"/>; a capsule's axis is its Y.</summary>
    private static Basis AlongY(Vector3 up)
    {
        Vector3 helper = Mathf.Abs(up.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 x = helper.Cross(up).Normalized();
        Vector3 z = x.Cross(up).Normalized();
        return new Basis(x, up, z);
    }

    /// <summary>Half the largest extent of the body's first collision shape.</summary>
    private static float RadiusOf(RigidBody3D body)
    {
        foreach (Node child in body.GetChildren())
        {
            if (child is not CollisionShape3D { Shape: Shape3D shape } collision)
            {
                continue;
            }

            float scale = collision.GlobalBasis.Scale.X;
            return scale * shape switch
            {
                BoxShape3D box => Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)) * 0.5f,
                SphereShape3D sphere => sphere.Radius,
                CapsuleShape3D capsule => Mathf.Max(capsule.Radius, capsule.Height * 0.5f),
                CylinderShape3D cylinder => Mathf.Max(cylinder.Radius, cylinder.Height * 0.5f),
                _ => 0.4f,
            };
        }

        return 0.4f;
    }
}
