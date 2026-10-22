using System;
using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// One released piece's chunks, or a building's, copied so a worker can build
/// the piece while the building keeps being carved (docs/decisions/0027).
/// </summary>
public sealed class GraphSnapshot
{
    public int[] CellChunk;
    public int Count;
    public int[] Solid;
    public Vector3I[] Min;
    public Vector3I[] Max;
    public int[] BoxOffset;
    public Vector3[] BoxCentre;
    public Vector3[] BoxSize;
    /// <summary>Chunks whose baked boxes no longer describe them (re-split).</summary>
    public bool[] Stale;
    public int[] LinkA;
    public int[] LinkB;
    public int[] LinkLive;
    public int[] LinkBuilt;
}

/// <summary>What strain tells the part that owns the graph: a creak (dust,
/// a sound, a shake), an edge let go, a small chunk to shed.</summary>
internal interface IStrainHost
{
    void StrainBegan(Vector3 local);
    void StrainFailed();
    void StrainShed(int chunk);
}

/// <summary>What the graph decided, for the worker that builds the pieces.</summary>
internal sealed class GraphDecision
{
    public bool Changed;
    public bool Emptied;
    public bool Collapsed;
    public bool BreakingUp;
    public Vector2 Support;
    public Vector2 Lean;
    public float OffCentre;
    public int Fail;
    public int Top;
    public bool Held;
    public List<int>[] PieceChunks = Array.Empty<List<int>>();
    public VoxelSpin[] PieceSpin = Array.Empty<VoxelSpin>();
    public bool[] PieceHinge = Array.Empty<bool>();
    public List<int> DustCells = new();
    public List<int> CrushedChunks = new();
    public GraphSnapshot Snapshot;
}

/// <summary>
/// The chunks of one part and how they hold each other. Baked by the importer
/// for a building, and carried by each piece that breaks off it. A carve
/// updates it as the cells go, so the structural check walks chunks, and a
/// chunk the cut actually split is flooded on its own (docs/decisions/0027).
/// </summary>
public sealed class VoxelChunkGraph
{
    private struct Link
    {
        public int Other;
        public int Live;
        public int Built;
        public float Weaken;
        public bool Shook;
    }

    private int[] _cell;
    private int _count;
    private int[] _solid;
    private int[] _ground;
    private Vector3I[] _min;
    private Vector3I[] _max;
    private int _layers;
    private int[] _layerCount;
    private float[] _layerSumX;
    private float[] _layerSumY;
    private List<Link>[] _links;
    private int[] _boxOffset;
    private Vector3[] _boxCentre;
    private Vector3[] _boxSize;
    /// <summary>A chunk a cut re-split keeps its id and its baked boxes,
    /// which still cover the cells that went to its new neighbours (or to a
    /// piece); its boxes are its cell bounds from then on.</summary>
    private bool[] _stale;
    private readonly HashSet<int> _dirty = new();
    private readonly List<int> _orphans = new();
    private readonly HashSet<int> _fling = new();
    private float _strainTick;
    // Some edge is weakening: only then is every edge walked each frame. A
    // nest of vines holds a hundred graphs that never strain (0024).
    private bool _straining;
    private float _shedWait;

    public int Count => _count;

    public static VoxelChunkGraph FromBake(ChunkBake bake)
    {
        if (bake == null || bake.Count <= 0)
        {
            return null;
        }
        var graph = new VoxelChunkGraph
        {
            _cell = (int[])bake.CellChunk.Clone(),
            _count = bake.Count,
            _solid = (int[])bake.Solid.Clone(),
            _ground = (int[])bake.GroundCells.Clone(),
            _layers = bake.Layers,
            _layerCount = (int[])bake.LayerCount.Clone(),
            _layerSumX = (float[])bake.LayerSumX.Clone(),
            _layerSumY = (float[])bake.LayerSumY.Clone(),
            _boxOffset = (int[])bake.BoxOffset.Clone(),
            _stale = new bool[bake.Count],
            _boxCentre = (Vector3[])bake.BoxCentre.Clone(),
            _boxSize = (Vector3[])bake.BoxSize.Clone(),
            _min = new Vector3I[bake.Count],
            _max = new Vector3I[bake.Count],
            _links = new List<Link>[bake.Count],
        };
        for (int c = 0; c < bake.Count; c++)
        {
            graph._min[c] = new Vector3I(bake.Min[c * 3], bake.Min[c * 3 + 1], bake.Min[c * 3 + 2]);
            graph._max[c] = new Vector3I(bake.Max[c * 3], bake.Max[c * 3 + 1], bake.Max[c * 3 + 2]);
            graph._links[c] = new List<Link>();
        }
        for (int e = 0; e < bake.EdgeA.Length; e++)
        {
            graph.AddLink(bake.EdgeA[e], bake.EdgeB[e], bake.EdgeFaces[e], bake.EdgeFaces[e]);
        }
        return graph;
    }

    /// <summary>The importer's meta, or null when this model has none yet.</summary>
    public static VoxelChunkGraph TryRead(Node node, int cells, string name)
    {
        if (node == null || !node.HasMeta(VoxelMeta.ChunkVersion))
        {
            return null;
        }
        int version = node.GetMeta(VoxelMeta.ChunkVersion).AsInt32();
        if (version != VoxChunker.FormatVersion)
        {
            GD.PushWarning($"VoxelChunkGraph: '{name}' has chunk version {version}; reimport the .vox.");
            return null;
        }
        int[] cell = node.GetMeta(VoxelMeta.ChunkCells).AsInt32Array();
        if (cell.Length != cells)
        {
            GD.PushWarning($"VoxelChunkGraph: '{name}' chunk meta does not match its voxels; reimport the .vox.");
            return null;
        }
        int count = node.GetMeta(VoxelMeta.ChunkCount).AsInt32();
        var bake = new ChunkBake
        {
            CellChunk = cell,
            Count = count,
            Solid = node.GetMeta(VoxelMeta.ChunkSolid).AsInt32Array(),
            Min = node.GetMeta(VoxelMeta.ChunkMin).AsInt32Array(),
            Max = node.GetMeta(VoxelMeta.ChunkMax).AsInt32Array(),
            Layers = node.GetMeta(VoxelMeta.ChunkLayers).AsInt32(),
            LayerCount = node.GetMeta(VoxelMeta.ChunkLayerCount).AsInt32Array(),
            LayerSumX = node.GetMeta(VoxelMeta.ChunkLayerSumX).AsFloat32Array(),
            LayerSumY = node.GetMeta(VoxelMeta.ChunkLayerSumY).AsFloat32Array(),
            EdgeA = node.GetMeta(VoxelMeta.ChunkEdgeA).AsInt32Array(),
            EdgeB = node.GetMeta(VoxelMeta.ChunkEdgeB).AsInt32Array(),
            EdgeFaces = node.GetMeta(VoxelMeta.ChunkEdgeFaces).AsInt32Array(),
            BoxOffset = node.GetMeta(VoxelMeta.ChunkBoxOffset).AsInt32Array(),
            BoxCentre = node.GetMeta(VoxelMeta.ChunkBoxCentre).AsVector3Array(),
            BoxSize = node.GetMeta(VoxelMeta.ChunkBoxSize).AsVector3Array(),
            GroundCells = node.GetMeta(VoxelMeta.ChunkGround).AsInt32Array(),
        };
        return FromBake(bake);
    }

    /// <summary>A cell just went. Counts, the layer sums and the shared faces follow.</summary>
    public void NoteRemoved(VoxelGrid grid, int index)
    {
        if (_cell == null || (uint)index >= (uint)_cell.Length)
        {
            return;
        }
        int chunk = _cell[index];
        if (chunk < 0 || chunk >= _count)
        {
            return;
        }
        _cell[index] = -1;
        _solid[chunk] = Math.Max(0, _solid[chunk] - 1);
        _stale[chunk] = true;
        Vector3I c = grid.CellOf(index);
        int layer = chunk * _layers + c.Z;
        if ((uint)layer < (uint)_layerCount.Length)
        {
            _layerCount[layer] = Math.Max(0, _layerCount[layer] - 1);
            _layerSumX[layer] -= c.X + 0.5f;
            _layerSumY[layer] -= c.Y + 0.5f;
        }
        if (c.Z == 0)
        {
            _ground[chunk] = Math.Max(0, _ground[chunk] - 1);
        }
        int sx = grid.Size.X, sxy = grid.Size.X * grid.Size.Y;
        void Neighbour(int j)
        {
            if ((uint)j >= (uint)_cell.Length || grid.Cells[j] == 0)
            {
                return;
            }
            int other = _cell[j];
            if (other < 0 || other == chunk)
            {
                return;
            }
            LoseFace(chunk, other);
        }
        int x = c.X, y = c.Y, z = c.Z;
        if (x > 0) Neighbour(index - 1);
        if (x + 1 < grid.Size.X) Neighbour(index + 1);
        if (y > 0) Neighbour(index - sx);
        if (y + 1 < grid.Size.Y) Neighbour(index + sx);
        if (z > 0) Neighbour(index - sxy);
        if (z + 1 < grid.Size.Z) Neighbour(index + sxy);
        _dirty.Add(chunk);
    }

    public int SolidOf(int chunk) => (uint)chunk < (uint)_count ? _solid[chunk] : 0;

    public int ChunkOf(int index) => _cell != null && (uint)index < (uint)_cell.Length ? _cell[index] : -1;

    public int ContactFaces(int chunk)
    {
        if ((uint)chunk >= (uint)_count)
        {
            return 0;
        }
        int faces = 0;
        foreach (Link link in _links[chunk])
        {
            faces += link.Live;
        }
        return faces;
    }

    /// <summary>The chunk a voxel point is in, or -1.</summary>
    public int ChunkAt(VoxelGrid grid, Vector3 vox)
    {
        int x = Mathf.FloorToInt(vox.X), y = Mathf.FloorToInt(vox.Y), z = Mathf.FloorToInt(vox.Z);
        if (!grid.InBounds(x, y, z))
        {
            return -1;
        }
        int i = grid.Index(x, y, z);
        if (grid.Cells[i] == 0)
        {
            // The ray stops on the face, just outside the voxel.
            for (int dz = 0; dz <= 1 && z - dz >= 0; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy, nz = z - dz;
                        if (!grid.InBounds(nx, ny, nz))
                        {
                            continue;
                        }
                        int j = grid.Index(nx, ny, nz);
                        if (grid.Cells[j] != 0 && _cell[j] >= 0)
                        {
                            return _cell[j];
                        }
                    }
            return -1;
        }
        return _cell[i];
    }

    /// <summary>Every live cell of one chunk.</summary>
    public void CellsOf(VoxelGrid grid, int chunk, List<int> into)
    {
        if ((uint)chunk >= (uint)_count || _solid[chunk] <= 0)
        {
            return;
        }
        Vector3I min = _min[chunk], max = _max[chunk];
        for (int z = min.Z; z <= max.Z; z++)
            for (int y = min.Y; y <= max.Y; y++)
                for (int x = min.X; x <= max.X; x++)
                {
                    int i = grid.Index(x, y, z);
                    if (_cell[i] == chunk && grid.Cells[i] != 0)
                    {
                        into.Add(i);
                    }
                }
    }

    /// <summary>Chunks whose centre lies in a crush slab, in voxel space.</summary>
    public void ChunksInSlab(Vector3 from, Vector3 along, float deep, float wide2, List<int> into)
    {
        for (int c = 0; c < _count; c++)
        {
            if (_solid[c] <= 0)
            {
                continue;
            }
            Vector3 centre = V(_min[c] + _max[c]) * 0.5f + new Vector3(0.5f, 0.5f, 0.5f);
            Vector3 q = centre - from;
            float d = q.Dot(along);
            if (d < -1.0f || d > deep || q.LengthSquared() - d * d > wide2)
            {
                continue;
            }
            into.Add(c);
        }
    }

    /// <summary>
    /// The baked boxes, when every chunk is as it was baked and they fit in
    /// <paramref name="maxBoxes"/>: exact, and finer than the worker's. False
    /// otherwise, and the worker's boxes, made from the cells, stay. A chunk
    /// a shot took cells from, or a cut re-split, has boxes over cells that
    /// are gone or belong to another piece.
    /// </summary>
    public bool TryBoxes(int maxBoxes, List<(Vector3 centre, Vector3 size)> into)
    {
        for (int c = 0; c < _count; c++)
        {
            if (_stale[c] && _solid[c] > 0)
            {
                return false;
            }
        }
        int baked = _boxOffset[_count];
        if (baked <= 0 || baked > maxBoxes)
        {
            return false;
        }
        for (int i = 0; i < baked; i++)
        {
            into.Add((_boxCentre[i], _boxSize[i]));
        }
        return true;
    }

    /// <summary>Damaged edges that still carry something above them weaken,
    /// then let go. A cut with nothing left holding does not come here: it
    /// has already failed the check. <paramref name="stressSeconds"/> at 0
    /// does nothing.</summary>
    internal void TickStrain(float dt, VoxelDestructionStats stats, IStrainHost host)
    {
        if (stats.StressSeconds <= 0.0f || _count == 0)
        {
            return;
        }
        _strainTick -= dt;
        if (_strainTick <= 0.0f)
        {
            _strainTick = 0.25f;
            ConsiderStrain(stats);
        }
        if (!_straining)
        {
            return;
        }
        bool any = false;
        for (int c = 0; c < _count; c++)
        {
            List<Link> links = _links[c];
            for (int i = 0; i < links.Count; i++)
            {
                Link link = links[i];
                if (link.Weaken < 0.0f || link.Live <= 0 || c > link.Other)
                {
                    continue;
                }
                any = true;
                link.Weaken -= dt;
                if (!link.Shook)
                {
                    link.Shook = true;
                    host.StrainBegan(Centre(c));
                }
                links[i] = link;
                Mirror(c, link);
                if (link.Weaken > 0.0f)
                {
                    continue;
                }
                link.Live = 0;
                links[i] = link;
                Mirror(c, link);
                _dirty.Add(c);
                _dirty.Add(link.Other);
                host.StrainFailed();
            }
        }
        if (!any)
        {
            _straining = false;
            return;
        }
        _shedWait -= dt;
        if (_shedWait <= 0.0f)
        {
            _shedWait = 1.0f;
            host.StrainShed(SmallestDirty());
        }
    }

    public Vector3 Centre(int chunk)
    {
        if ((uint)chunk >= (uint)_count || _boxCentre.Length == 0)
        {
            return Vector3.Zero;
        }
        int from = _boxOffset[chunk], to = _boxOffset[chunk + 1];
        if (from >= to)
        {
            return BoxBounds(chunk, out Vector3 centre, out _) ? centre : Vector3.Zero;
        }
        Vector3 sum = Vector3.Zero;
        for (int i = from; i < to; i++)
        {
            sum += _boxCentre[i];
        }
        return sum / (to - from);
    }

    /// <summary>Copies what the worker needs. The cell array is the part's
    /// reused buffer, already filled.</summary>
    public GraphSnapshot Snapshot(int[] cellChunk)
    {
        var snap = new GraphSnapshot
        {
            CellChunk = cellChunk,
            Count = _count,
            Solid = (int[])_solid.Clone(),
            Min = (Vector3I[])_min.Clone(),
            Max = (Vector3I[])_max.Clone(),
            BoxOffset = (int[])_boxOffset.Clone(),
            BoxCentre = (Vector3[])_boxCentre.Clone(),
            BoxSize = (Vector3[])_boxSize.Clone(),
            Stale = (bool[])_stale.Clone(),
        };
        int links = 0;
        for (int c = 0; c < _count; c++)
        {
            foreach (Link link in _links[c])
            {
                if (c < link.Other)
                {
                    links++;
                }
            }
        }
        snap.LinkA = new int[links];
        snap.LinkB = new int[links];
        snap.LinkLive = new int[links];
        snap.LinkBuilt = new int[links];
        int e = 0;
        for (int c = 0; c < _count; c++)
        {
            foreach (Link link in _links[c])
            {
                if (c < link.Other)
                {
                    snap.LinkA[e] = c;
                    snap.LinkB[e] = link.Other;
                    snap.LinkLive[e] = link.Live;
                    snap.LinkBuilt[e] = link.Built;
                    e++;
                }
            }
        }
        return snap;
    }

    /// <summary>
    /// The chunk the cut went through is flooded on its own cells. What it
    /// came apart into becomes chunks; a scrap under <paramref name="minVoxels"/>
    /// is listed for dust and left in the grid until the plan removes it.
    /// </summary>
    public void SplitDirty(VoxelGrid grid, int minVoxels, List<int> dust)
    {
        if (_dirty.Count == 0)
        {
            return;
        }
        int[] pending = new int[_dirty.Count];
        _dirty.CopyTo(pending);
        _dirty.Clear();
        foreach (int chunk in pending)
        {
            if ((uint)chunk >= (uint)_count || _solid[chunk] <= 0)
            {
                continue;
            }
            List<List<int>> islands = Islands(grid, chunk);
            if (islands.Count <= 1)
            {
                continue;
            }
            islands.Sort((a, b) => b.Count.CompareTo(a.Count));
            var fresh = new List<int> { chunk };
            Reassign(grid, chunk, islands[0]);
            _stale[chunk] = true;
            for (int k = 1; k < islands.Count; k++)
            {
                // A scrap is dust only when it holds nothing: one still
                // touching another chunk or the ground may be the bridge a
                // branch hangs from, and dusting it dropped the branch.
                if (islands[k].Count < minVoxels && !TouchesOther(grid, islands[k], chunk))
                {
                    foreach (int cell in islands[k])
                    {
                        _cell[cell] = -1;
                        dust.Add(cell);
                    }
                    continue;
                }
                fresh.Add(AddChunk(grid, islands[k]));
            }
            Recount(grid, fresh);
        }
    }

    private bool TouchesOther(VoxelGrid grid, List<int> cells, int chunk)
    {
        int sx = grid.Size.X, sxy = grid.Size.X * grid.Size.Y;
        foreach (int i in cells)
        {
            Vector3I c = grid.CellOf(i);
            if (c.Z == 0)
            {
                return true;
            }
            bool Other(int j) => grid.Cells[j] != 0 && _cell[j] >= 0 && _cell[j] != chunk;
            if ((c.X > 0 && Other(i - 1)) || (c.X + 1 < grid.Size.X && Other(i + 1))
                || (c.Y > 0 && Other(i - sx)) || (c.Y + 1 < grid.Size.Y && Other(i + sx))
                || Other(i - sxy) || (c.Z + 1 < grid.Size.Z && Other(i + sxy)))
            {
                return true;
            }
        }
        return false;
    }

    internal GraphDecision Decide(VoxelGrid grid, VoxelDestructionStats stats, VoxelAnchor anchor, int[] builtCount, float[] builtHalf)
    {
        var dust = new List<int>();
        SplitDirty(grid, stats.MinChunkVoxels, dust);
        // Scraps an earlier decision took out of the graph for dust, whose
        // plan was thrown away (the part was hit again while it was worked
        // out): still in the grid, in no chunk, so never checked again. They
        // hung in the air for good. Offered again until a plan removes them.
        foreach (int cell in _orphans)
        {
            if (grid.Cells[cell] != 0 && !dust.Contains(cell))
            {
                dust.Add(cell);
            }
        }
        _orphans.Clear();
        _orphans.AddRange(dust);
        var decision = new GraphDecision { DustCells = dust };
        var seen = new bool[_count];
        var queue = new Queue<int>();

        if (anchor == VoxelAnchor.Ground)
        {
            for (int c = 0; c < _count; c++)
            {
                if (_ground[c] > 0 && _solid[c] > 0)
                {
                    seen[c] = true;
                    queue.Enqueue(c);
                }
            }
            Flood(stats, seen, queue);
        }
        else
        {
            int keep = LargestComponent(stats, seen);
            Array.Fill(seen, false);
            if (keep >= 0)
            {
                seen[keep] = true;
                queue.Enqueue(keep);
                Flood(stats, seen, queue);
            }
        }

        var loose = new List<List<int>>();
        var looseSeen = new bool[_count];
        for (int c = 0; c < _count; c++)
        {
            if (seen[c] || looseSeen[c] || _solid[c] <= 0)
            {
                continue;
            }
            var island = new List<int>();
            queue.Clear();
            looseSeen[c] = true;
            queue.Enqueue(c);
            Flood(stats, looseSeen, queue, island);
            if (island.Count > 0)
            {
                loose.Add(island);
            }
        }

        bool collapsed = anchor == VoxelAnchor.Ground && builtCount != null && LoadFailed(stats, seen, builtCount, builtHalf, decision);
        var pieces = new List<List<int>>();
        var spins = new List<VoxelSpin>();
        var hinges = new List<bool>();
        foreach (List<int> island in loose)
        {
            if (collapsed && AllAbove(island, decision.Top))
            {
                continue;
            }
            pieces.Add(island);
            spins.Add(Fling(island) ? VoxelSpin.Own : anchor == VoxelAnchor.Ground ? VoxelSpin.Still : VoxelSpin.Own);
            hinges.Add(false);
        }
        if (collapsed)
        {
            var falling = new List<int>();
            for (int c = 0; c < _count; c++)
            {
                if (!seen[c] || _solid[c] <= 0)
                {
                    continue;
                }
                float z = (_min[c].Z + _max[c].Z) * 0.5f;
                if (z >= decision.Top)
                {
                    falling.Add(c);
                }
                else if (z >= decision.Fail)
                {
                    decision.CrushedChunks.Add(c);
                }
            }
            foreach (List<int> island in Components(falling, stats))
            {
                pieces.Add(island);
                spins.Add(VoxelSpin.Topple);
                hinges.Add(false);
            }
            int largest = -1, largestN = 0;
            for (int p = 0; p < pieces.Count; p++)
            {
                if (spins[p] != VoxelSpin.Topple)
                {
                    continue;
                }
                int n = 0;
                foreach (int c in pieces[p])
                {
                    n += _solid[c];
                }
                if (n > largestN)
                {
                    largestN = n;
                    largest = p;
                }
            }
            if (largest >= 0)
            {
                hinges[largest] = true;
            }
        }

        decision.PieceChunks = pieces.ToArray();
        decision.PieceSpin = spins.ToArray();
        decision.PieceHinge = hinges.ToArray();
        decision.Changed = pieces.Count > 0 || dust.Count > 0 || decision.CrushedChunks.Count > 0;
        int standing = 0;
        for (int c = 0; c < _count; c++)
        {
            if (_solid[c] > 0 && (anchor != VoxelAnchor.Ground || _ground[c] > 0 || seen[c]))
            {
                standing += seen[c] ? _solid[c] : 0;
            }
        }
        decision.Emptied = standing <= 0;
        _fling.Clear();
        return decision;
    }

    /// <summary>Groups whole chunks into the blocks a landing breaks along.</summary>
    internal GraphDecision Breakup(VoxelGrid grid, VoxelDestructionStats stats, VoxelBreakup breakup)
    {
        var decision = new GraphDecision { BreakingUp = true, Changed = true, Emptied = true };
        var alive = new List<int>();
        Vector3I min = new(int.MaxValue, int.MaxValue, int.MaxValue);
        Vector3I max = new(int.MinValue, int.MinValue, int.MinValue);
        for (int c = 0; c < _count; c++)
        {
            if (_solid[c] <= 0)
            {
                continue;
            }
            alive.Add(c);
            min = min.Min(_min[c]);
            max = max.Max(_max[c]);
        }
        Vector3I extent = max - min + Vector3I.One;
        Vector3 block = breakup switch
        {
            VoxelBreakup.Crumble => Block(extent, grid.VoxelSize, stats.MinCrumbleSize, 0.5f),
            VoxelBreakup.Snap => Snap(extent, grid.VoxelSize, stats.SnapLength),
            VoxelBreakup.Fracture => Fracture(alive, extent, grid.VoxelSize, stats),
            _ => Vector3.Zero,
        };
        var groups = new Dictionary<int, List<int>>();
        foreach (int c in alive)
        {
            int key = 0;
            if (block != Vector3.Zero)
            {
                Vector3 centre = V(_min[c] + _max[c]) * 0.5f - new Vector3(min.X, min.Y, min.Z);
                int kx = Mathf.FloorToInt(centre.X / block.X);
                int ky = Mathf.FloorToInt(centre.Y / block.Y);
                int kz = Mathf.FloorToInt(centre.Z / block.Z);
                key = kx + 1024 * (ky + 1024 * kz);
            }
            if (!groups.TryGetValue(key, out List<int> list))
            {
                list = new List<int>();
                groups[key] = list;
            }
            list.Add(c);
        }
        decision.PieceChunks = new List<int>[groups.Count];
        decision.PieceSpin = new VoxelSpin[groups.Count];
        decision.PieceHinge = new bool[groups.Count];
        int p = 0;
        foreach (List<int> list in groups.Values)
        {
            decision.PieceChunks[p] = list;
            decision.PieceSpin[p] = VoxelSpin.Own;
            p++;
        }
        return decision;
    }

    public void MarkFling(int chunk)
    {
        if ((uint)chunk < (uint)_count)
        {
            _fling.Add(chunk);
        }
    }

    /// <summary>Drops every edge of a chunk, so the next check lets it go.</summary>
    public void Sever(int chunk)
    {
        if ((uint)chunk >= (uint)_count)
        {
            return;
        }
        for (int i = 0; i < _links[chunk].Count; i++)
        {
            Link link = _links[chunk][i];
            link.Live = 0;
            link.Weaken = -1.0f;
            _links[chunk][i] = link;
            Mirror(chunk, link);
        }
        _dirty.Add(chunk);
        _fling.Add(chunk);
    }

    /// <summary>The piece's own graph, in its grid's space. Reads the snapshot only.</summary>
    public static VoxelChunkGraph Extract(GraphSnapshot snap, List<int> cells, VoxelGrid oldGrid, VoxelGrid piece, int align, Vector3 offset)
    {
        if (snap == null || cells.Count == 0)
        {
            return null;
        }
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (int i in cells)
        {
            min = min.Min(oldGrid.CellOf(i));
        }
        min = min / Math.Max(1, align) * Math.Max(1, align);
        var map = new int[snap.Count];
        Array.Fill(map, -1);
        int count = 0;
        foreach (int i in cells)
        {
            int id = snap.CellChunk[i];
            if (id >= 0 && id < map.Length && map[id] < 0)
            {
                map[id] = count++;
            }
        }
        if (count == 0)
        {
            return null;
        }
        var graph = new VoxelChunkGraph
        {
            _cell = new int[piece.Cells.Length],
            _count = count,
            _solid = new int[count],
            _ground = new int[count],
            _min = new Vector3I[count],
            _max = new Vector3I[count],
            _layers = piece.Size.Z,
            _layerCount = new int[count * piece.Size.Z],
            _layerSumX = new float[count * piece.Size.Z],
            _layerSumY = new float[count * piece.Size.Z],
            _links = new List<Link>[count],
            _boxOffset = new int[count + 1],
            _stale = new bool[count],
        };
        Array.Fill(graph._cell, -1);
        for (int c = 0; c < count; c++)
        {
            graph._min[c] = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
            graph._max[c] = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
            graph._links[c] = new List<Link>();
        }
        var centres = new List<Vector3>();
        var sizes = new List<Vector3>();
        // Baked boxes live in the old part's node space, and the piece's node
        // sits at offset there, so a point moves by -offset and nothing more.
        // Copy slides the grid's cells to min, but it moves the grid's origin
        // with them (centre - min), so node space does not see the slide.
        // Adding ToGodot(min) put a piece's collision as far above its voxels
        // as it had stood in the model: 37 m for a branch, and the piece fell
        // through the ground with its boxes up in the air.
        Vector3 shift = -offset;
        var inverse = new int[count];
        for (int old = 0; old < map.Length; old++)
        {
            if (map[old] >= 0)
            {
                inverse[map[old]] = old;
            }
        }
        foreach (int i in cells)
        {
            int id = snap.CellChunk[i];
            if (id < 0 || id >= map.Length || map[id] < 0)
            {
                continue;
            }
            Vector3I p = oldGrid.CellOf(i) - min;
            if (!piece.InBounds(p.X, p.Y, p.Z))
            {
                continue;
            }
            int ni = piece.Index(p.X, p.Y, p.Z);
            int chunk = map[id];
            graph._cell[ni] = chunk;
            graph._solid[chunk]++;
            graph._min[chunk] = graph._min[chunk].Min(p);
            graph._max[chunk] = graph._max[chunk].Max(p);
            if ((uint)p.Z < (uint)piece.Size.Z)
            {
                int layer = chunk * piece.Size.Z + p.Z;
                graph._layerCount[layer]++;
                graph._layerSumX[layer] += p.X + 0.5f;
                graph._layerSumY[layer] += p.Y + 0.5f;
            }
        }
        if (snap.LinkA != null)
        {
            for (int e = 0; e < snap.LinkA.Length; e++)
            {
                int a = snap.LinkA[e], b = snap.LinkB[e];
                if ((uint)a >= (uint)map.Length || (uint)b >= (uint)map.Length)
                {
                    continue;
                }
                if (map[a] < 0 || map[b] < 0 || snap.LinkLive[e] <= 0)
                {
                    continue;
                }
                graph.AddLink(map[a], map[b], snap.LinkLive[e], Math.Max(snap.LinkLive[e], snap.LinkBuilt[e]));
            }
        }
        for (int c = 0; c < count; c++)
        {
            graph._boxOffset[c] = centres.Count;
            if (graph._solid[c] <= 0)
            {
                continue;
            }
            int old = inverse[c];
            int from = snap.BoxOffset != null && old + 1 < snap.BoxOffset.Length ? snap.BoxOffset[old] : 0;
            int to = snap.BoxOffset != null && old + 1 < snap.BoxOffset.Length ? snap.BoxOffset[old + 1] : 0;
            bool stale = snap.Stale != null && (uint)old < (uint)snap.Stale.Length && snap.Stale[old];
            if (!stale && from < to && snap.BoxCentre != null)
            {
                for (int b = from; b < to; b++)
                {
                    centres.Add(snap.BoxCentre[b] + shift);
                    sizes.Add(snap.BoxSize[b]);
                }
                continue;
            }
            // A bounds box is not exact: not offered as baked (TryBoxes).
            graph._stale[c] = true;
            Vector3 lo = VoxSpace.ToGodot(V(graph._min[c]) - piece.Origin) * piece.VoxelSize;
            Vector3 hi = VoxSpace.ToGodot(V(graph._max[c] + Vector3I.One) - piece.Origin) * piece.VoxelSize;
            centres.Add((lo + hi) * 0.5f);
            sizes.Add((lo - hi).Abs());
        }
        graph._boxOffset[count] = centres.Count;
        graph._boxCentre = centres.ToArray();
        graph._boxSize = sizes.ToArray();
        return graph;
    }

    /// <summary>Baked-or-bounds boxes in this graph's node space.</summary>
    public void FillBoxes(List<(Vector3 centre, Vector3 size)> into)
    {
        for (int i = 0; i < _boxCentre.Length; i++)
        {
            into.Add((_boxCentre[i], _boxSize[i]));
        }
    }

    public void CopyCells(int[] into)
    {
        Array.Copy(_cell, into, Math.Min(_cell.Length, into.Length));
    }

    public int CellLength => _cell.Length;

    private void ConsiderStrain(VoxelDestructionStats stats)
    {
        // No weight check, no strain: a tree or a vine holds by connection
        // alone (0024). Strain cut every nicked edge with anything above it,
        // and a shot branch let go of others a second and a half later.
        if (stats.MinSupportFraction <= 0.0f)
        {
            return;
        }
        for (int c = 0; c < _count; c++)
        {
            for (int i = 0; i < _links[c].Count; i++)
            {
                Link link = _links[c][i];
                if (c > link.Other || link.Live <= 0 || link.Built <= 0 || link.Weaken >= 0.0f)
                {
                    continue;
                }
                float lost = 1.0f - (float)link.Live / link.Built;
                if (lost < stats.StressLimit)
                {
                    continue;
                }
                int upper = _max[c].Z >= _max[link.Other].Z ? c : link.Other;
                int lower = upper == c ? link.Other : c;
                if (_max[upper].Z <= _min[lower].Z)
                {
                    continue;
                }
                bool above = false;
                for (int z = _max[lower].Z + 1; z < _layers; z++)
                {
                    if (_layerCount[upper * _layers + z] > 0 || _solid[upper] > _layerCount[upper * _layers + Math.Min(z, _layers - 1)])
                    {
                        above = _solid[upper] > 0;
                        break;
                    }
                }
                if (!above && _solid[upper] <= 0)
                {
                    continue;
                }
                if (_max[upper].Z <= _max[lower].Z && _solid[upper] < 1)
                {
                    continue;
                }
                // Overloaded, as 0027 says, not merely nicked: what is left of
                // all the upper chunk's contacts with chunks at or below it is
                // under MinSupportFraction of what it was built with. One
                // damaged edge beside healthy ones carries nothing alone.
                if (SupportShare(upper) >= stats.MinSupportFraction)
                {
                    continue;
                }
                link.Weaken = stats.StressSeconds;
                _links[c][i] = link;
                Mirror(c, link);
                _straining = true;
            }
        }
    }

    /// <summary>Live over built faces of a chunk's contacts with chunks whose
    /// middle is not above its own: how much of what held it up is left.</summary>
    private float SupportShare(int chunk)
    {
        float middle = (_min[chunk].Z + _max[chunk].Z) * 0.5f;
        int live = 0, built = 0;
        foreach (Link link in _links[chunk])
        {
            int other = link.Other;
            if ((_min[other].Z + _max[other].Z) * 0.5f > middle || link.Built <= 0)
            {
                continue;
            }
            live += _solid[other] > 0 ? link.Live : 0;
            built += link.Built;
        }
        return built == 0 ? 1.0f : (float)live / built;
    }

    private int SmallestDirty()
    {
        int best = -1, bestN = int.MaxValue;
        foreach (int c in _dirty)
        {
            if ((uint)c >= (uint)_count || _solid[c] <= 0 || _solid[c] >= bestN)
            {
                continue;
            }
            bool weakening = false;
            foreach (Link link in _links[c])
            {
                if (link.Weaken >= 0.0f)
                {
                    weakening = true;
                    break;
                }
            }
            if (!weakening)
            {
                continue;
            }
            best = c;
            bestN = _solid[c];
        }
        return bestN > 800 ? -1 : best;
    }

    private bool LoadFailed(VoxelDestructionStats stats, bool[] standing, int[] builtCount, float[] builtHalf, GraphDecision decision)
    {
        int layers = Math.Min(_layers, builtCount.Length);
        var count = new int[layers];
        var sumX = new float[layers];
        var sumY = new float[layers];
        for (int c = 0; c < _count; c++)
        {
            if (!standing[c])
            {
                continue;
            }
            for (int z = 0; z < layers; z++)
            {
                int n = _layerCount[c * _layers + z];
                count[z] += n;
                sumX[z] += _layerSumX[c * _layers + z];
                sumY[z] += _layerSumY[c * _layers + z];
            }
        }
        int fail = -1;
        Vector2 failLean = Vector2.Zero, failSupport = Vector2.Zero;
        float failOff = 0.0f;
        float above = 0.0f, aboveX = 0.0f, aboveY = 0.0f;
        for (int z = layers - 1; z >= 0; z--)
        {
            if (above >= stats.MinLoadVoxels && builtCount[z] > 0)
            {
                var load = new Vector2(aboveX, aboveY) / above;
                Vector2 support = count[z] > 0 ? new Vector2(sumX[z], sumY[z]) / count[z] : load;
                float kept = (float)count[z] / builtCount[z];
                float offCentre = Mathf.Clamp(load.DistanceTo(support) / Math.Max(1.0f, builtHalf[z]), 0.0f, 1.0f);
                float loaded = Mathf.Clamp(above / (builtCount[z] * stats.FullLoadLayers), 0.0f, 1.0f);
                if (kept * (1.0f - offCentre) < stats.MinSupportFraction * loaded)
                {
                    fail = z;
                    failLean = load - support;
                    failSupport = support;
                    failOff = offCentre;
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
        decision.Collapsed = true;
        decision.Fail = fail;
        decision.Top = fail + Math.Max(1, DepthVoxels);
        decision.Support = failSupport;
        decision.Lean = failLean;
        decision.OffCentre = failOff;
        decision.Held = count[fail] > 0;
        return true;
    }

    /// <summary>FailureDepth is in metres; the part sets <see cref="DepthVoxels"/>
    /// before the check. Stored here so the graph does not re-read the grid.</summary>
    public int DepthVoxels { get; set; } = 8;

    private bool AllAbove(List<int> island, int top)
    {
        foreach (int c in island)
        {
            if ((_min[c].Z + _max[c].Z) * 0.5f < top)
            {
                return false;
            }
        }
        return island.Count > 0;
    }

    private bool Fling(List<int> island)
    {
        foreach (int c in island)
        {
            if (_fling.Contains(c))
            {
                return true;
            }
        }
        return false;
    }

    private void Flood(VoxelDestructionStats stats, bool[] seen, Queue<int> queue, List<int> into = null)
    {
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            into?.Add(c);
            foreach (Link link in _links[c])
            {
                if (link.Live <= 0 || seen[link.Other] || _solid[link.Other] <= 0 || !Holds(link, stats))
                {
                    continue;
                }
                seen[link.Other] = true;
                queue.Enqueue(link.Other);
            }
        }
    }

    private int LargestComponent(VoxelDestructionStats stats, bool[] scratch)
    {
        int best = -1, bestN = 0;
        var seen = new bool[_count];
        var queue = new Queue<int>();
        for (int c = 0; c < _count; c++)
        {
            if (seen[c] || _solid[c] <= 0)
            {
                continue;
            }
            seen[c] = true;
            queue.Enqueue(c);
            int n = 0;
            int start = c;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                n += _solid[cur];
                foreach (Link link in _links[cur])
                {
                    if (link.Live <= 0 || seen[link.Other] || !Holds(link, stats))
                    {
                        continue;
                    }
                    seen[link.Other] = true;
                    queue.Enqueue(link.Other);
                }
            }
            if (n > bestN)
            {
                bestN = n;
                best = start;
            }
        }
        return best;
    }

    private List<List<int>> Components(List<int> chunks, VoxelDestructionStats stats)
    {
        var set = new HashSet<int>(chunks);
        var seen = new HashSet<int>();
        var islands = new List<List<int>>();
        foreach (int start in chunks)
        {
            if (!seen.Add(start))
            {
                continue;
            }
            var island = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                island.Add(c);
                foreach (Link link in _links[c])
                {
                    // Holds before seen: a chunk met over a broken link must
                    // still start an island of its own, or it neither falls
                    // nor stands, and hangs in the air.
                    if (!set.Contains(link.Other) || !Holds(link, stats) || !seen.Add(link.Other))
                    {
                        continue;
                    }
                    queue.Enqueue(link.Other);
                }
            }
            islands.Add(island);
        }
        return islands;
    }

    private static bool Holds(Link link, VoxelDestructionStats stats) =>
        link.Live > 0 && (stats.MinContactShare <= 0.0f || link.Built <= 0
            || link.Live >= stats.MinContactShare * link.Built);

    private int[] _stamp;
    private int _mark;
    private int[] _queue;

    private List<List<int>> Islands(VoxelGrid grid, int chunk)
    {
        Vector3I min = _min[chunk], max = _max[chunk];
        var islands = new List<List<int>>();
        int sx = grid.Size.X, sxy = grid.Size.X * grid.Size.Y;
        int volume = (max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
        if (volume <= 0 || volume > 200000)
        {
            return islands;
        }
        if (_stamp == null || _stamp.Length < _cell.Length)
        {
            _stamp = new int[_cell.Length];
        }
        if (++_mark == 0)
        {
            Array.Clear(_stamp);
            _mark = 1;
        }
        _queue ??= new int[64];
        int mark = _mark;
        for (int z = min.Z; z <= max.Z; z++)
            for (int y = min.Y; y <= max.Y; y++)
                for (int x = min.X; x <= max.X; x++)
                {
                    int start = grid.Index(x, y, z);
                    if (_cell[start] != chunk || grid.Cells[start] == 0 || _stamp[start] == mark)
                    {
                        continue;
                    }
                    var island = new List<int>();
                    int head = 0, tail = 0;
                    _stamp[start] = mark;
                    _queue[tail++] = start;
                    while (head < tail)
                    {
                        int i = _queue[head++];
                        island.Add(i);
                        Vector3I c = grid.CellOf(i);
                        void Visit(int j)
                        {
                            if (_cell[j] == chunk && grid.Cells[j] != 0 && _stamp[j] != mark)
                            {
                                _stamp[j] = mark;
                                if (tail == _queue.Length)
                                {
                                    Array.Resize(ref _queue, _queue.Length * 2);
                                }
                                _queue[tail++] = j;
                            }
                        }
                        if (c.X > min.X) Visit(i - 1);
                        if (c.X < max.X) Visit(i + 1);
                        if (c.Y > min.Y) Visit(i - sx);
                        if (c.Y < max.Y) Visit(i + sx);
                        if (c.Z > min.Z) Visit(i - sxy);
                        if (c.Z < max.Z) Visit(i + sxy);
                    }
                    islands.Add(island);
                    // Still one piece: the rest of the box is the hole the cut left.
                    if (island.Count >= _solid[chunk])
                    {
                        return islands;
                    }
                }
        return islands;
    }

    private void Reassign(VoxelGrid grid, int chunk, List<int> cells)
    {
        _solid[chunk] = cells.Count;
        Array.Clear(_layerCount, chunk * _layers, _layers);
        Array.Clear(_layerSumX, chunk * _layers, _layers);
        Array.Clear(_layerSumY, chunk * _layers, _layers);
        _ground[chunk] = 0;
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        foreach (int i in cells)
        {
            _cell[i] = chunk;
            Vector3I c = grid.CellOf(i);
            min = min.Min(c);
            max = max.Max(c);
            int layer = chunk * _layers + c.Z;
            _layerCount[layer]++;
            _layerSumX[layer] += c.X + 0.5f;
            _layerSumY[layer] += c.Y + 0.5f;
            if (c.Z == 0)
            {
                _ground[chunk]++;
            }
        }
        _min[chunk] = min;
        _max[chunk] = max;
    }

    private int AddChunk(VoxelGrid grid, List<int> cells)
    {
        int id = _count++;
        Array.Resize(ref _solid, _count);
        Array.Resize(ref _ground, _count);
        Array.Resize(ref _min, _count);
        Array.Resize(ref _max, _count);
        Array.Resize(ref _links, _count);
        Array.Resize(ref _stale, _count);
        _links[id] = new List<Link>();
        int layers = _layers;
        Array.Resize(ref _layerCount, _count * layers);
        Array.Resize(ref _layerSumX, _count * layers);
        Array.Resize(ref _layerSumY, _count * layers);
        // Boxes grow by one bounds box in Recount's caller via AppendBox.
        Reassign(grid, id, cells);
        AppendBoundsBox(id, grid);
        return id;
    }

    private void AppendBoundsBox(int chunk, VoxelGrid grid)
    {
        var centres = new List<Vector3>(_boxCentre);
        var sizes = new List<Vector3>(_boxSize);
        var offset = new List<int>(_boxOffset);
        while (offset.Count < _count + 1)
        {
            offset.Add(centres.Count);
        }
        Vector3 lo = VoxSpace.ToGodot(V(_min[chunk]) - grid.Origin) * grid.VoxelSize;
        Vector3 hi = VoxSpace.ToGodot(V(_max[chunk] + Vector3I.One) - grid.Origin) * grid.VoxelSize;
        centres.Add((lo + hi) * 0.5f);
        sizes.Add((lo - hi).Abs());
        offset[_count] = centres.Count;
        _boxCentre = centres.ToArray();
        _boxSize = sizes.ToArray();
        _boxOffset = offset.ToArray();
    }

    private void Recount(VoxelGrid grid, List<int> chunks)
    {
        var previous = new Dictionary<long, int>();
        foreach (int c in chunks)
        {
            foreach (Link link in _links[c])
            {
                if (chunks.Contains(link.Other))
                {
                    continue;
                }
                long key = c < link.Other ? c + ((long)link.Other << 20) : link.Other + ((long)c << 20);
                previous[key] = Math.Max(previous.GetValueOrDefault(key), link.Built);
            }
            _links[c].Clear();
        }
        foreach (int c in chunks)
        {
            for (int i = _links.Length - 1; i >= 0 && i >= _count; i--)
            {
            }
            // Drop the other end of any link that pointed back at a recounted chunk.
        }
        for (int c = 0; c < _count; c++)
        {
            if (chunks.Contains(c))
            {
                continue;
            }
            _links[c].RemoveAll(link => chunks.Contains(link.Other));
        }
        int sx = grid.Size.X, sxy = grid.Size.X * grid.Size.Y;
        var faces = new Dictionary<long, int>();
        var recounted = new HashSet<int>(chunks);
        foreach (int chunk in chunks)
        {
            CellsOf(grid, chunk, _scan);
            foreach (int i in _scan)
            {
                Vector3I c = grid.CellOf(i);
                // All six sides: a chunk that was not recounted is never
                // scanned, so a face on its side of this one is counted from
                // here or not at all. Between two recounted chunks only the
                // positive side counts, so each face counts once.
                void Face(int j, bool positive)
                {
                    int other = _cell[j];
                    if (other < 0 || other == chunk || grid.Cells[j] == 0 || (!positive && recounted.Contains(other)))
                    {
                        return;
                    }
                    long key = chunk < other ? chunk + ((long)other << 20) : other + ((long)chunk << 20);
                    faces[key] = faces.GetValueOrDefault(key) + 1;
                }
                if (c.X + 1 < grid.Size.X) Face(i + 1, true);
                if (c.Y + 1 < grid.Size.Y) Face(i + sx, true);
                if (c.Z + 1 < grid.Size.Z) Face(i + sxy, true);
                if (c.X > 0) Face(i - 1, false);
                if (c.Y > 0) Face(i - sx, false);
                if (c.Z > 0) Face(i - sxy, false);
            }
            _scan.Clear();
        }
        foreach ((long key, int live) in faces)
        {
            int a = (int)(key & 0xfffff), b = (int)(key >> 20);
            int built = previous.GetValueOrDefault(key, live);
            AddLink(a, b, live, Math.Max(built, live));
        }
    }

    private readonly List<int> _scan = new();

    private void AddLink(int a, int b, int live, int built)
    {
        if (a == b || (uint)a >= (uint)_count || (uint)b >= (uint)_count)
        {
            return;
        }
        _links[a].Add(new Link { Other = b, Live = live, Built = built, Weaken = -1.0f });
        _links[b].Add(new Link { Other = a, Live = live, Built = built, Weaken = -1.0f });
    }

    private void LoseFace(int a, int b)
    {
        for (int i = 0; i < _links[a].Count; i++)
        {
            if (_links[a][i].Other != b)
            {
                continue;
            }
            Link link = _links[a][i];
            link.Live = Math.Max(0, link.Live - 1);
            _links[a][i] = link;
            break;
        }
        for (int i = 0; i < _links[b].Count; i++)
        {
            if (_links[b][i].Other != a)
            {
                continue;
            }
            Link link = _links[b][i];
            link.Live = Math.Max(0, link.Live - 1);
            _links[b][i] = link;
            break;
        }
    }

    private void Mirror(int from, Link link)
    {
        int other = link.Other;
        if ((uint)other >= (uint)_count)
        {
            return;
        }
        for (int i = 0; i < _links[other].Count; i++)
        {
            if (_links[other][i].Other != from)
            {
                continue;
            }
            Link back = _links[other][i];
            back.Live = link.Live;
            back.Weaken = link.Weaken;
            back.Shook = link.Shook;
            _links[other][i] = back;
            return;
        }
    }

    /// <summary>The box round all of a chunk's boxes. It returned the
    /// chunk's first box only: a leafy chunk's first greedy box is a sliver,
    /// and a piece flew with that sliver for its collision.</summary>
    private bool BoxBounds(int chunk, out Vector3 centre, out Vector3 size)
    {
        centre = size = Vector3.Zero;
        if ((uint)chunk + 1 >= (uint)_boxOffset.Length)
        {
            return false;
        }
        int from = _boxOffset[chunk], to = Math.Min(_boxOffset[chunk + 1], _boxCentre.Length);
        if (from >= to)
        {
            return false;
        }
        Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 hi = -lo;
        for (int i = from; i < to; i++)
        {
            Vector3 half = _boxSize[i] * 0.5f;
            lo = lo.Min(_boxCentre[i] - half);
            hi = hi.Max(_boxCentre[i] + half);
        }
        centre = (lo + hi) * 0.5f;
        size = hi - lo;
        return true;
    }

    private static Vector3 Block(Vector3I size, float voxel, float minMetres, float fraction)
    {
        float least = minMetres / voxel;
        var block = new Vector3(
            Mathf.Max(size.X * fraction, least), Mathf.Max(size.Y * fraction, least), Mathf.Max(size.Z * fraction, least));
        int n = Mathf.CeilToInt(size.X / block.X) * Mathf.CeilToInt(size.Y / block.Y) * Mathf.CeilToInt(size.Z / block.Z);
        return n > 1 ? block : Vector3.Zero;
    }

    private static Vector3 Snap(Vector3I size, float voxel, float length)
    {
        var block = new Vector3(size.X, size.Y, size.Z);
        int axis = (int)block.MaxAxisIndex();
        int segments = Mathf.RoundToInt(block[axis] * voxel / length);
        if (segments < 2)
        {
            return Vector3.Zero;
        }
        block[axis] /= segments;
        return block;
    }

    private Vector3 Fracture(List<int> chunks, Vector3I size, float voxel, VoxelDestructionStats stats)
    {
        int cells = 0;
        foreach (int c in chunks)
        {
            cells += _solid[c];
        }
        if (cells * voxel * voxel * voxel < stats.FractureVolume)
        {
            return Vector3.Zero;
        }
        var block = new Vector3(stats.FractureWidth / voxel, stats.FractureWidth / voxel, stats.FractureHeight / voxel);
        int Blocks() => Mathf.CeilToInt(size.X / block.X) * Mathf.CeilToInt(size.Y / block.Y) * Mathf.CeilToInt(size.Z / block.Z);
        while (Blocks() > stats.MaxFracturePieces)
        {
            block *= 1.25f;
        }
        return Blocks() > 1 ? block : Vector3.Zero;
    }

    private static Vector3 V(Vector3I v) => new(v.X, v.Y, v.Z);
}
