using System;
using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>What a piece hangs from: what keeps its voxels in place.</summary>
public enum VoxelAnchor
{
    /// <summary>The ground: cells in its bottom layer. A building.</summary>
    Ground,

    /// <summary>Its hinge: cells near its pivot. A leg, a turret, an arm.</summary>
    Hinge,

    /// <summary>Itself: the biggest connected piece stays, the rest falls.
    /// A mech's body, a loose chunk.</summary>
    Largest,
}

/// <summary>
/// One destructible piece: a voxel grid drawn as sections, optionally given
/// box collision, carved by hits, and checked after each burst of hits for
/// what is no longer held. Whatever has come loose is lifted out and handed
/// to <see cref="VoxelDebris"/> as a falling chunk, or dust if it is small.
///
/// It sits under the node it replaces (an imported part's mesh, a chunk's
/// body) with an identity transform, so it moves with whatever moves that
/// node: a leg swinging, a chunk tumbling.
///
/// Every voxel model in the game is made of these, which is what makes them all
/// break the same way (docs/decisions/0011).
/// </summary>
public partial class VoxelPart : Node3D
{
    public VoxelGrid Grid { get; private set; }
    public VoxelAnchor Anchor { get; private set; }
    public Color[] Palette { get; private set; }
    public float[] Emission { get; private set; }
    public Material Material { get; private set; }

    /// <summary>Gets box collision, section by section. Null for a part whose
    /// body is something else's (a mech moves on its hull).</summary>
    public CollisionObject3D CollisionOwner { get; set; }

    /// <summary>How coarse its collision boxes are, in voxels: 1 follows every
    /// voxel; 2 boxes 2x2x2 blocks, for a loose chunk with no need to be exact.</summary>
    public int CollisionStep { get; set; } = 1;

    /// <summary>The piece's velocity, for what it throws off. Defaults to still.</summary>
    public Func<Vector3> Velocity { get; set; } = () => Vector3.Zero;

    /// <summary>Up, for dust and for what is thrown. Defaults to world up.</summary>
    public Func<Vector3> Up { get; set; } = () => Vector3.Up;

    /// <summary>Raised once, when nothing of it is left or nothing holds it.</summary>
    public event Action<VoxelPart> Emptied;

    /// <summary>Raised after every carve, with the voxels it took.</summary>
    public event Action<VoxelPart, int> Carved;

    public float Health01 => Grid == null || Grid.InitialCount == 0 ? 0.0f : (float)Grid.SolidCount / Grid.InitialCount;

    public bool IsEmpty => Grid == null || Grid.SolidCount <= 0 || _emptied;

    private VoxelDestructionStats _stats;
    private Vector3I _sectionCount;
    private MeshInstance3D[] _meshes;
    private List<uint>[] _shapeOwners;
    private bool[] _meshDirty;
    private bool _anyDirty;
    private float _integrityTimer = -1.0f;
    private bool _emptied;
    private readonly List<VoxelGrid.Removed> _removed = new();

    public void Setup(VoxelGrid grid, VoxelAnchor anchor, Color[] palette, float[] emission, Material material,
        VoxelDestructionStats stats)
    {
        Grid = grid;
        Anchor = anchor;
        Palette = palette;
        Emission = emission;
        Material = material;
        _stats = stats;
    }

    public override void _Ready()
    {
        if (Grid == null)
        {
            return;
        }

        int edge = Math.Max(4, _stats.SectionEdge);
        _sectionCount = new Vector3I(
            (Grid.Size.X + edge - 1) / edge, (Grid.Size.Y + edge - 1) / edge, (Grid.Size.Z + edge - 1) / edge);
        int sections = _sectionCount.X * _sectionCount.Y * _sectionCount.Z;
        _meshes = new MeshInstance3D[sections];
        _shapeOwners = new List<uint>[sections];
        _meshDirty = new bool[sections];
        for (int s = 0; s < sections; s++)
        {
            _shapeOwners[s] = new List<uint>();
            RebuildSection(s);
        }
    }

    // ---------------- Damage ----------------

    /// <summary>
    /// Takes a sphere out, world space. Dust where it went, the touched
    /// sections re-meshed next frame, and a structural check after the
    /// burst. Returns the voxels removed.
    /// </summary>
    public int Carve(Vector3 worldCentre, float radius)
    {
        if (IsEmpty)
        {
            return 0;
        }

        _removed.Clear();
        int count = Grid.Carve(ToLocal(worldCentre), radius, _removed, out Vector3I min, out Vector3I max);
        if (count == 0)
        {
            return 0;
        }

        MarkDirty(min, max);
        Dust(_removed);
        _integrityTimer = _stats.IntegrityDelay;
        Carved?.Invoke(this, count);
        if (Grid.SolidCount <= 0)
        {
            RaiseEmptied();
        }
        return count;
    }

    /// <summary>The first solid cell a world-space ray meets, as a world point.</summary>
    public bool Raycast(Vector3 from, Vector3 direction, float maxDistance, out Vector3 point)
    {
        point = default;
        if (IsEmpty)
        {
            return false;
        }
        Vector3 localFrom = ToLocal(from);
        Vector3 localDirection = GlobalBasis.Inverse() * direction;
        if (!Grid.Raycast(localFrom, localDirection, maxDistance, out Vector3I cell))
        {
            return false;
        }
        point = ToGlobal(Grid.CellCentre(cell.X, cell.Y, cell.Z));
        return true;
    }

    /// <summary>Nearest point of the solid box to a world point.</summary>
    public Vector3 NearestPoint(Vector3 from)
    {
        if (!Grid.SolidBounds(out Aabb box))
        {
            return GlobalPosition;
        }
        Vector3 local = ToLocal(from);
        Vector3 clamped = local.Clamp(box.Position, box.End);
        return ToGlobal(clamped);
    }

    /// <summary>
    /// Splits along a plane through its middle (node space normal): the voxels
    /// on the plane go to dust, what is left either side falls as chunks. How
    /// a loose piece breaks when it lands hard.
    /// </summary>
    public void Shatter(Vector3 localNormal, Vector3 push)
    {
        if (IsEmpty)
        {
            return;
        }
        Vector3 n = VoxSpace.FromGodot(localNormal.Normalized());
        _removed.Clear();
        for (int i = 0; i < Grid.Cells.Length; i++)
        {
            if (Grid.Cells[i] == 0)
            {
                continue;
            }
            Vector3I c = Grid.CellOf(i);
            float d = (new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) - Grid.Origin).Dot(n);
            if (Mathf.Abs(d) < 0.6f)
            {
                _removed.Add(new VoxelGrid.Removed(Grid.CellCentre(c.X, c.Y, c.Z), Grid.Cells[i]));
                Grid.Carve(Grid.CellCentre(c.X, c.Y, c.Z), Grid.VoxelSize * 0.1f, null, out _, out _);
            }
        }
        Dust(_removed);
        BreakAway(push);
    }

    /// <summary>Turns every voxel left into loose chunks: the part is shot off
    /// or its owner destroyed.</summary>
    public void BreakAway(Vector3 push)
    {
        if (IsEmpty)
        {
            return;
        }
        var anchored = new List<bool>();
        List<List<int>> islands = Grid.Islands(_ => false, anchored);
        foreach (List<int> island in islands)
        {
            Release(island, push);
        }
        MarkAllDirty();
        RaiseEmptied();
    }

    // ---------------- Per frame ----------------

    public override void _Process(double delta)
    {
        if (Grid == null)
        {
            return;
        }

        if (_integrityTimer >= 0.0f)
        {
            _integrityTimer -= (float)delta;
            if (_integrityTimer < 0.0f && !VoxelBudget.TakeIntegrityCheck(_stats))
            {
                _integrityTimer = 0.0f;
            }
            else if (_integrityTimer < 0.0f)
            {
                CheckIntegrity();
            }
        }

        if (!_anyDirty)
        {
            return;
        }

        bool remaining = false;
        for (int s = 0; s < _meshDirty.Length; s++)
        {
            if (!_meshDirty[s])
            {
                continue;
            }
            if (!VoxelBudget.TakeSection(_stats))
            {
                remaining = true;
                break;
            }
            _meshDirty[s] = false;
            RebuildSection(s);
        }
        _anyDirty = remaining;
    }

    // ---------------- Structure ----------------

    private void CheckIntegrity()
    {
        if (IsEmpty)
        {
            return;
        }

        float hingeCells = Mathf.Max(1.5f, _stats.HingeRadius / Grid.VoxelSize);
        float hinge2 = hingeCells * hingeCells;
        Func<int, bool> isAnchor = Anchor switch
        {
            VoxelAnchor.Ground => i => Grid.CellOf(i).Z == 0,
            VoxelAnchor.Hinge => i =>
            {
                Vector3I c = Grid.CellOf(i);
                Vector3 d = new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) - Grid.Origin;
                return d.LengthSquared() <= hinge2;
            },
            _ => _ => false,
        };

        var anchored = new List<bool>();
        List<List<int>> islands = Grid.Islands(isAnchor, anchored);
        if (islands.Count == 0)
        {
            RaiseEmptied();
            return;
        }

        int keep = -1;
        if (Anchor == VoxelAnchor.Largest)
        {
            for (int k = 0; k < islands.Count; k++)
            {
                if (keep < 0 || islands[k].Count > islands[keep].Count)
                {
                    keep = k;
                }
            }
        }

        bool anyHeld = false;
        bool changed = false;
        for (int k = 0; k < islands.Count; k++)
        {
            bool held = Anchor == VoxelAnchor.Largest ? k == keep : anchored[k];
            if (held)
            {
                anyHeld = true;
                continue;
            }
            Release(islands[k], Vector3.Zero);
            changed = true;
        }

        if (changed)
        {
            MarkAllDirty();
        }
        if (!anyHeld)
        {
            RaiseEmptied();
        }
    }

    /// <summary>Lifts an island out as a chunk, or dust if it is small.</summary>
    private void Release(List<int> island, Vector3 push)
    {
        Vector3 velocity = Velocity() + push;
        if (island.Count < _stats.MinChunkVoxels || VoxelDebris.Current == null)
        {
            _removed.Clear();
            foreach (int i in island)
            {
                Vector3I c = Grid.CellOf(i);
                _removed.Add(new VoxelGrid.Removed(Grid.CellCentre(c.X, c.Y, c.Z), Grid.Cells[i]));
            }
            Grid.Extract(island, out _);
            Dust(_removed);
            return;
        }

        VoxelGrid piece = Grid.Extract(island, out Vector3 offset);
        Transform3D at = GlobalTransform * new Transform3D(Basis.Identity, offset);
        VoxelDebris.Current.SpawnChunk(piece, Palette, Emission, Material, at, velocity);
    }

    private void RaiseEmptied()
    {
        if (_emptied)
        {
            return;
        }
        _emptied = true;
        Emptied?.Invoke(this);
    }

    // ---------------- Sections ----------------

    private void MarkDirty(Vector3I min, Vector3I max)
    {
        int edge = Math.Max(4, _stats.SectionEdge);
        // A carve changes the faces of its neighbours too: one cell of margin.
        Vector3I lo = (min - Vector3I.One).Clamp(Vector3I.Zero, Grid.Size - Vector3I.One) / edge;
        Vector3I hi = (max + Vector3I.One).Clamp(Vector3I.Zero, Grid.Size - Vector3I.One) / edge;
        for (int z = lo.Z; z <= hi.Z; z++)
            for (int y = lo.Y; y <= hi.Y; y++)
                for (int x = lo.X; x <= hi.X; x++)
                {
                    _meshDirty[x + _sectionCount.X * (y + _sectionCount.Y * z)] = true;
                }
        _anyDirty = true;
    }

    private void MarkAllDirty()
    {
        for (int s = 0; s < _meshDirty.Length; s++)
        {
            _meshDirty[s] = true;
        }
        _anyDirty = true;
    }

    private void RebuildSection(int s)
    {
        int edge = Math.Max(4, _stats.SectionEdge);
        int sx = s % _sectionCount.X, sy = (s / _sectionCount.X) % _sectionCount.Y, sz = s / (_sectionCount.X * _sectionCount.Y);
        var min = new Vector3I(sx * edge, sy * edge, sz * edge);
        Vector3I max = (min + new Vector3I(edge, edge, edge)).Min(Grid.Size);

        Godot.Collections.Array arrays = VoxMesher.Build(Grid.Cells, Grid.Size, min, max, Palette, Emission,
            Grid.Origin, Grid.VoxelSize, -1);
        MeshInstance3D instance = _meshes[s];
        if (arrays == null)
        {
            if (instance != null)
            {
                instance.Mesh = null;
            }
        }
        else
        {
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, Material);
            if (instance == null)
            {
                instance = new MeshInstance3D { Name = $"Section{s}" };
                AddChild(instance);
                _meshes[s] = instance;
            }
            instance.Mesh = mesh;
        }

        if (CollisionOwner == null)
        {
            return;
        }
        // One shape owner per box: an owner's shapes share its transform.
        foreach (uint owner in _shapeOwners[s])
        {
            CollisionOwner.RemoveShapeOwner(owner);
        }
        _shapeOwners[s].Clear();
        var boxes = new List<(Vector3 centre, Vector3 size)>();
        Grid.Boxes(min, max, CollisionStep, boxes);
        Transform3D toOwner = CollisionOwner.GlobalTransform.AffineInverse() * GlobalTransform;
        foreach ((Vector3 centre, Vector3 size) in boxes)
        {
            uint owner = CollisionOwner.CreateShapeOwner(this);
            CollisionOwner.ShapeOwnerAddShape(owner, new BoxShape3D { Size = size });
            CollisionOwner.ShapeOwnerSetTransform(owner, toOwner * new Transform3D(Basis.Identity, centre));
            _shapeOwners[s].Add(owner);
        }
    }

    // ---------------- Dust ----------------

    private void Dust(List<VoxelGrid.Removed> removed)
    {
        if (removed.Count == 0 || VoxelDebris.Current == null)
        {
            return;
        }
        Vector3 centre = Vector3.Zero;
        Color colour = new(0, 0, 0, 0);
        foreach (VoxelGrid.Removed r in removed)
        {
            centre += r.Position;
            colour += Palette[r.Colour];
        }
        centre /= removed.Count;
        colour /= removed.Count;
        float edge = Mathf.Pow(removed.Count, 1.0f / 3.0f) * Grid.VoxelSize;
        VoxelDebris.Current.Dust(ToGlobal(centre), Up(), Velocity(), colour.LinearToSrgb(), edge);
    }
}
