using Godot;
using DillControl.Horde;

namespace DillControl.Levels;

/// <summary>
/// The horde's ground in an arena: <see cref="FlatGround"/>'s square of
/// eight-neighbour cells, standing on <see cref="ArenaHeights"/> instead of
/// one height, with the planet's slope costs (dearer the steeper, a wall
/// past <c>MaxSlopeDegrees</c>). The terrain's own colliders are never an
/// obstacle; what stands on it is.
/// </summary>
public sealed class ArenaGround : IHordeGround, IFlowGraph, IObstacleGround
{
    private static readonly int[] StepU = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] StepV = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private readonly ArenaHeights _heights;
    private readonly Vector2 _min;
    private readonly float _size;
    private readonly float _inverseCell;
    private readonly float _maxSlope;
    private readonly Vector3[] _tangent = new Vector3[8];
    private readonly float[] _arc = new float[8];
    private readonly Vector3[] _centre;
    private readonly byte[] _slopeWeight;
    private readonly bool[] _obstacle;

    public ArenaGround(ArenaHeights heights, float cellSize, float maxSlopeDegrees)
    {
        _heights = heights;
        CellSize = Mathf.Max(cellSize, 0.5f);
        GridSize = Mathf.Max(4, Mathf.FloorToInt(heights.Size / CellSize));
        _size = GridSize * CellSize;
        float inset = (heights.Size - _size) * 0.5f;
        _min = heights.Min + new Vector2(inset, inset);
        _inverseCell = 1.0f / CellSize;
        _maxSlope = Mathf.DegToRad(maxSlopeDegrees);
        CellCount = GridSize * GridSize;

        for (int direction = 0; direction < 8; direction++)
        {
            Vector3 step = new(StepU[direction], 0.0f, StepV[direction]);
            _tangent[direction] = step.Normalized();
            _arc[direction] = step.Length() * CellSize;
        }

        _centre = new Vector3[CellCount];
        _slopeWeight = new byte[CellCount];
        _obstacle = new bool[CellCount];
        for (int cell = 0; cell < CellCount; cell++)
        {
            int v = cell / GridSize;
            int u = cell - (v * GridSize);
            float x = _min.X + ((u + 0.5f) * CellSize);
            float z = _min.Y + ((v + 0.5f) * CellSize);
            _centre[cell] = new Vector3(x, heights.HeightAt(x, z), z);
            _slopeWeight[cell] = SlopeWeight(heights.SlopeAt(x, z, CellSize * 0.5f));
        }
    }

    public float CellSize { get; }

    public int GridSize { get; }

    // ---------------- IHordeGround ----------------

    public int CellCount { get; }

    public Aabb Bounds => new(
        new Vector3(_min.X, _heights.Lowest - 20.0f, _min.Y),
        new Vector3(_size, (_heights.Highest - _heights.Lowest) + 240.0f, _size));

    public float HorizonRadius => 0.0f;

    public Vector3 UpAt(Vector3 point) => Vector3.Up;

    public Vector3 GroundBelow(Vector3 point, out int cell)
    {
        cell = CellOf(point);
        return new Vector3(point.X, _heights.HeightAt(point.X, point.Z), point.Z);
    }

    public Vector3 Scatter(Vector3 centre, float outer, float inner, float u1, float u2)
    {
        float angle = u1 * Mathf.Tau;
        float innerSquared = inner * inner;
        float distance = Mathf.Sqrt(innerSquared + (u2 * ((outer * outer) - innerSquared)));
        Vector3 point = centre + (new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * distance);

        float margin = CellSize;
        point.X = Mathf.Clamp(point.X, _min.X + margin, _min.X + _size - margin);
        point.Z = Mathf.Clamp(point.Z, _min.Y + margin, _min.Y + _size - margin);
        point.Y = _heights.HeightAt(point.X, point.Z);
        return point;
    }

    public int CellOf(Vector3 point)
    {
        int u = Mathf.Clamp((int)((point.X - _min.X) * _inverseCell), 0, GridSize - 1);
        int v = Mathf.Clamp((int)((point.Z - _min.Y) * _inverseCell), 0, GridSize - 1);
        return (v * GridSize) + u;
    }

    // ---------------- IFlowGraph ----------------

    public int Directions => 8;

    public float LongestPath => _size * 2.0f;

    public int Neighbour(int cell, int direction)
    {
        int v = cell / GridSize;
        int u = cell - (v * GridSize);
        int nu = u + StepU[direction];
        int nv = v + StepV[direction];
        return nu < 0 || nv < 0 || nu >= GridSize || nv >= GridSize ? -1 : (nv * GridSize) + nu;
    }

    public Vector3 NeighbourTangent(int cell, int direction) => _tangent[direction];

    public float NeighbourArc(int cell, int direction) => _arc[direction];

    public Vector3 CellCentre(int cell) => _centre[cell];

    // ---------------- Slope and obstacles ----------------

    /// <summary>Dearer the steeper, a wall past the steepest the horde climbs (as the planet's).</summary>
    private byte SlopeWeight(float slope)
    {
        if (slope >= _maxSlope)
        {
            return FlowField.Blocked;
        }
        float t = slope / _maxSlope;
        return (byte)Mathf.Clamp(1 + Mathf.RoundToInt(t * t * (FlowField.MaxWeight - 1)), 1, FlowField.MaxWeight);
    }

    public void StampSlope(FlowField flow)
    {
        for (int cell = 0; cell < CellCount; cell++)
        {
            flow.SetWeight(cell, _slopeWeight[cell]);
        }
    }

    /// <summary>
    /// Blocks every cell something stands in, one physics box per cell from
    /// knee to head height over the ground. The terrain's colliders are in
    /// <paramref name="exclude"/>, or every slope would find its own hill.
    /// </summary>
    public int StampObstacles(PhysicsDirectSpaceState3D space, FlowField flow, uint mask, Godot.Collections.Array<Rid> exclude)
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

        int blocked = 0;
        for (int cell = 0; cell < CellCount; cell++)
        {
            query.Transform = new Transform3D(Basis.Identity, _centre[cell] + new Vector3(0.0f, 1.3f, 0.0f));
            if (space.IntersectShape(query, 1).Count > 0)
            {
                flow.SetWeight(cell, FlowField.Blocked);
                _obstacle[cell] = true;
                blocked++;
            }
        }
        return blocked;
    }

    public int RestampObstacles(PhysicsDirectSpaceState3D space, FlowField flow, uint mask, Vector3 centre, float radius,
                                System.Func<GodotObject, bool> blocks)
    {
        using PhysicsShapeQueryParameters3D query = ObstacleQuery.Box(CellSize, mask, out BoxShape3D box);
        using (box)
        {
            bool Blocks(GodotObject collider) => collider is not SculptedTerrain && blocks(collider);
            int u0 = Mathf.Clamp((int)((centre.X - radius - _min.X) * _inverseCell), 0, GridSize - 1);
            int u1 = Mathf.Clamp((int)((centre.X + radius - _min.X) * _inverseCell), 0, GridSize - 1);
            int v0 = Mathf.Clamp((int)((centre.Z - radius - _min.Y) * _inverseCell), 0, GridSize - 1);
            int v1 = Mathf.Clamp((int)((centre.Z + radius - _min.Y) * _inverseCell), 0, GridSize - 1);
            float radiusSquared = radius * radius;
            int changed = 0;
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                {
                    int cell = (v * GridSize) + u;
                    Vector3 at = _centre[cell];
                    if (new Vector2(at.X - centre.X, at.Z - centre.Z).LengthSquared() > radiusSquared)
                    {
                        continue;
                    }
                    query.Transform = new Transform3D(Basis.Identity, at + new Vector3(0.0f, 1.3f, 0.0f));
                    bool blocked = ObstacleQuery.Blocked(space, query, Blocks);
                    if (blocked == _obstacle[cell])
                    {
                        continue;
                    }
                    _obstacle[cell] = blocked;
                    flow.SetWeight(cell, blocked ? FlowField.Blocked : _slopeWeight[cell]);
                    changed++;
                }
            return changed;
        }
    }
}
