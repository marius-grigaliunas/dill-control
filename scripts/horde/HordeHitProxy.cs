using Godot;
using DillControl.Core;

namespace DillControl.Horde;

/// <summary>
/// A collider that stands in for whichever agent is currently near enough to be
/// shot at.
///
/// The horde has no nodes and no physics bodies, which is what makes thousands
/// of agents affordable -- and it is also what would make them unshootable,
/// because a hitscan ray has nothing to intersect. Rather than give every agent
/// a collider and lose the whole architecture, a small fixed pool of these is
/// reassigned every frame to the nearest agents. Everything past that pool is
/// a capsule in an array, which is all it needs to be: a hitscan shot lands
/// on what is near and in front of you, and a blast reaches the rest through
/// <see cref="EventBus.AreaBlast"/>.
///
/// It carries a real <see cref="Hitbox"/>, so bullets,
/// the machete and grenade splash all reach the horde through the project's
/// existing damage path with no weapon code changed and no branch added
/// anywhere -- and a weak point is still just a multiplier on a collider.
/// </summary>
public partial class HordeHitProxy : Node3D, IDamageable, ISplashExempt
{
    /// <summary>Index of the agent this proxy currently represents, or -1.</summary>
    public int AgentIndex { get; private set; } = -1;

    /// <summary>
    /// Generation of that agent. An agent can die and its slot be refilled in
    /// the same frame a second pellet is resolving, so the id is checked before
    /// damage lands or the replacement inherits the wound.
    /// </summary>
    public int AgentId { get; private set; }

    public HordeManager Horde { get; set; }

    /// <summary>
    /// False while unassigned, which makes Hitbox.TakeHit drop the hit rather
    /// than forward damage into an empty slot.
    /// </summary>
    public bool IsAlive => AgentIndex >= 0 && Horde != null;

    private Hitbox _body;
    private Hitbox _head;
    private CapsuleShape3D _bodyShape;
    private SphereShape3D _headShape;
    private CollisionShape3D _bodyCollision;
    private CollisionShape3D _headCollision;

    /// <summary>
    /// Which kind this proxy is currently shaped like, so the shapes are only
    /// rewritten when the agent under them is actually a different size.
    /// </summary>
    private int _shapedKind = -1;

    /// <summary>Whether the proxy is wearing an agent rather than parked.</summary>
    private bool _active;

    /// <summary>
    /// Where this proxy's areas wait while it is not wearing anyone: far below
    /// the ground, where no ray, blast or sweep ever reaches, one spot apiece
    /// so parked areas do not stack on each other.
    ///
    /// DECISION -- parked, not switched off. Releasing used to clear
    /// Monitorable on both areas and claiming set it again, and under Jolt
    /// each of those takes the area out of the broadphase and puts it back --
    /// churn that peaks exactly when the nearest agents die fastest. A parked
    /// area is only moved, which is what every live one already does every
    /// frame. Measured, it made no difference either way to the bike gun's
    /// first five seconds into a fresh crowd: that burst's cost is the kills
    /// themselves, not the colliders changing hands.
    /// </summary>
    private Transform3D _park;

    private static int _parkedSlots;

    /// <summary>
    /// How far below the world's origin parked areas wait. The lab parked them
    /// at the planet's centre, which on the flats is the middle of the ground.
    /// </summary>
    private const float ParkDepth = -5000.0f;

    // Cached: GetRid is a call into the engine, and this moves every frame.
    private Rid _bodyRid;
    private Rid _headRid;

    /// <summary>Where the areas were last pushed, between selections.</summary>
    private Vector3 _pushed;

    /// <summary>
    /// How far an agent moves before its collider follows between
    /// selections. A capsule is 0.35 m or more in radius, and every push is a
    /// broadphase update: the nearest agents walk about five centimetres a
    /// frame, so following every frame was 192 area moves a frame for
    /// differences smaller than a bullet could find.
    /// </summary>
    private const float FollowDistance = 0.08f;

    /// <summary>
    /// Two colliders, not one: a body and a head.
    ///
    /// This is the project's weak-point mechanic applied to the horde -- a
    /// multiplier on a collider, with no branch in the weapon and none here
    /// either. Retuning how much a headshot is
    /// worth is one number.
    ///
    /// The body capsule deliberately stops short of the head rather than
    /// spanning the whole agent. Two overlapping shapes would let the ray meet
    /// the body first on a shot aimed at the head, and the weak point would be
    /// unreachable while still looking like it worked.
    /// </summary>
    public void Build(float radius, float height, float weakPointMultiplier)
    {
        float headRadius = radius * 0.8f;
        float bodyHeight = Mathf.Max(height - (headRadius * 2.0f), radius * 2.0f);
        float bodyCentre = (height * 0.5f) - (bodyHeight * 0.5f) - (headRadius * 2.0f) + headRadius;

        _bodyShape = new CapsuleShape3D { Radius = radius, Height = bodyHeight };
        _headShape = new SphereShape3D { Radius = headRadius };

        _body = BuildHitbox("BodyHitbox", 1.0f, false, _bodyShape,
            new Vector3(0.0f, bodyCentre, 0.0f), out _bodyCollision);

        _head = BuildHitbox("HeadHitbox", weakPointMultiplier, true, _headShape,
            new Vector3(0.0f, (height * 0.5f) - headRadius, 0.0f), out _headCollision);

        _park = new Transform3D(Basis.Identity, new Vector3((_parkedSlots++ % 4096) * 6.0f, ParkDepth, 0.0f));
        _bodyRid = _body.GetRid();
        _headRid = _head.GetRid();
        _active = true;
        Release();
    }

    /// <summary>
    /// Reshapes the proxy for a different kind of agent.
    ///
    /// The pool is reassigned every frame to whoever is nearest, and a tank is
    /// four times the silhouette of a cornichon -- a fixed-size collider would mean
    /// shooting a shape that does not match what is drawn. Gated on the kind
    /// actually changing, because writing a shape is what makes the solver
    /// rebuild it, and most frames a proxy keeps the agent it had.
    /// </summary>
    public void SetShape(int kind, float radius, float height)
    {
        if (_shapedKind == kind || _bodyShape == null)
        {
            return;
        }

        _shapedKind = kind;

        float headRadius = radius * 0.8f;
        float bodyHeight = Mathf.Max(height - (headRadius * 2.0f), radius * 2.0f);
        float bodyCentre = (height * 0.5f) - (bodyHeight * 0.5f) - (headRadius * 2.0f) + headRadius;

        _bodyShape.Radius = radius;
        _bodyShape.Height = bodyHeight;
        _headShape.Radius = headRadius;

        _bodyCollision.Position = new Vector3(0.0f, bodyCentre, 0.0f);
        _headCollision.Position = new Vector3(0.0f, (height * 0.5f) - headRadius, 0.0f);
    }

    private Hitbox BuildHitbox(string name, float multiplier, bool weakPoint, Shape3D shape, Vector3 offset,
                               out CollisionShape3D collision)
    {
        Hitbox hitbox = new()
        {
            Name = name,
            DamageMultiplier = multiplier,
            IsWeakPoint = weakPoint,
            Layer = GameLayers.HordeHitbox,
        };

        collision = new CollisionShape3D
        {
            Name = "Collision",
            Shape = shape,
            Position = offset,
        };
        hitbox.AddChild(collision);

        AddChild(hitbox);
        return hitbox;
    }

    public void SetWeakPointMultiplier(float multiplier)
    {
        if (_head != null)
        {
            _head.DamageMultiplier = multiplier;
        }
    }

    /// <summary>Points this proxy at an agent and moves it into place.</summary>
    /// <param name="syncNode">
    /// Whether the node itself moves, or only its two areas in the physics
    /// server. Moving the node sends a transform notification down through
    /// both areas and both shapes; ninety-six of those every frame was over a
    /// millisecond. The server-only move is what a query sees, and the node is
    /// brought into line on every reselection, a few frames apart, so anything
    /// reading a hitbox's GlobalPosition -- the flame cone, a thrown body's
    /// sweep -- is never more than a few centimetres behind.
    /// </param>
    public void Assign(int index, int id, Vector3 position, Vector3 up, bool syncNode = true)
    {
        bool same = _active && AgentIndex == index && AgentId == id;
        AgentIndex = index;
        AgentId = id;

        if (same && !syncNode && position.DistanceSquaredTo(_pushed) < FollowDistance * FollowDistance)
        {
            return;
        }
        _pushed = position;

        // The capsule is upright along the surface normal, like the agent it
        // stands for, so a shot that looks like it grazed a leg did.
        Vector3 forward = up.Cross(Vector3.Right);
        if (forward.LengthSquared() < 1e-6f)
        {
            forward = up.Cross(Vector3.Forward);
        }
        forward = forward.Normalized();

        Transform3D transform = new(new Basis(forward.Cross(up), up, -forward), position);

        if (same && !syncNode)
        {
            // The hitboxes sit at the proxy's origin, so each area's
            // transform is the proxy's.
            PhysicsServer3D.AreaSetTransform(_bodyRid, transform);
            PhysicsServer3D.AreaSetTransform(_headRid, transform);
            return;
        }

        GlobalTransform = transform;
        if (!_active)
        {
            _active = true;
            Visible = true;
        }
    }

    /// <summary>Parks the proxy where nothing can hit it.</summary>
    public void Release()
    {
        AgentIndex = -1;
        if (!_active || _body == null)
        {
            return;
        }

        _active = false;
        Visible = false;

        // Moved through the server only: the node keeps its last transform,
        // and the next Assign sets the node, which moves the areas back.
        PhysicsServer3D.AreaSetTransform(_bodyRid, _park);
        PhysicsServer3D.AreaSetTransform(_headRid, _park);
    }

    public void ApplyDamage(DamageInfo info)
    {
        // Splash belongs to the bus path, not to this one.
        //
        // Blast.ApplyDamage broadcasts EventBus.AreaBlast *and* runs its own
        // overlap query, and that query's mask includes the Hitbox layer these
        // proxies sit on -- so an agent wearing a proxy inside a blast was
        // being hurt once down each path. The generation check did not save it:
        // it only drops the second hit when the first already killed.
        //
        // The bus path is the one that has to win, because it reaches every
        // agent in the blast, where this one can only ever reach the couple of
        // dozen nearest the player. That is the whole reason AreaBlast is on
        // the bus. Fixing it here rather than in Blast also keeps the
        // dependency pointing the right way: Blast broadcasts and the horde
        // listens, so core/ never learns that a horde exists.
        //
        // Hitscan and melee are unaffected and still arrive through here.
        if (info != null && info.IsSplash)
        {
            return;
        }

        Horde?.DamageAgent(AgentIndex, AgentId, info);
    }
}
