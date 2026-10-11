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
public partial class VoxelChunk : RigidBody3D, IDamageable, IBlastSurface, IGrabbable
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

    public bool IsSettled { get; private set; }

    public bool IsHinged => _hinge != null;

    // ---------------- Gravity gun ----------------

    public bool CanGrab => _part != null && !_breaking && _grid.SolidCount <= _stats.MaxGrabVoxels && _hinge == null;

    public bool IsHeld { get; private set; }

    /// <summary>Held, it is awake and stays so: it does not settle, freeze,
    /// join a heap or go for the cap.</summary>
    public void SetHeld(bool held)
    {
        IsHeld = held;
        if (held)
        {
            IsSettled = false;
            Freeze = false;
            _still = 0.0f;
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
        (MeshInstance3D[], Vector3)? meshes = null, ChunkBuild build = null)
    {
        _retired = false;
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
        AngularDampMode = DampMode.Combine;
        AngularDamp = _stats.AngularDamp;

        _part = new VoxelPart
        {
            Name = "Voxels",
            CollisionOwner = this,
            CollisionStep = _build?.Step ?? VoxelSplit.CollisionStepFor(_grid, _stats.MaxChunkBoxes),
            Velocity = () => LinearVelocity,
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
        _part.Emptied += _ => Callable.From(Retire).CallDeferred();
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
            if (_heavy && _hinge == null && _hangSoon == null && change > _stats.CrushThroughSpeed && CrushThrough(change))
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
            if (slam > _stats.BreakSpeed && _grid.SolidCount >= _stats.MinBreakVoxels)
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

        _still =!IsHeld && (Sleeping || velocity.LengthSquared() < 0.01f) ? _still + dt : 0.0f;
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
        }
        Refine();
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
            VoxelDebris.Current.Dust(ToGlobal(local), up, velocity * 0.2f, colour, _stats.TrailDustSize);
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
    /// Once it has lain still a moment and was not taken into a heap, its
    /// collision is re-boxed as finely as <see cref="VoxelDestructionStats.RestingChunkBoxes"/>
    /// allows. It flew with boxes up to 8 voxels across (4 m on a building),
    /// each the size of whatever voxels were left in it, so a hole shot through
    /// a fallen piece still stopped you. The step is worked out on a worker
    /// from a copy of its cells; the re-boxing goes to the workers like any.
    /// </summary>
    private void Refine()
    {
        if (_refine != null && _refine.IsCompleted)
        {
            if (_refine.IsCompletedSuccessfully && _part != null && _refine.Result < _part.CollisionStep)
            {
                if (PerfLog.Recording)
                {
                    PerfLog.Event($"refine {VoxelCount}v step {_part.CollisionStep}->{_refine.Result}");
                }
                _part.Rebox(_refine.Result);
            }
            _refine = null;
        }
        if (_refined || _part == null || IsHeld || _part.CollisionStep <= 1
            || _still < _stats.BakeAfterSeconds + 0.5f || _grid.SolidCount == 0)
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
                VoxelDebris.Current.Dust(_hingePivot, WorldUp.At(_hingePivot), Vector3.Zero, colour, 2.0f + 4.0f * angle / limit);
            }
            return;
        }

        if (IsInstanceValid(_hinge))
        {
            _hinge.QueueFree();
        }
        _hinge = null;
        VoxelDebris.Current?.Dust(_hingePivot, WorldUp.At(_hingePivot), LinearVelocity * 0.3f, colour, 7.0f);
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
        debris.LeaveRubble(centre, footprint + _grid.VoxelSize, crushed, _colours, _palette, _emission, _material,
            _grid.VoxelSize);

        Vector3 side = normal.Cross(Mathf.Abs(normal.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        Color colour = VoxelDebris.AverageColour(_grid, _palette);
        float edge = footprint + 1.0f;
        int ring = _stats.CrushDustRing;
        float turn = (float)GD.RandRange(0.0, Mathf.Tau);
        for (int k = 0; k < ring; k++)
        {
            Vector3 out_ = side.Rotated(normal, turn + Mathf.Tau * k / ring);
            debris.Dust(centre + out_ * edge, normal, out_ * 4.0f + LinearVelocity * 0.3f, colour, _stats.CrushDepth * 1.4f);
        }
        for (int k = 0; k < _stats.SpallPerCrush; k++)
        {
            Vector3 out_ = side.Rotated(normal, (float)GD.RandRange(0.0, Mathf.Tau));
            Vector3 velocity = out_ * (float)GD.RandRange(5.0, 11.0) + normal * (float)GD.RandRange(2.0, 6.0);
            debris.Spall(centre + out_ * edge + normal * 1.0f, velocity, _colours, _palette, _emission, _material,
                _grid.VoxelSize);
        }
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
        // From its centre of mass to past its farthest corner.
        float reach = (new Vector3(_grid.Size.X, _grid.Size.Y, _grid.Size.Z) * _grid.VoxelSize).Length() * 0.5f;
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
                VoxelDebris.AverageColour(_grid, _palette), Mathf.Pow(_grid.SolidCount, 1.0f / 3.0f) * _grid.VoxelSize);
        }
        Callable.From(Retire).CallDeferred();
    }

    public VoxelGrid Grid => _grid;
    public Color[] Palette => _palette;
    public float[] Emission => _emission;
    public Material Material => _material;

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
        _lastVelocity = Vector3.Zero;
        _lastSpin = Vector3.Zero;
        _age = 0.0f;
        _still = 0.0f;
        _crushCooldown = 0.0f;
        _breaking = false;
        _heavy = false;
        _trails = false;
        _trailDust = 0.0f;
        _hinge = null;
        _hangSoon = null;
        _hingeAge = 0.0f;
        _hingeDust = 0.0f;
        _refined = false;
        _refine = null;
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
            IsSettled = false;
            Freeze = false;
            _still = 0.0f;
        }
        VoxelDamage.Apply(info, _parts, _stats, this);
    }

    public Vector3 NearestBlastPoint(Vector3 from) => VoxelDamage.NearestPoint(_parts, from, GlobalPosition);
}
