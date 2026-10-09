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
public partial class RubblePile : Node3D
{
    public Color[] Palette { get; private set; }
    public float Reach { get; private set; }

    private VoxelPart _part;
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
        _part.Setup(_grid, VoxelAnchor.Largest, palette, emission, material, stats);
        AddChild(_part);

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

    public override void _PhysicsProcess(double delta)
    {
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
                // tiles share their edge vertices.
                for (int j = 0; j <= Tile; j++)
                    for (int i = 0; i <= Tile; i++)
                    {
                        int x = Math.Min(tx * Tile + i, n - 1), y = Math.Min(ty * Tile + Tile - j, n - 1);
                        _tileMap[i + (Tile + 1) * j] = _height[x + n * y];
                    }
                if (_tiles[t] == null)
                {
                    _tiles[t] = new HeightMapShape3D { MapWidth = Tile + 1, MapDepth = Tile + 1 };
                    var body = new StaticBody3D
                    {
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

    /// <summary>Marks the tiles a column's height belongs to (shared edges
    /// included) for a refresh.</summary>
    private void Touch(int x, int y)
    {
        int tx = x / Tile, ty = y / Tile;
        _tileDirty[tx + _tileCount * ty] = true;
        if (x % Tile == 0 && tx > 0)
        {
            _tileDirty[tx - 1 + _tileCount * ty] = true;
        }
        if (y % Tile == 0 && ty > 0)
        {
            _tileDirty[tx + _tileCount * (ty - 1)] = true;
        }
        if (x % Tile == 0 && y % Tile == 0 && tx > 0 && ty > 0)
        {
            _tileDirty[tx - 1 + _tileCount * (ty - 1)] = true;
        }
        _mapDirty = true;
    }

    /// <summary>A colour for a cell from a few of the crushed ones, the same
    /// over little patches: a heap reads as rubble, and patches of one colour
    /// mesh into far fewer faces than voxels of every colour.</summary>
    private static byte Patch(IReadOnlyList<byte> colours, int x, int y, int z)
    {
        uint h = (uint)((x >> 1) * 73856093) ^ (uint)((y >> 1) * 19349663) ^ (uint)((z >> 1) * 83492791);
        h ^= h >> 13;
        return colours[(int)(h % (uint)Math.Min(colours.Count, 4))];
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
    public void Bake(VoxelGrid piece, Transform3D pieceTransform)
    {
        float voxel = _grid.VoxelSize;
        Transform3D toPile = GlobalTransform.AffineInverse() * pieceTransform;
        _cells.Clear();
        Vector3I min = _size, max = Vector3I.Zero;
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
                continue;
            }
            _cells.Add((_grid.Index(cell.X, cell.Y, cell.Z), colour));
            int column = cell.X + _size.X * cell.Y;
            _height[column] = Math.Max(_height[column], cell.Z + 1);
            Touch(cell.X, cell.Y);
            min = min.Min(cell);
            max = max.Max(cell);
        }
        _part.Fill(_cells, min, max);
    }

    private static readonly (int, int)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) };

    private float Gauss()
    {
        double u = 1.0 - _random.NextDouble(), v = _random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(Math.Tau * v));
    }
}
