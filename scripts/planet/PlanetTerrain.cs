using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using DillControl.Core;
using DillControl.Horde;

namespace DillControl.Planet;

/// <summary>
/// The planet in the level: a <see cref="PlanetHeightfield"/> drawn as chunks
/// near the viewer and a <see cref="PlanetFarShell"/> everywhere, with
/// colliders streamed round the camera, a point-gravity well, craters dug by
/// blasts, and the authored <see cref="PlanetSite"/>s stood on the ground.
///
/// Three decisions are load-bearing.
///
/// It is built in C#, not saved into a scene: craters rebuild chunks while the
/// game runs, so the build path exists anyway, and a baked copy could only
/// ever disagree with it.
///
/// It sits at the world origin (<c>docs/decisions/0018</c>). Every query on
/// the heightfield, the horde's ground and <see cref="WorldUp"/> treats a
/// world point as a direction from the centre; a moved planet would draw in
/// the right place and answer about the wrong one.
///
/// Colliders are streamed: only chunks within
/// <see cref="PlanetStreamingStats.CollisionRingChunks"/> of the camera, and
/// round any site that asks to keep them, carry a shape. A rigid body anywhere
/// else has nothing to land on.
///
/// Ported from the lab's <c>PlanetTerrain</c>; what <c>SpikeDirector</c> did
/// round it (streaming each frame, placing things on the ground, digging
/// craters from blasts) is here now, because no director exists to do it.
/// </summary>
[GlobalClass]
public partial class PlanetTerrain : Node3D, IHordeGroundProvider
{
    [ExportGroup("Data")]
    /// <summary>
    /// The planet's shape, when it is authored. Ignored when an
    /// <see cref="Archetype"/> is set: the seed rolls the shape instead.
    /// </summary>
    [Export]
    public PlanetSpec Spec { get; set; }

    /// <summary>A kind of planet to roll from <see cref="Seed"/>. Empty builds <see cref="Spec"/> as authored.</summary>
    [Export]
    public PlanetArchetype Archetype { get; set; }

    /// <summary>Looks the seed picks between. Empty leaves the material, sky and sun as authored.</summary>
    [Export]
    public Godot.Collections.Array<PlanetBiome> Biomes { get; set; } = new();

    /// <summary>
    /// The seed that rolls the shape and picks and shifts the biome. A bench
    /// sweep overrides it (<see cref="BenchSeedMeta"/>); a contract will set
    /// it before building.
    /// </summary>
    [Export]
    public long Seed { get; set; } = 1;

    [Export]
    public PlanetStreamingStats Stats { get; set; }

    [Export]
    public Material TerrainMaterial { get; set; }

    [ExportGroup("Scene Wiring")]
    /// <summary>Authored places to level and stand on the ground at build: spawn, compound, clusters.</summary>
    [Export]
    public Godot.Collections.Array<PlanetSite> Sites { get; set; } = new();

    /// <summary>Build in <c>_Ready</c>. Off when something else (a loading stage) calls <see cref="Build"/>.</summary>
    [Export]
    public bool BuildOnReady { get; set; } = true;

    /// <summary>The level's environment, for the biome's sky and fog.</summary>
    [Export]
    public WorldEnvironment Environment { get; set; }

    /// <summary>The level's sun, for the biome's light and where it stands.</summary>
    [Export]
    public DirectionalLight3D Sun { get; set; }

    /// <summary>A meta value on the tree's root that, when set, replaces <see cref="Seed"/>: how <c>bench.gd planets</c> sweeps seeds.</summary>
    public const string BenchSeedMeta = "bench_planet_seed";

    /// <summary>The biome this planet was painted with, or null.</summary>
    public PlanetBiome Biome { get; private set; }

    /// <summary>The seed this planet was built from: what the rest of the system rolls from too.</summary>
    public ulong BuiltSeed { get; private set; }

    /// <summary>Local signal: the planet exists, its sites are on the ground, and the ground under them collides.</summary>
    [Signal]
    public delegate void BuiltEventHandler();

    /// <summary>Local signal: the ground moved, round a point.</summary>
    [Signal]
    public delegate void TerrainDeformedEventHandler(Vector3 centre, float radius);

    public PlanetHeightfield Heights { get; private set; }

    public bool IsBuilt { get; private set; }

    public float Radius => Heights?.Radius ?? 0.0f;

    public int ChunksPerFaceEdge { get; private set; }
    public int ChunkVertexEdge { get; private set; }
    public int FaceVertexEdge => Heights?.FaceVertexEdge ?? 0;

    public int ChunkCount => _chunks?.Length ?? 0;
    public int ResidentColliderCount { get; private set; }
    public int VisibleChunkCount { get; private set; }
    public int PendingBuildCount => _buildQueue.Count;

    /// <summary>Milliseconds the last <see cref="Build"/> took, generation included.</summary>
    public float LastBuildMilliseconds { get; private set; }

    public float LastCraterMilliseconds { get; private set; }

    private PlanetStreamingStats _stats;
    private PlanetChunk[] _chunks;
    private bool[] _queued;
    private bool[] _keepCollider;

    /// <summary>Per chunk, the cosine of how far off the viewer it may be and still collide.</summary>
    private float[] _collisionCosine;

    /// <summary>The largest chunk's angular radius.</summary>
    private float _widestChunk;
    private bool[] _dirtyFlag;
    private readonly List<int> _dirtyChunks = new();
    private readonly Queue<int> _buildQueue = new();
    private readonly Queue<int> _colliderQueue = new();
    private int[] _sharedIndices;
    private Vector3[] _paddedScratch;
    private Vector3[] _vertexScratch;
    private Vector3[] _normalScratch;
    private Color[] _dugScratch;
    private Area3D _gravityField;
    private PlanetFarShell _farShell;

    private PlanetGround _hordeGround;
    private FlowField _hordeFlow;

    private int _sectionRebuild;
    private int _sectionStream;
    private int _sectionCrater;

    public override void _Ready()
    {
        _sectionRebuild = FrameProfiler.Section("planet chunk build");
        _sectionStream = FrameProfiler.Section("planet stream");
        _sectionCrater = FrameProfiler.Section("planet crater");

        if (BuildOnReady)
        {
            Window root = GetTree().Root;
            ulong seed = (ulong)(root.HasMeta(BenchSeedMeta) ? root.GetMeta(BenchSeedMeta).AsInt64() : Seed);
            PlanetSpec spec = Archetype != null ? Archetype.Roll(seed) : Spec;
            if (spec == null)
            {
                GD.PushError($"PlanetTerrain '{Name}' has neither a PlanetSpec nor an archetype; no planet.");
                return;
            }
            PlanetBiome biome = null;
            if (Biomes.Count > 0)
            {
                biome = Biomes[SeededRandom.Stream(seed, "biome pick").RangeInt(0, Biomes.Count - 1)];
            }
            Build(spec, biome, seed);
        }
    }

    public override void _ExitTree()
    {
        if (IsBuilt)
        {
            WorldUp.SetFlat();
        }
        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast -= OnAreaBlast;
        }
    }

    // ---------------- Build ----------------

    /// <summary>
    /// Generates the heightfield, levels the sites' pads, makes the chunks and
    /// the far shell, stands the sites on the ground and builds the ground
    /// under them now, so whatever spawns there lands. The rest of the chunks
    /// are queued and built a few a frame.
    /// </summary>
    public void Build(PlanetSpec spec, PlanetBiome biome = null, ulong seed = 0)
    {
        long started = Stopwatch.GetTimestamp();
        Spec = spec;
        Biome = biome;
        BuiltSeed = seed;
        _stats = Stats ?? new PlanetStreamingStats();

        if (GlobalPosition.LengthSquared() > 0.01f)
        {
            GD.PushWarning($"PlanetTerrain '{Name}' is not at the world origin; every query assumes it is (decision 0018).");
        }

        ChunkVertexEdge = Mathf.Max(3, _stats.ChunkVertexEdge);
        int span = ChunkVertexEdge - 1;
        float faceArc = Mathf.Pi * 0.5f * spec.Radius;
        ChunksPerFaceEdge = Mathf.Max(1, Mathf.RoundToInt(faceArc / (Mathf.Max(_stats.VertexSpacing, 0.5f) * span)));

        Heights = new PlanetHeightfield(spec.Radius, spec.Amplitude, (ChunksPerFaceEdge * span) + 1, spec.DigFloorDepth);
        Heights.Generate(spec);
        foreach (PlanetSite site in Sites)
        {
            if (site != null && site.PadRadius > 0.0f)
            {
                Heights.FlattenPad(site.Direction, site.PadRadius, site.PadBlend);
            }
        }

        // Altitude bands are fractions of the relief, so the shader has to
        // know this planet's.
        if (TerrainMaterial is ShaderMaterial shader)
        {
            shader.SetShaderParameter("planet_radius", spec.Radius);
            shader.SetShaderParameter("terrain_amplitude", Mathf.Max(spec.Amplitude, 0.001f));
        }

        ClearChunks();
        BuildSharedIndices();
        AllocateScratch();
        CreateChunks();

        _farShell?.QueueFree();
        _farShell = new PlanetFarShell { Name = "FarShell" };
        AddChild(_farShell);
        _farShell.Build(Heights, _stats.FarShellVertexEdge, _stats.FarShellSink, TerrainMaterial);

        EnsureGravityField(spec.SurfaceGravity);
        if (!IsBuilt)
        {
            WorldUp.SetRadial();
        }

        PlaceSites();

        // Painted after the sites stand, because the sun's elevation is
        // measured over the first of them: the compound.
        biome?.Apply(seed, Sites.Count > 0 && Sites[0] != null ? Sites[0].Direction : Vector3.Up,
                     TerrainMaterial as ShaderMaterial, Environment?.Environment, Sun);

        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast -= OnAreaBlast;
            EventBus.Instance.AreaBlast += OnAreaBlast;
        }

        IsBuilt = true;
        LastBuildMilliseconds = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        GD.Print(System.FormattableString.Invariant(
            $"[planet] built: radius {spec.Radius:F0} m, relief {spec.Amplitude:F1} m, ridges {spec.RidgeShare:F2}, {spec.CraterCount} craters, {biome?.DisplayName ?? "authored look"}; {ChunkCount} chunks of {ChunkVertexEdge}², {Heights.VertexSpacing:F2} m spacing, noise seed {spec.Seed}, {LastBuildMilliseconds:F0} ms"));
        EmitSignal(SignalName.Built);
    }

    /// <summary>
    /// Stands every site on the ground, upright and keeping its heading, marks
    /// the chunks round any that keep their colliders, and builds the ground
    /// under each now.
    /// </summary>
    private void PlaceSites()
    {
        foreach (PlanetSite site in Sites)
        {
            if (site == null)
            {
                continue;
            }

            Vector3 up = site.Direction;
            Vector3 facing = -site.GlobalBasis.Z;
            site.GlobalTransform = new Transform3D(
                PlanetHeightfield.UprightBasis(up, facing),
                Heights.SurfacePoint(up) + (up * site.Lift));

            if (site.KeepColliderRadius > 0.0f)
            {
                float arc = site.KeepColliderRadius / Heights.Radius;
                for (int i = 0; i < _chunks.Length; i++)
                {
                    if (_chunks[i].CentreNormal.AngleTo(up) < arc + _chunks[i].AngularRadius)
                    {
                        _keepCollider[i] = true;
                    }
                }
            }

            BuildNow(up, Mathf.Max(1, _stats.CollisionRingChunks));
        }
    }

    /// <summary>Builds the chunks round a direction this frame, colliders included.</summary>
    public void BuildNow(Vector3 direction, int ring)
    {
        Vector3 d = direction.Normalized();
        for (int i = 0; i < _chunks.Length; i++)
        {
            PlanetChunk chunk = _chunks[i];
            if (chunk.CentreNormal.AngleTo(d) >= chunk.AngularRadius * (ring + 1.0f) && !_keepCollider[i])
            {
                continue;
            }
            if (_queued[i] || chunk.GeometryDirty)
            {
                chunk.RebuildMesh();
                _queued[i] = false;
            }
            if (!chunk.HasCollision)
            {
                chunk.RebuildCollider();
            }
        }
    }

    /// <summary>
    /// True once every chunk within the collision ring round a direction is
    /// built and carries its collider: what a player stood there needs.
    /// </summary>
    public bool GroundReady(Vector3 direction)
    {
        if (_chunks == null)
        {
            return false;
        }

        Vector3 d = direction.Normalized();
        for (int i = 0; i < _chunks.Length; i++)
        {
            PlanetChunk chunk = _chunks[i];
            if (chunk.CentreNormal.AngleTo(d) < chunk.AngularRadius * (_stats.CollisionRingChunks + 1.0f)
                && (_queued[i] || chunk.GeometryDirty || !chunk.HasCollision))
            {
                return false;
            }
        }
        return true;
    }

    private void ClearChunks()
    {
        if (_chunks != null)
        {
            foreach (PlanetChunk chunk in _chunks)
            {
                chunk?.QueueFree();
            }
        }
        _chunks = null;
        _buildQueue.Clear();
        _colliderQueue.Clear();
        _dirtyChunks.Clear();
        ResidentColliderCount = 0;
    }

    private void BuildSharedIndices()
    {
        // Identical for every chunk and never changes, so one copy is shared.
        _sharedIndices = GridIndices(ChunkVertexEdge, 1);

        // Coarser grids over the same vertices, for distance. Seen from 300 m
        // up the full-resolution chunks were 8 ms of GPU. Godot picks a level
        // per chunk by screen-space error; the key is that error in metres,
        // understated on purpose so a level arrives while its triangles are
        // still a pixel or two, well inside the fog. A crack between two
        // levels shows the far shell, a metre or two under, in the same
        // colours.
        _lodIndices = new Godot.Collections.Dictionary();
        float spacing = Heights.VertexSpacing;
        for (int stride = 2; stride < ChunkVertexEdge - 1; stride *= 2)
        {
            if ((ChunkVertexEdge - 1) % stride != 0)
            {
                break;
            }
            _lodIndices[spacing * stride * _stats.LodErrorScale] = GridIndices(ChunkVertexEdge, stride);
        }
    }

    /// <summary>The ChunkVertexEdge² grid's triangles, every <paramref name="stride"/>th vertex.</summary>
    private static int[] GridIndices(int c, int stride)
    {
        int quads = (c - 1) / stride;
        int[] indices = new int[quads * quads * 6];

        int write = 0;
        for (int j = 0; j < c - 1; j += stride)
        {
            for (int i = 0; i < c - 1; i += stride)
            {
                int v00 = (j * c) + i;
                int v10 = v00 + stride;
                int v01 = v00 + (c * stride);
                int v11 = v01 + stride;

                // Wound so the outward face is front-facing, given that
                // AxisA cross AxisB is the outward normal on every face.
                indices[write++] = v00;
                indices[write++] = v01;
                indices[write++] = v11;

                indices[write++] = v00;
                indices[write++] = v11;
                indices[write++] = v10;
            }
        }
        return indices;
    }

    private Godot.Collections.Dictionary _lodIndices;

    /// <summary>The coarser index grids for a chunk's mesh, keyed by error. Shared; never change.</summary>
    public Godot.Collections.Dictionary LodIndices => _lodIndices;

    private void AllocateScratch()
    {
        int c = ChunkVertexEdge;
        _paddedScratch = new Vector3[(c + 2) * (c + 2)];
        _vertexScratch = new Vector3[c * c];
        _normalScratch = new Vector3[c * c];
        _dugScratch = new Color[c * c];
    }

    private void CreateChunks()
    {
        int n = ChunksPerFaceEdge;
        _chunks = new PlanetChunk[CubeSphere.FaceCount * n * n];
        _queued = new bool[_chunks.Length];
        _keepCollider = new bool[_chunks.Length];
        _dirtyFlag = new bool[_chunks.Length];
        _collisionCosine = new float[_chunks.Length];
        _widestChunk = 0.0f;

        for (int f = 0; f < CubeSphere.FaceCount; f++)
        {
            for (int cv = 0; cv < n; cv++)
            {
                for (int cu = 0; cu < n; cu++)
                {
                    PlanetChunk chunk = new();
                    int index = ChunkIndex(f, cu, cv);
                    _chunks[index] = chunk;
                    AddChild(chunk);
                    chunk.Initialise(this, f, cu, cv, TerrainMaterial);
                    _buildQueue.Enqueue(index);
                    _queued[index] = true;
                    _collisionCosine[index] = Mathf.Cos(Mathf.Min(chunk.AngularRadius * (_stats.CollisionRingChunks + 1.0f), Mathf.Pi));
                    _widestChunk = Mathf.Max(_widestChunk, chunk.AngularRadius);
                }
            }
        }
    }

    /// <summary>
    /// The gravity well. An Area3D rather than anything custom, because the
    /// player, the vehicles and every rigid body already read
    /// <c>GetGravity()</c> and need no knowledge of a planet.
    /// </summary>
    private void EnsureGravityField(float gravity)
    {
        _gravityField?.QueueFree();
        _gravityField = new Area3D
        {
            Name = "Gravity",
            Priority = 1,
            GravitySpaceOverride = Area3D.SpaceOverride.Replace,
            GravityPoint = true,
            GravityPointUnitDistance = 0.0f,
            GravityPointCenter = Vector3.Zero,
            Gravity = gravity,

            // Layer 0, not the default World: hitscan queries collide with
            // areas on World, and a planet-sized field there would swallow
            // every shot fired inside it.
            CollisionLayer = 0,
            CollisionMask = GameLayers.Mask(GameLayers.Player, GameLayers.Enemy, GameLayers.Prop, GameLayers.Projectile),
            Monitorable = false,
        };
        _gravityField.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new SphereShape3D { Radius = Heights.Radius * _stats.GravityFieldMultiplier },
        });
        AddChild(_gravityField);
    }

    // ---------------- Chunk geometry ----------------

    public int ChunkIndex(int face, int chunkU, int chunkV) =>
        (((face * ChunksPerFaceEdge) + chunkV) * ChunksPerFaceEdge) + chunkU;

    /// <summary>Fills one chunk's arrays, in chunk-local space. See the overload with <c>dug</c>.</summary>
    public void BuildChunkArrays(PlanetChunk chunk, out Vector3[] vertices, out Vector3[] normals, out int[] indices) =>
        BuildChunkArrays(chunk, out vertices, out normals, out indices, out _);

    /// <summary>
    /// Fills vertex, normal and index arrays for one chunk, plus how far each
    /// vertex has been dug toward the floor as a vertex colour (red 1 on
    /// untouched ground, 0 at the floor; null when nothing in the chunk is
    /// dug, which the shader reads as white).
    ///
    /// Positions are computed on a one-vertex padded border so a chunk's edge
    /// normals are two-sided differences like every other vertex. At a face
    /// seam the padding falls outside the face, and CubeSphere.Direction
    /// extrapolates onto the neighbour, so lighting is continuous there.
    /// </summary>
    public void BuildChunkArrays(PlanetChunk chunk, out Vector3[] vertices, out Vector3[] normals, out int[] indices,
                                 out Color[] dug)
    {
        dug = null;
        int c = ChunkVertexEdge;
        int padded = c + 2;
        int span = c - 1;
        int edge = Heights.FaceVertexEdge;
        float inverse = 2.0f / (edge - 1);
        float radius = Heights.Radius;
        float floorDepth = Heights.DigFloorDepth;

        int baseU = chunk.ChunkU * span;
        int baseV = chunk.ChunkV * span;
        Vector3 origin = chunk.Centre;

        for (int j = -1; j <= c; j++)
        {
            int v = baseV + j;
            float b = (v * inverse) - 1.0f;
            for (int i = -1; i <= c; i++)
            {
                int u = baseU + i;
                float a = (u * inverse) - 1.0f;

                Vector3 direction = CubeSphere.Direction(chunk.Face, a, b);
                float height = u >= 0 && u < edge && v >= 0 && v < edge
                    ? Heights.HeightAt(Heights.VertexIndex(chunk.Face, u, v))
                    : Heights.SampleHeight(direction);

                _paddedScratch[((j + 1) * padded) + (i + 1)] = direction * (radius + height);
            }
        }

        for (int j = 0; j < c; j++)
        {
            for (int i = 0; i < c; i++)
            {
                int p = ((j + 1) * padded) + (i + 1);
                Vector3 alongA = _paddedScratch[p + 1] - _paddedScratch[p - 1];
                Vector3 alongB = _paddedScratch[p + padded] - _paddedScratch[p - padded];

                int k = (j * c) + i;
                _vertexScratch[k] = _paddedScratch[p] - origin;

                if (floorDepth > 0.0f)
                {
                    float depth = Heights.DugAt(Heights.VertexIndex(chunk.Face, baseU + i, baseV + j));
                    if (depth > 0.5f)
                    {
                        if (dug == null)
                        {
                            System.Array.Fill(_dugScratch, Colors.White);
                            dug = _dugScratch;
                        }
                        float share = Mathf.Clamp(depth / floorDepth, 0.0f, 1.0f);
                        dug[k] = new Color(1.0f - share, 1.0f, 1.0f, 1.0f);
                    }
                }

                // Oriented against the outward radial rather than trusting the
                // cross product's sign: an inward normal leaves a chunk lit by
                // ambient alone.
                Vector3 normal = alongA.Cross(alongB).Normalized();
                _normalScratch[k] = normal.Dot(_paddedScratch[p]) < 0.0f ? -normal : normal;
            }
        }

        vertices = _vertexScratch;
        normals = _normalScratch;
        indices = _sharedIndices;
    }

    // ---------------- Per-frame work ----------------

    public override void _Process(double delta)
    {
        if (!IsBuilt)
        {
            return;
        }

        Camera3D camera = GetViewport().GetCamera3D();
        Vector3 viewer = camera?.GlobalPosition ?? Vector3.Up * (Heights.Radius + 2.0f);
        UpdateStreaming(viewer);
        ProcessBuildQueue(_stats.MaxChunkBuildsPerFrame, _stats.ChunkBuildBudgetMs);
    }

    /// <summary>
    /// Drains queued chunk builds a few a frame, within a time budget after
    /// the first, which always runs so the queue cannot stall. Stale
    /// colliders first: the mesh that made them stale is already on screen.
    /// </summary>
    private void ProcessBuildQueue(int maxBuilds, float budgetMilliseconds)
    {
        if (_buildQueue.Count == 0 && _colliderQueue.Count == 0)
        {
            return;
        }

        using FrameProfiler.Scope scope = FrameProfiler.Begin(_sectionRebuild);
        long start = Stopwatch.GetTimestamp();
        long budget = budgetMilliseconds > 0.0f
            ? (long)(budgetMilliseconds * Stopwatch.Frequency / 1000.0)
            : long.MaxValue;

        int built = 0;
        while (built < maxBuilds && (_buildQueue.Count > 0 || _colliderQueue.Count > 0))
        {
            if (built > 0 && Stopwatch.GetTimestamp() - start >= budget)
            {
                break;
            }

            if (_colliderQueue.Count > 0)
            {
                PlanetChunk stale = _chunks[_colliderQueue.Dequeue()];
                if (stale.ColliderStale && stale.HasCollision)
                {
                    stale.RebuildCollider();
                }
                stale.ColliderStale = false;
                built++;
                continue;
            }

            int index = _buildQueue.Dequeue();
            if (!_queued[index])
            {
                // Built out of turn by BuildNow.
                continue;
            }
            _queued[index] = false;
            _chunks[index].RebuildMesh();
            if (_chunks[index].ColliderStale)
            {
                _colliderQueue.Enqueue(index);
            }
            built++;
        }
    }

    /// <summary>
    /// Keeps a ring of colliders round the viewer and draws only the chunks
    /// that are over neither the horizon nor the near draw distance; the far
    /// shell covers the rest.
    ///
    /// The horizon cull matters more than it looks: at eye height the horizon
    /// is tens of metres away, almost every chunk is behind the curve, and the
    /// engine's frustum culler cannot know that.
    /// </summary>
    private void UpdateStreaming(Vector3 viewer)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(_sectionStream);
        float radius = Heights.Radius;
        Vector3 viewerDirection = viewer.LengthSquared() > 1e-6f ? viewer.Normalized() : Vector3.Up;
        float viewerHeight = Heights.AltitudeOf(viewer);

        CubeSphere.Locate(viewerDirection, out int viewerFace, out float a, out float b);
        int n = ChunksPerFaceEdge;
        int span = ChunkVertexEdge - 1;
        int edge = Heights.FaceVertexEdge;
        int viewerU = Mathf.Clamp((int)((a + 1.0f) * 0.5f * (edge - 1)) / span, 0, n - 1);
        int viewerV = Mathf.Clamp((int)((b + 1.0f) * 0.5f * (edge - 1)) / span, 0, n - 1);
        int ring = _stats.CollisionRingChunks;

        // How far over the curve a chunk centre can be and still show a
        // corner: the viewer's own horizon plus the relief's, plus the widest
        // chunk. Compared as cosines against one dot product a chunk: this
        // walks every chunk every frame, and two arccosines each were most of
        // what it cost (0.6-0.9 ms at 1,176 chunks).
        float eyeHorizon = Mathf.Acos(radius / (radius + Mathf.Max(viewerHeight, 0.1f)));
        float reliefHorizon = Mathf.Acos(radius / (radius + Mathf.Max(Heights.Amplitude, 1.0f)));
        float showCosine = Mathf.Cos(Mathf.Min(eyeHorizon + reliefHorizon + _widestChunk, Mathf.Pi));
        float draw = _stats.NearDrawDistance + (_widestChunk * radius);
        float drawSquared = draw * draw;

        int resident = 0;
        int visible = 0;

        for (int i = 0; i < _chunks.Length; i++)
        {
            PlanetChunk chunk = _chunks[i];
            float along = chunk.CentreNormal.Dot(viewerDirection);

            bool near = _keepCollider[i]
                        || (chunk.Face == viewerFace
                            && Mathf.Abs(chunk.ChunkU - viewerU) <= ring
                            && Mathf.Abs(chunk.ChunkV - viewerV) <= ring)
                        // Chunks on another face still collide near a seam.
                        || along > _collisionCosine[i];

            if (near)
            {
                if (!chunk.HasCollision && !chunk.GeometryDirty)
                {
                    chunk.RebuildCollider();
                }
                if (chunk.HasCollision)
                {
                    resident++;
                }
            }
            else if (chunk.HasCollision)
            {
                chunk.ReleaseCollider();
            }

            bool show = along > showCosine && chunk.Centre.DistanceSquaredTo(viewer) < drawSquared;
            chunk.SetMeshVisible(show);
            if (show)
            {
                visible++;
            }
        }

        ResidentColliderCount = resident;
        VisibleChunkCount = visible;
    }

    // ---------------- Craters ----------------

    /// <summary>
    /// A blast that names a crater digs it; one that names none digs one
    /// sized from its own radius, if it is big enough. Applied by 3D distance,
    /// so an air burst or a hit high on a building leaves the ground alone.
    /// </summary>
    private void OnAreaBlast(BlastInfo blast)
    {
        if (!IsBuilt || blast == null || blast.Sustained)
        {
            return;
        }

        if (blast.CraterRadius > 0.0f)
        {
            Crater(blast.Centre, blast.CraterRadius, blast.CraterDepth);
        }
        else if (!blast.NoCrater && _stats.BlastCraterScale > 0.0f && blast.Radius >= _stats.BlastCraterMinRadius)
        {
            float radius = blast.Radius * _stats.BlastCraterScale;
            Crater(blast.Centre, radius, radius * _stats.BlastCraterDepthRatio);
        }
    }

    /// <summary>
    /// Digs a crater, queues every chunk it touched, and restamps the horde's
    /// slopes round it so the crowd routes round the new hole.
    /// </summary>
    public void Crater(Vector3 centre, float radius, float depth)
    {
        if (!IsBuilt || radius <= 0.0f)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp();
        using (FrameProfiler.Begin(_sectionCrater))
        {
            float share = Mathf.Clamp(depth / Mathf.Max(_stats.CraterReferenceDepth, 0.01f), 0.0f, 1.0f);
            _dirtyChunks.Clear();
            System.Array.Clear(_dirtyFlag, 0, _dirtyFlag.Length);

            Heights.ApplyCrater(centre, radius, depth, _stats.CraterRimHeight * share, _stats.CraterRimSharpness,
                                MarkChunksDirty);

            foreach (int index in _dirtyChunks)
            {
                _chunks[index].GeometryDirty = true;
                if (!_queued[index])
                {
                    _queued[index] = true;
                    _buildQueue.Enqueue(index);
                }
            }

            radius = Mathf.Max(radius, Heights.LastCraterRadius);
            if (_hordeGround != null && _hordeFlow != null)
            {
                _hordeGround.StampSlope(_hordeFlow, _stats.MaxSlopeDegrees, centre, radius * 1.5f);
            }
        }
        LastCraterMilliseconds = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        EmitSignal(SignalName.TerrainDeformed, centre, radius);
    }

    /// <summary>
    /// A vertex on a chunk boundary belongs to up to four chunks, all of which
    /// have to be rebuilt or a crack opens along the shared edge.
    /// </summary>
    private void MarkChunksDirty(int face, int u, int v)
    {
        int span = ChunkVertexEdge - 1;
        int n = ChunksPerFaceEdge;

        int cu0 = u == 0 ? 0 : Mathf.Min((u - 1) / span, n - 1);
        int cu1 = Mathf.Min(u / span, n - 1);
        int cv0 = v == 0 ? 0 : Mathf.Min((v - 1) / span, n - 1);
        int cv1 = Mathf.Min(v / span, n - 1);

        for (int cv = cv0; cv <= cv1; cv++)
        {
            for (int cu = cu0; cu <= cu1; cu++)
            {
                int index = ChunkIndex(face, cu, cv);
                if (!_dirtyFlag[index])
                {
                    _dirtyFlag[index] = true;
                    _dirtyChunks.Add(index);
                }
            }
        }
    }

    // ---------------- The horde's ground ----------------

    public bool HordeGroundReady => IsBuilt;

    public IHordeGround CreateHordeGround()
    {
        _hordeGround = new PlanetGround(Heights, _stats.FlowCellSize);
        return _hordeGround;
    }

    public int PrepareFlow(FlowField flow, PhysicsDirectSpaceState3D space, Vector3 garrison)
    {
        _hordeFlow = flow;
        _hordeGround.StampSlope(flow, _stats.MaxSlopeDegrees);

        var exclude = new Godot.Collections.Array<Rid>();
        foreach (PlanetChunk chunk in _chunks)
        {
            if (chunk.HasCollision)
            {
                exclude.Add(chunk.GetRid());
            }
        }
        return _hordeGround.StampObstacles(space, flow, FlatGround.ObstacleMask, garrison, _stats.ObstacleStampRadius,
                                           exclude);
    }

    /// <summary>A point <paramref name="altitude"/> metres over the ground in a direction, for <c>bench.gd</c>'s <c>@lift</c>.</summary>
    public Vector3 PointAbove(Vector3 direction, float altitude)
    {
        Vector3 unit = direction.Normalized();
        return Heights.SurfacePoint(unit) + (unit * altitude);
    }

    /// <summary>One line of the planet's state, for <c>bench.gd</c>.</summary>
    public string BenchStatus() => System.FormattableString.Invariant(
        $"chunks drawn={VisibleChunkCount}/{ChunkCount} colliders={ResidentColliderCount} queued={PendingBuildCount} crater={LastCraterMilliseconds:F1}ms");
}
