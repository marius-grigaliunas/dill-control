using System;
using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Player;

/// <summary>How the line is being used, once it is attached or still in the air.</summary>
public enum GrappleLineMode
{
    /// <summary>Held swing: gravity, pumping, reel and payout. The original rope.</summary>
    Swing,

    /// <summary>Latched zip: a pull toward the anchor, hands free to shoot.</summary>
    Zip,

    /// <summary>Hooked a giant: circle-strafe on a cylinder around her axis.</summary>
    Orbit,
}

/// <summary>
/// Cylinder an orbit rides, in world space. Filled by whoever knows the
/// giant; the hook only stores and reports it.
/// </summary>
public readonly struct GrappleOrbitAxis
{
    public Vector3 Origin { get; init; }
    public Vector3 Axis { get; init; }
    public float Height { get; init; }
    public float MinRadius { get; init; }
    public Vector3 Velocity { get; init; }
}

/// <summary>
/// The line. Throws a hook, holds on to what it hit, and reports where that
/// is -- nothing more. The swing, the reel and the momentum all belong to
/// <see cref="PlayerController"/>, which is the only thing with authority over
/// velocity.
///
/// DECISION -- a grapple, not more jetpack. The fantasy is anchoring to
/// something huge, reeling in and swinging round it on momentum, which is a
/// different feeling from hovering: the rope is a constraint the player works
/// against, and a jetpack is a force they merely apply. It also gives on-foot
/// play an escape from the horde that the bike gives only at range.
///
/// Not a weapon and not a loadout slot. It is fired
/// with the weapon still up, on its own button, so shooting and cutting while
/// on the line is possible -- which is the whole of "land the cut at speed".
/// It never touches the loadout, on the same terms the grenade thrower does
/// not reach for the weapon it displaces.
///
/// Ticked explicitly by the controller rather than from its own
/// _PhysicsProcess. A child's physics callback runs after its parent's, so the
/// anchor would be a frame stale by the time the rope read it; calling it
/// from the controller puts it exactly where the rope needs it.
///
/// PLACEHOLDER visuals: the line and the hook are untextured boxes.
/// </summary>
public partial class GrappleHook : Node3D
{
    [ExportGroup("Data")]
    [Export]
    public GrappleTuning Tuning { get; set; }

    [ExportGroup("Scene Wiring")]
    /// <summary>What the hook is aimed along. The node itself is where the
    /// line leaves the hand, low and to the left of the view.</summary>
    [Export]
    public Camera3D AimCamera { get; set; }

    [ExportGroup("Appearance")]
    [Export]
    public Color LineColour { get; set; } = new(0.20f, 0.17f, 0.12f);

    [Export]
    public Color HookColour { get; set; } = new(0.55f, 0.57f, 0.60f);

    [Export(PropertyHint.Range, "0.005,0.2,0.005,suffix:m")]
    public float LineThickness { get; set; } = 0.03f;

    /// <summary>
    /// A ray against ground that has no collider, for the level to answer.
    /// Origin, direction and length in; the world point it hit, or null.
    ///
    /// Terrain colliders are streamed in a ring about forty metres wide, so a
    /// physics ray thrown at a far hillside finds nothing even though the
    /// hillside is drawn. The anchor is only ever a world point, so it does not
    /// need a collider to hold on to -- it needs something that knows where the
    /// ground is. The spike level assigns this from its heightfield; a level
    /// with ordinary static geometry leaves it null and physics answers alone.
    /// </summary>
    public Func<Vector3, Vector3, float, Vector3?> SurfaceRay { get; set; }

    /// <summary>
    /// Whether this body is a giant the hook should orbit rather than zip or
    /// swing. Null, or a miss, is an ordinary anchor. Returning nothing after
    /// an orbit has started is a shake-off: the hook lets go.
    ///
    /// Assigned by the level, the same way <see cref="SurfaceRay"/> is: this
    /// file must not name a boss.
    /// </summary>
    public Func<Node3D, GrappleOrbitAxis?> OrbitQuery { get; set; }

    [Signal]
    public delegate void AttachedEventHandler();

    /// <summary>Let go. <paramref name="snapped"/> is true when what the hook
    /// held on to was destroyed or carved out from under it.</summary>
    [Signal]
    public delegate void ReleasedEventHandler(bool snapped);

    private enum HookState
    {
        Idle,
        Flying,
        Attached,
    }

    private HookState _state = HookState.Idle;

    private Vector3 _hookPosition;
    private Vector3 _hookDirection;
    private float _hookTravelled;

    /// <summary>
    /// What the hook is holding, if it is a body. Null for ground the level's
    /// <see cref="SurfaceRay"/> found, which has no node to hold.
    /// </summary>
    private Node3D _anchorBody;
    private Vector3 _anchorLocal;
    private Vector3 _anchorLocalNormal;

    /// <summary>
    /// True when the anchor body could never move. Such a body is only a
    /// collider over something fixed, so losing it -- a terrain chunk streamed
    /// out behind the player -- leaves the point exactly where it was, and the
    /// line holds. A body that can move takes its point with it when it goes.
    /// </summary>
    private bool _anchorFixed;

    private float _snapCheckTimer;
    private float _probeTimer;
    private bool _probeInRange;
    private bool _reeling;
    private SoundLoop _reelLoop;

    /// <summary>How long the button has been down this throw, for tap vs hold.</summary>
    private float _pressTime;

    /// <summary>
    /// True when a tap latched the line: it stays attached without the button
    /// held, and a second press (or jump, on a zip) lets go.
    /// </summary>
    private bool _latched;

    private MeshInstance3D _line;
    private MeshInstance3D _hook;

    public bool IsAttached => _state == HookState.Attached;

    /// <summary>True from the throw until the line is let go or the hook misses.</summary>
    public bool IsActive => _state != HookState.Idle;

    /// <summary>Zip, swing or orbit. Meaningful while active.</summary>
    public GrappleLineMode Mode { get; private set; } = GrappleLineMode.Swing;

    /// <summary>The giant's cylinder, when <see cref="Mode"/> is orbit.</summary>
    public GrappleOrbitAxis? CurrentOrbit { get; private set; }

    /// <summary>True when a tap owns the line and the button does not have to stay held.</summary>
    public bool IsLatched => _latched && _state != HookState.Idle;

    /// <summary>Where the line is fixed, in world space. Meaningful only while attached.</summary>
    public Vector3 AnchorPoint { get; private set; }

    /// <summary>How fast the anchor itself is moving, clamped. Zero for fixed ground.</summary>
    public Vector3 AnchorVelocity { get; private set; }

    /// <summary>The surface normal at the anchor, kept current as its body turns.</summary>
    public Vector3 AnchorNormal { get; private set; } = Vector3.Up;

    /// <summary>
    /// Current length of the line. Written by the controller as it reels and
    /// pays out -- the hook only sets it once, on attaching.
    /// </summary>
    public float RopeLength { get; set; }

    /// <summary>True when the crosshair is over something the hook could reach.
    /// Sampled a few times a second, for the HUD.</summary>
    public bool TargetInRange => _state == HookState.Idle && _probeInRange;

    public override void _Ready()
    {
        if (Tuning == null)
        {
            GD.PushError("GrappleHook has no GrappleTuning assigned; the line is off.");
        }

        AimCamera ??= GetViewport().GetCamera3D();
        BuildVisuals();
    }

    public override void _ExitTree()
    {
        SetReeling(false);
    }

    /// <summary>
    /// Advances the hook one physics frame. Returns true while the line is
    /// attached, which is the only thing the rope needs to know.
    /// </summary>
    /// <param name="ropeOrigin">Where the line pulls on the body, in world space.</param>
    /// <param name="self">The owner's body, excluded from every ray.</param>
    public bool PhysicsTick(float dt, Vector3 ropeOrigin, Rid self)
    {
        if (Tuning == null || !Tuning.GrappleEnabled)
        {
            if (_state != HookState.Idle)
            {
                Release(false);
            }
            return false;
        }

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        bool held = captured && Input.IsActionPressed("grapple");
        bool justPressed = captured && Input.IsActionJustPressed("grapple");
        bool justReleased = captured && Input.IsActionJustReleased("grapple");

        switch (_state)
        {
            case HookState.Idle:
                if (justPressed)
                {
                    Throw();
                }
                else
                {
                    TickProbe(dt, self);
                }
                break;

            case HookState.Flying:
                _pressTime += dt;
                if (justReleased && _pressTime <= Tuning.GrappleTapSeconds)
                {
                    _latched = true;
                    Mode = GrappleLineMode.Zip;
                }
                else if (!held && !_latched)
                {
                    // Let go before it landed, and it was a hold: the throw is
                    // simply withdrawn.
                    _state = HookState.Idle;
                    break;
                }

                TickFlight(dt, ropeOrigin, self);
                break;

            case HookState.Attached:
                if (_latched)
                {
                    if (justPressed)
                    {
                        Release(false);
                        break;
                    }

                    // Space lets go of a zip so a shot can happen at the apex.
                    // Orbit uses jump to reel the radius, so it does not.
                    if (Mode == GrappleLineMode.Zip && captured && Input.IsActionJustPressed("jump"))
                    {
                        Release(false);
                        break;
                    }
                }
                else if (!held)
                {
                    Release(false);
                    break;
                }

                TickAnchor(dt, ropeOrigin, self);
                break;
        }

        return _state == HookState.Attached;
    }

    /// <summary>
    /// Lets go of whatever the hook holds, or withdraws a hook still in the air.
    /// Safe to call in any state.
    /// </summary>
    public void Release(bool snapped)
    {
        HookState was = _state;
        _state = HookState.Idle;
        _anchorBody = null;
        AnchorVelocity = Vector3.Zero;
        CurrentOrbit = null;
        _latched = false;
        Mode = GrappleLineMode.Swing;
        SetReeling(false);

        if (was == HookState.Attached)
        {
            AudioDirector.PlayAt(Sfx.GrappleRelease, AnchorPoint);
            EmitSignal(SignalName.Released, snapped);
        }
    }

    /// <summary>The controller says whether the line is being hauled in, for the winch sound.</summary>
    public void SetReeling(bool reeling)
    {
        if (reeling == _reeling)
        {
            return;
        }

        _reeling = reeling;
        if (reeling)
        {
            _reelLoop ??= AudioDirector.Loop(Sfx.GrappleReelLoop, this);
        }
        else
        {
            _reelLoop?.Stop();
            _reelLoop = null;
        }
    }

    // ---------------- Throw ----------------

    private void Throw()
    {
        _hookPosition = AimCamera != null ? AimCamera.GlobalPosition : GlobalPosition;
        _hookDirection = AimDirection();
        _hookTravelled = 0.0f;
        _pressTime = 0.0f;
        _latched = false;
        Mode = GrappleLineMode.Swing;
        CurrentOrbit = null;
        _state = HookState.Flying;

        AudioDirector.PlayAt(Sfx.GrappleFire, GlobalPosition);
    }

    /// <summary>
    /// One segment of flight, swept by one ray -- the rocket's arrangement, and
    /// for the rocket's reason: at 500 m/s a hook covers about eight metres a
    /// frame, and a shape moved in steps would pass clean through a pillar.
    /// A speed of zero sweeps the whole range in one go, which is "instant".
    /// </summary>
    private void TickFlight(float dt, Vector3 ropeOrigin, Rid self)
    {
        float remaining = Tuning.GrappleRange - _hookTravelled;
        float step = Tuning.GrappleHookSpeed <= 0.0f
            ? remaining
            : Mathf.Min(Tuning.GrappleHookSpeed * dt, remaining);

        Vector3 from = _hookPosition;
        Vector3 to = from + (_hookDirection * step);

        if (CastAnchor(from, to, self, out Vector3 point, out Vector3 normal, out Node3D body))
        {
            if (body is RigidBody3D rigid && !rigid.Freeze)
            {
                // A loose crate cannot hold a player up. Letting the line fix
                // to one would read as the rope being broken rather than as the
                // thing being too light, so it is a miss.
                _state = HookState.Idle;
                return;
            }

            Attach(point, normal, body, ropeOrigin);
            return;
        }

        _hookPosition = to;
        _hookTravelled += step;

        if (_hookTravelled >= Tuning.GrappleRange - 0.001f)
        {
            _state = HookState.Idle;
        }
    }

    private void Attach(Vector3 point, Vector3 normal, Node3D body, Vector3 ropeOrigin)
    {
        _state = HookState.Attached;
        _anchorBody = body;
        _anchorFixed = body == null || (body is StaticBody3D && body is not AnimatableBody3D);

        if (body != null)
        {
            Transform3D inverse = body.GlobalTransform.AffineInverse();
            _anchorLocal = inverse * point;
            _anchorLocalNormal = (inverse.Basis * normal).Normalized();
        }

        AnchorPoint = point;
        AnchorNormal = normal;
        AnchorVelocity = Vector3.Zero;
        RopeLength = Mathf.Max(Tuning.GrappleMinLength, ropeOrigin.DistanceTo(point));
        _snapCheckTimer = SnapCheckInterval;

        ResolveMode(ropeOrigin);

        AudioDirector.PlayAt(Sfx.GrappleAttach, point);
        EmitSignal(SignalName.Attached);
    }

    /// <summary>
    /// Zip if a tap latched the throw, orbit if the body is a giant, otherwise
    /// a held swing. A tap onto a giant is an orbit, not a zip through her.
    /// </summary>
    private void ResolveMode(Vector3 ropeOrigin)
    {
        GrappleOrbitAxis? orbit = _anchorBody != null ? OrbitQuery?.Invoke(_anchorBody) : null;
        if (orbit.HasValue)
        {
            Mode = GrappleLineMode.Orbit;
            CurrentOrbit = orbit;
            PlaceOrbitAnchor(orbit.Value, ropeOrigin);
            return;
        }

        CurrentOrbit = null;
        Mode = _latched ? GrappleLineMode.Zip : GrappleLineMode.Swing;
    }

    // ---------------- Holding on ----------------

    /// <summary>
    /// Keeps the anchor on its body. Stored as a point in the body's own frame
    /// and carried out again every frame, so a hook in the side of something
    /// that hops, rolls or topples goes with it -- and the anchor's own
    /// velocity is what the rope measures stretch against, so riding a moving
    /// anchor does not read as being yanked.
    /// </summary>
    private void TickAnchor(float dt, Vector3 ropeOrigin, Rid self)
    {
        if (_anchorBody != null)
        {
            if (!IsInstanceValid(_anchorBody) || !_anchorBody.IsInsideTree())
            {
                _anchorBody = null;
                if (!_anchorFixed)
                {
                    Release(true);
                    return;
                }
            }
            else if (Mode == GrappleLineMode.Orbit)
            {
                GrappleOrbitAxis? orbit = OrbitQuery?.Invoke(_anchorBody);
                if (!orbit.HasValue)
                {
                    Release(true);
                    return;
                }

                CurrentOrbit = orbit;
                PlaceOrbitAnchor(orbit.Value, ropeOrigin);
                return;
            }
            else
            {
                Transform3D frame = _anchorBody.GlobalTransform;
                Vector3 point = frame * _anchorLocal;
                Vector3 velocity = dt > 0.0f ? (point - AnchorPoint) / dt : Vector3.Zero;

                AnchorVelocity = velocity.LimitLength(Tuning.GrappleAnchorMaxSpeed);
                AnchorPoint = point;
                AnchorNormal = (frame.Basis * _anchorLocalNormal).Normalized();
            }
        }
        else
        {
            AnchorVelocity = Vector3.Zero;
        }

        _snapCheckTimer -= dt;
        if (_snapCheckTimer > 0.0f)
        {
            return;
        }
        _snapCheckTimer = SnapCheckInterval;

        // Ground found by the level's heightfield has no collider to test; and
        // a fixed anchor whose collider was streamed away is still ground.
        if (_anchorBody == null)
        {
            return;
        }

        // Is there still something under the hook? A voxel wall the hook was
        // in may have been shot away since -- and a line hanging from a hole
        // is exactly the thing that should drop you.
        Vector3 from = AnchorPoint + (AnchorNormal * SnapProbeDepth);
        Vector3 to = AnchorPoint - (AnchorNormal * SnapProbeDepth);
        if (!CastPhysics(from, to, self, out _, out _, out _))
        {
            Release(true);
        }
    }

    /// <summary>
    /// Puts the visual hook on the giant's surface facing the player, so the
    /// line reads as attached to her rather than to a point inside.
    /// </summary>
    private void PlaceOrbitAnchor(in GrappleOrbitAxis orbit, Vector3 ropeOrigin)
    {
        Vector3 axis = orbit.Axis.LengthSquared() > 0.0001f ? orbit.Axis.Normalized() : Vector3.Up;
        Vector3 fromAxis = ropeOrigin - orbit.Origin;
        Vector3 radial = fromAxis - (axis * fromAxis.Dot(axis));
        if (radial.LengthSquared() < 0.0001f)
        {
            radial = axis.Cross(Vector3.Right);
            if (radial.LengthSquared() < 0.0001f)
            {
                radial = axis.Cross(Vector3.Forward);
            }
        }

        radial = radial.Normalized();
        float along = fromAxis.Dot(axis);
        AnchorPoint = orbit.Origin + (axis * along) + (radial * orbit.MinRadius);
        AnchorNormal = radial;
        AnchorVelocity = orbit.Velocity.LimitLength(Tuning.GrappleAnchorMaxSpeed);
    }

    /// <summary>How often the hook checks that what it holds is still there.</summary>
    private const float SnapCheckInterval = 0.2f;

    /// <summary>How far either side of the surface that check looks.</summary>
    private const float SnapProbeDepth = 0.5f;

    // ---------------- Rays ----------------

    /// <summary>
    /// Physics first, because that is what a body the hook can ride answers
    /// to; the level's heightfield second, for ground past the collider ring.
    /// </summary>
    private bool CastAnchor(Vector3 from, Vector3 to, Rid self, out Vector3 point, out Vector3 normal, out Node3D body)
    {
        if (CastPhysics(from, to, self, out point, out normal, out body))
        {
            return true;
        }

        body = null;
        if (SurfaceRay != null)
        {
            Vector3 delta = to - from;
            float length = delta.Length();
            if (length > 0.0001f)
            {
                Vector3? ground = SurfaceRay(from, delta / length, length);
                if (ground.HasValue)
                {
                    point = ground.Value;
                    // The heightfield reports a point, not a face. Radially out
                    // is what "up" means on a planet and is close enough for
                    // the one thing the normal is used for: perching.
                    normal = point.LengthSquared() > 0.0001f ? point.Normalized() : Vector3.Up;
                    return true;
                }
            }
        }

        point = default;
        normal = default;
        return false;
    }

    private bool CastPhysics(Vector3 from, Vector3 to, Rid self, out Vector3 point, out Vector3 normal, out Node3D body)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        using PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to, AnchorMask);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        if (self.IsValid)
        {
            query.Exclude = new Godot.Collections.Array<Rid> { self };
        }

        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0)
        {
            point = default;
            normal = default;
            body = null;
            return false;
        }

        point = hit["position"].AsVector3();
        normal = hit["normal"].AsVector3();
        body = hit["collider"].AsGodotObject() as Node3D;
        return true;
    }

    /// <summary>World and props: things with enough to them to hang from.</summary>
    private static uint AnchorMask => GameLayers.Mask(GameLayers.World, GameLayers.Prop);

    /// <summary>
    /// Whether the crosshair is over something in reach, a few times a second.
    /// A throw that silently falls short is a mechanic nobody trusts; a throw
    /// the HUD said would land is one they will try at speed.
    /// </summary>
    private void TickProbe(float dt, Rid self)
    {
        _probeTimer -= dt;
        if (_probeTimer > 0.0f)
        {
            return;
        }
        _probeTimer = ProbeInterval;

        Vector3 from = AimCamera != null ? AimCamera.GlobalPosition : GlobalPosition;
        Vector3 to = from + (AimDirection() * Tuning.GrappleRange);
        _probeInRange = CastAnchor(from, to, self, out _, out _, out Node3D body)
                        && !(body is RigidBody3D rigid && !rigid.Freeze);
    }

    private const float ProbeInterval = 0.1f;

    private Vector3 AimDirection() =>
        AimCamera != null ? -AimCamera.GlobalBasis.Z.Normalized() : -GlobalBasis.Z.Normalized();

    // ---------------- Visuals ----------------

    private void BuildVisuals()
    {
        _line = new MeshInstance3D
        {
            Name = "Line",
            TopLevel = true,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = Unshaded(LineColour),
        };
        AddChild(_line);

        _hook = new MeshInstance3D
        {
            Name = "Hook",
            TopLevel = true,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.14f, 0.22f) },
            MaterialOverride = Unshaded(HookColour),
        };
        AddChild(_hook);
    }

    private static StandardMaterial3D Unshaded(Color colour) => new()
    {
        AlbedoColor = colour,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    /// <summary>
    /// Redrawn on the render frame, from wherever the hand is to wherever the
    /// hook is. The physics frame only moves the hook; drawing the line there
    /// would leave it a frame behind a camera that bobs on the render frame.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_line == null)
        {
            return;
        }

        bool visible = _state != HookState.Idle;
        _line.Visible = visible;
        _hook.Visible = visible;
        if (!visible)
        {
            return;
        }

        Vector3 from = GlobalPosition;
        Vector3 tip = _state == HookState.Attached ? AnchorPoint : _hookPosition;
        Vector3 span = tip - from;
        float length = span.Length();
        if (length < 0.01f)
        {
            _line.Visible = false;
            return;
        }

        Vector3 forward = span / length;
        Vector3 up = AimCamera != null ? AimCamera.GlobalBasis.Y : Vector3.Up;
        if (Mathf.Abs(forward.Dot(up)) > 0.99f)
        {
            up = AimCamera != null ? AimCamera.GlobalBasis.X : Vector3.Right;
        }

        Basis look = Basis.LookingAt(forward, up);
        _line.GlobalTransform = new Transform3D(
            look * Basis.FromScale(new Vector3(LineThickness, LineThickness, length)),
            from + (span * 0.5f));
        _hook.GlobalTransform = new Transform3D(look, tip);
    }
}
