using Godot;
using DillControl.Horde;

namespace DillControl.Planet;

/// <summary>
/// The planet as the horde sees it: a cube-sphere grid of flow cells over the
/// heightfield, with up away from the centre and a horizon to cull against.
///
/// The horde's ground and the flow field's graph in one, as
/// <see cref="FlatGround"/> is on the flats. Eight neighbours, like the flats,
/// so a crowd crossing open ground walks the diagonal rather than a staircase;
/// at the eight cube corners one diagonal does not exist and is left empty.
///
/// Everything a horde pass calls is arithmetic over the heightfield and arrays
/// built here once, so it is safe from the parallel passes (see
/// <see cref="IHordeGround"/>). Craters change heights on the main thread
/// between ticks.
/// </summary>
public sealed class PlanetGround : IHordeGround, IFlowGraph
{
    private const int DirectionCount = 8;

    // Each slot as a pair of CubeSphere steps: orthogonal slots have one.
    private static readonly int[] FirstStep =
    {
        CubeSphere.DirPlusA, CubeSphere.DirPlusA, CubeSphere.DirPlusB, CubeSphere.DirMinusA,
        CubeSphere.DirMinusA, CubeSphere.DirMinusA, CubeSphere.DirMinusB, CubeSphere.DirPlusA,
    };

    private static readonly int[] SecondStep = { -1, CubeSphere.DirPlusB, -1, CubeSphere.DirPlusB, -1, CubeSphere.DirMinusB, -1, CubeSphere.DirMinusB };

    private readonly PlanetHeightfield _heights;
    private readonly Vector3[] _cellDirection;
    private readonly Vector3[] _cellCentre;
    private int[] _neighbour;
    private Vector3[] _tangent;
    private float[] _arc;

    /// <summary>Cells something authored stands in. A slope restamp leaves them blocked.</summary>
    private readonly bool[] _obstacle;

    public PlanetGround(PlanetHeightfield heights, float wantedCellSize)
    {
        _heights = heights;
        float faceArc = Mathf.Pi * 0.5f * heights.Radius;
        // Rounded, not to a power of two: the lab's grid was a power of two
        // for its wetness texture, which did not come across, and NearestPo2
        // rounds up -- a 1350 m planet got four times the cells of a 1200 m
        // one, and four times the flow build.
        GridSize = Mathf.Max(4, Mathf.RoundToInt(faceArc / Mathf.Max(wantedCellSize, 1.0f)));
        CellCount = CubeSphere.FaceCount * GridSize * GridSize;
        CellSize = faceArc / GridSize;

        _cellDirection = new Vector3[CellCount];
        _cellCentre = new Vector3[CellCount];
        _neighbour = new int[CellCount * DirectionCount];
        _tangent = new Vector3[CellCount * DirectionCount];
        _arc = new float[CellCount * DirectionCount];
        _obstacle = new bool[CellCount];

        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int v = 0; v < GridSize; v++)
            {
                for (int u = 0; u < GridSize; u++)
                {
                    int cell = Cell(face, u, v);
                    Vector3 direction = CubeSphere.CellDirection(GridSize, face, u, v);
                    _cellDirection[cell] = direction;
                    _cellCentre[cell] = heights.SurfacePoint(direction);
                }
            }
        }

        BuildNeighbours();
    }

    /// <summary>Cells along one face edge.</summary>
    public int GridSize { get; }

    public int Cell(int face, int u, int v) => (((face * GridSize) + v) * GridSize) + u;

    public Vector3 CellDirection(int cell) => _cellDirection[cell];

    // ---------------- IHordeGround ----------------

    public int CellCount { get; }

    public float CellSize { get; }

    public Aabb Bounds
    {
        get
        {
            float reach = _heights.Radius + _heights.Amplitude + 220.0f;
            return new Aabb(new Vector3(-reach, -reach, -reach), new Vector3(reach, reach, reach) * 2.0f);
        }
    }

    public float HorizonRadius => _heights.Radius;

    public Vector3 UpAt(Vector3 point)
    {
        float length = point.Length();
        return length > 0.001f ? point / length : Vector3.Up;
    }

    public Vector3 GroundBelow(Vector3 point, out int cell)
    {
        if (!CubeSphere.Locate(point, out int face, out float a, out float b))
        {
            cell = 0;
            return _cellCentre[0];
        }

        cell = CellOf(face, a, b);
        Vector3 unit = point.Normalized();
        return unit * (_heights.Radius + _heights.SampleHeight(face, a, b));
    }

    public Vector3 Scatter(Vector3 centre, float outer, float inner, float u1, float u2)
    {
        Vector3 up = UpAt(centre);
        Vector3 east = up.Cross(Mathf.Abs(up.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
        Vector3 north = up.Cross(east);

        float angle = u1 * Mathf.Tau;
        float innerSquared = inner * inner;
        float distance = Mathf.Sqrt(innerSquared + (u2 * ((outer * outer) - innerSquared)));

        // Walked along the surface: the ring's radius is an arc, not a chord.
        Vector3 along = (east * Mathf.Cos(angle)) + (north * Mathf.Sin(angle));
        float arc = Mathf.Min(distance / _heights.Radius, Mathf.Pi);
        Vector3 direction = (up * Mathf.Cos(arc)) + (along * Mathf.Sin(arc));
        return _heights.SurfacePoint(direction);
    }

    public int CellOf(Vector3 point)
    {
        return CubeSphere.Locate(point, out int face, out float a, out float b) ? CellOf(face, a, b) : 0;
    }

    public int CellOf(int face, float a, float b)
    {
        int u = Mathf.Clamp((int)((a + 1.0f) * 0.5f * GridSize), 0, GridSize - 1);
        int v = Mathf.Clamp((int)((b + 1.0f) * 0.5f * GridSize), 0, GridSize - 1);
        return Cell(face, u, v);
    }

    // ---------------- IFlowGraph ----------------

    public int Directions => DirectionCount;

    /// <summary>Half way round, by the longest way a search could take: generous on purpose.</summary>
    public float LongestPath => Mathf.Pi * _heights.Radius * 2.0f;

    public int Neighbour(int cell, int direction) => _neighbour[(cell * DirectionCount) + direction];

    public Vector3 NeighbourTangent(int cell, int direction) => _tangent[(cell * DirectionCount) + direction];

    public float NeighbourArc(int cell, int direction) => _arc[(cell * DirectionCount) + direction];

    public Vector3 CellCentre(int cell) => _cellCentre[cell];

    private void BuildNeighbours()
    {
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int v = 0; v < GridSize; v++)
            {
                for (int u = 0; u < GridSize; u++)
                {
                    int cell = Cell(face, u, v);
                    Vector3 here = _cellDirection[cell];

                    for (int direction = 0; direction < DirectionCount; direction++)
                    {
                        int slot = (cell * DirectionCount) + direction;
                        int nFace, nU, nV;
                        bool exists = SecondStep[direction] < 0
                            ? CubeSphere.Step(GridSize, face, u, v, FirstStep[direction], out nFace, out nU, out nV, out _)
                            : CubeSphere.StepDiagonal(GridSize, face, u, v, FirstStep[direction], SecondStep[direction],
                                                      out nFace, out nU, out nV);
                        if (!exists)
                        {
                            _neighbour[slot] = -1;
                            _tangent[slot] = Vector3.Zero;
                            _arc[slot] = 0.0f;
                            continue;
                        }

                        int neighbour = Cell(nFace, nU, nV);
                        Vector3 there = _cellDirection[neighbour];

                        // From the two directions rather than the face axes, so
                        // it stays right where a step crossed a seam.
                        Vector3 tangent = (there - here).Slide(here);
                        _neighbour[slot] = neighbour;
                        _tangent[slot] = tangent.LengthSquared() > 1e-12f ? tangent.Normalized() : Vector3.Zero;
                        _arc[slot] = here.AngleTo(there) * _heights.Radius;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Drops the neighbour tables once the flow field has copied them (it
    /// copies at construction and never asks again). At 8 m cells they are
    /// tens of megabytes held for nothing. The graph half of this class
    /// answers nothing after it.
    /// </summary>
    public void ReleaseGraph()
    {
        _neighbour = null;
        _tangent = null;
        _arc = null;
    }

    // ---------------- Stamping ----------------

    /// <summary>
    /// Weights every cell by its slope: dearer the steeper, a wall past
    /// <paramref name="maxSlopeDegrees"/>. Within <paramref name="radius"/> of
    /// <paramref name="centre"/> only, when given one -- after a crater.
    /// Cells an authored thing blocks stay blocked.
    /// </summary>
    public void StampSlope(FlowField flow, float maxSlopeDegrees, Vector3 centre = default, float radius = 0.0f)
    {
        float maxSlope = Mathf.DegToRad(maxSlopeDegrees);
        float sampleArc = CellSize * 0.5f;
        Vector3 direction = centre.LengthSquared() > 0.0f ? centre.Normalized() : Vector3.Zero;
        float minDot = radius > 0.0f
            ? Mathf.Cos(Mathf.Min((radius / _heights.Radius) + (CellSize * 2.0f / _heights.Radius), Mathf.Pi))
            : -2.0f;

        for (int cell = 0; cell < CellCount; cell++)
        {
            if (_obstacle[cell] || (radius > 0.0f && _cellDirection[cell].Dot(direction) < minDot))
            {
                continue;
            }

            _cellCentre[cell] = _heights.SurfacePoint(_cellDirection[cell]);
            float slope = _heights.SlopeAt(_cellDirection[cell], sampleArc);
            if (slope >= maxSlope)
            {
                flow.SetWeight(cell, FlowField.Blocked);
                continue;
            }

            float t = slope / maxSlope;
            flow.SetWeight(cell, (byte)Mathf.Clamp(1 + Mathf.RoundToInt(t * t * (FlowField.MaxWeight - 1)), 1, FlowField.MaxWeight));
        }
    }

    /// <summary>
    /// Blocks every cell within <paramref name="radius"/> of
    /// <paramref name="centre"/> that something authored stands in, by one
    /// physics box per cell from knee to head height, upright on the ground.
    /// The terrain's own colliders are passed in <paramref name="exclude"/>,
    /// or every slope would find the hill it stands on.
    /// </summary>
    public int StampObstacles(PhysicsDirectSpaceState3D space, FlowField flow, uint mask, Vector3 centre, float radius,
                              Godot.Collections.Array<Rid> exclude)
    {
        using BoxShape3D box = new() { Size = new Vector3(CellSize * 0.9f, 1.6f, CellSize * 0.9f) };
        using PhysicsShapeQueryParameters3D query = new()
        {
            Shape = box,
            CollisionMask = mask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = exclude,
        };

        float minDot = Mathf.Cos(Mathf.Min(radius / _heights.Radius, Mathf.Pi));
        Vector3 direction = centre.Normalized();
        int blocked = 0;
        for (int cell = 0; cell < CellCount; cell++)
        {
            if (_cellDirection[cell].Dot(direction) < minDot)
            {
                continue;
            }

            Vector3 up = _cellDirection[cell];
            query.Transform = new Transform3D(PlanetHeightfield.UprightBasis(up, Vector3.Forward), _cellCentre[cell] + (up * 1.3f));
            if (space.IntersectShape(query, 1).Count > 0)
            {
                flow.SetWeight(cell, FlowField.Blocked);
                _obstacle[cell] = true;
                blocked++;
            }
        }

        return blocked;
    }
}
