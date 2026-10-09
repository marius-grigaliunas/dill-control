using System;
using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// The voxels of one destructible piece, and everything that can be asked of
/// them: carve a sphere out, march a ray in, find what is still connected,
/// lift a loose piece out as its own grid, and decompose what is left into
/// boxes for collision.
///
/// Cells are in MagicaVoxel's order and axes (index <c>x + X * (y + Y * z)</c>,
/// Z up), exactly as the importer stored them. <see cref="Origin"/> is the
/// voxel-space point that sits at the owning node's origin, so a cell's
/// position in that node's space is
/// <c>VoxSpace.ToGodot(cell + 0.5 - Origin) * VoxelSize</c>, the mesher's rule.
///
/// Plain C#: no nodes, no engine calls but maths, so it can be reasoned about
/// and tested on its own.
/// </summary>
public sealed class VoxelGrid
{
    public readonly Vector3I Size;
    public readonly byte[] Cells;
    public readonly Vector3 Origin;
    public readonly float VoxelSize;

    public int SolidCount { get; private set; }

    /// <summary>Goes up with every change to the cells: a plan worked out from
    /// a copy of them still holds while it has not moved.</summary>
    public int Version { get; private set; }

    /// <summary>The solid count it was built with: health is what is left of this.</summary>
    public readonly int InitialCount;

    public VoxelGrid(Vector3I size, byte[] cells, Vector3 origin, float voxelSize)
    {
        Size = size;
        Cells = cells;
        Origin = origin;
        VoxelSize = voxelSize;
        int count = 0;
        foreach (byte c in cells)
        {
            count += c != 0 ? 1 : 0;
        }
        SolidCount = count;
        InitialCount = count;
    }

    public int Index(int x, int y, int z) => x + Size.X * (y + Size.Y * z);

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && y >= 0 && z >= 0 && x < Size.X && y < Size.Y && z < Size.Z;

    public byte At(int x, int y, int z) => InBounds(x, y, z) ? Cells[Index(x, y, z)] : (byte)0;

    public Vector3I CellOf(int index) =>
        new(index % Size.X, (index / Size.X) % Size.Y, index / (Size.X * Size.Y));

    /// <summary>A cell's centre in the owning node's space.</summary>
    public Vector3 CellCentre(int x, int y, int z) =>
        VoxSpace.ToGodot(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) - Origin) * VoxelSize;

    /// <summary>A point in the owning node's space, in voxel coordinates.</summary>
    public Vector3 ToVox(Vector3 local) => VoxSpace.FromGodot(local / VoxelSize) + Origin;

    /// <summary>A voxel removed by a carve: where it was (node space) and its colour index.</summary>
    public readonly struct Removed
    {
        public readonly Vector3 Position;
        public readonly byte Colour;

        public Removed(Vector3 position, byte colour)
        {
            Position = position;
            Colour = colour;
        }
    }

    /// <summary>
    /// Removes every voxel whose centre is inside the sphere, at least the one
    /// the centre is in. Returns how many went, reports each one into
    /// <paramref name="removed"/> when given, and the box of cells touched.
    /// </summary>
    public int Carve(Vector3 localCentre, float radius, List<Removed> removed,
        out Vector3I touchedMin, out Vector3I touchedMax)
    {
        Vector3 c = ToVox(localCentre);
        float r = radius / VoxelSize;
        float r2 = r * r;
        int reach = Mathf.CeilToInt(r) + 1;
        var centre = new Vector3I(Mathf.FloorToInt(c.X), Mathf.FloorToInt(c.Y), Mathf.FloorToInt(c.Z));

        touchedMin = new Vector3I(
            Math.Max(0, centre.X - reach), Math.Max(0, centre.Y - reach), Math.Max(0, centre.Z - reach));
        touchedMax = new Vector3I(
            Math.Min(Size.X - 1, centre.X + reach), Math.Min(Size.Y - 1, centre.Y + reach), Math.Min(Size.Z - 1, centre.Z + reach));

        int count = 0;
        for (int z = touchedMin.Z; z <= touchedMax.Z; z++)
            for (int y = touchedMin.Y; y <= touchedMax.Y; y++)
                for (int x = touchedMin.X; x <= touchedMax.X; x++)
                {
                    int i = Index(x, y, z);
                    byte colour = Cells[i];
                    if (colour == 0)
                    {
                        continue;
                    }
                    float dx = x + 0.5f - c.X, dy = y + 0.5f - c.Y, dz = z + 0.5f - c.Z;
                    bool inside = dx * dx + dy * dy + dz * dz <= r2
                                  || (x == centre.X && y == centre.Y && z == centre.Z);
                    if (!inside)
                    {
                        continue;
                    }
                    Cells[i] = 0;
                    count++;
                    removed?.Add(new Removed(CellCentre(x, y, z), colour));
                }

        SolidCount -= count;
        if (count > 0)
        {
            Version++;
        }
        return count;
    }

    /// <summary>
    /// Walks a ray through the grid (node space in, node space out) and
    /// returns the first solid cell it enters within <paramref name="maxDistance"/>
    /// metres. Amanatides and Woo: one step per cell crossed.
    /// </summary>
    public bool Raycast(Vector3 localFrom, Vector3 localDirection, float maxDistance, out Vector3I cell)
    {
        cell = default;
        Vector3 o = ToVox(localFrom);
        Vector3 d = VoxSpace.FromGodot(localDirection.Normalized());
        float max = maxDistance / VoxelSize;

        // Start at the grid's box if the ray begins outside it.
        float tEnter = 0.0f, tExit = max;
        for (int a = 0; a < 3; a++)
        {
            float lo = 0.0f, hi = a == 0 ? Size.X : a == 1 ? Size.Y : Size.Z;
            if (Mathf.Abs(d[a]) < 1e-8f)
            {
                if (o[a] < lo || o[a] >= hi)
                {
                    return false;
                }
                continue;
            }
            float t0 = (lo - o[a]) / d[a], t1 = (hi - o[a]) / d[a];
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }
            tEnter = Mathf.Max(tEnter, t0);
            tExit = Mathf.Min(tExit, t1);
        }
        if (tEnter > tExit)
        {
            return false;
        }

        Vector3 p = o + d * (tEnter + 1e-4f);
        int x = Mathf.Clamp(Mathf.FloorToInt(p.X), 0, Size.X - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(p.Y), 0, Size.Y - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(p.Z), 0, Size.Z - 1);
        int sx = Math.Sign(d.X), sy = Math.Sign(d.Y), sz = Math.Sign(d.Z);
        float Next(float pos, int i, int s) => s > 0 ? i + 1 - pos : pos - i;
        float dtx = sx != 0 ? Mathf.Abs(1.0f / d.X) : float.MaxValue;
        float dty = sy != 0 ? Mathf.Abs(1.0f / d.Y) : float.MaxValue;
        float dtz = sz != 0 ? Mathf.Abs(1.0f / d.Z) : float.MaxValue;
        float tx = sx != 0 ? Next(p.X, x, sx) * dtx : float.MaxValue;
        float ty = sy != 0 ? Next(p.Y, y, sy) * dty : float.MaxValue;
        float tz = sz != 0 ? Next(p.Z, z, sz) * dtz : float.MaxValue;
        float travelled = tEnter;

        while (travelled <= tExit)
        {
            if (Cells[Index(x, y, z)] != 0)
            {
                cell = new Vector3I(x, y, z);
                return true;
            }
            if (tx <= ty && tx <= tz)
            {
                x += sx; travelled = tEnter + tx; tx += dtx;
            }
            else if (ty <= tz)
            {
                y += sy; travelled = tEnter + ty; ty += dty;
            }
            else
            {
                z += sz; travelled = tEnter + tz; tz += dtz;
            }
            if (!InBounds(x, y, z))
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>Box of the solid cells, in node space, or false when empty.</summary>
    public bool SolidBounds(out Aabb bounds)
    {
        bounds = default;
        if (SolidCount <= 0)
        {
            return false;
        }
        // Row by row, each searched from both ends with vector instructions:
        // a crushing tower asks every time it lands.
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        for (int z = 0; z < Size.Z; z++)
            for (int y = 0; y < Size.Y; y++)
            {
                ReadOnlySpan<byte> row = Cells.AsSpan(Index(0, y, z), Size.X);
                int first = row.IndexOfAnyExcept((byte)0);
                if (first < 0)
                {
                    continue;
                }
                int last = row.LastIndexOfAnyExcept((byte)0);
                min = new Vector3I(Math.Min(min.X, first), Math.Min(min.Y, y), Math.Min(min.Z, z));
                max = new Vector3I(Math.Max(max.X, last), Math.Max(max.Y, y), Math.Max(max.Z, z));
            }
        Vector3 a = VoxSpace.ToGodot(new Vector3(min.X, min.Y, min.Z) - Origin) * VoxelSize;
        Vector3 b = VoxSpace.ToGodot(new Vector3(max.X + 1, max.Y + 1, max.Z + 1) - Origin) * VoxelSize;
        bounds = new Aabb(a.Min(b), (a - b).Abs());
        return true;
    }

    // ---------------- Connectivity ----------------

    private int[] _label;
    private int[] _queue;

    /// <summary>
    /// Splits the solid cells into face-connected islands. Each is a list of
    /// cell indices; <paramref name="anchored"/> says which touch an anchor
    /// cell (<paramref name="isAnchor"/>). One flood fill over the grid.
    /// </summary>
    public List<List<int>> Islands(Func<int, bool> isAnchor, List<bool> anchored)
    {
        int n = Cells.Length;
        if (_label == null || _label.Length != n)
        {
            _label = new int[n];
            _queue = new int[n];
        }
        Array.Fill(_label, -1);
        var islands = new List<List<int>>();
        anchored.Clear();
        int sx = Size.X, sxy = Size.X * Size.Y;

        for (int start = 0; start < n; start++)
        {
            if (Cells[start] == 0 || _label[start] >= 0)
            {
                continue;
            }
            int id = islands.Count;
            var members = new List<int>();
            bool anchor = false;
            int head = 0, tail = 0;
            _queue[tail++] = start;
            _label[start] = id;
            while (head < tail)
            {
                int i = _queue[head++];
                members.Add(i);
                anchor |= isAnchor(i);
                int x = i % sx, y = (i / sx) % Size.Y, z = i / sxy;
                void Visit(int j)
                {
                    if (Cells[j] != 0 && _label[j] < 0)
                    {
                        _label[j] = id;
                        _queue[tail++] = j;
                    }
                }
                if (x > 0) Visit(i - 1);
                if (x < sx - 1) Visit(i + 1);
                if (y > 0) Visit(i - sx);
                if (y < Size.Y - 1) Visit(i + sx);
                if (z > 0) Visit(i - sxy);
                if (z < Size.Z - 1) Visit(i + sxy);
            }
            islands.Add(members);
            anchored.Add(anchor);
        }
        return islands;
    }

    /// <summary>
    /// Splits an island into pieces: cells with the same key that are
    /// face-connected to each other. A fracture gives each block its own key;
    /// one key for all gives the island's connected parts.
    /// </summary>
    public List<List<int>> Pieces(List<int> island, Func<int, int> key)
    {
        int n = Cells.Length;
        if (_label == null || _label.Length != n)
        {
            _label = new int[n];
            _queue = new int[n];
        }
        if (_key == null || _key.Length != n)
        {
            _key = new int[n];
        }
        // -1 not in the island, -2 in it and not yet reached.
        Array.Fill(_label, -1);
        foreach (int i in island)
        {
            _label[i] = -2;
            _key[i] = key(i);
        }

        var pieces = new List<List<int>>();
        int sx = Size.X, sxy = Size.X * Size.Y;
        foreach (int start in island)
        {
            if (_label[start] != -2)
            {
                continue;
            }
            int id = pieces.Count;
            int k = _key[start];
            var members = new List<int>();
            int head = 0, tail = 0;
            _queue[tail++] = start;
            _label[start] = id;
            while (head < tail)
            {
                int i = _queue[head++];
                members.Add(i);
                int x = i % sx, y = (i / sx) % Size.Y, z = i / sxy;
                void Visit(int j)
                {
                    if (_label[j] == -2 && _key[j] == k)
                    {
                        _label[j] = id;
                        _queue[tail++] = j;
                    }
                }
                if (x > 0) Visit(i - 1);
                if (x < sx - 1) Visit(i + 1);
                if (y > 0) Visit(i - sx);
                if (y < Size.Y - 1) Visit(i + sx);
                if (z > 0) Visit(i - sxy);
                if (z < Size.Z - 1) Visit(i + sxy);
            }
            pieces.Add(members);
        }
        return pieces;
    }

    private int[] _key;

    /// <summary>Clears the given cells, reporting each into <paramref name="removed"/>.</summary>
    public void Remove(List<int> cells, List<Removed> removed)
    {
        foreach (int i in cells)
        {
            if (Cells[i] == 0)
            {
                continue;
            }
            if (removed != null)
            {
                Vector3I c = CellOf(i);
                removed.Add(new Removed(CellCentre(c.X, c.Y, c.Z), Cells[i]));
            }
            Cells[i] = 0;
            SolidCount--;
        }
        Version++;
    }

    /// <summary>
    /// Lifts an island out into a grid of its own, sized to it, with its
    /// origin at its centre of mass so a rigid body built on it turns about
    /// the right point. The cells are cleared here. <paramref name="offset"/>
    /// is where the new grid's origin sits in this grid's node space.
    /// </summary>
    public VoxelGrid Extract(List<int> island, out Vector3 offset, int align = 1) => Copy(island, out offset, align, clear: true);

    /// <summary>The same, leaving its cells where they are: for working out
    /// a split from a copy of a grid.</summary>
    public VoxelGrid Copy(List<int> island, out Vector3 offset, int align, bool clear)
    {
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        Vector3 sum = Vector3.Zero;
        foreach (int i in island)
        {
            Vector3I c = CellOf(i);
            min = new Vector3I(Math.Min(min.X, c.X), Math.Min(min.Y, c.Y), Math.Min(min.Z, c.Z));
            max = new Vector3I(Math.Max(max.X, c.X), Math.Max(max.Y, c.Y), Math.Max(max.Z, c.Z));
            sum += new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);
        }
        Vector3 centre = sum / island.Count;
        // Started on a multiple of align, so the new grid's sections line up
        // with this one's.
        min = min / align * align;
        Vector3I size = max - min + Vector3I.One;
        byte[] cells = new byte[size.X * size.Y * size.Z];
        foreach (int i in island)
        {
            Vector3I c = CellOf(i) - min;
            cells[c.X + size.X * (c.Y + size.Y * c.Z)] = Cells[i];
            if (clear)
            {
                Cells[i] = 0;
            }
        }
        if (clear)
        {
            SolidCount -= island.Count;
            Version++;
        }
        offset = VoxSpace.ToGodot(centre - Origin) * VoxelSize;
        return new VoxelGrid(size, cells, centre - new Vector3(min.X, min.Y, min.Z), VoxelSize);
    }

    // ---------------- Collision ----------------

    /// <summary>
    /// Covers the solid cells of a region with boxes: greedy, X then Y then Z,
    /// so a solid block is one box and a wall a few. Node space, as
    /// (centre, size). Each cell's box is at most <paramref name="step"/>
    /// cells coarse: at 2, a 2x2x2 block counts as solid if any of it is.
    /// </summary>
    public void Boxes(Vector3I regionMin, Vector3I regionMax, int step, List<(Vector3 centre, Vector3 size)> into)
    {
        step = Math.Max(1, step);
        Vector3I cells = new(
            (regionMax.X - regionMin.X + step - 1) / step,
            (regionMax.Y - regionMin.Y + step - 1) / step,
            (regionMax.Z - regionMin.Z + step - 1) / step);
        int count = cells.X * cells.Y * cells.Z;
        if (count <= 0)
        {
            return;
        }
        bool[] solid = new bool[count];
        bool[] used = new bool[count];
        for (int z = 0; z < cells.Z; z++)
            for (int y = 0; y < cells.Y; y++)
                for (int x = 0; x < cells.X; x++)
                {
                    bool any = false;
                    for (int dz = 0; dz < step && !any; dz++)
                        for (int dy = 0; dy < step && !any; dy++)
                            for (int dx = 0; dx < step && !any; dx++)
                            {
                                int gx = regionMin.X + x * step + dx, gy = regionMin.Y + y * step + dy, gz = regionMin.Z + z * step + dz;
                                any = gx < regionMax.X && gy < regionMax.Y && gz < regionMax.Z && At(gx, gy, gz) != 0;
                            }
                    solid[x + cells.X * (y + cells.Y * z)] = any;
                }

        int I(int x, int y, int z) => x + cells.X * (y + cells.Y * z);
        for (int z = 0; z < cells.Z; z++)
            for (int y = 0; y < cells.Y; y++)
                for (int x = 0; x < cells.X; x++)
                {
                    if (!solid[I(x, y, z)] || used[I(x, y, z)])
                    {
                        continue;
                    }
                    int w = 1;
                    while (x + w < cells.X && solid[I(x + w, y, z)] && !used[I(x + w, y, z)]) w++;
                    int h = 1;
                    while (y + h < cells.Y && RowFree(x, y + h, z, w)) h++;
                    int d = 1;
                    while (z + d < cells.Z && SlabFree(x, y, z + d, w, h)) d++;
                    for (int dz = 0; dz < d; dz++)
                        for (int dy = 0; dy < h; dy++)
                            for (int dx = 0; dx < w; dx++)
                                used[I(x + dx, y + dy, z + dz)] = true;

                    var lo = new Vector3I(regionMin.X + x * step, regionMin.Y + y * step, regionMin.Z + z * step);
                    var hi = new Vector3I(
                        Math.Min(regionMin.X + (x + w) * step, regionMax.X),
                        Math.Min(regionMin.Y + (y + h) * step, regionMax.Y),
                        Math.Min(regionMin.Z + (z + d) * step, regionMax.Z));
                    if (step > 1)
                    {
                        // A coarse box shrinks to the voxels in it: otherwise
                        // it overhangs them by up to a step, and a piece rests
                        // on air or comes up inside what is under it.
                        Tighten(ref lo, ref hi);
                    }
                    Vector3 a = VoxSpace.ToGodot(lo - Origin) * VoxelSize;
                    Vector3 b = VoxSpace.ToGodot(hi - Origin) * VoxelSize;
                    into.Add(((a + b) * 0.5f, (a - b).Abs()));
                }

        void Tighten(ref Vector3I lo, ref Vector3I hi)
        {
            var min = hi;
            var max = lo - Vector3I.One;
            for (int gz = lo.Z; gz < hi.Z; gz++)
                for (int gy = lo.Y; gy < hi.Y; gy++)
                {
                    int row = Index(0, gy, gz);
                    for (int gx = lo.X; gx < hi.X; gx++)
                    {
                        if (Cells[row + gx] != 0)
                        {
                            min = min.Min(new Vector3I(gx, gy, gz));
                            max = max.Max(new Vector3I(gx, gy, gz));
                        }
                    }
                }
            if (max.X >= min.X)
            {
                lo = min;
                hi = max + Vector3I.One;
            }
        }

        bool RowFree(int x, int y, int z, int w)
        {
            for (int dx = 0; dx < w; dx++)
                if (!solid[I(x + dx, y, z)] || used[I(x + dx, y, z)]) return false;
            return true;
        }

        bool SlabFree(int x, int y, int z, int w, int h)
        {
            for (int dy = 0; dy < h; dy++)
                if (!RowFree(x, y + dy, z, w)) return false;
            return true;
        }
    }
}
