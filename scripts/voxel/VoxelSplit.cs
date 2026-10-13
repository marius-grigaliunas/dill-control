using System;
using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>How a part comes apart, as <see cref="VoxelSplit"/> is asked.</summary>
internal enum VoxelBreakup
{
    /// <summary>Falls as one body, however big: a building's upper part.</summary>
    Intact,

    /// <summary>A big one splits into storey-sized blocks: a thing destroyed outright.</summary>
    Fracture,

    /// <summary>Into blocks about half its own size: a piece landing hard.</summary>
    Crumble,

    /// <summary>Across its length into segments: a long piece landing on its side.</summary>
    Snap,
}

/// <summary>How a released piece starts turning.</summary>
internal enum VoxelSpin
{
    /// <summary>Not at all: what comes loose of a building.</summary>
    Still,

    /// <summary>A spin of its own, slower the bigger it is.</summary>
    Own,

    /// <summary>The topple of the collapse it is part of, about its pivot.</summary>
    Topple,
}

/// <summary>
/// What a piece made mid-game needs before it is made: its collision's
/// coarseness, and its boxes and meshes section by section. Built with the
/// plan, on the worker, so making the piece costs the main thread almost
/// nothing.
/// </summary>
public sealed class ChunkBuild
{
    public int Step;
    public List<(Vector3 centre, Vector3 size)>[] Boxes;
    public VoxMesher.Arrays[] Meshes;

    /// <summary>Sections it is to take over its old part's mesh for, where
    /// that mesh is up to date; its own is used where it is not.</summary>
    public bool[] Skipped;
}

/// <summary>One piece of a plan: which cells leave, and the piece they make.</summary>
internal sealed class SplitPiece
{
    public List<int> Cells;
    public Vector3I First;
    public Vector3I Last;
    public VoxelGrid Grid;
    public Vector3 Offset;
    public VoxelSpin Spin;
    public bool Hinge;

    /// <summary>Its part's sections it takes whole, with the index each has in
    /// the piece: their meshes are handed over rather than built.</summary>
    public List<(int part, int piece)> Give;

    /// <summary>Which way it is thrown off the rest, in the part's space, or zero.</summary>
    public Vector3 Away;

    public ChunkBuild Build;

    /// <summary>The chunks this piece is made of, in its own grid. Null when
    /// the part it came off has no chunk graph.</summary>
    public VoxelChunkGraph Graph;
}

/// <summary>A worked-out split, for the part to carry out on the main thread.</summary>
internal sealed class SplitPlan
{
    /// <summary>The grid's version it was worked out from.</summary>
    public int Version;

    public List<SplitPiece> Pieces = new();

    /// <summary>Cells too few to make a piece: they go to dust.</summary>
    public List<int> Dust = new();

    /// <summary>A failed storey, crushed: it goes up as clouds round the footprint.</summary>
    public List<int> Crushed;

    /// <summary>Nothing holds it any more.</summary>
    public bool Emptied;

    /// <summary>All of it goes: its collision goes in one step.</summary>
    public bool BreakingUp;

    public bool Changed;

    // A collapse, in grid coordinates: where what is left of the support is,
    // which way the load hangs off it and how far, and whether anything of
    // the failed layer still stands to hang on.
    public bool Collapsed;
    public Vector2 Support;
    public Vector2 Lean;
    public float OffCentre;
    public int Fail;
    public int Top;
    public bool Held;

    /// <summary>The part's sections the split changes, as they will be after
    /// it: mesh (null for none) and boxes (null for a part with no collision).</summary>
    public List<(int section, VoxMesher.Arrays mesh, List<(Vector3, Vector3)> boxes)> Sections = new();

    // A crumble's burst of dust: where, in the part's space, and how big.
    public Vector3 BurstCentre;
    public float BurstSize;
}

/// <summary>
/// Works out how a part comes apart: what no longer holds (and, for a
/// building, what can no longer carry its load), and the pieces that leave,
/// each with its grid, collision and meshes ready. Touches nothing of Godot's
/// but its maths, so it runs on a worker thread from a copy of a part's cells
/// (docs/decisions/0015). The part carries the plan out if its cells have not
/// changed meanwhile.
/// </summary>
internal sealed class VoxelSplit
{
    public VoxelGrid Grid;
    public VoxelAnchor Anchor;
    public VoxelDestructionStats Stats;
    public int[] BuiltCount;
    public float[] BuiltHalf;
    public int Edge;
    public Vector3I SectionCount;
    public Color[] Palette;
    public float[] Emission;
    public int Seed;

    // Of the loose pieces it makes, read from VoxelDebris on the main thread.
    public int ChunkEdge;
    public int MaxChunkBoxes;
    public int MinChunkVoxels;

    /// <summary>What has come loose, and for a building what has given way.</summary>
    public SplitPlan Integrity()
    {
        var plan = new SplitPlan { Version = Grid.Version };
        float hingeCells = Mathf.Max(1.5f, Stats.HingeRadius / Grid.VoxelSize);
        float hinge2 = hingeCells * hingeCells;
        Func<int, bool> isAnchor = Anchor switch
        {
            VoxelAnchor.Ground => i => i < Grid.Size.X * Grid.Size.Y,
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
            plan.Emptied = true;
            return plan;
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
        for (int k = 0; k < islands.Count; k++)
        {
            bool held = Anchor == VoxelAnchor.Largest ? k == keep : anchored[k];
            if (held)
            {
                anyHeld = true;
                continue;
            }
            // What comes loose of a building falls whole, unspun, and breaks
            // where it lands; anything else is thrown off with a spin.
            // A copy: the flood's lists are reused (VoxelGrid.Islands).
            Release(plan, new List<int>(islands[k]), VoxelBreakup.Intact,
                Anchor == VoxelAnchor.Ground ? VoxelSpin.Still : VoxelSpin.Own);
            plan.Changed = true;
        }

        // What still stands must still carry what is above it. One failure a
        // check: the falling pieces land on what is left, carve it, and the
        // check after that finds the next.
        if (Anchor == VoxelAnchor.Ground && BuiltCount != null)
        {
            for (int k = 0; k < islands.Count; k++)
            {
                if (anchored[k] && CheckLoad(plan, islands[k]))
                {
                    plan.Changed = true;
                    break;
                }
            }
        }

        plan.Emptied = !anyHeld;
        return plan;
    }

    /// <summary>All of it, in pieces: a piece landing hard, or a thing destroyed.</summary>
    public SplitPlan Breakup(VoxelBreakup breakup)
    {
        var plan = new SplitPlan { Version = Grid.Version, BreakingUp = true, Changed = true, Emptied = true };
        var anchored = new List<bool>();
        List<List<int>> islands = Grid.Islands(_ => false, anchored);
        if (breakup is VoxelBreakup.Crumble or VoxelBreakup.Snap && Grid.SolidBounds(out Aabb box))
        {
            plan.BurstCentre = box.GetCenter();
            plan.BurstSize = Mathf.Pow(Grid.SolidCount, 1.0f / 3.0f) * Grid.VoxelSize * 1.5f;
        }
        foreach (List<int> island in islands)
        {
            Release(plan, new List<int>(island), breakup, VoxelSpin.Own);
        }
        return plan;
    }

    /// <summary>
    /// Builds a plan from a chunk-graph decision. The decision was made on the
    /// main thread; this only gathers cells and meshes, so it runs on a worker
    /// from a copy of the grid and of the chunk ids.
    /// </summary>
    public SplitPlan Assemble(GraphDecision decision, int[] chunkOf)
    {
        var plan = new SplitPlan
        {
            Version = Grid.Version,
            Changed = decision.Changed,
            Emptied = decision.Emptied,
            BreakingUp = decision.BreakingUp,
            Collapsed = decision.Collapsed,
            Support = decision.Support,
            Lean = decision.Lean,
            OffCentre = decision.OffCentre,
            Fail = decision.Fail,
            Top = decision.Top,
            Held = decision.Held,
        };
        if (decision.DustCells != null)
        {
            foreach (int i in decision.DustCells)
            {
                if ((uint)i < (uint)Grid.Cells.Length && Grid.Cells[i] != 0)
                {
                    plan.Dust.Add(i);
                }
            }
        }
        if (decision.BreakingUp && Grid.SolidBounds(out Aabb box))
        {
            plan.BurstCentre = box.GetCenter();
            plan.BurstSize = Mathf.Pow(Grid.SolidCount, 1.0f / 3.0f) * Grid.VoxelSize * 1.5f;
        }

        int pieces = decision.PieceChunks?.Length ?? 0;
        var role = new Dictionary<int, int>();
        var crushed = new HashSet<int>();
        if (decision.CrushedChunks != null)
        {
            foreach (int c in decision.CrushedChunks)
            {
                crushed.Add(c);
            }
        }
        var lists = new List<int>[pieces];
        for (int p = 0; p < pieces; p++)
        {
            lists[p] = new List<int>();
            foreach (int c in decision.PieceChunks[p])
            {
                if (!crushed.Contains(c))
                {
                    role[c] = p;
                }
            }
        }
        var crushedCells = new List<int>();
        for (int i = 0; i < Grid.Cells.Length; i++)
        {
            if (Grid.Cells[i] == 0 || (uint)i >= (uint)chunkOf.Length)
            {
                continue;
            }
            int c = chunkOf[i];
            if (c < 0)
            {
                continue;
            }
            if (crushed.Contains(c))
            {
                crushedCells.Add(i);
            }
            else if (role.TryGetValue(c, out int p))
            {
                lists[p].Add(i);
            }
        }
        if (crushedCells.Count > 0)
        {
            plan.Crushed = crushedCells;
        }

        Vector3 whole = Vector3.Zero;
        int wholeN = 0;
        if (decision.BreakingUp)
        {
            for (int p = 0; p < pieces; p++)
            {
                whole += Centre(lists[p]) * lists[p].Count;
                wholeN += lists[p].Count;
            }
            if (wholeN > 0)
            {
                whole /= wholeN;
            }
        }
        for (int p = 0; p < pieces; p++)
        {
            if (lists[p].Count < MinChunkVoxels)
            {
                plan.Dust.AddRange(lists[p]);
                continue;
            }
            VoxelSpin spin = decision.PieceSpin != null && p < decision.PieceSpin.Length ? decision.PieceSpin[p] : VoxelSpin.Own;
            Vector3 away = Vector3.Zero;
            if (decision.BreakingUp && wholeN > 0)
            {
                away = Centre(lists[p]) - whole;
                away = away.LengthSquared() > 1e-4f ? away.Normalized() : Vector3.Zero;
            }
            AddPiece(plan, lists[p], spin, away);
            SplitPiece piece = plan.Pieces[^1];
            if (decision.PieceHinge != null && p < decision.PieceHinge.Length)
            {
                piece.Hinge = decision.PieceHinge[p];
            }
            int align = piece.Cells.Count > Edge * Edge && ChunkEdge == Edge ? Edge : 1;
            piece.Graph = VoxelChunkGraph.Extract(decision.Snapshot, piece.Cells, Grid, piece.Grid, align, piece.Offset);
            UseGraphBoxes(piece);
        }
        return plan;
    }

    /// <summary>Swaps a piece's collision for its chunks' baked boxes when
    /// those fit the cap. Otherwise the boxes <see cref="AddPiece"/> built stay.</summary>
    private void UseGraphBoxes(SplitPiece piece)
    {
        if (piece.Graph == null || piece.Build == null)
        {
            return;
        }
        var boxes = new List<(Vector3, Vector3)>();
        if (!piece.Graph.TryBoxes(MaxChunkBoxes, boxes))
        {
            return;
        }
        int edge = Math.Max(4, ChunkEdge);
        var sections = new Vector3I(
            (piece.Grid.Size.X + edge - 1) / edge,
            (piece.Grid.Size.Y + edge - 1) / edge,
            (piece.Grid.Size.Z + edge - 1) / edge);
        foreach (List<(Vector3, Vector3)> list in piece.Build.Boxes)
        {
            list?.Clear();
        }
        foreach ((Vector3 centre, Vector3 size) in boxes)
        {
            Vector3 vox = piece.Grid.ToVox(centre);
            int x = Mathf.Clamp(Mathf.FloorToInt(vox.X) / edge, 0, sections.X - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(vox.Y) / edge, 0, sections.Y - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(vox.Z) / edge, 0, sections.Z - 1);
            int s = x + sections.X * (y + sections.Y * z);
            if ((uint)s < (uint)piece.Build.Boxes.Length && piece.Build.Boxes[s] != null)
            {
                piece.Build.Boxes[s].Add((centre, size));
            }
        }
        piece.Build.Step = 1;
    }

    /// <summary>
    /// Builds the sections the plan changes as they will be once it is carried
    /// out, so the part has nothing to re-mesh or re-box itself. Clears the
    /// plan's cells from the grid it works on: only ever a copy. Nothing for
    /// a plan that takes all of it.
    /// </summary>
    public void Settle(SplitPlan plan, int step, bool boxes)
    {
        if (plan.BreakingUp)
        {
            return;
        }
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        void Clear(List<int> cells)
        {
            foreach (int i in cells)
            {
                Vector3I c = Grid.CellOf(i);
                min = min.Min(c);
                max = max.Max(c);
                Grid.Cells[i] = 0;
            }
        }
        Clear(plan.Dust);
        if (plan.Crushed != null)
        {
            Clear(plan.Crushed);
        }
        foreach (SplitPiece piece in plan.Pieces)
        {
            Clear(piece.Cells);
        }
        if (min.X == int.MaxValue)
        {
            return;
        }
        Grid.Changed();

        // A change alters its neighbours' faces too: one cell of margin.
        Vector3I lo = (min - Vector3I.One).Clamp(Vector3I.Zero, Grid.Size - Vector3I.One) / Edge;
        Vector3I hi = (max + Vector3I.One).Clamp(Vector3I.Zero, Grid.Size - Vector3I.One) / Edge;
        for (int z = lo.Z; z <= hi.Z; z++)
            for (int y = lo.Y; y <= hi.Y; y++)
                for (int x = lo.X; x <= hi.X; x++)
                {
                    var from = new Vector3I(x, y, z) * Edge;
                    Vector3I to = (from + new Vector3I(Edge, Edge, Edge)).Min(Grid.Size);
                    VoxMesher.Arrays mesh = VoxMesher.BuildArrays(Grid.Cells, Grid.Size, from, to, Palette, Emission,
                        Grid.Origin, Grid.VoxelSize, -1);
                    List<(Vector3, Vector3)> list = null;
                    if (boxes)
                    {
                        list = new List<(Vector3, Vector3)>();
                        Grid.Boxes(from, to, step, list);
                    }
                    plan.Sections.Add((x + SectionCount.X * (y + SectionCount.Y * z), mesh, list));
                }
    }

    /// <summary>Too small to crumble (or snap): it would come out as one piece.</summary>
    public static bool TooSmallToSplit(VoxelGrid grid, VoxelDestructionStats stats, bool snap)
    {
        if (!grid.SolidBounds(out Aabb box))
        {
            return true;
        }
        Vector3 size = VoxSpace.FromGodot(box.Size / grid.VoxelSize).Abs();
        var cells = new Vector3I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y), Mathf.RoundToInt(size.Z));
        return (snap ? SnapBlock(cells, grid.VoxelSize, stats) : CrumbleBlock(cells, grid.VoxelSize, stats)) == Vector3.Zero;
    }

    /// <summary>
    /// Finds the lowest layer of a standing island that can no longer carry
    /// what is above it, and brings that down: its storey is crushed to dust
    /// and everything above falls, tipping about what was left of the support
    /// towards the side that lost it.
    ///
    /// A layer's support is the share of its as-built voxels it still has,
    /// reduced by how far the load's centre hangs off the support's centre (as
    /// a fraction of the footprint's half width). It fails below
    /// <see cref="VoxelDestructionStats.MinSupportFraction"/> scaled by how
    /// loaded it is. Per-layer sums over the island: one pass, no flood.
    /// </summary>
    private bool CheckLoad(SplitPlan plan, List<int> island)
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
            if (above >= Stats.MinLoadVoxels && BuiltCount[z] > 0)
            {
                var load = new Vector2(aboveX, aboveY) / above;
                Vector2 support = count[z] > 0 ? new Vector2(sumX[z], sumY[z]) / count[z] : load;
                float kept = (float)count[z] / BuiltCount[z];
                float offCentre = Mathf.Clamp(load.DistanceTo(support) / BuiltHalf[z], 0.0f, 1.0f);
                float loaded = Mathf.Clamp(above / (BuiltCount[z] * Stats.FullLoadLayers), 0.0f, 1.0f);
                if (kept * (1.0f - offCentre) < Stats.MinSupportFraction * loaded)
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

        // The storey from the failed layer up buckles; what is above drops
        // through it whole.
        int top = fail + Math.Max(1, Mathf.RoundToInt(Stats.FailureDepth / Grid.VoxelSize));
        var crushed = new List<int>();
        var falling = new List<int>();
        foreach (int i in island)
        {
            int z = Grid.CellOf(i).Z;
            if (z >= top)
            {
                falling.Add(i);
            }
            else if (z >= fail)
            {
                crushed.Add(i);
            }
        }

        plan.Collapsed = true;
        plan.Crushed = crushed;
        plan.Support = failSupport;
        plan.Lean = failLean;
        plan.OffCentre = failOffCentre;
        plan.Fail = fail;
        plan.Top = top;
        plan.Held = count[fail] > 0;

        int first = plan.Pieces.Count;
        foreach (List<int> piece in Grid.Pieces(falling, _ => 0))
        {
            Release(plan, piece, VoxelBreakup.Intact, VoxelSpin.Topple);
        }
        // Only the biggest can hang on what is left.
        SplitPiece largest = null;
        for (int k = first; k < plan.Pieces.Count; k++)
        {
            if (largest == null || plan.Pieces[k].Cells.Count > largest.Cells.Count)
            {
                largest = plan.Pieces[k];
            }
        }
        if (largest != null)
        {
            largest.Hinge = true;
        }
        return true;
    }

    /// <summary>
    /// An island leaves: as dust if it is small, else as a piece, or unless
    /// <see cref="VoxelBreakup.Intact"/> as blocks of a piece split first.
    /// </summary>
    private void Release(SplitPlan plan, List<int> island, VoxelBreakup breakup, VoxelSpin spin)
    {
        if (island.Count < MinChunkVoxels)
        {
            plan.Dust.AddRange(island);
            return;
        }

        Vector3I min = CellMin(island);
        Vector3I extent = CellMax(island) - min + Vector3I.One;
        Vector3 block = breakup switch
        {
            VoxelBreakup.Crumble => CrumbleBlock(extent, Grid.VoxelSize, Stats),
            VoxelBreakup.Snap => SnapBlock(extent, Grid.VoxelSize, Stats),
            VoxelBreakup.Fracture => FractureBlock(island.Count, extent, Grid.VoxelSize, Stats),
            _ => Vector3.Zero,
        };
        if (block == Vector3.Zero)
        {
            AddPiece(plan, island, spin, Vector3.Zero);
            return;
        }

        int seed = Seed;
        int Key(int i)
        {
            // Block boundaries shifted by up to a third of a block in steps of
            // a few voxels, so the blocks break out jagged rather than as boxes.
            // Not along a side that is not split: it would shave its edges off.
            Vector3I c = Grid.CellOf(i) - min;
            float jx = block.X < extent.X ? Jitter(c.Y >> 2, c.Z >> 2, seed) * block.X * 0.3f : 0.0f;
            float jy = block.Y < extent.Y ? Jitter(c.X >> 2, c.Z >> 2, seed + 1) * block.Y * 0.3f : 0.0f;
            float jz = block.Z < extent.Z ? Jitter(c.X >> 2, c.Y >> 2, seed + 2) * block.Z * 0.3f : 0.0f;
            int kx = Mathf.FloorToInt((c.X + jx) / block.X) + 1;
            int ky = Mathf.FloorToInt((c.Y + jy) / block.Y) + 1;
            int kz = Mathf.FloorToInt((c.Z + jz) / block.Z) + 1;
            return kx + 1024 * (ky + 1024 * kz);
        }

        bool apart = breakup is VoxelBreakup.Crumble or VoxelBreakup.Snap;
        Vector3 centre = Centre(island);
        foreach (List<int> piece in Grid.Pieces(island, Key))
        {
            if (piece.Count < MinChunkVoxels)
            {
                plan.Dust.AddRange(piece);
                continue;
            }
            // A landing piece comes apart outwards, not as a stack.
            Vector3 away = apart ? Centre(piece) - centre : Vector3.Zero;
            AddPiece(plan, piece, spin, away.LengthSquared() > 1e-4f ? away.Normalized() : Vector3.Zero);
        }
    }

    private void AddPiece(SplitPlan plan, List<int> cells, VoxelSpin spin, Vector3 away)
    {
        var piece = new SplitPiece
        {
            Cells = cells,
            First = CellMin(cells),
            Last = CellMax(cells),
            Spin = spin,
            Away = away,
        };

        // A piece big enough to span sections starts on a section boundary,
        // so it can take over the meshes of the sections it takes whole.
        bool align = cells.Count > Edge * Edge && ChunkEdge == Edge;
        Vector3I alignedMin = piece.First / Edge * Edge;
        piece.Grid = Grid.Copy(cells, out piece.Offset, align ? Edge : 1, clear: false);
        if (align)
        {
            piece.Give = Whole(cells, alignedMin);
        }
        piece.Build = Build(piece.Grid, piece.Give);
        plan.Pieces.Add(piece);
    }

    /// <summary>The part's sections a piece takes all of, with each one's
    /// index in the piece (whose grid starts at <paramref name="pieceMin"/>).</summary>
    private List<(int part, int piece)> Whole(List<int> cells, Vector3I pieceMin)
    {
        var counts = new Dictionary<int, int>();
        foreach (int i in cells)
        {
            Vector3I c = Grid.CellOf(i) / Edge;
            int s = c.X + SectionCount.X * (c.Y + SectionCount.Y * c.Z);
            counts.TryGetValue(s, out int n);
            counts[s] = n + 1;
        }

        Vector3I size = CellMax(cells) - pieceMin + Vector3I.One;
        Vector3I pieceSections = new((size.X + Edge - 1) / Edge, (size.Y + Edge - 1) / Edge, (size.Z + Edge - 1) / Edge);
        Vector3I first = pieceMin / Edge;
        var whole = new List<(int, int)>();
        foreach ((int s, int n) in counts)
        {
            var c = new Vector3I(s % SectionCount.X, (s / SectionCount.X) % SectionCount.Y, s / (SectionCount.X * SectionCount.Y));
            if (n != SolidIn(c * Edge, ((c + Vector3I.One) * Edge).Min(Grid.Size)))
            {
                continue;
            }
            Vector3I p = c - first;
            whole.Add((s, p.X + pieceSections.X * (p.Y + pieceSections.Y * p.Z)));
        }
        return whole;
    }

    private int SolidIn(Vector3I min, Vector3I max)
    {
        int n = 0;
        for (int z = min.Z; z < max.Z; z++)
            for (int y = min.Y; y < max.Y; y++)
            {
                int row = Grid.Index(min.X, y, z);
                for (int x = 0; x < max.X - min.X; x++)
                {
                    n += Grid.Cells[row + x] != 0 ? 1 : 0;
                }
            }
        return n;
    }

    /// <summary>A piece's collision and meshes, section by section.</summary>
    private ChunkBuild Build(VoxelGrid grid, List<(int part, int piece)> give)
    {
        int edge = Math.Max(4, ChunkEdge);
        var sections = new Vector3I((grid.Size.X + edge - 1) / edge, (grid.Size.Y + edge - 1) / edge, (grid.Size.Z + edge - 1) / edge);
        int count = sections.X * sections.Y * sections.Z;
        var build = new ChunkBuild
        {
            Step = CollisionStepFor(grid, MaxChunkBoxes),
            Boxes = new List<(Vector3, Vector3)>[count],
            Meshes = new VoxMesher.Arrays[count],
            Skipped = new bool[count],
        };
        if (give != null)
        {
            foreach ((int _, int piece) in give)
            {
                if (piece >= 0 && piece < count)
                {
                    build.Skipped[piece] = true;
                }
            }
        }
        for (int s = 0; s < count; s++)
        {
            var min = new Vector3I(s % sections.X, (s / sections.X) % sections.Y, s / (sections.X * sections.Y)) * edge;
            Vector3I max = (min + new Vector3I(edge, edge, edge)).Min(grid.Size);
            build.Boxes[s] = new List<(Vector3, Vector3)>();
            grid.Boxes(min, max, build.Step, build.Boxes[s]);
            // Built even where it is to take over its part's mesh: that mesh
            // may not be up to date when the plan is carried out, and a piece
            // must never be made with a section missing.
            build.Meshes[s] = VoxMesher.BuildArrays(grid.Cells, grid.Size, min, max, Palette, Emission, grid.Origin, grid.VoxelSize, -1);
        }
        return build;
    }

    /// <summary>
    /// The finest collision that stays within <paramref name="maxBoxes"/>:
    /// exact voxels if it can, so pieces of one fracture start touching rather
    /// than overlapping.
    /// </summary>
    public static int CollisionStepFor(VoxelGrid grid, int maxBoxes)
    {
        var boxes = new List<(Vector3, Vector3)>();
        int step = grid.SolidCount > 4000 ? 2 : 1;
        for (; step < 8; step *= 2)
        {
            boxes.Clear();
            grid.Boxes(Vector3I.Zero, grid.Size, step, boxes);
            if (boxes.Count <= maxBoxes)
            {
                break;
            }
        }
        return step;
    }

    /// <summary>Fracture blocks for a released piece, in voxels: storey-sized,
    /// grown until there are no more than the cap. Zero: too small to fracture.</summary>
    private static Vector3 FractureBlock(int cells, Vector3I size, float voxel, VoxelDestructionStats stats)
    {
        if (cells * voxel * voxel * voxel < stats.FractureVolume)
        {
            return Vector3.Zero;
        }
        var block = new Vector3(stats.FractureWidth / voxel, stats.FractureWidth / voxel, stats.FractureHeight / voxel);
        while (Blocks(size, block) > stats.MaxFracturePieces)
        {
            block *= 1.25f;
        }
        return Blocks(size, block) > 1 ? block : Vector3.Zero;
    }

    /// <summary>Crumble blocks: half the piece's size each way, no smaller than
    /// <see cref="VoxelDestructionStats.MinCrumbleSize"/>. Zero: too small to split.</summary>
    private static Vector3 CrumbleBlock(Vector3I size, float voxel, VoxelDestructionStats stats)
    {
        float least = stats.MinCrumbleSize / voxel;
        var block = new Vector3(
            Mathf.Max(size.X * 0.5f, least), Mathf.Max(size.Y * 0.5f, least), Mathf.Max(size.Z * 0.5f, least));
        return Blocks(size, block) > 1 ? block : Vector3.Zero;
    }

    /// <summary>Snap blocks: its whole size, but its longest side cut into
    /// segments of about <see cref="VoxelDestructionStats.SnapLength"/>.
    /// Zero: too short to snap.</summary>
    private static Vector3 SnapBlock(Vector3I size, float voxel, VoxelDestructionStats stats)
    {
        var block = new Vector3(size.X, size.Y, size.Z);
        int axis = (int)block.MaxAxisIndex();
        int segments = Mathf.RoundToInt(block[axis] * voxel / stats.SnapLength);
        if (segments < 2)
        {
            return Vector3.Zero;
        }
        block[axis] /= segments;
        return block;
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
            min = min.Min(Grid.CellOf(i));
        }
        return min;
    }

    private Vector3I CellMax(List<int> cells)
    {
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        foreach (int i in cells)
        {
            max = max.Max(Grid.CellOf(i));
        }
        return max;
    }

    /// <summary>Centre of a set of cells, in the part's space.</summary>
    private Vector3 Centre(List<int> cells)
    {
        Vector3 sum = Vector3.Zero;
        foreach (int i in cells)
        {
            Vector3I c = Grid.CellOf(i);
            sum += Grid.CellCentre(c.X, c.Y, c.Z);
        }
        return sum / cells.Count;
    }
}
