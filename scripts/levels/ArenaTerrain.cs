using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using DillControl.Core;
using DillControl.Horde;
using DillControl.Planet;

namespace DillControl.Levels;

/// <summary>
/// The ground of an arena: a square of terrain, built from a little rolling
/// noise, a rim of hills round the edge and the <see cref="TerrainFeature"/>
/// children placed by hand. A tool script, so the ground is there in the
/// editor to place trees and props on, and it follows a feature that is
/// dragged.
///
/// Not the planet (decision 0026). The planet's ground exists only once the
/// game runs, so nothing could be dressed on it by hand; this is flat-world,
/// up is +Y, and the planet stays as it is for later.
///
/// Its own position is the middle of the square and the height of zero. It
/// is never rotated or scaled. It also makes the horde's ground, so a
/// <see cref="HordeDirector"/> names it as its provider.
/// </summary>
[Tool]
[GlobalClass]
public partial class ArenaTerrain : Node3D, IHordeGroundProvider
{
    private ArenaTerrainSpec _spec;
    private Material _material;

    [Export]
    public ArenaTerrainSpec Spec
    {
        get => _spec;
        set { _spec = value; MarkDirty(); }
    }

    /// <summary><c>mat_arena_terrain.tres</c>: the planet's terrain shader, told the ground is flat.</summary>
    [Export]
    public Material Material
    {
        get => _material;
        set { _material = value; MarkDirty(); }
    }

    /// <summary>
    /// The branches whose children the button below stands on the ground:
    /// Foliage, Props, Spawns. Only direct children, each by its own origin.
    /// </summary>
    [Export]
    public Godot.Collections.Array<Node3D> GroundedRoots { get; set; } = new();

    [ExportToolButton("Rebuild the ground")]
    public Callable RebuildButton => Callable.From(Build);

    /// <summary>Moves every child of <see cref="GroundedRoots"/> up or down onto the ground. No undo: save first.</summary>
    [ExportToolButton("Stand children on the ground")]
    public Callable StandButton => Callable.From(StandChildrenOnGround);

    public ArenaHeights Heights { get; private set; }

    public bool HordeGroundReady => Heights != null && _chunks.Count > 0;

    private readonly List<ArenaChunk> _chunks = new();
    private ArenaGround _ground;
    private bool _dirty;
    private float _dirtyTimer;

    public override void _Ready()
    {
        Build();
        SetProcess(Engine.IsEditorHint());
    }

    /// <summary>Something that shapes the ground changed; rebuilt shortly, once dragging stops.</summary>
    public void MarkDirty()
    {
        _dirty = true;
        _dirtyTimer = 0.0f;
    }

    public override void _Process(double delta)
    {
        if (!_dirty)
        {
            return;
        }
        _dirtyTimer += (float)delta;
        if (_dirtyTimer >= 0.25f)
        {
            Build();
        }
    }

    // ---------------- Building ----------------

    public void Build()
    {
        _dirty = false;
        if (Spec == null || !IsInsideTree())
        {
            return;
        }

        ulong started = Time.GetTicksUsec();
        Heights = new ArenaHeights(GlobalPosition, Spec.Size, Spec.Spacing);
        WriteHeights(Heights);
        Heights.Measure();
        BuildChunks(withCollision: !Engine.IsEditorHint());

        if (!Engine.IsEditorHint())
        {
            GD.Print(System.FormattableString.Invariant(
                $"[arena] ground: {Heights.Count}x{Heights.Count} samples of {Heights.Spacing:F1} m, {_chunks.Count} chunks, {Heights.Lowest:F0} to {Heights.Highest:F0} m, {(Time.GetTicksUsec() - started) / 1000.0:F1} ms"));
        }
    }

    private readonly struct Feature
    {
        public readonly TerrainFeature.Shape Kind;
        public readonly Vector2 Centre;
        public readonly Vector2 Axis;
        public readonly float Level;
        public readonly float Radius;
        public readonly float Height;
        public readonly float Edge;
        public readonly float HalfLength;
        public readonly float Roughness;

        public Feature(TerrainFeature node, Vector3 origin)
        {
            Kind = node.Kind;
            Vector3 at = node.GlobalPosition - origin;
            Centre = new Vector2(at.X, at.Z);
            Vector3 x = node.GlobalBasis.X;
            Axis = new Vector2(x.X, x.Z).LengthSquared() > 1e-6f ? new Vector2(x.X, x.Z).Normalized() : Vector2.Right;
            Level = at.Y;
            Radius = Mathf.Max(node.Radius, 0.5f);
            Height = node.Height;
            Edge = Mathf.Max(node.Edge, 0.5f);
            HalfLength = node.Length * 0.5f;
            Roughness = node.Roughness;
        }
    }

    private void WriteHeights(ArenaHeights heights)
    {
        ArenaTerrainSpec spec = Spec;
        var shapes = new List<Feature>();
        var pads = new List<Feature>();
        foreach (Node child in GetChildren())
        {
            if (child is TerrainFeature feature && feature.Visible)
            {
                (feature.Kind == TerrainFeature.Shape.Pad ? pads : shapes).Add(new Feature(feature, GlobalPosition));
            }
        }

        var noise = new PlanetNoise(SeededRandom.Stream((ulong)spec.Seed, "arena-terrain").NextSeed());
        var outline = new PlanetNoise(SeededRandom.Stream((ulong)spec.Seed, "arena-outline").NextSeed());
        float frequency = 1.0f / Mathf.Max(spec.NoiseScale, 1.0f);
        float half = heights.Size * 0.5f;
        int count = heights.Count;

        Parallel.For(0, count, z =>
        {
            for (int x = 0; x < count; x++)
            {
                float px = (x * heights.Spacing) - half;
                float pz = (z * heights.Spacing) - half;
                var p = new Vector3(px, 0.37f, pz);

                float rolling = noise.Fractal(p, frequency, spec.NoiseOctaves, 2.0f, 0.5f);
                float ridged = noise.Ridged(p + new Vector3(91.0f, 0.0f, -53.0f), frequency, spec.NoiseOctaves, 2.0f, 0.5f);
                float h = spec.NoiseAmplitude * Mathf.Lerp(rolling, ridged, spec.RidgeShare);

                float toEdge = half - Mathf.Max(Mathf.Abs(px), Mathf.Abs(pz));
                if (spec.RimWidth > 0.0f && toEdge < spec.RimWidth)
                {
                    float t = 1.0f - (toEdge / spec.RimWidth);
                    float wobble = 0.75f + (0.5f * outline.Fractal(p * 3.0f, frequency, 2, 2.0f, 0.5f));
                    h += spec.RimHeight * wobble * t * t * (3.0f - (2.0f * t));
                }

                var at = new Vector2(px, pz);
                foreach (Feature f in shapes)
                {
                    h += Shape(f, at, outline);
                }
                foreach (Feature f in pads)
                {
                    float d = at.DistanceTo(f.Centre);
                    float w = 1.0f - Smooth((d - f.Radius) / f.Edge);
                    h = Mathf.Lerp(h, f.Level, w);
                }

                heights.Heights[(z * count) + x] = h;
            }
        });
    }

    private static float Shape(in Feature f, Vector2 at, PlanetNoise outline)
    {
        Vector2 offset = at - f.Centre;
        float d;
        if (f.Kind == TerrainFeature.Shape.Ridge)
        {
            float along = Mathf.Clamp(offset.Dot(f.Axis), -f.HalfLength, f.HalfLength);
            d = (offset - (f.Axis * along)).Length();
        }
        else
        {
            d = offset.Length();
        }

        if (d > f.Radius * (1.0f + f.Roughness) + f.Edge)
        {
            return 0.0f;
        }

        // The outline wanders in and out round the shape, so a mesa is a
        // butte and not a drum.
        if (f.Roughness > 0.0f)
        {
            var p = new Vector3(at.X, 11.3f, at.Y);
            d /= 1.0f + (f.Roughness * outline.Fractal(p, 1.0f / Mathf.Max(f.Radius * 0.6f, 4.0f), 3, 2.0f, 0.5f));
        }

        switch (f.Kind)
        {
            case TerrainFeature.Shape.Mesa:
                // Flat to the cliff, then down over Edge metres, with a
                // little scree at the foot.
                float cliff = 1.0f - Smooth((d - (f.Radius - f.Edge)) / f.Edge);
                float scree = 1.0f - Smooth((d - f.Radius) / (f.Edge * 1.5f));
                return f.Height * ((cliff * 0.92f) + (scree * 0.08f));
            case TerrainFeature.Shape.Basin:
                return -f.Height * Bump(d / f.Radius);
            default:
                return f.Height * Bump(d / f.Radius);
        }
    }

    /// <summary>1 at 0, 0 from 1 on, smooth at both ends.</summary>
    private static float Bump(float t) => t >= 1.0f ? 0.0f : 0.5f + (0.5f * Mathf.Cos(Mathf.Max(t, 0.0f) * Mathf.Pi));

    /// <summary>0 below 0, 1 above 1, smoothstep between.</summary>
    private static float Smooth(float t)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        return t * t * (3.0f - (2.0f * t));
    }

    private void BuildChunks(bool withCollision)
    {
        foreach (ArenaChunk chunk in _chunks)
        {
            chunk.QueueFree();
        }
        _chunks.Clear();

        ArenaHeights heights = Heights;
        int cells = heights.Count - 1;
        int perChunk = Mathf.Max(1, Mathf.CeilToInt(cells / (float)Mathf.Max(Spec.Chunks, 1)));
        Vector3 origin = GlobalPosition;

        for (int cz = 0; cz * perChunk < cells; cz++)
        {
            for (int cx = 0; cx * perChunk < cells; cx++)
            {
                int x0 = cx * perChunk;
                int z0 = cz * perChunk;
                int x1 = Mathf.Min(x0 + perChunk, cells);
                int z1 = Mathf.Min(z0 + perChunk, cells);
                _chunks.Add(BuildChunk(heights, origin, x0, z0, x1, z1, withCollision));
            }
        }
    }

    private ArenaChunk BuildChunk(ArenaHeights heights, Vector3 origin, int x0, int z0, int x1, int z1, bool withCollision)
    {
        int w = x1 - x0 + 1;
        int d = z1 - z0 + 1;
        var vertices = new Vector3[w * d];
        var normals = new Vector3[w * d];
        for (int z = 0; z < d; z++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector3 point = heights.SamplePoint(x0 + x, z0 + z);
                vertices[(z * w) + x] = point - origin;
                normals[(z * w) + x] = heights.NormalAt(point.X, point.Z, heights.Spacing);
            }
        }

        var indices = new int[(w - 1) * (d - 1) * 6];
        int n = 0;
        for (int z = 0; z < d - 1; z++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                int a = (z * w) + x;
                int b = a + 1;
                int c = a + w;
                int e = c + 1;
                indices[n++] = a;
                indices[n++] = b;
                indices[n++] = c;
                indices[n++] = b;
                indices[n++] = e;
                indices[n++] = c;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (Material != null)
        {
            mesh.SurfaceSetMaterial(0, Material);
        }

        var chunk = new ArenaChunk
        {
            Name = $"Chunk_{x0}_{z0}",
            CollisionLayer = GameLayers.Mask(GameLayers.World),
            CollisionMask = 0,
        };
        chunk.AddChild(new MeshInstance3D { Name = "Mesh", Mesh = mesh });

        if (withCollision)
        {
            var faces = new Vector3[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                faces[i] = vertices[indices[i]];
            }
            var shape = new ConcavePolygonShape3D();
            shape.SetFaces(faces);
            chunk.AddChild(new CollisionShape3D { Name = "Shape", Shape = shape });
        }

        // No owner: generated, never saved into the scene.
        AddChild(chunk, false, InternalMode.Back);
        return chunk;
    }

    // ---------------- In the editor ----------------

    private void StandChildrenOnGround()
    {
        if (Heights == null)
        {
            Build();
        }
        if (Heights == null)
        {
            return;
        }

        int moved = 0;
        foreach (Node3D root in GroundedRoots)
        {
            if (root == null)
            {
                continue;
            }
            foreach (Node child in root.GetChildren())
            {
                if (child is Node3D node)
                {
                    Vector3 at = node.GlobalPosition;
                    node.GlobalPosition = new Vector3(at.X, Heights.HeightAt(at.X, at.Z), at.Z);
                    moved++;
                }
            }
        }
        GD.Print($"[arena] stood {moved} nodes on the ground.");
    }

    // ---------------- The horde's ground ----------------

    public IHordeGround CreateHordeGround()
    {
        _ground = new ArenaGround(Heights, Spec.FlowCellSize, Spec.MaxSlopeDegrees);
        return _ground;
    }

    public int PrepareFlow(FlowField flow, PhysicsDirectSpaceState3D space, Vector3 garrison)
    {
        _ground.StampSlope(flow);
        var exclude = new Godot.Collections.Array<Rid>();
        foreach (ArenaChunk chunk in _chunks)
        {
            exclude.Add(chunk.GetRid());
        }
        return _ground.StampObstacles(space, flow, FlatGround.ObstacleMask, exclude);
    }
}
