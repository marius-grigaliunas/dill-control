using System;
using System.Collections.Generic;
using DillControl.Audio;
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
/// A building also carries its own weight: a layer that has lost too much of
/// what held it up gives way, and what is above comes down tipping towards the
/// side that went. Anything big that comes loose fractures into storey-sized
/// blocks that fall together and crumble as they land (docs/decisions/0013).
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

    // As built, per layer (Ground only): how many cells, and half the narrower
    // side of their footprint, in voxels. What the load check measures against.
    private int[] _builtCount;
    private float[] _builtHalf;

    public void Setup(VoxelGrid grid, VoxelAnchor anchor, Color[] palette, float[] emission, Material material,
        VoxelDestructionStats stats)
    {
        Grid = grid;
        Anchor = anchor;
        Palette = palette;
        Emission = emission;
        Material = material;
        _stats = stats;
        if (anchor == VoxelAnchor.Ground)
        {
            MeasureBuilt();
        }
    }

    private void MeasureBuilt()
    {
        int layers = Grid.Size.Z;
        _builtCount = new int[layers];
        _builtHalf = new float[layers];
        var min = new Vector2I[layers];
        var max = new Vector2I[layers];
        for (int z = 0; z < layers; z++)
        {
            min[z] = new Vector2I(int.MaxValue, int.MaxValue);
            max[z] = new Vector2I(int.MinValue, int.MinValue);
        }
        for (int i = 0; i < Grid.Cells.Length; i++)
        {
            if (Grid.Cells[i] == 0)
            {
                continue;
            }
            Vector3I c = Grid.CellOf(i);
            _builtCount[c.Z]++;
            min[c.Z] = new Vector2I(Math.Min(min[c.Z].X, c.X), Math.Min(min[c.Z].Y, c.Y));
            max[c.Z] = new Vector2I(Math.Max(max[c.Z].X, c.X), Math.Max(max[c.Z].Y, c.Y));
        }
        for (int z = 0; z < layers; z++)
        {
            if (_builtCount[z] > 0)
            {
                Vector2I size = max[z] - min[z] + Vector2I.One;
                _builtHalf[z] = Mathf.Max(1.0f, Math.Min(size.X, size.Y) * 0.5f);
            }
        }
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

    /// <summary>
    /// Breaks into blocks about half its own size, each thrown a little apart,
    /// in a burst of dust: how a big loose piece comes apart when it lands
    /// hard. False when it is too small to split, and nothing was done.
    /// </summary>
    public bool Crumble(Vector3 push)
    {
        if (IsEmpty)
        {
            return false;
        }
        var anchored = new List<bool>();
        List<List<int>> islands = Grid.Islands(_ => false, anchored);
        if (islands.Count == 1 && CrumbleBlock(islands[0]) == Vector3.Zero)
        {
            return false;
        }

        if (VoxelDebris.Current != null && Grid.SolidBounds(out Aabb box))
        {
            VoxelDebris.Current.Dust(ToGlobal(box.GetCenter()), Up(), Velocity() * 0.3f,
                VoxelDebris.AverageColour(Grid, Palette), Mathf.Pow(Grid.SolidCount, 1.0f / 3.0f) * Grid.VoxelSize * 1.5f);
        }
        foreach (List<int> island in islands)
        {
            Release(island, push, null, Vector3.Zero, crumble: true);
        }
        MarkAllDirty();
        RaiseEmptied();
        return true;
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
            Release(island, push, null, Vector3.Zero, crumble: false);
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
            Release(islands[k], Vector3.Zero, null, Vector3.Zero, crumble: false);
            changed = true;
        }

        // What still stands must still carry what is above it. One failure a
        // check: the falling pieces land on what is left, carve it, and the
        // check after that finds the next.
        if (Anchor == VoxelAnchor.Ground)
        {
            for (int k = 0; k < islands.Count; k++)
            {
                if (anchored[k] && CheckLoad(islands[k]))
                {
                    changed = true;
                    break;
                }
            }
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

    /// <summary>
    /// Finds the lowest layer of a standing island that can no longer carry
    /// what is above it, and brings that down: the layer's last voxels are
    /// crushed to dust and everything above falls, tipping about what was left
    /// of the support towards the side that lost it.
    ///
    /// A layer's support is the share of its as-built voxels it still has,
    /// reduced by how far the load's centre hangs off the support's centre (as
    /// a fraction of the footprint's half width). It fails below
    /// <see cref="VoxelDestructionStats.MinSupportFraction"/> scaled by how
    /// loaded it is. Per-layer sums over the island: one pass, no flood.
    /// </summary>
    private bool CheckLoad(List<int> island)
    {
        int layers = Grid.Size.Z;
        var count = new int[layers];
        var sumX = new float[layers];
        var sumY = new float[layers];
        foreach (int i in island)
        {
            Vector3I c = Grid.CellOf(i);
            count[c.Z]++;
            sumX[c.Z] += c.X + 0.5f;
            sumY[c.Z] += c.Y + 0.5f;
        }

        int fail = -1;
        Vector2 failLean = Vector2.Zero, failSupport = Vector2.Zero;
        float failOffCentre = 0.0f;
        float above = 0.0f, aboveX = 0.0f, aboveY = 0.0f;
        for (int z = layers - 1; z >= 0; z--)
        {
            if (above >= _stats.MinLoadVoxels && _builtCount[z] > 0)
            {
                var load = new Vector2(aboveX, aboveY) / above;
                Vector2 support = count[z] > 0 ? new Vector2(sumX[z], sumY[z]) / count[z] : load;
                float kept = (float)count[z] / _builtCount[z];
                float offCentre = Mathf.Clamp(load.DistanceTo(support) / _builtHalf[z], 0.0f, 1.0f);
                float loaded = Mathf.Clamp(above / (_builtCount[z] * _stats.FullLoadLayers), 0.0f, 1.0f);
                if (kept * (1.0f - offCentre) < _stats.MinSupportFraction * loaded)
                {
                    // Keep going down: the lowest failure brings the most down.
                    fail = z;
                    failLean = load - support;
                    failSupport = support;
                    failOffCentre = offCentre;
                }
            }
            above += count[z];
            aboveX += sumX[z];
            aboveY += sumY[z];
        }
        if (fail < 0)
        {
            return false;
        }

        var crushed = new List<int>();
        var falling = new List<int>();
        foreach (int i in island)
        {
            int z = Grid.CellOf(i).Z;
            if (z == fail)
            {
                crushed.Add(i);
            }
            else if (z > fail)
            {
                falling.Add(i);
            }
        }

        _removed.Clear();
        Grid.Remove(crushed, _removed);
        CollapseDust(_removed);

        // Tip about the remains of the support, towards where the load hangs.
        Vector3 pivot = ToGlobal(VoxSpace.ToGodot(new Vector3(failSupport.X, failSupport.Y, fail + 0.5f) - Grid.Origin) * Grid.VoxelSize);
        Vector3 up = Up();
        Vector3 lean = GlobalBasis * VoxSpace.ToGodot(new Vector3(failLean.X, failLean.Y, 0.0f));
        lean -= up * lean.Dot(up);
        if (lean.LengthSquared() < 1e-4f)
        {
            lean = up.Cross(Vector3.Right).Rotated(up, GD.Randf() * Mathf.Tau);
        }
        Vector3 spin = up.Cross(lean.Normalized()).Normalized() * (_stats.ToppleSpin * (0.35f + 0.65f * failOffCentre));

        foreach (List<int> piece in Grid.Pieces(falling, _ => 0))
        {
            Release(piece, Vector3.Zero, spin, pivot, crumble: false);
        }
        AudioDirector.PlayAt(Sfx.VoxelCollapse, pivot);
        return true;
    }

    /// <summary>
    /// Lifts an island out as loose pieces, or dust if it is small. A big one
    /// fractures into storey-sized blocks first (with <paramref name="crumble"/>,
    /// into blocks about half its own size), all starting as one rigid body
    /// would: each moves at the velocity its place in the whole has.
    /// </summary>
    /// <param name="spin">The whole's angular velocity about
    /// <paramref name="pivot"/>; null throws each piece off with a spin of
    /// its own.</param>
    private void Release(List<int> island, Vector3 push, Vector3? spin, Vector3 pivot, bool crumble)
    {
        Vector3 velocity = Velocity() + push;
        if (island.Count < _stats.MinChunkVoxels || VoxelDebris.Current == null)
        {
            _removed.Clear();
            Grid.Remove(island, _removed);
            Dust(_removed);
            return;
        }

        Vector3 block = crumble ? CrumbleBlock(island) : FractureBlock(island);
        if (block == Vector3.Zero)
        {
            SpawnPiece(island, velocity, spin, pivot);
            return;
        }

        Vector3I min = CellMin(island);
        int seed = (int)(GD.Randi() & 0xffff);
        int Key(int i)
        {
            // Block boundaries shifted by up to a third of a block in steps of
            // a few voxels, so the blocks break out jagged rather than as boxes.
            Vector3I c = Grid.CellOf(i) - min;
            float jx = Jitter(c.Y >> 2, c.Z >> 2, seed) * block.X * 0.3f;
            float jy = Jitter(c.X >> 2, c.Z >> 2, seed + 1) * block.Y * 0.3f;
            float jz = Jitter(c.X >> 2, c.Y >> 2, seed + 2) * block.Z * 0.3f;
            int kx = Mathf.FloorToInt((c.X + jx) / block.X) + 1;
            int ky = Mathf.FloorToInt((c.Y + jy) / block.Y) + 1;
            int kz = Mathf.FloorToInt((c.Z + jz) / block.Z) + 1;
            return kx + 1024 * (ky + 1024 * kz);
        }

        Vector3 centre = PieceCentre(island);
        _removed.Clear();
        foreach (List<int> piece in Grid.Pieces(island, Key))
        {
            if (piece.Count < _stats.MinChunkVoxels)
            {
                Grid.Remove(piece, _removed);
                continue;
            }
            Vector3 pieceVelocity = velocity;
            if (crumble)
            {
                // A landing piece comes apart outwards, not as a stack.
                Vector3 away = PieceCentre(piece) - centre;
                if (away.LengthSquared() > 1e-4f)
                {
                    pieceVelocity += away.Normalized() * (_stats.BreakawaySpeed * 0.5f);
                }
            }
            SpawnPiece(piece, pieceVelocity, spin, pivot);
        }
        Dust(_removed);
    }

    private void SpawnPiece(List<int> piece, Vector3 velocity, Vector3? spin, Vector3 pivot)
    {
        VoxelGrid grid = Grid.Extract(piece, out Vector3 offset);
        Transform3D at = GlobalTransform * new Transform3D(Basis.Identity, offset);
        if (spin is Vector3 w)
        {
            velocity += w.Cross(at.Origin - pivot);
        }
        VoxelDebris.Current.SpawnChunk(grid, Palette, Emission, Material, at, velocity, spin);
    }

    /// <summary>Fracture blocks for a released piece, in voxels: storey-sized,
    /// grown until there are no more than the cap. Zero: too small to fracture.</summary>
    private Vector3 FractureBlock(List<int> island)
    {
        float cell = Grid.VoxelSize;
        if (island.Count * cell * cell * cell < _stats.FractureVolume)
        {
            return Vector3.Zero;
        }
        Vector3I size = CellMax(island) - CellMin(island) + Vector3I.One;
        var block = new Vector3(_stats.FractureWidth / cell, _stats.FractureWidth / cell, _stats.FractureHeight / cell);
        while (Blocks(size, block) > _stats.MaxFracturePieces)
        {
            block *= 1.25f;
        }
        return Blocks(size, block) > 1 ? block : Vector3.Zero;
    }

    /// <summary>Crumble blocks: half the piece's size each way, no smaller than
    /// <see cref="VoxelDestructionStats.MinCrumbleSize"/>. Zero: too small to split.</summary>
    private Vector3 CrumbleBlock(List<int> island)
    {
        Vector3I size = CellMax(island) - CellMin(island) + Vector3I.One;
        float least = _stats.MinCrumbleSize / Grid.VoxelSize;
        var block = new Vector3(
            Mathf.Max(size.X * 0.5f, least), Mathf.Max(size.Y * 0.5f, least), Mathf.Max(size.Z * 0.5f, least));
        return Blocks(size, block) > 1 ? block : Vector3.Zero;
    }

    private static int Blocks(Vector3I size, Vector3 block) =>
        Mathf.CeilToInt(size.X / block.X) * Mathf.CeilToInt(size.Y / block.Y) * Mathf.CeilToInt(size.Z / block.Z);

    private static float Jitter(int a, int b, int seed)
    {
        uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(seed * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xffff) / 32767.5f - 1.0f;
    }

    private Vector3I CellMin(List<int> cells)
    {
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (int i in cells)
        {
            Vector3I c = Grid.CellOf(i);
            min = new Vector3I(Math.Min(min.X, c.X), Math.Min(min.Y, c.Y), Math.Min(min.Z, c.Z));
        }
        return min;
    }

    private Vector3I CellMax(List<int> cells)
    {
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        foreach (int i in cells)
        {
            Vector3I c = Grid.CellOf(i);
            max = new Vector3I(Math.Max(max.X, c.X), Math.Max(max.Y, c.Y), Math.Max(max.Z, c.Z));
        }
        return max;
    }

    /// <summary>World-space centre of a set of cells.</summary>
    private Vector3 PieceCentre(List<int> cells)
    {
        Vector3 sum = Vector3.Zero;
        foreach (int i in cells)
        {
            Vector3I c = Grid.CellOf(i);
            sum += Grid.CellCentre(c.X, c.Y, c.Z);
        }
        return ToGlobal(sum / cells.Count);
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

    /// <summary>A failed layer goes up as big clouds all round its footprint,
    /// not one puff at its middle: four-metre bins, each raising a cloud sized
    /// for the storey coming down into it.</summary>
    private void CollapseDust(List<VoxelGrid.Removed> removed)
    {
        if (removed.Count == 0 || VoxelDebris.Current == null)
        {
            return;
        }
        var bins = new Dictionary<Vector2I, (Vector3 sum, Color colour, int n)>();
        foreach (VoxelGrid.Removed r in removed)
        {
            var key = new Vector2I(Mathf.FloorToInt(r.Position.X / 4.0f), Mathf.FloorToInt(r.Position.Z / 4.0f));
            bins.TryGetValue(key, out (Vector3 sum, Color colour, int n) bin);
            bins[key] = (bin.sum + r.Position, bin.colour + Palette[r.Colour], bin.n + 1);
        }
        foreach ((Vector3 sum, Color colour, int n) in bins.Values)
        {
            VoxelDebris.Current.Dust(ToGlobal(sum / n), Up(), Velocity(), (colour / n).LinearToSrgb(), 4.0f);
        }
    }
}
