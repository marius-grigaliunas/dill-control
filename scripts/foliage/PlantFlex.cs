using System;
using System.Collections.Generic;
using DillControl.Core;
using DillControl.Voxel;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// Makes a voxel plant bend: in its wrapper beside the <see cref="VoxelBody"/>,
/// after the model. It reads the plant's voxels layer by layer, builds a
/// <see cref="Rod"/> as thick as they are at each height, and from then on the
/// plant leans in the wind, shakes when it is hit, gives under a grapple line
/// and goes soft where it is cut (docs/decisions/0032). It breaks exactly as
/// before: the voxels and their damage are the body's.
///
/// Its collision moves with it, section by section, and carries what stands
/// on it; hits are taken back to where its voxels are (<see cref="IVoxelBend"/>).
/// Cut so deep it leans on its limit and cannot hold itself, it gives way at
/// its weakest layer, and what is above falls from the pose it was in.
/// </summary>
public partial class PlantFlex : Node, IVoxelBend
{
    [Export]
    public VoxelBody Body { get; set; }

    [Export]
    public FlexStats Stats { get; set; }

    private static readonly Dictionary<VoxelBody, PlantFlex> ByBody = new();

    /// <summary>The plant a collider belongs to, if it bends. For the grapple.</summary>
    public static PlantFlex Of(GodotObject collider) =>
        collider is VoxelCollider { Body: not null } c && ByBody.TryGetValue(c.Body, out PlantFlex flex) ? flex : null;

    internal Rod Rod { get; private set; }
    internal Vector3 Foot { get; private set; }
    internal Vector3 Up { get; private set; }
    internal Vector3 AxisX { get; private set; }
    internal Vector3 AxisZ { get; private set; }
    internal int Row { get; set; } = -1;

    private readonly List<VoxelPart> _parts = new();

    // Per part: which height bin each of its layers falls in.
    private readonly List<int[]> _layerBin = new();
    private readonly List<float[]> _layerHeight = new();
    private int[] _currentBins;
    private float _proppedFor;

    // Seconds left in which a piece breaking off is the one that gave way,
    // and the height it gave way at.
    private float _givingWay;
    private float _gaveWayAt;

    // Per height bin, as built: the middle of its voxels, in world space.
    private Vector3[] _binCentre;
    private Camera3D _camera;
    private float _binSize;
    private int _bins;
    private int[] _builtBins;
    private float _massPerVoxel;

    private int _countedVersion;
    private float _recountTimer;
    private volatile int[] _recounted;
    private int _recountVersion;
    private bool _inFlight;

    public override void _Ready()
    {
        Stats ??= GD.Load<FlexStats>(FlexStats.VinePath);
        if (Body == null || Body.Parts.Count == 0 || Body.Model == null)
        {
            GD.PushWarning($"PlantFlex '{GetParent()?.Name}': no voxel body to bend.");
            return;
        }

        Foot = Body.Model.GlobalPosition;
        Up = WorldUp.At(Foot);
        AxisX = Vector3.Right.Slide(Up);
        AxisX = AxisX.LengthSquared() < 1e-4f ? Up.Cross(Vector3.Forward).Normalized() : AxisX.Normalized();
        AxisZ = AxisX.Cross(Up);

        float top = 0.0f;
        _binSize = float.MaxValue;
        foreach (VoxelPart part in Body.Parts)
        {
            if (part.Grid == null)
            {
                continue;
            }
            _parts.Add(part);
            _binSize = Mathf.Min(_binSize, part.Grid.VoxelSize);
        }
        if (_parts.Count == 0)
        {
            return;
        }

        var heights = new List<float[]>();
        foreach (VoxelPart part in _parts)
        {
            VoxelGrid grid = part.Grid;
            Transform3D frame = part.GlobalTransform;
            var height = new float[grid.Size.Z];
            for (int z = 0; z < grid.Size.Z; z++)
            {
                height[z] = (frame * grid.CellCentre(0, 0, z) - Foot).Dot(Up);
                top = Mathf.Max(top, height[z] + (grid.VoxelSize * 0.5f));
            }
            heights.Add(height);
        }
        _bins = Math.Max(1, Mathf.CeilToInt(top / _binSize));
        foreach (float[] height in heights)
        {
            var bin = new int[height.Length];
            for (int z = 0; z < height.Length; z++)
            {
                bin[z] = Math.Clamp((int)(height[z] / _binSize), 0, _bins - 1);
            }
            _layerBin.Add(bin);
            _layerHeight.Add(height);
        }

        // Counted here, at load, the once: what it was built with.
        _builtBins = new int[_bins];
        for (int p = 0; p < _parts.Count; p++)
        {
            CountInto(_parts[p].Grid.Cells, _parts[p].Grid.Size, _layerBin[p], _builtBins);
        }
        _currentBins = _builtBins;
        Centres();
        int total = 0;
        foreach (int count in _builtBins)
        {
            total += count;
        }
        _massPerVoxel = Stats.MassKg / Math.Max(1, total);
        _countedVersion = Version();

        int segments = Math.Clamp(Stats.Segments, 2, Rod.MaxSegments);
        Rod = new Rod(segments, Mathf.Max(top, 1.0f) / segments);
        Profile(_builtBins, out float[] mass, out float[] section);
        Rod.Build(mass, section, Stats.PeriodSeconds, Stats.Damping, Stats.Taper, Stats.Weakening,
            Mathf.DegToRad(Stats.MaxLeanDegrees), WorldUp.GravityAt(Foot).Length());

        ByBody[Body] = this;
        Body.Damaged += OnDamaged;
        Body.Bend = this;
        FlexField.Join(this);
        var own = new Dictionary<Material, ShaderMaterial>();
        foreach (VoxelPart part in _parts)
        {
            if (part.Material != null && !own.ContainsKey(part.Material))
            {
                own[part.Material] = FlexField.MaterialFor(this, part.Material);
            }
            part.FlexMaterial = part.Material != null ? own[part.Material] : null;
            part.Bend = this;
            // Drawn whole until it changes: that model, and its coarse
            // copies further off, bend too.
            foreach (MeshInstance3D whole in Body.WholeDrawn)
            {
                if (whole == part.GetParent() || whole.GetParent() == part.GetParent())
                {
                    whole.MaterialOverride = part.FlexMaterial;
                }
            }
        }
        OwnMaterials.AddRange(own.Values);
    }

    /// <summary>
    /// For <c>bench.gd</c>: a bite out of the stalk 2 m up, from its -X side,
    /// sized to take a little over half its section, wherever in its grid the
    /// stalk stands. What is left should lean on what remains.
    /// </summary>
    public void BenchCarveFoot()
    {
        foreach (VoxelPart part in _parts)
        {
            if (StalkAt(part, 2.0f, out Vector3 centre, out float radius))
            {
                part.Carve(centre - (AxisX * (radius * 0.6f)), radius * 0.9f);
            }
        }
    }

    /// <summary>
    /// Its ground is gone from under it (a crater under its foot,
    /// <c>NestBuilder</c>): the foot that stood in it is taken out, so nothing
    /// of it stands on the ground any more and the integrity check lets it
    /// fall over from the pose it was in.
    /// </summary>
    public void Unroot()
    {
        // Over the way its weight hangs, from its foot, as a plant that gives
        // way goes (Release): let go upright it would stand in its crater.
        _givingWay = 3.0f;
        _gaveWayAt = 0.0f;
        foreach (VoxelPart part in _parts)
        {
            if (IsInstanceValid(part) && part.Grid != null && StalkAt(part, 0.0f, out Vector3 centre, out float radius))
            {
                part.Carve(centre - (Up * radius * 0.3f), (radius * 1.5f) + part.Grid.VoxelSize);
            }
        }
    }

    /// <summary>
    /// For <c>bench.gd</c>: somewhere to stand, <paramref name="height"/>
    /// metres up or a little above: the top of the voxel furthest out from the
    /// stalk with nothing over it (an arm or a leaf), where it is drawn now,
    /// a metre over it.
    /// </summary>
    public Vector3 BenchPerch(string height)
    {
        float metres = float.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        foreach (VoxelPart part in _parts)
        {
            VoxelGrid grid = part.Grid;
            if (!StalkAt(part, metres, out Vector3 centre, out float radius))
            {
                continue;
            }
            Vector3 best = Vector3.Zero;
            float far = radius + 2.0f;
            int z0 = Math.Clamp(Mathf.RoundToInt(metres / grid.VoxelSize), 0, grid.Size.Z - 3);
            int z1 = Math.Min(grid.Size.Z - 3, z0 + Mathf.RoundToInt(20.0f / grid.VoxelSize));
            for (int z = z0; z <= z1; z++)
            {
                for (int y = 0; y < grid.Size.Y; y++)
                {
                    for (int x = 0; x < grid.Size.X; x++)
                    {
                        if (grid.At(x, y, z) == 0 || grid.At(x, y, z + 1) != 0 || grid.At(x, y, z + 2) != 0
                            || grid.At(x, y, z + 3) != 0 || grid.At(x, y, z + 4) != 0)
                        {
                            continue;
                        }
                        Vector3 top = part.GlobalTransform * (grid.CellCentre(x, y, z) + (Vector3.Up * grid.VoxelSize * 0.5f));
                        float out_ = (top - centre).Slide(Up).Length();
                        // Not the very tip of an arm: a little way in, on solid going.
                        if (out_ > far && out_ < 14.0f && grid.At(x + 1, y, z) != 0 && grid.At(x - 1, y, z) != 0)
                        {
                            far = out_;
                            best = top;
                        }
                    }
                }
            }
            if (best != Vector3.Zero)
            {
                GD.Print(System.FormattableString.Invariant($"[flex] perch on {GetParent()?.Name} {far:F1} m out at {best}"));
                return ToDrawn(best) + (Up * 1.0f);
            }
        }
        return Foot + (Up * metres);
    }

    /// <summary>For <c>bench.gd</c>: the middle of the stalk <paramref name="height"/> metres up, to aim at.</summary>
    public Vector3 BenchStalk(string height)
    {
        float metres = float.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        foreach (VoxelPart part in _parts)
        {
            if (StalkAt(part, metres, out Vector3 centre, out _))
            {
                return centre;
            }
        }
        return Foot + (Up * metres);
    }

    // The middle of the stalk in one layer, and the radius of a disc as big.
    // Followed up from the foot, each layer counting only what is near the
    // last one's middle: a whole layer's middle is pulled off the stalk, into
    // the air, by an arm or a leaf on one side.
    private static bool StalkAt(VoxelPart part, float height, out Vector3 centre, out float radius)
    {
        VoxelGrid grid = part.Grid;
        int top = Math.Clamp(Mathf.RoundToInt(height / grid.VoxelSize), 0, grid.Size.Z - 1);
        float cx = 0.0f, cy = 0.0f, reach = float.MaxValue, footReach = float.MaxValue;
        int count = 0, z = 0;
        for (; z <= top; z++)
        {
            float sx = 0.0f, sy = 0.0f;
            int n = 0;
            for (int y = 0; y < grid.Size.Y; y++)
            {
                for (int x = 0; x < grid.Size.X; x++)
                {
                    if (grid.Cells[grid.Index(x, y, z)] == 0)
                    {
                        continue;
                    }
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (reach < float.MaxValue && (dx * dx) + (dy * dy) > reach * reach)
                    {
                        continue;
                    }
                    sx += x + 0.5f;
                    sy += y + 0.5f;
                    n++;
                }
            }
            if (n == 0)
            {
                break;
            }
            cx = sx / n;
            cy = sy / n;
            count = n;
            // A little more than the stalk is wide, never more than the foot allows.
            float r = Mathf.Sqrt(n / Mathf.Pi);
            if (footReach == float.MaxValue)
            {
                footReach = (r * 1.5f) + (2.0f / grid.VoxelSize);
            }
            reach = Mathf.Min(r + (2.0f / grid.VoxelSize), footReach);
        }
        z = Math.Min(z, top);
        centre = count > 0 ? part.GlobalTransform * grid.CellCentre(Mathf.FloorToInt(cx), Mathf.FloorToInt(cy), z) : Vector3.Zero;
        radius = Mathf.Sqrt(count / Mathf.Pi) * grid.VoxelSize;
        return count > 0;
    }

    public override void _ExitTree()
    {
        if (Rod == null)
        {
            return;
        }
        FlexField.Leave(this);
        if (Body != null)
        {
            Body.Damaged -= OnDamaged;
            Body.Bend = null;
            ByBody.Remove(Body);
        }
        foreach (VoxelPart part in _parts)
        {
            if (IsInstanceValid(part))
            {
                part.FlexMaterial = null;
                part.Bend = null;
            }
        }
        Rod = null;
    }

    /// <summary>Its own copies of its material, bending by its row (FlexField).</summary>
    internal List<ShaderMaterial> OwnMaterials { get; } = new();

    /// <summary>A copy of <paramref name="from"/> (a shader with the flex
    /// uniforms) that bends as this plant does: what its leaves are drawn in.
    /// Null before it has a spine.</summary>
    internal ShaderMaterial BendMaterial(Material from)
    {
        if (Rod == null)
        {
            return null;
        }
        ShaderMaterial own = FlexField.MaterialFor(this, from);
        if (own != null)
        {
            OwnMaterials.Add(own);
        }
        return own;
    }

    public Vector3 ToBuilt(Vector3 drawn)
    {
        // The move depends on height, and height hardly moves: twice is plenty.
        Vector3 built = drawn - OffsetAt(drawn);
        return drawn - OffsetAt(built);
    }

    public Vector3 ToDrawn(Vector3 built) => built + OffsetAt(built);

    public void Release(Aabb bounds, ref Transform3D at, ref Vector3 velocity, ref Vector3? spin, out float angularDamp,
        out Vector3 hingePivot, out Vector3 hingeAxis)
    {
        hingePivot = Vector3.Zero;
        hingeAxis = Vector3.Zero;
        // A long piece of stalk keeps what turning it has: damped like a
        // block, one let go nearly upright never gets going and stands.
        angularDamp = bounds.GetLongestAxisSize() >= Stats.SlenderMetres ? Stats.FallingAngularDamp : -1.0f;
        if (Rod == null)
        {
            return;
        }
        Vector3 centre = at * bounds.GetCenter();
        float bottom = float.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            bottom = Mathf.Min(bottom, ((at * bounds.GetEndpoint(i)) - Foot).Dot(Up));
        }
        bottom = Mathf.Max(bottom, 0.0f);
        float middle = (centre - Foot).Dot(Up);

        // Turned as the spine was at its foot, about that point, and moved
        // as that point was drawn: a rigid piece as near the bent one as it can be.
        Rod.Motion(bottom, out Vector2 tilt, out Vector2 tiltRate, out _);
        Rod.Motion(middle, out _, out _, out Vector2 moving);
        Vector3 pivot = centre + (Up * (bottom - middle));
        Vector3 turnBy = Up.Cross(World(tilt, 0.0f));
        Basis turn = turnBy.LengthSquared() > 1e-10f ? new Basis(turnBy.Normalized(), turnBy.Length()) : Basis.Identity;
        at = new Transform3D(turn * at.Basis, pivot + OffsetAt(pivot) + (turn * (at.Origin - pivot)));
        velocity += World(moving, 0.0f);
        Vector3 turning = Up.Cross(World(tiltRate, 0.0f));
        if (_givingWay > 0.0f && bottom >= _gaveWayAt - Rod.SegmentLength)
        {
            // What gave way goes over the way it leant, from the start: a tall
            // piece let go almost upright otherwise stands for seconds.
            // Over the way its weight hangs, as drawn now: the built shape
            // leans and twists, and the bend is on top of that.
            Vector3 weight = ToDrawn(WeightAbove(_gaveWayAt, out Vector3 cutCentre));
            Vector3 cut = ToDrawn(cutCentre);
            Vector3 lean = (weight - cut).Slide(Up);
            if (lean.LengthSquared() < 0.01f)
            {
                lean = World(tilt, 0.0f);
            }
            if (lean.LengthSquared() < 1e-8f)
            {
                lean = FlexField.Wind?.Direction.Slide(Up) ?? AxisX;
            }
            Vector3 over = lean.Normalized();
            Vector3 topple = Up.Cross(over) * Stats.ToppleSpin;
            turning += topple;

            // Hinged just off the middle of the cut on that side until it
            // tears free (VoxelChunk): no landing flat on its stump and
            // crushing down it, standing.
            int bin = Math.Clamp((int)(_gaveWayAt / _binSize), 0, _bins - 1);
            float radius = Mathf.Sqrt(_builtBins[bin] / Mathf.Pi) * _binSize;
            hingePivot = cut + (over * (radius * 0.25f));
            hingeAxis = Up.Cross(over);
            // Its middle swinging about the hinge as fast as it turns: spin
            // alone, the hinge takes most of it back.
            velocity += topple.Cross(at * bounds.GetCenter() - hingePivot);
        }
        if (turning.LengthSquared() > 1e-4f)
        {
            spin = (spin ?? Vector3.Zero) + turning;
        }
        Released++;
        if (bounds.GetLongestAxisSize() >= Stats.SlenderMetres)
        {
            FallDust(pivot, (at * bounds.GetCenter()) - Foot, bounds.GetLongestAxisSize());
        }
    }

    private double _fallDustAt = -10.0;

    /// <summary>
    /// A long piece of it has gone: big dark clouds burst where it broke, and
    /// when it has had time to come down, along the ground the way it went.
    /// Once a fall, however many pieces it breaks into.
    /// </summary>
    private void FallDust(Vector3 broke, Vector3 outward, float length)
    {
        VoxelDebris debris = VoxelDebris.Current;
        double now = Time.GetTicksMsec() / 1000.0;
        if (Stats.FallDustPuffs <= 0 || debris == null || now - _fallDustAt < 2.0)
        {
            return;
        }
        _fallDustAt = now;
        Color colour = Stats.FallDustColour;
        float edge = Stats.FallDustEdgeM;
        Vector3 side = AxisX;
        for (int k = 0; k < Stats.FallDustPuffs; k++)
        {
            Vector3 round = side.Rotated(Up, k * Mathf.Tau / Stats.FallDustPuffs) * (edge * 0.35f);
            debris.Dust(broke + round, Up, round * 0.4f, colour, edge);
        }

        // Where it lands: from the foot out the way it is falling, as far as it is long.
        Vector3 over = outward.Slide(Up);
        over = over.LengthSquared() > 0.01f ? over.Normalized() : (FlexField.Wind?.Direction.Slide(Up).Normalized() ?? AxisX);
        Vector3 foot = Foot, up = Up;
        float step = Stats.FallDustStepM;
        float landIn = Stats.FallLandK * Mathf.Sqrt(length / 9.8f);
        GetTree().CreateTimer(landIn).Timeout += () =>
        {
            if (VoxelDebris.Current == null)
            {
                return;
            }
            for (float d = length * 0.3f; d <= length; d += step)
            {
                Vector3 at = foot + (over * d);
                // On a planet the ground falls away under a straight line.
                float drop = d * up.AngleTo(WorldUp.At(at)) * 0.5f;
                VoxelDebris.Current.Dust(at - (up * drop), WorldUp.At(at), over * 2.0f, colour, edge);
            }
        };
    }

    internal int Released { get; private set; }

    /// <summary>How far the drawn plant is from its built shape at a point of it, in world space.</summary>
    public Vector3 OffsetAt(Vector3 point)
    {
        if (Rod == null)
        {
            return Vector3.Zero;
        }
        Rod.At((point - Foot).Dot(Up), out Vector2 offset, out float drop);
        return (AxisX * offset.X) + (AxisZ * offset.Y) - (Up * drop);
    }

    /// <summary>A pull at a point of it, this tick: a grapple line, in newtons.</summary>
    public void Pull(Vector3 point, Vector3 force)
    {
        if (Rod == null)
        {
            return;
        }
        Pulls++;
        Rod.AddForce((point - Foot).Dot(Up), Flat(force) * Stats.LinePull);
    }

    /// <summary>For the bench: hits taken and ticks pulled on.</summary>
    internal int Hits { get; private set; }

    internal int Pulls { get; private set; }

    /// <summary>For the bench: explosions that shook it.</summary>
    internal int Kicks { get; private set; }

    /// <summary>For the bench: the furthest its tip has been since it was last reported.</summary>
    internal float PeakTip { get; set; }

    internal int Recounts { get; private set; }

    private Vector2 Flat(Vector3 v) => new(v.Dot(AxisX), v.Dot(AxisZ));

    internal Vector3 World(Vector2 offset, float drop) => (AxisX * offset.X) + (AxisZ * offset.Y) - (Up * drop);

    private void OnDamaged(DamageInfo info)
    {
        if (Rod == null || info == null)
        {
            return;
        }
        Hits++;
        if (!info.IsExplosive)
        {
            // A round, a blade, a flame: it bites the voxels and leaves an
            // 80 m plant standing still. Only a detonation shakes it.
            return;
        }
        Kicks++;
        Vector3 push = info.Direction;
        if (push.LengthSquared() < 1e-6f)
        {
            push = (info.Position - Foot).Slide(Up);
        }
        Vector2 flat = Flat(push);
        if (flat.LengthSquared() < 1e-6f)
        {
            return;
        }
        float impulse = Stats.KickPerDamage * info.Amount;
        if (Stats.MaxKick > 0.0f)
        {
            impulse = Mathf.Min(impulse, Stats.MaxKick);
        }
        Rod.AddImpulse((info.Position - Foot).Dot(Up), flat.Normalized() * impulse);
    }

    /// <summary>One physics tick: the wind on every segment, then the rod.</summary>
    internal void Step(float dt, WindStats wind, double time)
    {
        if (Rod == null)
        {
            return;
        }
        Recount(dt);

        // Far off, the spine is stepped every few ticks, the time at once,
        // and its collision caught up every few more.
        _away = CameraDistance();
        ulong tick = Engine.GetPhysicsFrames() + (ulong)Math.Max(Row, 0);
        bool far = _away > Stats.StepFarMetres && Stats.StepFarEvery > 1;
        _owed += dt;
        if (far && tick % (ulong)Stats.StepFarEvery != 0)
        {
            return;
        }
        dt = _owed;
        _owed = 0.0f;

        Vector2 blowing = wind != null ? Flat(wind.At(Foot, Up, time)) : Vector2.Zero;
        float push = Stats.WindPush;
        int n = Rod.Segments;
        for (int s = 0; s < n; s++)
        {
            // Stronger aloft: the foot is in the lee of everything round it.
            float aloft = 0.3f + (0.7f * (s + 0.5f) / n);
            Rod.SetSegmentForce(s, blowing * (push * aloft * SegmentMass(s)));
        }
        Rod.Step(dt);
        PeakTip = Mathf.Max(PeakTip, Rod.Offset(Rod.Segments).Length());
        GiveWay(dt);
        Follow(dt);
    }

    /// <summary>
    /// Its collision to where it is drawn. Every tick near the camera, so it
    /// carries whoever stands on it; further off only when a section has moved
    /// by a step, still, so a rocket from afar meets the plant it is aimed at.
    /// </summary>
    private void Follow(float dt)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionFollow);
        Vector3 near = _camera != null && IsInstanceValid(_camera) ? _camera.GlobalPosition : Foot;
        float nearRadius = Stats.FollowSectionMetres;
        if (_away >= Stats.FollowNearMetres)
        {
            // Far: caught up a plant at a time, only where it has drifted.
            nearRadius = 0.0f;
            _sinceFollow++;
            if (_sinceFollow < Stats.FollowFarEvery)
            {
                return;
            }
            _sinceFollow = 0;
        }
        _offsetAt ??= OffsetAt;
        foreach (VoxelPart part in _parts)
        {
            if (IsInstanceValid(part) && !part.IsEmpty)
            {
                part.BendSections(_offsetAt, dt, near, nearRadius, Stats.FollowFarStep);
            }
        }
    }

    private static readonly int SectionFollow = FrameProfiler.Section("plant follow");

    private Func<Vector3, Vector3> _offsetAt;
    private float _away;
    private float _owed;
    private int _sinceFollow;

    /// <summary>How far the camera is from its foot, across the ground.</summary>
    private float CameraDistance()
    {
        if (_camera == null || !IsInstanceValid(_camera) || !_camera.IsInsideTree())
        {
            _camera = GetViewport()?.GetCamera3D();
        }
        return _camera == null ? 0.0f : (_camera.GlobalPosition - Foot).Slide(Up).Length();
    }

    /// <summary>
    /// A joint cut weak and kinked past <see cref="FlexStats.GiveWayDegrees"/>
    /// (or the plant leaning on its limit) is not holding the plant up: after
    /// a moment it gives way at the thinnest layer of that joint, and the
    /// voxels split there as for any cut (0024); what was above falls from
    /// where it was drawn (<see cref="Release"/>). Its own weight, the wind
    /// and a player swinging on it are what kink it.
    /// </summary>
    private void GiveWay(float dt)
    {
        _givingWay = Mathf.Max(0.0f, _givingWay - dt);
        if (_proppedFor < 0.0f)
        {
            // Just gave way: not again until what fell has been counted off.
            _proppedFor = Mathf.Min(0.0f, _proppedFor + dt);
            return;
        }
        float left = Rod.Softest(out int joint);
        bool kinked = Rod.Kink(joint) > Mathf.DegToRad(Stats.GiveWayDegrees);
        if ((Rod.Propped < 0 && !kinked) || left > Stats.GiveWayBelow)
        {
            _proppedFor = 0.0f;
            return;
        }
        _proppedFor += dt;
        if (_proppedFor < Stats.GiveWaySeconds)
        {
            return;
        }
        _proppedFor = -2.0f;

        float length = Rod.SegmentLength;
        float from = (joint - 0.5f) * length, to = (joint + 0.5f) * length;
        int thinnest = -1;
        float least = float.MaxValue;
        for (int b = Math.Max(0, (int)(from / _binSize)); b < _bins && b * _binSize < to; b++)
        {
            if (_builtBins[b] > 0 && _currentBins[b] > 0)
            {
                float share = (float)_currentBins[b] / _builtBins[b];
                if (share < least)
                {
                    least = share;
                    thinnest = b;
                }
            }
        }
        if (thinnest < 0)
        {
            return;
        }
        float height = (thinnest + 0.5f) * _binSize;
        _givingWay = 3.0f;
        _gaveWayAt = height;
        if (PerfLog.Recording)
        {
            PerfLog.Event(System.FormattableString.Invariant($"plant-gives-way {GetParent()?.Name} at {height:F1}m left={least:P0}"));
        }
        for (int p = 0; p < _parts.Count; p++)
        {
            float[] heights = _layerHeight[p];
            int layer = 0;
            for (int z = 1; z < heights.Length; z++)
            {
                if (Mathf.Abs(heights[z] - height) < Mathf.Abs(heights[layer] - height))
                {
                    layer = z;
                }
            }
            VoxelPart part = _parts[p];
            part.Cut(layer * part.Grid.VoxelSize, part.Grid.VoxelSize, 1.0f);
        }
        GaveWay++;
    }

    internal int GaveWay { get; private set; }

    private float[] _segmentMass;

    private float SegmentMass(int s) => _segmentMass != null ? _segmentMass[s] : 0.0f;

    private void Profile(int[] bins, out float[] mass, out float[] section)
    {
        mass = new float[Rod.Segments];
        section = new float[Rod.Segments];
        RodProfile.From(bins, _builtBins, _binSize, Rod.Segments, Rod.SegmentLength, _massPerVoxel, mass, section);
        _segmentMass = mass;
    }

    private int Version()
    {
        int version = 0;
        foreach (VoxelPart part in _parts)
        {
            version = unchecked((version * 31) + (part.Grid?.Version ?? 0));
        }
        return version;
    }

    /// <summary>
    /// What is left, counted again after damage. Scales with the plant's
    /// voxels, so it is worked out on a voxel worker from a copy of the cells
    /// and used only if nothing has changed since (docs/decisions/0015).
    /// </summary>
    private void Recount(float dt)
    {
        int[] done = _recounted;
        if (done != null)
        {
            _recounted = null;
            _inFlight = false;
            if (_recountVersion == Version())
            {
                _countedVersion = _recountVersion;
                _currentBins = done;
                Profile(done, out float[] mass, out float[] section);
                Rod.SetProfile(mass, section, Stats.Damping);
                Recounts++;
            }
        }

        _recountTimer -= dt;
        if (_inFlight || _recountTimer > 0.0f)
        {
            return;
        }
        int version = Version();
        if (version == _countedVersion)
        {
            return;
        }
        _recountTimer = Stats.RecountSeconds;
        _inFlight = true;
        _recountVersion = version;

        var cells = new byte[_parts.Count][];
        var sizes = new Vector3I[_parts.Count];
        for (int p = 0; p < _parts.Count; p++)
        {
            cells[p] = (byte[])_parts[p].Grid.Cells.Clone();
            sizes[p] = _parts[p].Grid.Size;
        }
        List<int[]> layerBin = _layerBin;
        int bins = _bins;
        VoxelWorkers.Run(() =>
        {
            var counted = new int[bins];
            for (int p = 0; p < cells.Length; p++)
            {
                CountInto(cells[p], sizes[p], layerBin[p], counted);
            }
            _recounted = counted;
        });
    }

    /// <summary>Where each height bin's voxels are centred, as built: which way what is above a cut leans.</summary>
    private void Centres()
    {
        var sums = new Vector3[_bins];
        var counts = new int[_bins];
        for (int p = 0; p < _parts.Count; p++)
        {
            VoxelGrid grid = _parts[p].Grid;
            Transform3D frame = _parts[p].GlobalTransform;
            int[] bin = _layerBin[p];
            for (int z = 0; z < grid.Size.Z; z++)
            {
                Vector3 sum = Vector3.Zero;
                int count = 0;
                for (int y = 0; y < grid.Size.Y; y++)
                {
                    int row = grid.Index(0, y, z);
                    for (int x = 0; x < grid.Size.X; x++)
                    {
                        if (grid.Cells[row + x] != 0)
                        {
                            sum += new Vector3(x, y, 0);
                            count++;
                        }
                    }
                }
                if (count > 0)
                {
                    Vector3 mean = sum / count;
                    sums[bin[z]] += (frame * grid.CellCentre(0, 0, z) + (frame.Basis * (VoxSpace.ToGodot(new Vector3(mean.X, mean.Y, 0)) * grid.VoxelSize))) * count;
                    counts[bin[z]] += count;
                }
            }
        }
        _binCentre = new Vector3[_bins];
        for (int b = 0; b < _bins; b++)
        {
            _binCentre[b] = counts[b] > 0 ? sums[b] / counts[b] : Foot + (Up * ((b + 0.5f) * _binSize));
        }
    }

    /// <summary>The middle of the weight above a height, as built.</summary>
    private Vector3 WeightAbove(float height, out Vector3 cutCentre)
    {
        int from = Math.Clamp((int)(height / _binSize), 0, _bins - 1);
        cutCentre = _binCentre[from];
        Vector3 sum = Vector3.Zero;
        float weight = 0.0f;
        for (int b = from + 1; b < _bins; b++)
        {
            int count = _currentBins[b];
            sum += _binCentre[b] * count;
            weight += count;
        }
        return weight > 0.0f ? sum / weight : cutCentre;
    }

    private static void CountInto(byte[] cells, Vector3I size, int[] layerBin, int[] into)
    {
        int layer = size.X * size.Y;
        for (int z = 0; z < size.Z; z++)
        {
            int count = 0;
            int start = z * layer;
            for (int i = start; i < start + layer; i++)
            {
                if (cells[i] != 0)
                {
                    count++;
                }
            }
            into[layerBin[z]] += count;
        }
    }
}
