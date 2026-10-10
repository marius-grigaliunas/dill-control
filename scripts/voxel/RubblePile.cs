using System;
using System.Collections.Generic;
using DillControl.Core;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// The heap a collapse leaves round its base: where the storeys a falling
/// tower crushes go, rather than vanishing into dust. A static voxel model
/// that grows, drawn and collided like any other (its meshes and boxes built
/// on the voxel workers), with no physics body per piece of it.
///
/// Each voxel deposited lands at a random point in a ring round the
/// footprint, thickest near it, and rolls down to its lowest neighbour while
/// the drop is more than one voxel, so the heap keeps a loose slope. Nothing
/// lands inside the footprint: the stump and the tower crushing into it are
/// there.
///
/// Made and found by <see cref="VoxelDebris"/>, one per collapse site and
/// palette (docs/decisions/0016).
/// </summary>
public partial class RubblePile : Node3D, IDamageable, IBlastSurface
{
    public Color[] Palette { get; private set; }
    public float Reach { get; private set; }

    private VoxelPart _part;
    private VoxelDestructionStats _stats;
    private float[] _emission;
    private Material _material;
    private readonly List<VoxelPart> _parts = new();
    private float _reheight = -1.0f;
    private VoxelGrid _grid;
    private int[] _height;
    private bool[] _blocked;
    private Vector3I _size;
    private readonly Random _random = new();
    private readonly List<(int, byte)> _cells = new();

    /// <summary>A pile <paramref name="reach"/> metres round its centre,
    /// <paramref name="height"/> metres high at most.</summary>
    public void Setup(Color[] palette, float[] emission, Material material, VoxelDestructionStats stats,
        float voxel, float reach, float height)
    {
        Palette = palette;
        Reach = reach;
        int across = Mathf.CeilToInt(reach * 2.0f / voxel) | 1;
        _size = new Vector3I(across, across, Mathf.CeilToInt(height / voxel));
        _grid = new VoxelGrid(_size, new byte[_size.X * _size.Y * _size.Z],
            new Vector3(across * 0.5f, across * 0.5f, 0.0f), voxel);
        _height = new int[across * across];
        _blocked = new bool[across * across];
        _part = new VoxelPart { Name = "Voxels", BuildAtOnce = true };
        // Anchored to the ground like a building: what is shot loose of it
        // falls as pieces (with nothing built in it, it never fails under load).
        _part.Setup(_grid, VoxelAnchor.Ground, palette, emission, material, stats);
        _emission = emission;
        _material = material;
        AddChild(_part);
        _parts.Add(_part);
        _stats = stats;

        // DECISION -- heightfields, not boxes, in 4 m tiles. A heap's ragged
        // top boxed voxel by voxel came to thousands of boxes a section,
        // re-made at every crush: 20-36 ms a frame. One heightfield over the
        // whole heap also lay under the tower crushing down inside it, and
        // Jolt re-tested its 15 000 triangles against the tower at every
        // crush: 20 ms physics steps. Tiles exist only where there is rubble,
        // and a change rebuilds only its own.
        int tiles = (across + Tile - 1) / Tile;
        _tiles = new HeightMapShape3D[tiles * tiles];
        _tileDirty = new bool[tiles * tiles];
        _tileCount = tiles;
    }

    private const int Tile = 8;
    private HeightMapShape3D[] _tiles;
    private bool[] _tileDirty;
    private int _tileCount;
    private readonly float[] _tileMap = new float[(Tile + 1) * (Tile + 1)];

    private bool _mapDirty;
    private float _mapTimer;

    // ---------------- Damage ----------------

    public bool IsAlive => !_part.IsEmpty;

    /// <summary>
    /// A heap is rubble you can still blow apart: hits carve it the way they
    /// carve anything voxel (VoxelDamage), and what is cut loose falls. Its
    /// collision follows a moment later.
    /// </summary>
    public void ApplyDamage(DamageInfo info)
    {
        VoxelDamage.Apply(info, _parts, _stats, this);
        // Twice: once the carve is in, and once whatever it cut loose has gone.
        _reheight = 0.2f;
        _reheightAgain = true;
    }

    public Vector3 NearestBlastPoint(Vector3 from) => VoxelDamage.NearestPoint(_parts, from, GlobalPosition);

    /// <summary>
    /// Tears a lump out of the heap where the gravity gun took hold, as a
    /// loose piece made this frame, so it is in the gun's grip at once.
    /// </summary>
    public RigidBody3D Pluck(Vector3 point)
    {
        if (VoxelDebris.Current == null || _part.IsEmpty)
        {
            return null;
        }
        float voxel = _grid.VoxelSize;
        // Just inside the surface the gun's ray met.
        Vector3 at = _grid.ToVox(_part.ToLocal(point - WorldUp.At(point) * voxel * 0.5f));
        float r = _stats.PluckRadius / voxel;
        var cells = new List<int>();
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        int reach = Mathf.CeilToInt(r) + 1;
        for (int z = Math.Max(0, (int)at.Z - reach); z <= Math.Min(_size.Z - 1, (int)at.Z + reach); z++)
            for (int y = Math.Max(0, (int)at.Y - reach); y <= Math.Min(_size.Y - 1, (int)at.Y + reach); y++)
                for (int x = Math.Max(0, (int)at.X - reach); x <= Math.Min(_size.X - 1, (int)at.X + reach); x++)
                {
                    int i = _grid.Index(x, y, z);
                    if (_grid.Cells[i] == 0 || new Vector3(x + 0.5f, y + 0.5f, z + 0.5f).DistanceSquaredTo(at) > r * r)
                    {
                        continue;
                    }
                    cells.Add(i);
                    var c = new Vector3I(x, y, z);
                    min = min.Min(c);
                    max = max.Max(c);
                }
        if (cells.Count < 2)
        {
            return null;
        }
        VoxelGrid lump = _grid.Copy(cells, out Vector3 offset, 1, clear: false);
        _part.Take(cells, min, max);
        _reheight = 0.0f;
        Transform3D placed = _part.GlobalTransform * new Transform3D(Basis.Identity, offset);
        return VoxelDebris.Current.MakeNow(lump, Palette, _emission, _material, placed);
    }

    /// <summary>Adds every collision tile's RID.</summary>
    public void AddBodies(Godot.Collections.Array<Rid> into)
    {
        foreach (Node child in GetChildren())
        {
            if (child is RubbleCollider tile)
            {
                into.Add(tile.GetRid());
            }
        }
    }

    /// <summary>Rubble landing on rubble does not dig it.</summary>
    public bool Ignores(Node3D source) => source is VoxelChunk;

    private bool _reheightAgain;

    /// <summary>Every column's height again from its cells, for the
    /// collision: after hits, which take cells from anywhere in a column.</summary>
    private void Reheight()
    {
        int n = _size.X, layers = _size.Z;
        byte[] cells = _grid.Cells;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int column = x + n * y;
                int top = 0;
                for (int z = layers - 1; z >= 0; z--)
                {
                    if (cells[x + n * (y + n * z)] != 0)
                    {
                        top = z + 1;
                        break;
                    }
                }
                if (top != _height[column])
                {
                    _height[column] = top;
                    Touch(x, y);
                }
            }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_reheight >= 0.0f)
        {
            _reheight -= (float)delta;
            if (_reheight < 0.0f)
            {
                Reheight();
                if (_reheightAgain)
                {
                    _reheightAgain = false;
                    _reheight = 1.0f;
                }
            }
        }
        _mapTimer -= (float)delta;
        if (!_mapDirty || _mapTimer > 0.0f)
        {
            return;
        }
        // At most five times a second (a heap grows over seconds), and a few
        // tiles a tick: making forty tiles at once cost a frame 15 ms.
        using FrameProfiler.Scope scope = FrameProfiler.Begin(VoxelPart.SectionRubbleMap);
        _mapDirty = false;
        int budget = TilesPerTick;
        int n = _size.X;
        float voxel = _grid.VoxelSize;
        for (int ty = 0; ty < _tileCount; ty++)
            for (int tx = 0; tx < _tileCount; tx++)
            {
                int t = tx + _tileCount * ty;
                if (!_tileDirty[t])
                {
                    continue;
                }
                if (budget-- <= 0)
                {
                    // The rest next tick.
                    _mapDirty = true;
                    return;
                }
                _tileDirty[t] = false;
                // Rows run along +Z; the grid's y runs along -Z. Neighbouring
                // tiles share their edge vertices. Bare ground is a hole, not
                // a floor at the heap's base: where the ground dipped below
                // that base, a floor was an invisible shelf over the dip.
                // Under Jolt a NaN height leaves out every quad touching it,
                // so a column is a hole only when its neighbours are bare
                // too, and the heap's edge still slopes down to the ground.
                for (int j = 0; j <= Tile; j++)
                    for (int i = 0; i <= Tile; i++)
                    {
                        int x = Math.Min(tx * Tile + i, n - 1), y = Math.Min(ty * Tile + Tile - j, n - 1);
                        _tileMap[i + (Tile + 1) * j] = Bare(x, y) ? float.NaN : _height[x + n * y];
                    }
                if (_tiles[t] == null)
                {
                    _tiles[t] = new HeightMapShape3D { MapWidth = Tile + 1, MapDepth = Tile + 1 };
                    var body = new RubbleCollider
                    {
                        Pile = this,
                        Name = $"Collision{t}",
                        CollisionLayer = GameLayers.Mask(GameLayers.World, GameLayers.Prop),
                        CollisionMask = 0,
                        Position = new Vector3(tx * Tile + Tile * 0.5f - (n - 1) * 0.5f, 0.0f,
                            -(ty * Tile + Tile * 0.5f - (n - 1) * 0.5f)) * voxel,
                    };
                    body.AddChild(new CollisionShape3D { Shape = _tiles[t], Scale = Vector3.One * voxel });
                    AddChild(body);
                }
                _tiles[t].MapData = _tileMap;
            }
        _mapTimer = 0.2f;
    }

    private const int TilesPerTick = 4;

    /// <summary>No rubble in this column or any next to it.</summary>
    private bool Bare(int x, int y)
    {
        int n = _size.X;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < n && ny < n && _height[nx + n * ny] > 0)
                {
                    return false;
                }
            }
        return true;
    }

    /// <summary>Marks the tiles a column's height belongs to (shared edges
    /// included) for a refresh.</summary>
    private void Touch(int x, int y)
    {
        // A tile's vertices are columns tx*Tile to tx*Tile+Tile, and a
        // column decides its neighbours' holes too (Bare): every tile with a
        // vertex within a column of this one.
        int t0x = Math.Max(0, (x - 2) / Tile), t1x = Math.Min(_tileCount - 1, (x + 1) / Tile);
        int t0y = Math.Max(0, (y - 2) / Tile), t1y = Math.Min(_tileCount - 1, (y + 1) / Tile);
        for (int ty = t0y; ty <= t1y; ty++)
            for (int tx = t0x; tx <= t1x; tx++)
            {
                _tileDirty[tx + _tileCount * ty] = true;
            }
        _mapDirty = true;
    }

    /// <summary>A colour for a cell from the crushed ones, the same
    /// over little patches: a heap reads as rubble, and patches of one colour
    /// mesh into far fewer faces than voxels of every colour.</summary>
    private static byte Patch(IReadOnlyList<byte> colours, int x, int y, int z)
    {
        uint h = (uint)((x >> 1) * 73856093) ^ (uint)((y >> 1) * 19349663) ^ (uint)((z >> 1) * 83492791);
        h ^= h >> 13;
        return colours[(int)(h % (uint)colours.Count)];
    }

    /// <summary>
    /// Heaps <paramref name="count"/> voxels round a footprint
    /// <paramref name="inner"/> metres in radius centred on
    /// <paramref name="centre"/>, coloured from <paramref name="colours"/>.
    /// </summary>
    public void Deposit(Vector3 centre, float inner, int count, IReadOnlyList<byte> colours)
    {
        if (count <= 0 || colours.Count == 0)
        {
            return;
        }
        float voxel = _grid.VoxelSize;
        Vector3 local = ToLocal(centre) / voxel;
        float cx = local.X + _size.X * 0.5f, cy = -local.Z + _size.Y * 0.5f;
        float innerCells = inner / voxel;
        float outerCells = _size.X * 0.5f - 1.0f;
        float spread = Mathf.Max(1.0f, (outerCells - innerCells) * 0.22f);

        // The footprint takes nothing.
        for (int y = 0; y < _size.Y; y++)
            for (int x = 0; x < _size.X; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy < innerCells * innerCells)
                {
                    _blocked[x + _size.X * y] = true;
                }
            }

        _cells.Clear();
        Vector3I min = _size, max = Vector3I.Zero;
        for (int k = 0; k < count; k++)
        {
            double angle = _random.NextDouble() * Math.Tau;
            float r = innerCells + 0.5f + Mathf.Abs(Gauss()) * spread;
            int x = Mathf.Clamp((int)(cx + Math.Cos(angle) * r), 1, _size.X - 2);
            int y = Mathf.Clamp((int)(cy + Math.Sin(angle) * r), 1, _size.Y - 2);
            if (_blocked[x + _size.X * y])
            {
                continue;
            }
            // Roll down while the drop is more than a voxel.
            for (int step = 0; step < 12; step++)
            {
                int here = _height[x + _size.X * y];
                int bx = x, by = y, lowest = here - 1;
                foreach ((int ox, int oy) in Neighbours)
                {
                    int nx = x + ox, ny = y + oy;
                    if (nx < 1 || ny < 1 || nx > _size.X - 2 || ny > _size.Y - 2 || _blocked[nx + _size.X * ny])
                    {
                        continue;
                    }
                    int h = _height[nx + _size.X * ny];
                    if (h < lowest)
                    {
                        lowest = h;
                        bx = nx;
                        by = ny;
                    }
                }
                if (bx == x && by == y)
                {
                    break;
                }
                x = bx;
                y = by;
            }
            int column = x + _size.X * y;
            int z = _height[column];
            if (z >= _size.Z)
            {
                continue;
            }
            _height[column] = z + 1;
            Touch(x, y);
            _cells.Add((_grid.Index(x, y, z), Patch(colours, x, y, z)));
            min = min.Min(new Vector3I(x, y, z));
            max = max.Max(new Vector3I(x, y, z));
        }
        _part.Fill(_cells, min, max);
    }

    /// <summary>
    /// Takes in a piece that has come to rest, voxel for voxel where it lies:
    /// a settled piece leaves the loose-piece count without leaving the
    /// screen (VoxelDebris, past its cap).
    /// </summary>
    /// <returns>False, and nothing taken in, when more than a tenth of it
    /// would fall outside the heap's grid (below its base, where the ground
    /// dips, or past its edge): it would visibly lose those voxels.</returns>
    public bool Bake(VoxelGrid piece, Transform3D pieceTransform)
    {
        float voxel = _grid.VoxelSize;
        Transform3D toPile = GlobalTransform.AffineInverse() * pieceTransform;
        _cells.Clear();
        Vector3I min = _size, max = Vector3I.Zero;
        int outside = 0;
        for (int i = 0; i < piece.Cells.Length; i++)
        {
            byte colour = piece.Cells[i];
            if (colour == 0)
            {
                continue;
            }
            Vector3I c = piece.CellOf(i);
            Vector3 local = toPile * piece.CellCentre(c.X, c.Y, c.Z) / voxel;
            var cell = new Vector3I(
                Mathf.FloorToInt(local.X + _size.X * 0.5f),
                Mathf.FloorToInt(-local.Z + _size.Y * 0.5f),
                Mathf.FloorToInt(local.Y));
            if (cell.X < 0 || cell.Y < 0 || cell.Z < 0 || cell.X >= _size.X || cell.Y >= _size.Y || cell.Z >= _size.Z)
            {
                outside++;
                continue;
            }
            _cells.Add((_grid.Index(cell.X, cell.Y, cell.Z), colour));
            min = min.Min(cell);
            max = max.Max(cell);
        }
        if (_cells.Count == 0 || outside * 10 > _cells.Count + outside)
        {
            _cells.Clear();
            return false;
        }
        foreach ((int index, _) in _cells)
        {
            Vector3I cell = _grid.CellOf(index);
            int column = cell.X + _size.X * cell.Y;
            _height[column] = Math.Max(_height[column], cell.Z + 1);
            Touch(cell.X, cell.Y);
        }
        _part.Fill(_cells, min, max);
        return true;
    }

    private static readonly (int, int)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) };

    private float Gauss()
    {
        double u = 1.0 - _random.NextDouble(), v = _random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(Math.Tau * v));
    }
}

/// <summary>
/// One tile of a heap's collision, and what weapons hit: it relays damage to
/// its <see cref="RubblePile"/>, which takes it once however many tiles an
/// area hit reaches.
/// </summary>
public partial class RubbleCollider : StaticBody3D, IDamageable, IBlastSurface, IDamageRelay, IPluckable
{
    public RigidBody3D Pluck(Vector3 point) => Pile?.Pluck(point);

    public void AddSiblings(Godot.Collections.Array<Rid> into) => Pile?.AddBodies(into);

    public RubblePile Pile { get; set; }

    public IDamageable Receiver => Pile;

    public bool IsAlive => Pile != null && Pile.IsAlive;

    public void ApplyDamage(DamageInfo info) => Pile?.ApplyDamage(info);

    public Vector3 NearestBlastPoint(Vector3 from) => Pile?.NearestBlastPoint(from) ?? GlobalPosition;
}
