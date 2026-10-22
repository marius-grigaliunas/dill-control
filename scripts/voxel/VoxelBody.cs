using System;
using System.Collections.Generic;
using DillControl.Core;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// Makes an imported voxel model destructible. Put one in the wrapper scene
/// beside the model; at start it turns every part the importer gave voxels to
/// into a <see cref="VoxelPart"/>, and from then on the model breaks the way
/// every voxel model in the game breaks (docs/decisions/0011).
///
/// - <see cref="Static"/>: a building or a prop. Its parts hang from the
///   ground and get box collision of their own, a body per section, on World
///   and Prop, that follows every hole; the root needs no collision of its own.
/// - Otherwise: something that moves on a body of its own (a mech on its
///   hull). Parts listed in <see cref="Hinged"/> hang from their hinge, the
///   rest hold together as their biggest piece. Hits reach it through the
///   owner, which forwards <see cref="IDamageable"/> here.
/// </summary>
public partial class VoxelBody : Node, IDamageable, IBlastSurface
{
    [Export]
    public Node3D Model { get; set; }

    [Export]
    public VoxelDestructionStats Stats { get; set; }

    /// <summary>A building: anchored to the ground, collision of its own.</summary>
    [Export]
    public bool Static { get; set; }

    /// <summary>
    /// A static thing drawn as its imported model, one mesh, until anything
    /// about it changes; only then are its sections meshed (on the workers)
    /// and the model put away once they are drawn. Intact, it looks the same
    /// for one draw instead of dozens, and a forest of them loads in a
    /// fraction of the time (docs/decisions/0032).
    /// </summary>
    [Export]
    public bool WholeUntilChanged { get; set; }

    /// <summary>Parts that hang from their pivot: legs, a turret, arms.</summary>
    [Export]
    public Godot.Collections.Array<Node3D> Hinged { get; set; } = new();

    /// <summary>Where thrown-off pieces get their speed. Defaults to still.</summary>
    public Func<Vector3> Velocity { get; set; } = () => Vector3.Zero;

    /// <summary>Raised when a part has nothing left or nothing holds it.</summary>
    public event Action<Node3D, VoxelPart> PartEmptied;

    public IReadOnlyList<VoxelPart> Parts => _parts;

    /// <summary>What draws it while it is whole: the model's meshes and their
    /// coarse copies. Empty once its sections have taken over, or if it never
    /// drew whole. A plant gives them its bending material.</summary>
    public IReadOnlyList<MeshInstance3D> WholeDrawn => _whole;

    private readonly List<MeshInstance3D> _whole = new();

    private readonly List<VoxelPart> _parts = new();
    private readonly Dictionary<Node3D, VoxelPart> _byNode = new();

    public VoxelPart PartOf(Node3D node) => node != null && _byNode.TryGetValue(node, out VoxelPart part) ? part : null;

    public bool IsAlive
    {
        get
        {
            foreach (VoxelPart part in _parts)
            {
                if (!part.IsEmpty)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public override void _Ready()
    {
        Stats ??= GD.Load<VoxelDestructionStats>(VoxelDestructionStats.DefaultPath);
        if (Model == null || !Model.HasMeta(VoxelMeta.VoxelSize))
        {
            GD.PushWarning($"VoxelBody '{Name}': its model was not imported with voxels; reimport the .vox.");
            return;
        }

        float voxelSize = Model.GetMeta(VoxelMeta.VoxelSize).AsSingle();
        Color[] palette = Model.GetMeta(VoxelMeta.Palette).AsColorArray();
        float[] emission = Model.GetMeta(VoxelMeta.Emission).AsFloat32Array();
        var hinged = new HashSet<Node3D>();
        foreach (Node3D node in Hinged)
        {
            hinged.Add(node);
        }

        foreach (MeshInstance3D host in Hosts(Model))
        {
            var grid = new VoxelGrid(
                host.GetMeta(VoxelMeta.Dims).AsVector3I(),
                (byte[])host.GetMeta(VoxelMeta.Cells).AsByteArray().Clone(),
                host.GetMeta(VoxelMeta.Origin).AsVector3(),
                voxelSize);
            Material material = host.Mesh?.SurfaceGetMaterial(0) ?? host.GetSurfaceOverrideMaterial(0);
            bool whole = WholeUntilChanged && Static && host.Mesh != null;
            if (!whole)
            {
                host.Mesh = null;
            }

            VoxelAnchor anchor = Static ? VoxelAnchor.Ground : hinged.Contains(host) ? VoxelAnchor.Hinge : VoxelAnchor.Largest;
            var part = new VoxelPart { Name = "Voxels", Velocity = () => Velocity(), DeferMeshes = whole };
            part.Setup(grid, anchor, palette, emission, material, Stats);
            if (whole)
            {
                List<MeshInstance3D> drawn = DrawWhole(host, grid, palette, emission, material);
                part.Drawn += () =>
                {
                    foreach (MeshInstance3D mesh in drawn)
                    {
                        _whole.Remove(mesh);
                        if (!IsInstanceValid(mesh))
                        {
                            continue;
                        }
                        if (mesh == host)
                        {
                            mesh.Mesh = null;
                            mesh.VisibilityRangeEnd = 0.0f;
                        }
                        else
                        {
                            mesh.QueueFree();
                        }
                    }
                };
            }
            if (Static)
            {
                VoxelChunkGraph graph = VoxelChunkGraph.TryRead(host, grid.Cells.Length, host.Name);
                if (graph != null)
                {
                    part.UseGraph(graph);
                }
            }

            if (Static)
            {
                part.SectionBody = () => new VoxelCollider
                {
                    Body = this,
                    CollisionLayer = GameLayers.Mask(GameLayers.World, GameLayers.Structure),
                    CollisionMask = 0,
                };
            }

            host.AddChild(part);
            part.Emptied += p => PartEmptied?.Invoke(host, p);
            _parts.Add(part);
            _byNode[host] = part;
        }
    }

    // Coarse copies of a model, by the model's mesh and the block size: every
    // instance of one vine shares them.
    private static readonly Dictionary<(Mesh, int), ArrayMesh> Coarse = new();

    /// <summary>
    /// The model as imported near, and coarse copies of its voxels further
    /// off (<see cref="VoxelDestructionStats.CoarseFromMetres"/>), each drawn
    /// only in its own band of distance. Returns all of them.
    /// </summary>
    private List<MeshInstance3D> DrawWhole(MeshInstance3D host, VoxelGrid grid, Color[] palette, float[] emission,
        Material material)
    {
        var drawn = new List<MeshInstance3D> { host };
        float coarse = Stats.CoarseFromMetres, coarsest = Stats.CoarsestFromMetres;
        if (coarse > 0.0f)
        {
            host.VisibilityRangeEnd = coarse;
            host.VisibilityRangeEndMargin = 4.0f;
            float until = coarsest > coarse ? coarsest : 0.0f;
            AddCoarse(host, grid, palette, emission, material, 2, coarse, until, Stats.CoarseShadows, drawn);
            if (until > 0.0f)
            {
                AddCoarse(host, grid, palette, emission, material, 4, until, 0.0f, false, drawn);
            }
        }
        _whole.AddRange(drawn);
        return drawn;
    }

    private static void AddCoarse(MeshInstance3D host, VoxelGrid grid, Color[] palette, float[] emission,
        Material material, int block, float from, float until, bool shadows, List<MeshInstance3D> drawn)
    {
        if (!Coarse.TryGetValue((host.Mesh, block), out ArrayMesh mesh))
        {
            mesh = Coarsen(grid, palette, emission, material, block);
            Coarse[(host.Mesh, block)] = mesh;
        }
        if (mesh == null)
        {
            return;
        }
        var instance = new MeshInstance3D
        {
            Name = $"Coarse{block}",
            Mesh = mesh,
            VisibilityRangeBegin = from,
            VisibilityRangeBeginMargin = 4.0f,
            VisibilityRangeEnd = until,
            VisibilityRangeEndMargin = until > 0.0f ? 4.0f : 0.0f,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        host.AddChild(instance);
        drawn.Add(instance);
    }

    /// <summary>
    /// The grid in blocks of <paramref name="block"/> cubed: solid where any
    /// cell is (a one-voxel leaf survives, a little fatter), coloured as most
    /// of its cells are, meshed as any grid.
    /// </summary>
    private static ArrayMesh Coarsen(VoxelGrid grid, Color[] palette, float[] emission, Material material, int block)
    {
        var size = new Vector3I((grid.Size.X + block - 1) / block, (grid.Size.Y + block - 1) / block,
            (grid.Size.Z + block - 1) / block);
        var cells = new byte[size.X * size.Y * size.Z];
        var counts = new int[256];
        for (int z = 0; z < size.Z; z++)
            for (int y = 0; y < size.Y; y++)
                for (int x = 0; x < size.X; x++)
                {
                    Array.Clear(counts);
                    int best = 0;
                    byte colour = 0;
                    for (int dz = 0; dz < block; dz++)
                        for (int dy = 0; dy < block; dy++)
                            for (int dx = 0; dx < block; dx++)
                            {
                                byte c = grid.At((x * block) + dx, (y * block) + dy, (z * block) + dz);
                                if (c != 0 && ++counts[c] > best)
                                {
                                    best = counts[c];
                                    colour = c;
                                }
                            }
                    cells[x + (size.X * (y + (size.Y * z)))] = colour;
                }
        VoxMesher.Arrays arrays = VoxMesher.BuildArrays(cells, size, Vector3I.Zero, size, palette, emission,
            grid.Origin / block, grid.VoxelSize * block, -1);
        if (arrays?.Vertices == null)
        {
            return null;
        }
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays.ToGodot());
        arrays.Release();
        mesh.SurfaceSetMaterial(0, material);
        if (OS.IsDebugBuild())
        {
            int indices = mesh.SurfaceGetArrayIndexLen(0);
            GD.Print($"[voxel] coarse {block}x: {(indices > 0 ? indices : mesh.SurfaceGetArrayLen(0)) / 3} triangles, from {grid.SolidCount} voxels");
        }
        return mesh;
    }

    private static IEnumerable<MeshInstance3D> Hosts(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.HasMeta(VoxelMeta.Cells))
            {
                yield return mesh;
            }
            foreach (MeshInstance3D nested in Hosts(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Raised for every hit it takes, before the damage is worked out:
    /// a plant shakes (PlantFlex).</summary>
    public event Action<DamageInfo> Damaged;

    /// <summary>
    /// Set by a plant that bends (PlantFlex): its collision is moved with the
    /// bend, so what reaches it is taken back to where its voxels are.
    /// </summary>
    public IVoxelBend Bend { get; set; }

    /// <summary>A point where it is drawn, where its voxels are.</summary>
    public Vector3 Built(Vector3 point) => Bend?.ToBuilt(point) ?? point;

    /// <summary>
    /// Asked about every hit before its voxels are: true when the hit was taken
    /// by something that is not voxels, which then bites nothing. A plant's
    /// leaves (<see cref="Foliage.VineLeaves"/>).
    /// </summary>
    public Func<DamageInfo, bool> Intercept { get; set; }

    public void ApplyDamage(DamageInfo info, Node3D target)
    {
        Damaged?.Invoke(info);
        if (Intercept != null && Intercept(info))
        {
            return;
        }
        if (Bend == null)
        {
            VoxelDamage.Apply(info, _parts, Stats, target);
            return;
        }
        // Bitten where its voxels are, then told back where it was hit.
        Vector3 drawn = info.Position;
        info.Position = Bend.ToBuilt(drawn);
        VoxelDamage.Apply(info, _parts, Stats, target);
        info.Position = drawn;
    }

    /// <summary>A building's damage, from any of its sections' colliders:
    /// taken once, as the building.</summary>
    public void ApplyDamage(DamageInfo info) => ApplyDamage(info, Model);

    /// <summary>A piece crushing through has already taken its slab out of
    /// whatever building it landed on.</summary>
    public bool Ignores(Node3D source) => source is VoxelChunk { CrushingThrough: true };

    public Vector3 NearestBlastPoint(Vector3 from)
    {
        Vector3 nearest = VoxelDamage.NearestPoint(_parts, Built(from), Model?.GlobalPosition ?? Vector3.Zero);
        return Bend?.ToDrawn(nearest) ?? nearest;
    }

    /// <summary>Something heavy has landed on it: a slab under the contact goes
    /// to dust (<see cref="VoxelPart.CrushSlab"/>). Returns the voxels removed.</summary>
    public int Crush(Vector3 point, Vector3 direction, float depth, float radius, List<byte> colours = null)
    {
        int crushed = 0;
        foreach (VoxelPart part in _parts)
        {
            crushed += part.CrushSlab(Built(point), direction, depth, radius, out _, colours);
        }
        return crushed;
    }

    /// <summary>For <c>bench.gd</c>: a 1 m slit clean through at 2 m up, as a
    /// beam swept all the way round would leave.</summary>
    public void BenchCutThrough() => BenchCut(1.0f);

    /// <summary>For <c>bench.gd</c>: the same slit through 60% of the width
    /// from the -X side, as a beam swept along one side would leave.</summary>
    public void BenchCutSide() => BenchCut(0.6f);

    private void BenchCut(float share)
    {
        foreach (VoxelPart part in _parts)
        {
            part.Cut(2.0f, 1.0f, share);
        }
    }

    /// <summary>Everything left falls apart as chunks: the thing is destroyed.</summary>
    public void BreakAll(Vector3 push)
    {
        foreach (VoxelPart part in _parts)
        {
            part.BreakAway(push);
        }
    }
}

/// <summary>
/// The collision of one section of a static voxel body's part, and what
/// weapons hit: it relays damage to its <see cref="VoxelBody"/>, which takes
/// it once however many of its sections a blast reaches.
/// </summary>
public partial class VoxelCollider : StaticBody3D, IDamageable, IBlastSurface, IDamageRelay, ITearable
{
    public VoxelBody Body { get; set; }

    public IDamageable Receiver => Body;

    public void AddSiblings(Godot.Collections.Array<Rid> into)
    {
        if (Body == null)
        {
            return;
        }
        foreach (VoxelPart part in Body.Parts)
        {
            part.AddBodies(into);
        }
    }

    public bool IsAlive => Body != null && Body.IsAlive;

    public void ApplyDamage(DamageInfo info) => Body?.ApplyDamage(info);

    public float TearTime(Vector3 point)
    {
        if (Body == null)
        {
            return 0.0f;
        }
        float best = 0.0f;
        foreach (VoxelPart part in Body.Parts)
        {
            float time = part.TearTime(Body.Built(point));
            if (time < 0.0f)
            {
                return time;
            }
            if (time > best)
            {
                best = time;
            }
        }
        return best;
    }

    public RigidBody3D Tear(Vector3 point)
    {
        if (Body == null)
        {
            return null;
        }
        foreach (VoxelPart part in Body.Parts)
        {
            Vector3 built = Body.Built(point);
            if (part.TearTime(built) > 0.0f)
            {
                return part.Tear(built);
            }
        }
        return null;
    }

    public void Tug(Vector3 point, float delta)
    {
        if (Body == null)
        {
            return;
        }
        foreach (VoxelPart part in Body.Parts)
        {
            Vector3 built = Body.Built(point);
            if (part.TearTime(built) > 0.0f)
            {
                part.Tug(built, delta);
                return;
            }
        }
    }

    public Vector3 NearestBlastPoint(Vector3 from) => Body?.NearestBlastPoint(from) ?? GlobalPosition;
}
