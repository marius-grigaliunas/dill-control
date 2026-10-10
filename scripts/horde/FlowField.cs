using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace DillControl.Horde;

/// <summary>
/// A flow field over the whole ground: one search outward from a single target,
/// leaving every cell with a world-space direction that walks downhill toward
/// it.
///
/// This is the reason the horde can be thousands of agents. Almost everything
/// in the game wants to reach the same place, so the cost of pathing N agents
/// collapses from N searches to one search plus N array lookups. The same field
/// absorbs dynamic obstruction for free: a wall, a fresh crater or a flooded
/// patch is a cost stamp, and the horde reroutes on the next recompute with no
/// navmesh rebake anywhere.
///
/// The search is Dial's algorithm rather than a heap. Edge costs are small
/// integers, so a bucket queue is O(V + E) with no comparisons and no
/// allocation -- every array it touches is preallocated and reused.
///
/// Recompute runs on a worker thread into back buffers and is published by
/// swapping two references on the main thread. The worker touches nothing but
/// plain arrays: no Node, no Resource, no physics server.
///
/// Ported from the lab's planet folder onto <see cref="IFlowGraph"/>, so the
/// same search runs over the flats' square grid now and the planet's
/// cube-sphere grid later. Slope stamping went with the planet's terrain and
/// comes back with it.
/// </summary>
public sealed class FlowField
{
    /// <summary>Highest traversal weight before a cell is considered impassable.</summary>
    public const byte MaxWeight = 8;
    public const byte Blocked = 255;
    public const int Unreachable = int.MaxValue;

    private const int Idle = 0;
    private const int Building = 1;
    private const int ReadyToSwap = 2;

    private readonly IFlowGraph _grid;

    /// <summary>
    /// The graph, copied into flat arrays once. The search visits every edge
    /// of every cell, and through the interface that was a virtual call and an
    /// integer division per edge: on the flats' 90,000 cells, most of a 40 ms
    /// build. Cell-major, <see cref="_directions"/> slots per cell; a
    /// neighbour of -1 is an edge of the ground.
    /// </summary>
    private readonly int _directions;
    private readonly int[] _neighbour;
    private readonly float[] _arc;
    private readonly Vector3[] _tangent;

    /// <summary>
    /// Per-cell traversal weight as the *stamps* left it: slope, walls, craters.
    /// Everything that paints cost writes here and only here.
    /// </summary>
    private readonly byte[] _baseWeight;

    /// <summary>
    /// The weight the search actually reads: <see cref="_baseWeight"/> with any
    /// live overlay folded on top.
    ///
    /// The two are separate because an overlay has to be recomputed from
    /// scratch every time it changes. An overlay that read a weight, added to
    /// it and wrote it back to one array would be a ratchet: run twice on a
    /// timer, every cell it touched would climb to MaxWeight and stay there.
    /// Reading base and writing composed makes it idempotent, which is what
    /// makes it safe to run on the rebuild timer at all. (The lab's coolant
    /// wetness was that overlay.)
    ///
    /// Stamps write both, so a field with no overlay is exactly what it always
    /// was.
    /// </summary>
    private readonly byte[] _weight;

    /// <summary>Snapshot handed to the worker, so live edits cannot tear a search.</summary>
    private readonly byte[] _weightSnapshot;

    private int[] _costFront;
    private int[] _costBack;
    private Vector3[] _flowFront;
    private Vector3[] _flowBack;

    /// <summary>
    /// Bucket lists are chains of queue *entries*, not of cells.
    ///
    /// They used to thread one next pointer through each cell. A cell whose
    /// cost improved after it was queued was queued again, overwriting that
    /// pointer and silently cutting off the rest of the list it was still
    /// in, so every cell behind it in that bucket was never expanded. On the
    /// spike planet that left 6.3% of the surface unreachable with not one
    /// cell blocked, and 23,027 of 24,576 cells with a cost a plain Dijkstra
    /// disagreed with -- found by tools/voxel-bench's planet lab, whose test
    /// now holds this search to a reference Dijkstra. An entry pool keeps the
    /// no-allocation property: a cell is re-queued only when its cost falls,
    /// which can happen at most once per incoming edge, so four entries per
    /// cell plus the seed is enough -- one entry per incoming edge, in general.
    /// </summary>
    private readonly int[] _bucketHead;
    private readonly int[] _entryCell;
    private readonly int[] _entryNext;

    /// <summary>The cells the last search reached, in order: the only ones a bounded build turns into directions.</summary>
    private readonly int[] _reached;

    private int _state = Idle;
    private int _targetCell;
    private float _lastBuildMilliseconds;

    public FlowField(IFlowGraph grid)
    {
        _grid = grid;

        _directions = grid.Directions;
        int edges = grid.CellCount * _directions;
        _neighbour = new int[edges];
        _arc = new float[edges];
        _tangent = new Vector3[edges];
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            for (int direction = 0; direction < _directions; direction++)
            {
                int slot = (cell * _directions) + direction;
                _neighbour[slot] = grid.Neighbour(cell, direction);
                _arc[slot] = grid.NeighbourArc(cell, direction);
                _tangent[slot] = grid.NeighbourTangent(cell, direction);
            }
        }

        _baseWeight = new byte[grid.CellCount];
        _weight = new byte[grid.CellCount];
        _weightSnapshot = new byte[grid.CellCount];
        _costFront = new int[grid.CellCount];
        _costBack = new int[grid.CellCount];
        _reached = new int[grid.CellCount];
        _flowFront = new Vector3[grid.CellCount];
        _flowBack = new Vector3[grid.CellCount];

        // Enough buckets for the longest possible path at the worst passable
        // weight.
        int longestPath = (int)(grid.LongestPath * MaxWeight) + 16;
        _bucketHead = new int[longestPath];
        _entryCell = new int[(grid.CellCount * grid.Directions) + 1];
        _entryNext = new int[_entryCell.Length];

        for (int i = 0; i < _weight.Length; i++)
        {
            _baseWeight[i] = 1;
            _weight[i] = 1;
        }
        for (int i = 0; i < _costFront.Length; i++)
        {
            _costFront[i] = Unreachable;
        }
    }

    /// <summary>
    /// The costliest route the search follows, in its own units (metres times
    /// weight); zero searches everything. A planet's ground is hundreds of
    /// thousands of cells and the crowd that matters is within a kilometre of
    /// the player, so the planet bounds it; past the bound a cell is left
    /// unreached and the horde walks straight at its hunt point there
    /// (<see cref="IsBeyondSearch"/>). The flats search everything.
    /// </summary>
    public int MaxCost { get; set; }

    /// <summary>True when a cell is open but was past <see cref="MaxCost"/> in the last published search.</summary>
    public bool IsBeyondSearch(int cell) =>
        MaxCost > 0 && _costFront[cell] == Unreachable && _weight[cell] != Blocked;

    public int Generation { get; private set; }
    public int TargetCell => _targetCell;
    public float LastBuildMilliseconds => _lastBuildMilliseconds;
    public bool IsBuilding => Volatile.Read(ref _state) == Building;

    /// <summary>How many cells the last published field could actually reach.</summary>
    public int ReachableCells { get; private set; }

    /// <summary>The weight the search reads: base plus whatever overlays it.</summary>
    public byte WeightAt(int cell) => _weight[cell];

    /// <summary>The weight the stamps left, with no overlay folded in.</summary>
    public byte BaseWeightAt(int cell) => _baseWeight[cell];

    public int CostAt(int cell) => _costFront[cell];

    /// <summary>World-space unit direction toward the target, or zero if stranded.</summary>
    public Vector3 FlowAt(int cell) => _flowFront[cell];

    /// <summary>
    /// Stamps a weight. Writes the base and the composed copy together, so a
    /// field nobody overlays behaves exactly as it did before the split.
    /// </summary>
    public void SetWeight(int cell, byte weight)
    {
        _baseWeight[cell] = weight;
        _weight[cell] = weight;
    }

    /// <summary>
    /// Writes only the composed weight, leaving the base untouched. For an
    /// overlay that is recomputed in full each time it runs -- see the note on
    /// <see cref="_weight"/> for why an overlay must never write the base.
    /// </summary>
    public void SetComposedWeight(int cell, byte weight) => _weight[cell] = weight;

    public void ResetWeights()
    {
        for (int i = 0; i < _weight.Length; i++)
        {
            _baseWeight[i] = 1;
            _weight[i] = 1;
        }
    }

    /// <summary>
    /// Paints a disc of impassable cells. This is the acceptance test for the
    /// whole subsystem: stamp a wall across the horde's approach and they part
    /// around it on the next recompute, with nothing rebaked.
    /// </summary>
    public int StampWall(Vector3 centre, float radius)
    {
        float radiusSquared = radius * radius;
        int stamped = 0;

        for (int cell = 0; cell < _grid.CellCount; cell++)
        {
            if (_grid.CellCentre(cell).DistanceSquaredTo(centre) <= radiusSquared)
            {
                SetWeight(cell, Blocked);
                stamped++;
            }
        }
        return stamped;
    }

    /// <summary>
    /// Raises traversal cost over a disc without making it impassable.
    ///
    /// The difference from <see cref="StampWall"/> is the whole point of a
    /// funnel: a wall makes the horde go round, while expensive ground makes
    /// them *prefer* to go round and still come through if the alternative is
    /// worse. That is what bends an approach rather than blocking one.
    /// </summary>
    public int StampCost(Vector3 centre, float radius, byte weight)
    {
        float radiusSquared = radius * radius;
        byte clamped = (byte)Mathf.Clamp(weight, 1, MaxWeight);
        int stamped = 0;

        for (int cell = 0; cell < _grid.CellCount; cell++)
        {
            if (_baseWeight[cell] == Blocked
                || _grid.CellCentre(cell).DistanceSquaredTo(centre) > radiusSquared)
            {
                continue;
            }

            if (_baseWeight[cell] < clamped)
            {
                SetWeight(cell, clamped);
                stamped++;
            }
        }

        return stamped;
    }

    /// <summary>
    /// Kicks off a rebuild if one is not already running. Returns false when
    /// the previous build has not been collected yet, which is the caller's cue
    /// to try again next frame rather than to queue up work.
    /// </summary>
    public bool RequestBuild(int targetCell, bool threaded)
    {
        if (Interlocked.CompareExchange(ref _state, Building, Idle) != Idle)
        {
            return false;
        }

        _targetCell = Mathf.Clamp(targetCell, 0, _grid.CellCount - 1);
        System.Array.Copy(_weight, _weightSnapshot, _weight.Length);

        if (threaded)
        {
            Task.Run(RunBuild);
        }
        else
        {
            RunBuild();
            Collect();
        }
        return true;
    }

    /// <summary>
    /// Publishes a finished rebuild. Main thread only, and the only place the
    /// front buffers are ever reassigned.
    /// </summary>
    public bool Collect()
    {
        if (Volatile.Read(ref _state) != ReadyToSwap)
        {
            return false;
        }

        (_costFront, _costBack) = (_costBack, _costFront);
        (_flowFront, _flowBack) = (_flowBack, _flowFront);
        Generation++;

        Volatile.Write(ref _state, Idle);
        return true;
    }

    private void RunBuild()
    {
        Stopwatch watch = Stopwatch.StartNew();

        int cells = _grid.CellCount;
        int[] cost = _costBack;
        Vector3[] flow = _flowBack;

        for (int i = 0; i < cells; i++)
        {
            cost[i] = Unreachable;
        }
        System.Array.Fill(_bucketHead, -1);

        int highest = 0;
        int reached = 0;
        int maxCost = MaxCost > 0 ? MaxCost : int.MaxValue;

        // The target is seeded whatever its own weight says. Cost on the
        // destination cell is meaningless -- nothing has to leave it -- and
        // honouring a Blocked there would strand the entire horde with no
        // explanation the moment someone stamped an obstacle over the target.
        cost[_targetCell] = 0;
        _entryCell[0] = _targetCell;
        _entryNext[0] = -1;
        _bucketHead[0] = 0;
        int entries = 1;

        for (int bucket = 0; bucket < _bucketHead.Length; bucket++)
        {
            int entry = _bucketHead[bucket];
            if (entry == -1)
            {
                // Nothing left anywhere ahead of us means the search is done.
                if (bucket > highest || bucket > maxCost)
                {
                    break;
                }
                continue;
            }

            _bucketHead[bucket] = -1;

            while (entry != -1)
            {
                int next = _entryNext[entry];
                int cell = _entryCell[entry];

                // A cell can be queued more than once, each time in its own
                // entry; the one whose bucket matches its cost is current and
                // the rest are stale.
                if (cost[cell] == bucket)
                {
                    _reached[reached++] = cell;
                    int first = cell * _directions;
                    for (int slot = first; slot < first + _directions; slot++)
                    {
                        int neighbour = _neighbour[slot];
                        if (neighbour < 0)
                        {
                            continue;
                        }

                        byte weight = _weightSnapshot[neighbour];
                        if (weight == Blocked)
                        {
                            continue;
                        }

                        int step = (int)(_arc[slot] * weight);
                        if (step < 1)
                        {
                            step = 1;
                        }

                        int candidate = bucket + step;
                        if (candidate >= _bucketHead.Length || candidate > maxCost || candidate >= cost[neighbour])
                        {
                            continue;
                        }

                        cost[neighbour] = candidate;
                        _entryCell[entries] = neighbour;
                        _entryNext[entries] = _bucketHead[candidate];
                        _bucketHead[candidate] = entries;
                        entries++;
                        if (candidate > highest)
                        {
                            highest = candidate;
                        }
                    }
                }

                entry = next;
            }
        }

        if (MaxCost > 0)
        {
            BuildFlowVectorsReached(cost, flow, reached);
        }
        else
        {
            BuildFlowVectors(cost, flow);
        }
        ReachableCells = reached;

        watch.Stop();
        _lastBuildMilliseconds = (float)watch.Elapsed.TotalMilliseconds;

        Volatile.Write(ref _state, ReadyToSwap);
    }

    /// <summary>
    /// Turns the integration field into one world-space direction per cell.
    ///
    /// The direction is the summed gradient over every neighbour rather
    /// than a step toward the single cheapest one. Snapping to a few headings
    /// would make agents visibly jitter every time they crossed a cell
    /// boundary, and at this cell size that jitter would be metres wide.
    ///
    /// A blocked cell has no cost of its own, so it points at its cheapest
    /// open neighbour instead: an agent shoved into the footprint of a pillar
    /// walks back out rather than standing there stranded. The footprint is a
    /// whole cell and the pillar usually less, so this is routine near
    /// anything authored.
    /// </summary>
    private void BuildFlowVectors(int[] cost, Vector3[] flow)
    {
        for (int cell = 0; cell < flow.Length; cell++)
        {
            int here = cost[cell];
            int first = cell * _directions;

            if (here == Unreachable)
            {
                flow[cell] = _weightSnapshot[cell] == Blocked ? EscapeDirection(cost, first) : Vector3.Zero;
                continue;
            }

            Vector3 sum = Vector3.Zero;
            for (int slot = first; slot < first + _directions; slot++)
            {
                int neighbour = _neighbour[slot];
                if (neighbour < 0)
                {
                    continue;
                }

                int there = cost[neighbour];
                if (there < here)
                {
                    sum += _tangent[slot] * (here - there);
                }
            }

            flow[cell] = sum.LengthSquared() > 1e-8f ? sum.Normalized() : Vector3.Zero;
        }
    }

    /// <summary>
    /// The same directions for a bounded search: only the cells it reached,
    /// and the blocked cells beside them, so the pass costs what the search
    /// did rather than the whole planet. Everything else is cleared to zero.
    /// </summary>
    private void BuildFlowVectorsReached(int[] cost, Vector3[] flow, int reached)
    {
        System.Array.Clear(flow, 0, flow.Length);
        for (int i = 0; i < reached; i++)
        {
            int cell = _reached[i];
            int here = cost[cell];
            int first = cell * _directions;

            Vector3 sum = Vector3.Zero;
            for (int slot = first; slot < first + _directions; slot++)
            {
                int neighbour = _neighbour[slot];
                if (neighbour < 0)
                {
                    continue;
                }

                int there = cost[neighbour];
                if (there < here)
                {
                    sum += _tangent[slot] * (here - there);
                }
                else if (there == Unreachable && _weightSnapshot[neighbour] == Blocked)
                {
                    flow[neighbour] = EscapeDirection(cost, neighbour * _directions);
                }
            }

            flow[cell] = sum.LengthSquared() > 1e-8f ? sum.Normalized() : Vector3.Zero;
        }
    }

    /// <summary>Toward the cheapest reachable neighbour of a blocked cell, or zero if none is.</summary>
    private Vector3 EscapeDirection(int[] cost, int first)
    {
        int best = Unreachable;
        Vector3 direction = Vector3.Zero;
        for (int slot = first; slot < first + _directions; slot++)
        {
            int neighbour = _neighbour[slot];
            if (neighbour >= 0 && cost[neighbour] < best)
            {
                best = cost[neighbour];
                direction = _tangent[slot];
            }
        }

        return direction;
    }
}
