using System;
using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// What the importer bakes for one object: irregular chunks of about a metre,
/// the faces they share, collision boxes and per-layer sums. No nodes, so the
/// importer and the load-time rehearsal both call it (docs/decisions/0027).
/// </summary>
public sealed class ChunkBake
{
    public int[] CellChunk;
    public int Count;
    public int[] Solid;
    public int[] Min;
    public int[] Max;
    public int Layers;
    public int[] LayerCount;
    public float[] LayerSumX;
    public float[] LayerSumY;
    public int[] EdgeA;
    public int[] EdgeB;
    public int[] EdgeFaces;
    public int[] BoxOffset;
    public Vector3[] BoxCentre;
    public Vector3[] BoxSize;
    public int[] GroundCells;
}

/// <summary>Groups a model's cells into chunks. Thread-safe: nothing of Godot's but maths.</summary>
public static class VoxChunker
{
    public const int FormatVersion = 1;

    /// <summary>A full chunk heavier than this cannot be torn out by the gravity
    /// gun (<see cref="VoxelDestructionStats.MaxGrabVoxels"/>'s default).</summary>
    public const int GrabWarnVoxels = 600;

    /// <param name="kind">Material type per palette index. 0 shares with every
    /// other 0; any other value never shares a chunk with a different one.
    /// Null: one type.</param>
    public static ChunkBake Build(byte[] cells, Vector3I size, byte[] kind, int chunkSize, int jitter, int seed,
        Vector3 origin, float voxelSize)
    {
        int n = size.X * size.Y * size.Z;
        if (n == 0 || cells == null || cells.Length < n)
        {
            return null;
        }
        chunkSize = Math.Max(2, chunkSize);
        jitter = Math.Max(0, Math.Min(jitter, chunkSize / 2));

        var preliminary = new int[n];
        var keys = new Dictionary<long, int>();
        int next = 0;
        bool any = false;
        for (int z = 0; z < size.Z; z++)
            for (int y = 0; y < size.Y; y++)
                for (int x = 0; x < size.X; x++)
                {
                    int i = x + size.X * (y + size.Y * z);
                    if (cells[i] == 0)
                    {
                        preliminary[i] = -1;
                        continue;
                    }
                    any = true;
                    int bx = Bucket(x, y + z * 1024, 0, chunkSize, jitter, seed);
                    int by = Bucket(y, x + z * 1024, 1, chunkSize, jitter, seed);
                    int bz = Bucket(z, x + y * 1024, 2, chunkSize, jitter, seed);
                    int k = kind == null || cells[i] >= kind.Length ? 0 : kind[cells[i]];
                    long key = ((long)(bx + 4096) << 42) ^ ((long)(by + 4096) << 21) ^ (uint)(bz + 4096) ^ ((long)k << 60);
                    if (!keys.TryGetValue(key, out int id))
                    {
                        id = next++;
                        keys[key] = id;
                    }
                    preliminary[i] = id;
                }
        if (!any)
        {
            return null;
        }

        // One connected island per chunk: a lattice cell split by a window
        // becomes two chunks, not one that falls in two directions.
        var finalId = new int[n];
        Array.Fill(finalId, -1);
        var queue = new int[n];
        int count = 0;
        int sx = size.X, sxy = size.X * size.Y;
        for (int start = 0; start < n; start++)
        {
            if (preliminary[start] < 0 || finalId[start] >= 0)
            {
                continue;
            }
            int id = count++;
            int pre = preliminary[start];
            int head = 0, tail = 0;
            queue[tail++] = start;
            finalId[start] = id;
            while (head < tail)
            {
                int i = queue[head++];
                int x = i % sx, y = (i / sx) % size.Y, z = i / sxy;
                void Visit(int j)
                {
                    if (preliminary[j] == pre && finalId[j] < 0)
                    {
                        finalId[j] = id;
                        queue[tail++] = j;
                    }
                }
                if (x > 0) Visit(i - 1);
                if (x < sx - 1) Visit(i + 1);
                if (y > 0) Visit(i - sx);
                if (y < size.Y - 1) Visit(i + sx);
                if (z > 0) Visit(i - sxy);
                if (z < size.Z - 1) Visit(i + sxy);
            }
        }

        var solid = new int[count];
        for (int i = 0; i < n; i++)
        {
            if (finalId[i] >= 0)
            {
                solid[finalId[i]]++;
            }
        }

        // Slivers join the neighbour they share the most faces with.
        int sliver = Math.Max(6, chunkSize * chunkSize / 4);
        // A sliver joins a neighbour, and that neighbour stops taking them
        // once it is a full chunk. Left open, a thin wall's slivers walk
        // into one chunk the height of the building, and the cut's flood
        // then scans the whole wall.
        int cap = chunkSize * chunkSize * chunkSize;
        bool merged = true;
        var remap = new int[count];
        for (int i = 0; i < count; i++)
        {
            remap[i] = i;
        }
        int Guard = count;
        int Root(int id)
        {
            int guard = count;
            while (remap[id] != id && guard-- > 0)
            {
                id = remap[id];
            }
            return id;
        }
        while (merged && Guard-- > 0)
        {
            merged = false;
            var touch = new Dictionary<long, int>();
            for (int i = 0; i < n; i++)
            {
                int a = finalId[i];
                if (a < 0)
                {
                    continue;
                }
                a = Root(a);
                int x = i % sx, y = (i / sx) % size.Y, z = i / sxy;
                void Face(int j)
                {
                    int b = finalId[j];
                    if (b < 0)
                    {
                        return;
                    }
                    b = Root(b);
                    if (a == b)
                    {
                        return;
                    }
                    long key = a < b ? a + (long)b * count : b + (long)a * count;
                    touch.TryGetValue(key, out int faces);
                    touch[key] = faces + 1;
                }
                if (x < sx - 1) Face(i + 1);
                if (y < size.Y - 1) Face(i + sx);
                if (z < size.Z - 1) Face(i + sxy);
            }
            for (int c = 0; c < count; c++)
            {
                int id = remap[c];
                if (solid[c] <= 0 || solid[c] >= sliver || remap[c] != c)
                {
                    continue;
                }
                int best = -1, bestFaces = 0;
                foreach ((long key, int faces) in touch)
                {
                    int a = (int)(key % count), b = (int)(key / count);
                    int other = a == id ? b : b == id ? a : -1;
                    if (other < 0 || solid[other] + solid[c] > cap || faces <= bestFaces)
                    {
                        continue;
                    }
                    bestFaces = faces;
                    best = other;
                }
                if (best < 0)
                {
                    continue;
                }
                remap[c] = best;
                solid[best] += solid[c];
                solid[c] = 0;
                merged = true;
            }
        }
        // The remap above only follows one step. Fold it, then pack the ids.
        for (int c = 0; c < count; c++)
        {
            int id = c;
            int guard = count;
            while (remap[id] != id && guard-- > 0)
            {
                id = remap[id];
            }
            remap[c] = id;
        }
        var packed = new int[count];
        Array.Fill(packed, -1);
        int packedCount = 0;
        for (int c = 0; c < count; c++)
        {
            if (solid[c] > 0 && remap[c] == c && packed[c] < 0)
            {
                packed[c] = packedCount++;
            }
        }
        for (int i = 0; i < n; i++)
        {
            int id = finalId[i];
            finalId[i] = id < 0 ? -1 : packed[remap[id]];
        }

        var bake = new ChunkBake
        {
            CellChunk = finalId,
            Count = packedCount,
            Solid = new int[packedCount],
            Min = new int[packedCount * 3],
            Max = new int[packedCount * 3],
            Layers = size.Z,
            LayerCount = new int[packedCount * size.Z],
            LayerSumX = new float[packedCount * size.Z],
            LayerSumY = new float[packedCount * size.Z],
            GroundCells = new int[packedCount],
        };
        for (int c = 0; c < packedCount; c++)
        {
            bake.Min[c * 3] = int.MaxValue;
            bake.Min[c * 3 + 1] = int.MaxValue;
            bake.Min[c * 3 + 2] = int.MaxValue;
            bake.Max[c * 3] = int.MinValue;
            bake.Max[c * 3 + 1] = int.MinValue;
            bake.Max[c * 3 + 2] = int.MinValue;
        }
        var edge = new Dictionary<long, int>();
        for (int z = 0; z < size.Z; z++)
            for (int y = 0; y < size.Y; y++)
                for (int x = 0; x < size.X; x++)
                {
                    int i = x + size.X * (y + size.Y * z);
                    int id = finalId[i];
                    if (id < 0)
                    {
                        continue;
                    }
                    bake.Solid[id]++;
                    int o = id * 3;
                    bake.Min[o] = Math.Min(bake.Min[o], x);
                    bake.Min[o + 1] = Math.Min(bake.Min[o + 1], y);
                    bake.Min[o + 2] = Math.Min(bake.Min[o + 2], z);
                    bake.Max[o] = Math.Max(bake.Max[o], x);
                    bake.Max[o + 1] = Math.Max(bake.Max[o + 1], y);
                    bake.Max[o + 2] = Math.Max(bake.Max[o + 2], z);
                    int layer = id * size.Z + z;
                    bake.LayerCount[layer]++;
                    bake.LayerSumX[layer] += x + 0.5f;
                    bake.LayerSumY[layer] += y + 0.5f;
                    if (z == 0)
                    {
                        bake.GroundCells[id]++;
                    }
                    void Edge(int nx, int ny, int nz)
                    {
                        int j = nx + size.X * (ny + size.Y * nz);
                        int other = finalId[j];
                        if (other < 0 || other == id)
                        {
                            return;
                        }
                        int a = id < other ? id : other, b = id < other ? other : id;
                        long key = a + (long)b * packedCount;
                        edge.TryGetValue(key, out int faces);
                        edge[key] = faces + 1;
                    }
                    if (x + 1 < size.X) Edge(x + 1, y, z);
                    if (y + 1 < size.Y) Edge(x, y + 1, z);
                    if (z + 1 < size.Z) Edge(x, y, z + 1);
                }

        bake.EdgeA = new int[edge.Count];
        bake.EdgeB = new int[edge.Count];
        bake.EdgeFaces = new int[edge.Count];
        int e = 0;
        foreach ((long key, int faces) in edge)
        {
            bake.EdgeA[e] = (int)(key % packedCount);
            bake.EdgeB[e] = (int)(key / packedCount);
            bake.EdgeFaces[e] = faces;
            e++;
        }

        var centres = new List<Vector3>();
        var sizes = new List<Vector3>();
        bake.BoxOffset = new int[packedCount + 1];
        for (int c = 0; c < packedCount; c++)
        {
            bake.BoxOffset[c] = centres.Count;
            Boxes(cells, size, finalId, c, origin, voxelSize, centres, sizes);
        }
        bake.BoxOffset[packedCount] = centres.Count;
        bake.BoxCentre = centres.ToArray();
        bake.BoxSize = sizes.ToArray();
        return bake;
    }

    /// <summary>Palette index to a material-type byte. Untyped indices share 0.</summary>
    public static byte[] KindOf(string[] materialType)
    {
        var kind = new byte[256];
        var ids = new Dictionary<string, byte>();
        byte next = 1;
        for (int i = 1; i < 256 && materialType != null; i++)
        {
            string type = i < materialType.Length ? materialType[i] : null;
            if (string.IsNullOrEmpty(type))
            {
                continue;
            }
            if (!ids.TryGetValue(type, out byte id))
            {
                id = next == 255 ? (byte)1 : next++;
                ids[type] = id;
            }
            kind[i] = id;
        }
        return kind;
    }

    public static int SeedOf(string path)
    {
        int seed = 17;
        if (path == null)
        {
            return seed;
        }
        foreach (char ch in path)
        {
            seed = unchecked(seed * 31 + ch);
        }
        return seed == 0 ? 1 : seed;
    }

    private static int Bucket(int coord, int line, int axis, int chunkSize, int jitter, int seed)
    {
        int n = coord / chunkSize;
        if (jitter <= 0)
        {
            return n;
        }
        int shift = (int)Math.Round(Signed(n, line, axis, seed) * jitter);
        if (coord < n * chunkSize + shift && n > 0)
        {
            n--;
        }
        int next = (int)Math.Round(Signed(n + 1, line, axis, seed) * jitter);
        if (coord >= (n + 1) * chunkSize + next)
        {
            n++;
        }
        return n < 0 ? 0 : n;
    }

    private static float Signed(int a, int b, int c, int seed)
    {
        uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791) ^ (uint)seed;
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xffff) / 32767.5f - 1.0f;
    }

    /// <summary>Greedy boxes over one chunk's cells, in the part's node space.</summary>
    private static void Boxes(byte[] cells, Vector3I size, int[] chunk, int id, Vector3 origin, float voxelSize,
        List<Vector3> centres, List<Vector3> sizes)
    {
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        int sx = size.X, sxy = size.X * size.Y;
        for (int i = 0; i < chunk.Length; i++)
        {
            if (chunk[i] != id)
            {
                continue;
            }
            int x = i % sx, y = (i / sx) % size.Y, z = i / sxy;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, z);
        }
        if (minX == int.MaxValue)
        {
            return;
        }
        int dx = maxX - minX + 1, dy = maxY - minY + 1, dz = maxZ - minZ + 1;
        var solid = new bool[dx * dy * dz];
        var used = new bool[solid.Length];
        for (int z = 0; z < dz; z++)
            for (int y = 0; y < dy; y++)
                for (int x = 0; x < dx; x++)
                {
                    int i = (minX + x) + sx * ((minY + y) + size.Y * (minZ + z));
                    solid[x + dx * (y + dy * z)] = chunk[i] == id;
                }
        int I(int x, int y, int z) => x + dx * (y + dy * z);
        bool Row(int x, int y, int z, int w)
        {
            for (int k = 0; k < w; k++)
            {
                if (!solid[I(x + k, y, z)] || used[I(x + k, y, z)])
                {
                    return false;
                }
            }
            return true;
        }
        for (int z = 0; z < dz; z++)
            for (int y = 0; y < dy; y++)
                for (int x = 0; x < dx; x++)
                {
                    if (!solid[I(x, y, z)] || used[I(x, y, z)])
                    {
                        continue;
                    }
                    int w = 1;
                    while (x + w < dx && solid[I(x + w, y, z)] && !used[I(x + w, y, z)])
                    {
                        w++;
                    }
                    int h = 1;
                    while (y + h < dy && Row(x, y + h, z, w))
                    {
                        h++;
                    }
                    int d = 1;
                    while (z + d < dz)
                    {
                        bool ok = true;
                        for (int yy = 0; yy < h && ok; yy++)
                        {
                            ok = Row(x, y + yy, z + d, w);
                        }
                        if (!ok)
                        {
                            break;
                        }
                        d++;
                    }
                    for (int zz = 0; zz < d; zz++)
                        for (int yy = 0; yy < h; yy++)
                            for (int xx = 0; xx < w; xx++)
                            {
                                used[I(x + xx, y + yy, z + zz)] = true;
                            }
                    var lo = new Vector3(minX + x, minY + y, minZ + z);
                    var hi = new Vector3(minX + x + w, minY + y + h, minZ + z + d);
                    Vector3 a = VoxSpace.ToGodot(lo - origin) * voxelSize;
                    Vector3 b = VoxSpace.ToGodot(hi - origin) * voxelSize;
                    centres.Add((a + b) * 0.5f);
                    sizes.Add((a - b).Abs());
                }
    }
}
