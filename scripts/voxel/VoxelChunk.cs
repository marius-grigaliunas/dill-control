using System.Collections.Generic;
using DillControl.Audio;
using DillControl.Core;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// A loose piece of something that broke: a rigid body made of voxels. It is a
/// <see cref="VoxelPart"/> like any other, so it can be shot apart further,
/// and it breaks when it lands hard (<see cref="VoxelDestructionStats.BreakSpeed"/>),
/// crushes what it lands on, and once it has lain still long enough stops
/// being simulated and stays as rubble you can still blow up.
///
/// A big, tall one (a building's upper part) does not stop where it lands: it
/// crushes a slab of itself and of the building under it and keeps going, so
/// a tower comes down storey by storey and stays whole above the crush. One
/// that came loose on one side first hangs on a hinge to what is left, leans
/// over and tears free (docs/decisions/0014).
///
/// Made only by <see cref="VoxelDebris"/>, which counts them and keeps the
/// spent ones to use again: making one costs 0.7 ms (see VoxelDebris).
/// </summary>
public partial class VoxelChunk : RigidBody3D, IDamageable, IBlastSurface, IGrabbable, ITearable
{
    private VoxelPart _part;
    private VoxelDestructionStats _stats;
    private VoxelGrid _grid;
    private Color[] _palette;
    private float[] _emission;
    private Material _material;
    private Vector3 _lastVelocity;
    private float _age;
    private float _still;
    private float _crushCooldown;
    private bool _breaking;
    private bool _heavy;
    private float _halfLength;
    private Vector3 _lastSpin;
    private HingeJoint3D _hinge;
    private Vector3 _hingePivot;
    private Quaternion _hingeStart;
    private float _hingeDust;
    private float _hingeAge;
    private VoxelHinge? _hangSoon;
    private bool _trails;
    private float _trailDust;
    private float _dustScale = 1.0f;
    private bool _rubble = true;

    public bool IsSettled { get; private set; }

    public bool IsHinged => _hinge != null;

    /// <summary>
    /// Whether, landing hard, it crushes on through what it landed on, as a
    /// storey does (0014). Off for a plant's stalk: it tips over its stump
    /// and smashes where it lands rather than boring into the ground (0032).
    /// </summary>
    public bool CrushesThrough { get; set; } = true;

    // ---------------- Gravity gun ----------------

    /// <summary>Breaking on landing: its pieces are being worked out.</summary>
    public bool IsBreaking => _breaking;

    public bool IsRetired => _retired;

        public bool CanGrab => _part != null && !_breaking && _grid.SolidCount <= _stats.MaxGrabVoxels && _hinge == null;

    public bool IsHeld { get; private set; }

    /// <summary>Held, it is awake and stays so: it does not settle, freeze,
    /// join a heap or go for the cap.</summary>
    private VoxelChunkGraph _graph;

    /// <summary>Before it is added: the chunks this piece is made of.</summary>
    public void GiveGraph(VoxelChunkGraph graph) => _graph = graph;

    public float TearTime(Vector3 point) => _part?.TearTime(point) ?? 0.0f;

    public RigidBody3D Tear(Vector3 point) => _part?.Tear(point);

    public void Tug(Vector3 point, float delta) => _part?.Tug(point, delta);

    public void SetHeld(bool held)
    {
        IsHeld = held;
        if (held)
        {
            Wake();
        }
    }

    /// <summary>Seconds it has lain still.</summary>
    public float StillFor => _still;

    /// <summary>True while its landing's blast goes off in a crush-through.</summary>
    public bool CrushingThrough { get; private set; }

    public bool IsAlive => _part != null && !_part.IsEmpty;

    private readonly List<VoxelPart> _parts = new();

    private (MeshInstance3D[] meshes, Vector3 offset)? _meshes;

    private ChunkBuild _build;

    private bool _retired;

    public void Setup(VoxelGrid grid, Color[] palette, float[] emission, Material material, VoxelDestructionStats stats,
        (MeshInstance3D[], Vector3)? meshes = null, ChunkBuild build = null, float dustScale = 1.0f, bool rubble = true)
    {
        _retired = false;
        _dustScale = dustScale;
        _rubble = rubble;
        _build = build;
        _meshes = meshes;
        _grid = grid;
        _palette = palette;
        _emission = emission;
        _material = material;
        _stats = stats;
    }

    public override void _Ready()
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(VoxelPart.SectionChunkReady);
        CollisionLayer = GameLayers.Mask(GameLayers.Prop);
        CollisionMask = GameLayers.Mask(GameLayers.World, GameLayers.Prop);
        _travelFrom = GlobalPosition;
        float volume = _grid.SolidCount * _grid.VoxelSize * _grid.VoxelSize * _grid.VoxelSize;
        Weigh();
        CanSleep = true;
        _heavy = volume >= _stats.FractureVolume;
        _trails = _stats.TrailDustVolume > 0.0f && volume >= _stats.TrailDustVolume;
        _trailDust = 0.0f;
        _halfLength = Mathf.Max(_grid.Size.X, Mathf.Max(_grid.Size.Y, _grid.Size.Z)) * _grid.VoxelSize * 0.5f;
        if (_heavy)
        {
            // Where it lands decides what it crushes.
            ContactMonitor = true;
            MaxContactsReported = 8;
        }
        // Heavy: it lands and stays, grinds rather than skates, and a block
        // on its corner falls over instead of spinning on.
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = _stats.Friction, Bounce = 0.0f, Rough = true };
        // Swept, not stepped: a toppling tower's far end leaves at a hundred
        // metres a second, nearly the ground's whole thickness in one physics
        // step, and went through it between two. Jolt sweeps only a body that
        // moves far for its size in a step, so a resting piece costs nothing.
        ContinuousCd = true;
        AngularDampMode = DampMode.Combine;
        AngularDamp = _stats.AngularDamp;

        _part = new VoxelPart
        {
            Name = "Voxels",
            CollisionOwner = this,
            CollisionStep = _build?.Step ?? VoxelSplit.CollisionStepFor(_grid, _stats.MaxChunkBoxes),
            Velocity = () => LinearVelocity,
            DustScale = _dustScale,
            LeavesRubble = _rubble,
            // Made mid-game: its sections are built on worker threads, unless
            // it is small enough to build in the moment (a piece torn off a
            // heap is seen at once, in the gravity gun's grip).
            BuildAtOnce = _build == null && _grid.SolidCount <= 800,
            CastShadows = _grid.SolidCount > 2000,
        };
        if (_meshes is (MeshInstance3D[] meshes, Vector3 offset))
        {
            _part.Adopt(meshes, offset);
        }
        if (_build != null)
        {
            _part.Prebuilt(_build);
        }
        _meshes = null;
        _build = null;
        _part.Setup(_grid, VoxelAnchor.Largest, _palette, _emission, _material, _stats);
        if (_graph != null)
        {
            _part.UseGraph(_graph);
            _graph = null;
        }
        _part.Emptied += _ => Callable.From(Retire).CallDeferred();
        _part.Unbroken += () =>
        {
            _breaking = false;
            _part.Velocity = () => LinearVelocity;
        };
        AddChild(_part);
        _parts.Add(_part);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        _crushCooldown -= dt;

        if (_hangSoon is VoxelHinge soon && _age > dt)
        {
            _hangSoon = null;
            VoxelDebris.Current?.Hang(this, soon);
        }

        Vector3 velocity = LinearVelocity;
        float change = (_lastVelocity - velocity).Length();
        if (_hinge != null)
        {
            Lean(dt);
        }
        if (_age > 0.15f && !_breaking)
        {
            if (_heavy && CrushesThrough && _hinge == null && _hangSoon == null && change > _stats.CrushThroughSpeed
                && CrushThrough(change))
            {
                _lastVelocity = LinearVelocity;
                _lastSpin = AngularVelocity;
                return;
            }
            if (change > _stats.CrushSpeed && _crushCooldown <= 0.0f)
            {
                Crush(change);
            }
            // A long piece swinging down lands at its far end far faster than
            // its middle moves: count that slam too, or a toppled tower lies
            // down whole.
            float slam = Mathf.Max(change, (_lastSpin - AngularVelocity).Length() * _halfLength);
            // What leaves no rubble (a vine's stalk) snaps where it lands while
            // it is big, and its logs then lie: broken again at every bounce,
            // a felled vine went to dust over the loose-piece cap.
            int breaksFrom = _rubble ? _stats.MinBreakVoxels : _stats.SnapMinVoxels;
            if (slam > _stats.BreakSpeed && _grid.SolidCount >= breaksFrom)
            {
                Break(_lastVelocity);
                return;
            }
        }
        if (!IsHeld && LimitThrow(ref velocity))
        {
            LinearVelocity = velocity;
        }
        _lastVelocity = velocity;
        _lastSpin = AngularVelocity;
        if (_trails && !IsHeld && !IsSettled)
        {
            Trail(dt, velocity);
        }

        // Still: asleep, slow, or gone nowhere over the last second. A piece
        // grinding on rubble at a few centimetres a second, or jittering in
        // place, is too fast for Jolt to put to sleep and goes nowhere.
        _travelFor += dt;
        if (_travelFor >= 1.0f)
        {
            _creeping = GlobalPosition.DistanceTo(_travelFrom) < _stats.StillTravel;
            _travelFrom = GlobalPosition;
            _travelFor = 0.0f;
        }
        bool still = !IsHeld && _hinge == null && (Sleeping || velocity.LengthSquared() < 0.01f || _creeping);
        _still = still ? _still + dt : 0.0f;
        if (_still <= 0.0f)
        {
            // Moved: where it comes to rest next may be the ground.
            BakeRefused = false;
        }
        if (!IsSettled && _still > _stats.SettleSeconds)
        {
            // Rubble now: no simulation, still breakable.
            IsSettled = true;
            FreezeMode = FreezeModeEnum.Static;
            Freeze = true;
            _support = _stats.SupportCheckSeconds * (float)GD.RandRange(0.5, 1.5);
            if (_unsupportedAt is Vector3 at)
            {
                float fell = GlobalPosition.DistanceTo(at);
                _propped = fell < _stats.StillTravel;
                if (PerfLog.Recording)
                {
                    PerfLog.Event($"resettled {VoxelCount}v {fell:F2}m from where it lost support");
                }
            }
            _unsupportedAt = null;
        }
        else if (IsSettled)
        {
            WatchSupport(dt);
        }
        Refine();
    }

    private Vector3 _travelFrom;
    private float _travelFor;
    private bool _creeping;
    private float _support;

    /// <summary>
    /// Settled, it is frozen where it lay: when what it rests on goes (shot
    /// away, crushed, a piece under it baked or dusted), it wakes and falls
    /// rather than hanging in the air. Held is either of two looks: its boxes
    /// moved a little way down meet something, or a ray down from one of its
    /// lowest corners does. The first alone missed pieces resting on a heap's
    /// heightfield or on an edge, which woke and lay where they were.
    /// </summary>
    private void WatchSupport(float dt)
    {
        _support -= dt;
        if (_support > 0.0f || IsHeld || _propped)
        {
            return;
        }
        _support = _stats.SupportCheckSeconds;
        Rid rid = GetRid();
        if (!PhysicsServer3D.BodyGetSpace(rid).IsValid)
        {
            // Out of the world (a bench A/B): nothing to stand on, nothing to fall through.
            return;
        }
        Vector3 up = WorldUp.At(GlobalPosition);
        _supportTest ??= new PhysicsTestMotionParameters3D { MaxCollisions = 1, Margin = 0.02f };
        _supportTest.From = GlobalTransform;
        _supportTest.Motion = -up * SupportReach;
        if (PhysicsServer3D.BodyTestMotion(rid, _supportTest) || CornerHeld(up))
        {
            return;
        }
        if (PerfLog.Recording)
        {
            PerfLog.Event($"unsupported {VoxelCount}v");
        }
        Wake();
        _unsupportedAt = GlobalPosition;
    }

    /// <summary>How far down a settled piece looks for what holds it: under a
    /// voxel of a building's, so a gap the boxes round off is not a fall.</summary>
    private const float SupportReach = 0.25f;

    /// <summary>Rays down from its lowest box corners, the few within a
    /// voxel of its lowest point: whatever it lies on, it touches there.</summary>
    private bool CornerHeld(Vector3 up)
    {
        _corners.Clear();
        float lowest = float.MaxValue;
        Transform3D body = GlobalTransform;
        foreach (uint owner in GetShapeOwners())
        {
            if (IsShapeOwnerDisabled(owner))
            {
                continue;
            }
            Transform3D at = body * ShapeOwnerGetTransform(owner);
            for (int s = 0; s < ShapeOwnerGetShapeCount(owner); s++)
            {
                if (ShapeOwnerGetShape(owner, s) is not BoxShape3D box)
                {
                    continue;
                }
                Vector3 h = box.Size * 0.5f;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = at * new Vector3((k & 1) == 0 ? -h.X : h.X, (k & 2) == 0 ? -h.Y : h.Y, (k & 4) == 0 ? -h.Z : h.Z);
                    float d = corner.Dot(up);
                    if (d < lowest + _grid.VoxelSize)
                    {
                        lowest = Mathf.Min(lowest, d);
                        _corners.Add(corner);
                    }
                }
            }
        }
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        _supportRay ??= new PhysicsRayQueryParameters3D { Exclude = new Godot.Collections.Array<Rid> { GetRid() } };
        _supportRay.CollisionMask = CollisionMask;
        int rays = 0;
        foreach (Vector3 corner in _corners)
        {
            if (corner.Dot(up) > lowest + _grid.VoxelSize || rays++ >= MaxSupportRays)
            {
                continue;
            }
            _supportRay.From = corner + up * 0.15f;
            _supportRay.To = corner - up * (SupportReach + 0.1f);
            if (space.IntersectRay(_supportRay).Count > 0)
            {
                return true;
            }
        }
        return false;
    }

    private const int MaxSupportRays = 12;
    private readonly List<Vector3> _corners = new();
    private PhysicsRayQueryParameters3D _supportRay;
    private PhysicsTestMotionParameters3D _supportTest;
    private Vector3? _unsupportedAt;

    /// <summary>Woken as unsupported, it lay down again where it was: held by
    /// something neither look sees. It stops looking until a blast or the
    /// gravity gun moves it, rather than waking every few seconds.</summary>
    private bool _propped;

    /// <summary>
    /// A body again: unfrozen, its stillness forgotten, and back on the
    /// coarse boxes it flies with (a settled piece's fine ones are tested in
    /// every step while it is awake).
    /// </summary>
    private void Wake()
    {
        IsSettled = false;
        Freeze = false;
        // Unfrozen, a body that slept before it froze still sleeps, and Jolt
        // does not wake a sleeper when what it lay on goes: it hung in the air.
        Sleeping = false;
        _still = 0.0f;
        _creeping = false;
        _travelFor = 0.0f;
        _travelFrom = GlobalPosition;
        _refine = null;
        _propped = false;
        if (_refined && _part != null && _grid.SolidCount > 0)
        {
            _refined = false;
            _part.Rebox(VoxelSplit.CollisionStepFor(_grid, _stats.MaxChunkBoxes));
        }
    }

    /// <summary>
    /// A big piece falling sheds dust off its edges along its whole length,
    /// left hanging behind it: whatever happens to it next (crushed, broken,
    /// swapped for pieces) happens inside a cloud rather than with its ink
    /// outline in clear air. On a side of its box at random, which is where
    /// the outline is.
    /// </summary>
    private void Trail(float dt, Vector3 velocity)
    {
        _trailDust -= dt;
        if (_trailDust > 0.0f || velocity.LengthSquared() < _stats.TrailDustSpeed * _stats.TrailDustSpeed
            || VoxelDebris.Current == null || !_grid.SolidBounds(out Aabb box))
        {
            return;
        }
        _trailDust = _stats.TrailDustInterval;

        int along = (int)box.Size.MaxAxisIndex();
        int across = (along + 1) % 3, other = (along + 2) % 3;
        Vector3 centre = box.GetCenter(), half = box.Size * 0.5f;
        Color colour = VoxelDebris.AverageColour(_grid, _palette);
        Vector3 up = WorldUp.At(GlobalPosition);
        int points = _stats.TrailDustPoints;
        for (int k = 0; k < points; k++)
        {
            Vector3 local = centre;
            local[along] += half[along] * (points == 1 ? 0.0f : (2.0f * k / (points - 1) - 1.0f) * 0.9f);
            local[across] += half[across] * (GD.Randf() < 0.5f ? -1.0f : 1.0f);
            local[other] += half[other] * (float)GD.RandRange(-1.0, 1.0);
            VoxelDebris.Current.Dust(ToGlobal(local), up, velocity * 0.2f, colour, _stats.TrailDustSize, _dustScale);
        }
    }

    /// <summary>Caps how fast it goes up or sideways (falling is left
    /// alone). True when it was over.</summary>
    private bool LimitThrow(ref Vector3 velocity)
    {
        float cap = _stats.MaxThrowSpeed;
        if (cap <= 0.0f || velocity.LengthSquared() <= cap * cap)
        {
            return false;
        }
        Vector3 up = WorldUp.At(GlobalPosition);
        float down = Mathf.Min(0.0f, velocity.Dot(up));
        Vector3 rest = velocity - up * down;
        if (rest.LengthSquared() <= cap * cap)
        {
            return false;
        }
        velocity = rest.Normalized() * cap + up * down;
        return true;
    }

    /// <summary>Set by <see cref="VoxelDebris"/> when it would not join a heap
    /// where it lies (on a ledge, on another piece); cleared when it moves.</summary>
    public bool BakeRefused { get; set; }

    private bool _refined;
    private System.Threading.Tasks.Task<int> _refine;

    /// <summary>
    /// Once it has settled (it was not taken into a heap), its collision is
    /// re-boxed as finely as <see cref="VoxelDestructionStats.RestingChunkBoxes"/>
    /// allows, and <see cref="Wake"/> puts the coarse boxes back. It flew with
    /// boxes up to 8 voxels across (4 m on a building), each the size of
    /// whatever voxels were left in it, so a hole shot through a fallen piece
    /// still stopped you. The step is worked out on a worker
    /// from a copy of its cells; the re-boxing goes to the workers like any.
    /// </summary>
    private void Refine()
    {
        if (_refine != null && _refine.IsCompleted)
        {
            if (_refine.IsCompletedSuccessfully && IsSettled && _part != null && _refine.Result < _part.CollisionStep)
            {
                if (PerfLog.Recording)
                {
                    PerfLog.Event($"refine {VoxelCount}v step {_part.CollisionStep}->{_refine.Result}");
                }
                _part.Rebox(_refine.Result);
            }
            _refine = null;
        }
        if (_refined || _part == null || !IsSettled || _part.CollisionStep <= 1 || _grid.SolidCount == 0)
        {
            return;
        }
        _refined = true;
        var copy = new VoxelGrid(_grid.Size, (byte[])_grid.Cells.Clone(), _grid.Origin, _grid.VoxelSize);
        int max = _stats.RestingChunkBoxes, from = _part.CollisionStep;
        _refine = System.Threading.Tasks.Task.Run(() => FinestStep(copy, max, from));
    }

    /// <summary>The finest step below <paramref name="from"/> whose boxes
    /// stay within <paramref name="maxBoxes"/>; <paramref name="from"/> if none.</summary>
    private static int FinestStep(VoxelGrid grid, int maxBoxes, int from)
    {
        var boxes = new List<(Vector3, Vector3)>();
        for (int step = 1; step < from; step *= 2)
        {
            boxes.Clear();
            grid.Boxes(Vector3I.Zero, grid.Size, step, boxes);
            if (boxes.Count <= maxBoxes)
            {
                return step;
            }
        }
        return from;
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_hinge))
        {
            _hinge.QueueFree();
        }
        _hinge = null;
    }

    private void Weigh()
    {
        float volume = _grid.SolidCount * _grid.VoxelSize * _grid.VoxelSize * _grid.VoxelSize;
        Mass = Mathf.Clamp(volume * _stats.Density, 1.0f, 2_000_000.0f);
    }

    /// <summary>Hangs on <paramref name="hinge"/> from its next physics tick.</summary>
    public void HangSoon(VoxelHinge hinge) => _hangSoon = hinge;

    /// <summary>Hangs on a joint to what is left of the building it fell
    /// from, until it leans <see cref="VoxelDestructionStats.HingeBreakAngle"/>.</summary>
    public void HangOn(HingeJoint3D joint, Vector3 pivot)
    {
        _hinge = joint;
        _hingePivot = pivot;
        _hingeStart = GlobalBasis.GetRotationQuaternion();
    }

    /// <summary>
    /// Leaning on the hinge: what is left of the support buckles at the pivot,
    /// in puffs that grow as it goes over, and tears when it has leant far
    /// enough, when it has held as long as it can (propped on its own rubble,
    /// it would otherwise hang there), or when the building it hangs on has
    /// gone.
    /// </summary>
    private void Lean(float dt)
    {
        _hingeAge += dt;
        float angle = (GlobalBasis.GetRotationQuaternion() * _hingeStart.Inverse()).GetAngle();
        float limit = Mathf.DegToRad(_stats.HingeBreakAngle);
        bool anchored = IsInstanceValid(_hinge) && _hinge.GetNodeOrNull(_hinge.NodeB) is VoxelCollider { IsAlive: true };
        Color colour = VoxelDebris.AverageColour(_grid, _palette);
        if (angle < limit && _hingeAge < _stats.HingeHoldSeconds && anchored)
        {
            _hingeDust -= dt;
            if (_hingeDust <= 0.0f && VoxelDebris.Current != null)
            {
                _hingeDust = 0.35f;
                VoxelDebris.Current.Dust(_hingePivot, WorldUp.At(_hingePivot), Vector3.Zero, colour, 2.0f + 4.0f * angle / limit,
                    _dustScale);
            }
            return;
        }

        if (IsInstanceValid(_hinge))
        {
            _hinge.QueueFree();
        }
        _hinge = null;
        VoxelDebris.Current?.Dust(_hingePivot, WorldUp.At(_hingePivot), LinearVelocity * 0.3f, colour, 7.0f, _dustScale);
        AudioDirector.PlayAt(Sfx.VoxelCollapse, _hingePivot);
    }

    /// <summary>
    /// Lands and keeps going: a slab of itself and of the building under it,
    /// <see cref="VoxelDestructionStats.CrushDepth"/> deep across the contact,
    /// goes to dust, and it carries on with
    /// <see cref="VoxelDestructionStats.CrushKeep"/> of its speed. Only while
    /// it is taller than <see cref="VoxelDestructionStats.CrushStopHeight"/>
    /// along the way it landed: below that there is too little weight above
    /// the contact, and it lands like any piece; nor when it lands on its side.
    /// False when it did not crush.
    /// </summary>
    private bool CrushThrough(float speed)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(VoxelPart.SectionChunkLand);
        PhysicsDirectBodyState3D state = PhysicsServer3D.BodyGetDirectState(GetRid());
        int contacts = state?.GetContactCount() ?? 0;
        if (contacts == 0)
        {
            return false;
        }

        // The contacts' middle and their normal, pointing into this piece.
        Vector3 com = GlobalPosition + state.CenterOfMass;
        Vector3 centre = Vector3.Zero, normal = Vector3.Zero;
        VoxelBody under = null;
        for (int i = 0; i < contacts; i++)
        {
            Vector3 p = state.GetContactLocalPosition(i);
            Vector3 n = state.GetContactLocalNormal(i);
            centre += p;
            normal += n.Dot(com - p) < 0.0f ? -n : n;
            if (under == null && state.GetContactColliderObject(i) is VoxelCollider collider)
            {
                under = collider.Body;
            }
        }
        centre /= contacts;
        if (normal.LengthSquared() < 1e-6f)
        {
            return false;
        }
        normal = normal.Normalized();

        // The contact plane is the deepest contact; the slab reaches past the
        // contacts' own spread.
        float plane = float.MaxValue, spread = 0.0f;
        for (int i = 0; i < contacts; i++)
        {
            Vector3 p = state.GetContactLocalPosition(i);
            plane = Mathf.Min(plane, p.Dot(normal));
            Vector3 q = p - centre;
            spread = Mathf.Max(spread, (q - normal * q.Dot(normal)).Length());
        }
        centre += normal * (plane - centre.Dot(normal));

        if (!_grid.SolidBounds(out Aabb box))
        {
            return false;
        }
        // Its own slab reaches as wide as it is: a flat landing reports a
        // few contacts at its corners, and a slab only round those left the
        // rest of its footprint standing on what was not crushed. The plane
        // keeps a tilted landing's slab to the corner that struck.
        float lo = float.MaxValue, hi = float.MinValue, across = 0.0f;
        for (int k = 0; k < 8; k++)
        {
            Vector3 corner = ToGlobal(box.GetEndpoint(k)) - centre;
            float d = corner.Dot(normal) + centre.Dot(normal);
            lo = Mathf.Min(lo, d);
            hi = Mathf.Max(hi, d);
            across = Mathf.Max(across, (corner - normal * corner.Dot(normal)).Length());
        }
        float radius = Mathf.Max(spread + _stats.CrushWidth, across);
        // End on, and tall enough to have weight above the contact. A tower
        // that comes down on its side snaps instead (Break).
        if (hi - lo < _stats.CrushStopHeight || hi - lo < 0.5f * box.Size[(int)box.Size.MaxAxisIndex()])
        {
            return false;
        }

        // What it lands on is crushed under what of it was crushed: its own
        // footprint at the contact, with a little over.
        _colours.Clear();
        int crushed = _part.CrushSlab(centre, normal, _stats.CrushDepth, radius, out float footprint, _colours);
        radius = Mathf.Max(spread + _stats.CrushWidth, footprint + _grid.VoxelSize * 2.0f);
        crushed += under?.Crush(centre, -normal, _stats.CrushDepth, radius, _colours) ?? 0;
        if (crushed == 0)
        {
            return false;
        }
        if (PerfLog.Recording)
        {
            PerfLog.Event($"crush-through {VoxelCount}v crushed={crushed}v building={(under != null ? under.Name : "none")}");
        }
        Aftermath(centre, normal, footprint, crushed);
        // The building it lands on has had its slab: the blast is for what
        // is under it (VoxelBody.Ignores).
        CrushingThrough = true;
        Crush(speed, centre, radius);
        CrushingThrough = false;
        Weigh();
        LinearVelocity = _lastVelocity * _stats.CrushKeep;
        AngularVelocity = _lastSpin * _stats.CrushKeep;
        return true;
    }

    private readonly List<byte> _colours = new();

    /// <summary>
    /// What a crush leaves: its rubble share on the heap round the base, a
    /// ring of storey-sized clouds rolling out from the footprint, and a few
    /// small pieces thrown clear. What was crushed does not just go.
    /// </summary>
    private void Aftermath(Vector3 centre, Vector3 normal, float footprint, int crushed)
    {
        VoxelDebris debris = VoxelDebris.Current;
        if (debris == null || _colours.Count == 0)
        {
            return;
        }
        if (_rubble)
        {
            debris.LeaveRubble(centre, footprint + _grid.VoxelSize, crushed, _colours, _palette, _emission, _material,
                _grid.VoxelSize);
        }

        // Outward is level, about world up, not about the contact normal: the
        // normal averages contacts with the ground and the rubble, leans by
        // tens of degrees, and "outward" along it pointed metres underground.
        Vector3 up = WorldUp.At(centre);
        Vector3 side = up.Cross(Mathf.Abs(up.X) < 0.9f ? Vector3.Right : Vector3.Forward).Normalized();
        Color colour = VoxelDebris.AverageColour(_grid, _palette);
        float edge = footprint + 1.0f;
        int ring = _stats.CrushDustRing;
        float turn = (float)GD.RandRange(0.0, Mathf.Tau);
        for (int k = 0; k < ring; k++)
        {
            Vector3 out_ = side.Rotated(up, turn + Mathf.Tau * k / ring);
            if (OnSurface(centre + out_ * edge, up, 0.5f) is Vector3 at)
            {
                debris.Dust(at, up, out_ * 4.0f + LinearVelocity * 0.3f, colour, _stats.CrushDepth * 1.4f, _dustScale);
            }
        }
        for (int k = 0; k < _stats.SpallPerCrush; k++)
        {
            Vector3 out_ = side.Rotated(up, (float)GD.RandRange(0.0, Mathf.Tau));
            // A spall is up to 4 voxels across, centred: its half and a little.
            if (OnSurface(centre + out_ * edge, up, 2.5f * _grid.VoxelSize + 0.5f) is not Vector3 at)
            {
                continue;
            }
            Vector3 velocity = out_ * (float)GD.RandRange(5.0, 11.0) + up * (float)GD.RandRange(2.0, 6.0);
            debris.Spall(at, velocity, _colours, _palette, _emission, _material, _grid.VoxelSize, _dustScale, _rubble);
        }
    }

    /// <summary>
    /// Where something thrown out of a crush starts: <paramref name="clear"/>
    /// above whatever is under <paramref name="point"/> (ground, heap or
    /// building), found from well above the crush so a point that is already
    /// under the surface comes back out. Null when there is nothing under it.
    /// </summary>
    private Vector3? OnSurface(Vector3 point, Vector3 up, float clear)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return null;
        }
        float reach = _stats.CrushDepth + 8.0f;
        using var query = PhysicsRayQueryParameters3D.Create(point + up * reach, point - up * reach,
            GameLayers.Mask(GameLayers.World, GameLayers.Prop), new Godot.Collections.Array<Rid> { GetRid() });
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        return hit.Count == 0 ? null : hit["position"].AsVector3() + up * clear;
    }

    /// <summary>Voxels in it: how much it matters, when the cap needs room.</summary>
    public int VoxelCount => _grid?.SolidCount ?? 0;

    /// <summary>Lands hard on something: it hurts what is under it, through
    /// the same blast every explosive uses, so the horde, the player and the
    /// buildings take it with no special case. Its reach is its own size: a
    /// storey-sized block lands on everything under its footprint, and what
    /// it lands on is killed (<see cref="VoxelDestructionStats.CrushDamageMax"/>).
    /// Other loose pieces ignore it (<see cref="ApplyDamage"/>).</summary>
    private void Crush(float speed)
    {
        if (!VoxelBudget.TakeLandingBlast(_stats))
        {
            _crushCooldown = 0.4f;
            return;
        }
        // From its centre of mass to past its farthest corner; a piece of
        // something that leaves no rubble (a vine's stalk, tens of metres
        // long) only round its middle, where it snaps, not along all of it.
        float reach = (new Vector3(_grid.Size.X, _grid.Size.Y, _grid.Size.Z) * _grid.VoxelSize).Length() * 0.5f;
        if (!_rubble)
        {
            reach = Mathf.Min(reach, _stats.SnapReachM);
        }
        Crush(speed, GlobalPosition, reach);
    }

    /// <summary>The same, round where it actually landed: a tower crushing
    /// through lands only on what is under the crush.</summary>
    private void Crush(float speed, Vector3 centre, float reach)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(VoxelPart.SectionChunkBlast);
        _crushCooldown = 0.4f;
        float damage = Mathf.Min(Mass * speed * _stats.CrushDamagePerMomentum, _stats.CrushDamageMax);
        if (damage < 1.0f)
        {
            return;
        }
        float across = Mathf.Pow(_grid.SolidCount, 1.0f / 3.0f) * _grid.VoxelSize;
        AudioDirector.PlayAt(across > 3.0f ? Sfx.VoxelCollapse : Sfx.RubbleImpact, centre);
        reach += 1.5f;
        var blast = new Blast.Settings
        {
            Centre = centre,
            InnerRadius = reach * 0.7f,
            Radius = reach,
            Damage = damage,
            MinMultiplier = 0.5f,
            Up = WorldUp.At(centre),
            Source = this,
            SourceRid = GetRid(),
            NoCrater = true,
        };
        Blast.ApplyDamage(GetWorld3D().DirectSpaceState, blast);
    }

    private void Break(Vector3 velocity)
    {
        _breaking = true;
        if (PerfLog.Recording)
        {
            PerfLog.Event($"landed-break {VoxelCount}v at {velocity.Length():F0}m/s");
        }
        var normal = new Vector3((float)GD.RandRange(-1, 1), (float)GD.RandRange(-1, 1), (float)GD.RandRange(-1, 1));
        if (normal.LengthSquared() < 0.01f)
        {
            normal = Vector3.Right;
        }
        // The pieces keep some of the speed it had, spread a little apart: into
        // blocks half its size if it is big enough, else cracked in two.
        _part.Velocity = () => velocity * 0.4f;
        // Crumbling makes up to eight pieces; with no room for them under the
        // cap it would only push out pieces still falling, so it cracks instead.
        // A heavy one snaps across its length first, if it is long enough.
        bool room = VoxelDebris.Current != null && VoxelDebris.Current.HasRoom(8);
        if (!room || !((_heavy && _part.Crumble(Vector3.Zero, snap: true)) || _part.Crumble(Vector3.Zero)))
        {
            _part.Shatter(normal, normal.Normalized() * 1.5f);
        }
    }

    /// <summary>Gone to dust: the debris cap needed the room.</summary>
    public void Dissolve()
    {
        if (_part != null && !_part.IsEmpty && VoxelDebris.Current != null)
        {
            VoxelDebris.Current.Dust(GlobalPosition, WorldUp.At(GlobalPosition), LinearVelocity,
                VoxelDebris.AverageColour(_grid, _palette), Mathf.Pow(_grid.SolidCount, 1.0f / 3.0f) * _grid.VoxelSize,
                _dustScale);
        }
        Callable.From(Retire).CallDeferred();
    }

    public VoxelGrid Grid => _grid;
    public Color[] Palette => _palette;
    public float[] Emission => _emission;
    public Material Material => _material;
    public float DustScale => _dustScale;

    /// <summary>Whether its source heaps up (<see cref="VoxelDestructionStats.LeavesRubble"/>).</summary>
    public bool LeavesRubble => _rubble;

    /// <summary>The lowest corner of its collision boxes along <paramref name="up"/>,
    /// or NaN with none. What the ground actually holds up: the corners of its
    /// voxels' bounding box reach far below a long piece lying aslant.</summary>
    public float LowestBox(Vector3 up)
    {
        float lowest = float.NaN;
        Transform3D body = GlobalTransform;
        foreach (uint owner in GetShapeOwners())
        {
            if (IsShapeOwnerDisabled(owner))
            {
                continue;
            }
            Transform3D at = body * ShapeOwnerGetTransform(owner);
            for (int s = 0; s < ShapeOwnerGetShapeCount(owner); s++)
            {
                if (ShapeOwnerGetShape(owner, s) is not BoxShape3D box)
                {
                    continue;
                }
                Vector3 h = box.Size * 0.5f;
                for (int k = 0; k < 8; k++)
                {
                    var corner = new Vector3((k & 1) == 0 ? -h.X : h.X, (k & 2) == 0 ? -h.Y : h.Y, (k & 4) == 0 ? -h.Z : h.Z);
                    float d = (at * corner).Dot(up);
                    lowest = float.IsNaN(lowest) ? d : Mathf.Min(lowest, d);
                }
            }
        }
        return lowest;
    }

    /// <summary>Stays drawn, still and without collision, for
    /// <paramref name="seconds"/>, then retires.</summary>
    internal void RetireAfter(float seconds)
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        Freeze = true;
        GetTree().CreateTimer(seconds).Timeout += Retire;
    }

    /// <summary>Done with: back to <see cref="VoxelDebris"/> to be used again,
    /// or freed if there is none.</summary>
    internal void Retire()
    {
        if (_retired)
        {
            return;
        }
        _retired = true;
        if (VoxelDebris.Current != null && GetParent() == VoxelDebris.Current)
        {
            VoxelDebris.Current.Recycle(this);
        }
        else
        {
            QueueFree();
        }
    }

    /// <summary>Out of the tree and ready to be set up as a new piece: its
    /// voxels and collision gone, every trace of the last piece reset.</summary>
    internal void Clear()
    {
        if (_part != null)
        {
            RemoveChild(_part);
            _part.QueueFree();
        }
        foreach (int owner in GetShapeOwners())
        {
            RemoveShapeOwner((uint)owner);
        }
        _part = null;
        _parts.Clear();
        _grid = null;
        _palette = null;
        _emission = null;
        _material = null;
        _meshes = null;
        _build = null;
        _graph = null;
        _lastVelocity = Vector3.Zero;
        _lastSpin = Vector3.Zero;
        _age = 0.0f;
        _still = 0.0f;
        _crushCooldown = 0.0f;
        _breaking = false;
        _heavy = false;
        _dustScale = 1.0f;
        _rubble = true;
        _trails = false;
        _trailDust = 0.0f;
        _hinge = null;
        _hangSoon = null;
        _hingeAge = 0.0f;
        _hingeDust = 0.0f;
        _refined = false;
        _refine = null;
        _travelFor = 0.0f;
        _creeping = false;
        _support = 0.0f;
        _unsupportedAt = null;
        _propped = false;
        BakeRefused = false;
        IsSettled = false;
        IsHeld = false;
        Freeze = false;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        ContactMonitor = false;
        MaxContactsReported = 0;
        RequestReady();
    }

    public bool Ignores(Node3D source) => source is VoxelChunk;

    public void ApplyDamage(DamageInfo info)
    {
        // Rubble landing on rubble does not grind it to dust: a collapse
        // leaves a pile, not a cloud.
        if (info?.Source is VoxelChunk)
        {
            return;
        }
        if (IsSettled && info.IsSplash)
        {
            // A blast wakes rubble up.
            Wake();
        }
        VoxelDamage.Apply(info, _parts, _stats, this);
    }

    public Vector3 NearestBlastPoint(Vector3 from) => VoxelDamage.NearestPoint(_parts, from, GlobalPosition);
}
