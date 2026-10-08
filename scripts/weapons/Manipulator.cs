using System;
using System.Collections.Generic;
using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// Hold one thing in front of the camera, release to launch it.
///
/// The mechanism behind <see cref="GravityGun"/>, which is what the player
/// actually carries: the gun owns the trigger, the viewmodel and the numbers,
/// and pushes those numbers in here every frame. It used to be a key of its
/// own, bound in the spike director, which is why it still has no Input code
/// and no exports -- it was reported as reading like a feature rather than a
/// tool, and a thing you point and pull a trigger on is a weapon.
///
/// A velocity servo rather than a joint or a direct position write, because
/// teleporting a body each frame skips the solver -- and skipping the solver
/// hides exactly the cost this was first built to measure.
///
/// It reaches agents through opaque handles supplied by whoever owns the horde,
/// so this file never learns that a horde exists and never names the folder it
/// lives in.
/// </summary>
public partial class Manipulator : Node
{
    public float Range { get; set; } = 22.0f;
    public float HoldDistance { get; set; } = 4.6f;
    public float ServoStrength { get; set; } = 12.0f;
    public float MaxHoldSpeed { get; set; } = 26.0f;
    public float LaunchSpeed { get; set; } = 42.0f;

    /// <summary>
    /// The heaviest thing the beam can lift.
    ///
    /// It exists so enemy mass means something. If everything can be picked up
    /// the tool trivialises the horde: a heavy is a problem precisely because
    /// you cannot solve it by pointing at it, and a brood-mother that could be
    /// flung off the planet would make nests pointless.
    /// </summary>
    public float MaxGrabMass { get; set; } = 90.0f;

    /// <summary>Radius of the punt cone's shove, in metres.</summary>
    public float PuntRadius { get; set; } = 3.6f;

    /// <summary>How far in front of the camera the punt lands.</summary>
    public float PuntDistance { get; set; } = 4.2f;

    public float PuntImpulse { get; set; } = 26.0f;

    /// <summary>Upward share of a punt on an agent, relative to its push.</summary>
    private const float PuntLift = 0.4f;

    /// <summary>What a thrown or punted thing does to what it hits. See
    /// <see cref="ThrownImpact"/>.</summary>
    public ThrownImpact.Tuning Impact { get; set; } = new(2.0f, 8.0f, 20.0f, 4, 3.0f);

    public bool IsHolding => (_held != null && IsInstanceValid(_held)) || _heldAgent != 0;

    public string HeldName => _heldAgent != 0
        ? "agent"
        : _held != null && IsInstanceValid(_held) ? _held.Name.ToString() : "--";

    /// <summary>Why the last grab failed, for whoever wants to say so out loud.</summary>
    public string LastRefusal { get; private set; } = string.Empty;

    private RigidBody3D _held;
    private float _heldGravityScale = 1.0f;

    // ---------------- Reaching the horde without naming it ----------------
    //
    // The horde has no nodes; an agent is an index and a generation packed into
    // one opaque long. This file never unpacks it and never learns what it
    // means, which is what lets a permanent player tool reach into a subsystem
    // that lives in the folder marked for deletion. Whoever owns both assigns
    // these, exactly as the flight rig's host assigns its input actions.

    /// <summary>Node to agent handle. Zero means it was not an agent.</summary>
    public Func<Node3D, long> ResolveAgent { get; set; }

    /// <summary>Mass of an agent, for the same gate a rigid body goes through.</summary>
    public Func<long, float> AgentMass { get; set; }

    /// <summary>Moves a held agent. False means it died or the slot was refilled.</summary>
    public Func<long, Vector3, bool> MoveAgent { get; set; }

    /// <summary>Lets an agent go, with a launch velocity.</summary>
    public Action<long, Vector3> ReleaseAgent { get; set; }

    /// <summary>Where an agent is now, or null once it died or its slot was
    /// refilled. What lets a thrown agent be followed and hurt what it hits.</summary>
    public Func<long, Vector3?> AgentPosition { get; set; }

    /// <summary>
    /// Agents within a radius of a point, up to a count, into a list; returns
    /// how many. Assigned by the director, like the rest of these. For the
    /// gun's vortex.
    /// </summary>
    public Func<Vector3, float, int, System.Collections.Generic.List<long>, int> CollectAgents { get; set; }

    /// <summary>Damages an agent through its handle.</summary>
    public Action<long, DamageInfo> DamageAgent { get; set; }

    private long _heldAgent;
    private Vector3 _agentHoldPoint;

    /// <summary>Where the held thing is, for drawing a tether to it.</summary>
    public Vector3 HeldPosition => _heldAgent != 0
        ? _agentHoldPoint
        : _held != null && IsInstanceValid(_held) ? _held.GlobalPosition : Vector3.Zero;

    /// <summary>Tries to grab whatever the camera is looking at.</summary>
    public bool TryGrab(Camera3D camera)
    {
        if (camera == null || IsHolding)
        {
            return false;
        }

        LastRefusal = string.Empty;

        Vector3 origin = camera.GlobalPosition;
        Vector3 forward = -camera.GlobalBasis.Z.Normalized();

        PhysicsDirectSpaceState3D space = camera.GetWorld3D().DirectSpaceState;

        // An agent first, if the ray is pointing at one. A creature in front of
        // you is what you meant, not the crate behind it -- and a heavy pulled
        // off the drill is worth more than the damage it would have taken to
        // kill it.
        if (TryGrabAgent(space, origin, forward))
        {
            return true;
        }

        // A thin ray first: if you are actually pointing at a body, that body
        // is the one you meant.
        RigidBody3D body = RayGrab(space, origin, forward) ?? NearGrab(space, origin, forward);
        if (body == null)
        {
            return false;
        }

        if (body.Mass > MaxGrabMass)
        {
            LastRefusal = $"too heavy ({body.Mass:F0} > {MaxGrabMass:F0})";
            return false;
        }

        AudioDirector.PlayAt(Sfx.ManipulatorGrab, body.GlobalPosition);

        _held = body;
        _heldGravityScale = body.GravityScale;

        body.Freeze = false;
        body.Sleeping = false;
        body.GravityScale = 0.0f;
        body.AngularDamp = 2.0f;
        return true;
    }

    /// <summary>
    /// Takes hold of whatever agent the ray meets, if the level supplied the
    /// delegates that make agents reachable at all.
    /// </summary>
    private bool TryGrabAgent(PhysicsDirectSpaceState3D space, Vector3 origin, Vector3 forward)
    {
        if (ResolveAgent == null || MoveAgent == null)
        {
            return false;
        }

        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            origin, origin + (forward * Range));

        // Agents are only intersectable through the borrowed colliders that
        // stand in for them, and those are areas on the Hitbox layer.
        query.CollisionMask = GameLayers.AnyHitboxMask;
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;

        Godot.Collections.Dictionary result = space.IntersectRay(query);
        if (result.Count == 0 || !result.TryGetValue("collider", out Variant collider)
            || collider.AsGodotObject() is not Node3D node)
        {
            return false;
        }

        long handle = ResolveAgent(node);
        if (handle == 0)
        {
            return false;
        }

        float mass = AgentMass?.Invoke(handle) ?? 0.0f;
        if (mass > MaxGrabMass)
        {
            LastRefusal = $"too heavy ({mass:F0} > {MaxGrabMass:F0})";
            return false;
        }

        _heldAgent = handle;
        _agentHoldPoint = node.GlobalPosition;
        AudioDirector.PlayAt(Sfx.ManipulatorGrab, node.GlobalPosition);
        return true;
    }

    private RigidBody3D RayGrab(PhysicsDirectSpaceState3D space, Vector3 origin, Vector3 forward)
    {
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            origin, origin + (forward * Range));
        query.CollisionMask = GameLayers.Mask(GameLayers.Prop);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;

        Godot.Collections.Dictionary result = space.IntersectRay(query);
        return result.Count > 0 && result.TryGetValue("collider", out Variant collider)
            ? collider.AsGodotObject() as RigidBody3D
            : null;
    }

    /// <summary>
    /// The nearest loose body roughly in front of the camera.
    ///
    /// The ray alone asked the player to put a crosshair on a tumbling box at a
    /// distance, and missing it looked exactly like the key not being bound.
    /// This forgives the aim: a sphere in front of the camera, and of whatever
    /// it finds, the body closest to the sightline wins rather than the body
    /// closest to the player -- so pointing roughly at the far crate still
    /// takes the far crate.
    /// </summary>
    private RigidBody3D NearGrab(PhysicsDirectSpaceState3D space, Vector3 origin, Vector3 forward)
    {
        Vector3 centre = origin + (forward * AssistDistance);

        PhysicsShapeQueryParameters3D query = new()
        {
            Shape = new SphereShape3D { Radius = AssistRadius },
            Transform = new Transform3D(Basis.Identity, centre),
            CollisionMask = GameLayers.Mask(GameLayers.Prop),
            CollideWithAreas = false,
            CollideWithBodies = true,
        };

        RigidBody3D best = null;
        float bestOffset = float.MaxValue;

        foreach (Godot.Collections.Dictionary result in space.IntersectShape(query, 32))
        {
            if (!result.TryGetValue("collider", out Variant collider)
                || collider.AsGodotObject() is not RigidBody3D candidate)
            {
                continue;
            }

            Vector3 toBody = candidate.GlobalPosition - origin;
            float along = toBody.Dot(forward);
            if (along <= 0.0f || along > Range)
            {
                continue;
            }

            // Perpendicular distance from the sightline.
            float offset = (toBody - (forward * along)).Length();
            if (offset < bestOffset)
            {
                bestOffset = offset;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>How far in front of the camera the assist sphere sits.</summary>
    private const float AssistDistance = 5.0f;

    /// <summary>How forgiving the assist is. Generous: this is a sandbox toy.</summary>
    private const float AssistRadius = 4.0f;

    /// <summary>Lets go, adding the camera's forward as a launch impulse.</summary>
    public void Release(Camera3D camera, bool launch)
    {
        if (_heldAgent != 0)
        {
            Vector3 throwVelocity = launch && camera != null
                ? -camera.GlobalBasis.Z.Normalized() * LaunchSpeed
                : Vector3.Zero;

            if (launch)
            {
                AudioDirector.Play(Sfx.ManipulatorLaunch);
            }

            ReleaseAgent?.Invoke(_heldAgent, throwVelocity);
            if (launch)
            {
                TrackAgent(_heldAgent, _agentHoldPoint);
            }
            _heldAgent = 0;
            return;
        }

        if (!IsHolding)
        {
            _held = null;
            return;
        }

        _held.GravityScale = _heldGravityScale;
        _held.AngularDamp = 0.35f;

        if (launch && camera != null)
        {
            _held.LinearVelocity = -camera.GlobalBasis.Z.Normalized() * LaunchSpeed;
            AudioDirector.Play(Sfx.ManipulatorLaunch);
            ThrownImpact.Track(_held, Owner3D, Impact);
        }

        _held = null;
    }

    /// <summary>
    /// Shoves everything in front of the camera without taking hold of it.
    ///
    /// The design calls for a secondary that punts rather than grabs, and this
    /// is the half that stays useful when the thing in front of you is too
    /// heavy to lift or too close to line up. It reaches rigid bodies with an
    /// impulse and everything else through IBlastReceiver, which is the
    /// interface the player's own shove already arrives on -- so a punt moves a
    /// crate, a creature and another player by the same call.
    /// </summary>
    public void Punt(Camera3D camera)
    {
        if (camera == null || IsHolding)
        {
            return;
        }

        Vector3 origin = camera.GlobalPosition;
        Vector3 forward = -camera.GlobalBasis.Z.Normalized();
        Vector3 centre = origin + (forward * PuntDistance);

        PhysicsShapeQueryParameters3D query = new()
        {
            Shape = new SphereShape3D { Radius = PuntRadius },
            Transform = new Transform3D(Basis.Identity, centre),
            CollisionMask = GameLayers.Mask(GameLayers.Prop, GameLayers.Hitbox, GameLayers.HordeHitbox, GameLayers.Enemy),
            CollideWithAreas = true,
            CollideWithBodies = true,
        };

        // An agent wears two hitboxes; it is shoved, and followed, once.
        HashSet<long> punted = new();

        foreach (GodotObject hit in Blast.DistinctOverlaps(camera.GetWorld3D().DirectSpaceState, query, 32))
        {

            if (hit is RigidBody3D body)
            {
                body.Sleeping = false;
                body.ApplyCentralImpulse(forward * PuntImpulse * body.Mass);
                ThrownImpact.Track(body, Owner3D, Impact);
            }
            else if (hit is IBlastReceiver receiver)
            {
                receiver.ApplyBlastImpulse(forward * PuntImpulse);
            }
            else if (hit is Node3D node && ResolveAgent != null)
            {
                long handle = ResolveAgent(node);
                if (handle != 0 && punted.Add(handle))
                {
                    // Lifted as well as shoved, so a punt throws agents into
                    // the air rather than sliding them along the ground.
                    Vector3 lift = camera.GlobalBasis.Y.Normalized() * (PuntImpulse * PuntLift);
                    ReleaseAgent?.Invoke(handle, (forward * PuntImpulse) + lift);
                    TrackAgent(handle, node.GlobalPosition);
                }
            }
        }
    }

    /// <summary>What threw it, as far as a DamageInfo is concerned: the gun
    /// this mechanism is mounted in.</summary>
    private Node3D Owner3D => GetParent() as Node3D;

    /// <summary>
    /// Follows a thrown agent so it hurts what it is thrown into, and is hurt
    /// by it. An agent has no node to hang the tracker off, so it lives in the
    /// level and asks the horde where its agent is each frame.
    /// </summary>
    private void TrackAgent(long handle, Vector3 start)
    {
        if (AgentPosition == null || GetTree()?.CurrentScene is not Node level)
        {
            return;
        }

        ThrownImpact.Track(
            level,
            start,
            Owner3D,
            Impact,
            AgentMass?.Invoke(handle) ?? 70.0f,
            0.4f,
            () => AgentPosition(handle),
            obj => obj is Node3D node && ResolveAgent?.Invoke(node) == handle,
            DamageAgent == null ? null : info => DamageAgent(handle, info));
    }

    /// <summary>Drives the held body toward the hold point. Physics frame only.</summary>
    public void Tick(Camera3D camera, float delta)
    {
        if (camera == null)
        {
            Release(null, false);
            return;
        }

        if (_heldAgent != 0)
        {
            Vector3 hold = camera.GlobalPosition - (camera.GlobalBasis.Z.Normalized() * HoldDistance);
            _agentHoldPoint = hold;

            // An agent is moved, not driven: it has no velocity the solver
            // integrates, so there is nothing for a servo to servo toward. The
            // handle carries a generation, so a slot refilled by a respawn
            // while you were holding it reports false here rather than letting
            // you carry a stranger.
            if (MoveAgent == null || !MoveAgent(_heldAgent, hold))
            {
                _heldAgent = 0;
            }
            return;
        }

        if (!IsHolding)
        {
            _held = null;
            return;
        }

        Vector3 target = camera.GlobalPosition - (camera.GlobalBasis.Z.Normalized() * HoldDistance);
        Vector3 toTarget = target - _held.GlobalPosition;

        if (toTarget.Length() > Range * 1.5f)
        {
            // Dropped through geometry or shoved away hard; let it go rather
            // than dragging it through the world.
            Release(camera, false);
            return;
        }

        Vector3 velocity = toTarget * ServoStrength;
        float speed = velocity.Length();
        if (speed > MaxHoldSpeed)
        {
            velocity *= MaxHoldSpeed / speed;
        }

        _held.LinearVelocity = velocity;
    }
}
