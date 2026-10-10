using Godot;
using DillControl.Core;

namespace DillControl.Horde;

/// <summary>
/// Level ground, a square of it, cut into square flow cells.
///
/// The horde's ground and the flow field's graph in one, because on the flats
/// they are the same grid: a cell is where an agent stands and what the
/// search walks. Eight neighbours rather than the planet grid's four, which
/// costs a little more search and gives flow arrows on the diagonals, so a
/// crowd crossing open ground does not walk a staircase.
///
/// Height is one number. The flats are flat on purpose -- the focused level for
/// the line, the horde and the weapons -- and a heightfield is the planet's
/// job when it arrives.
/// </summary>
public sealed class FlatGround : IHordeGround, IFlowGraph, IObstacleGround
{
    private static readonly int[] StepU = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] StepV = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private readonly Vector3 _min;
    private readonly float _size;
    private readonly float _inverseCell;
    private readonly Vector3[] _tangent = new Vector3[8];
    private readonly float[] _arc = new float[8];

    public FlatGround(Vector3 centre, float size, float cellSize)
    {
        CellSize = Mathf.Max(cellSize, 0.5f);
        GridSize = Mathf.Max(4, Mathf.CeilToInt(size / CellSize));
        _size = GridSize * CellSize;
        _min = new Vector3(centre.X - (_size * 0.5f), centre.Y, centre.Z - (_size * 0.5f));
        _inverseCell = 1.0f / CellSize;
        Height = centre.Y;
        CellCount = GridSize * GridSize;

        for (int direction = 0; direction < 8; direction++)
        {
            Vector3 step = new(StepU[direction], 0.0f, StepV[direction]);
            _tangent[direction] = step.Normalized();
            _arc[direction] = step.Length() * CellSize;
        }
    }

    public float CellSize { get; }

    /// <summary>Cells along one side.</summary>
    public int GridSize { get; }

    /// <summary>The ground's height in world space.</summary>
    public float Height { get; }

    // ---------------- IHordeGround ----------------

    public int CellCount { get; }

    public Aabb Bounds => new(
        new Vector3(_min.X, Height - 20.0f, _min.Z),
        new Vector3(_size, 220.0f, _size));

    public float HorizonRadius => 0.0f;

    public Vector3 UpAt(Vector3 point) => Vector3.Up;

    public Vector3 GroundBelow(Vector3 point, out int cell)
    {
        cell = CellOf(point);
        return new Vector3(point.X, Height, point.Z);
    }

    public Vector3 Scatter(Vector3 centre, float outer, float inner, float u1, float u2)
    {
        float angle = u1 * Mathf.Tau;
        float innerSquared = inner * inner;
        float distance = Mathf.Sqrt(innerSquared + (u2 * ((outer * outer) - innerSquared)));
        Vector3 point = centre + (new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * distance);

        // Kept a cell inside the edge, where every agent still has somewhere
        // to walk to.
        float margin = CellSize;
        point.X = Mathf.Clamp(point.X, _min.X + margin, _min.X + _size - margin);
        point.Z = Mathf.Clamp(point.Z, _min.Z + margin, _min.Z + _size - margin);
        point.Y = Height;
        return point;
    }

    public int CellOf(Vector3 point)
    {
        int u = Mathf.Clamp((int)((point.X - _min.X) * _inverseCell), 0, GridSize - 1);
        int v = Mathf.Clamp((int)((point.Z - _min.Z) * _inverseCell), 0, GridSize - 1);
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

    public Vector3 CellCentre(int cell)
    {
        int v = cell / GridSize;
        int u = cell - (v * GridSize);
        return new Vector3(_min.X + ((u + 0.5f) * CellSize), Height, _min.Z + ((v + 0.5f) * CellSize));
    }

    // ---------------- Obstacles ----------------

    /// <summary>
    /// Blocks every cell something solid stands in, by asking physics once per
    /// cell with a box from knee to head height.
    ///
    /// This is what makes the compound authored rather than stamped by a
    /// generator: the pillars and blocks are nodes a designer moves, and the
    /// horde routes round wherever they now stand. The box starts above the
    /// ground's own top face, so the ground is never found. Run once at load;
    /// at 90,000 cells it is a few tens of milliseconds.
    /// </summary>
    /// <returns>How many cells were blocked.</returns>
    public int StampObstacles(PhysicsDirectSpaceState3D space, FlowField flow, uint mask)
    {
        using BoxShape3D box = new() { Size = new Vector3(CellSize * 0.9f, 1.6f, CellSize * 0.9f) };
        using PhysicsShapeQueryParameters3D query = new()
        {
            Shape = box,
            CollisionMask = mask,
            CollideWithAreas = false,
            CollideWithBodies = true,
        };

        int blocked = 0;
        for (int cell = 0; cell < CellCount; cell++)
        {
            query.Transform = new Transform3D(Basis.Identity, CellCentre(cell) + new Vector3(0.0f, 1.3f, 0.0f));
            if (space.IntersectShape(query, 1).Count > 0)
            {
                flow.SetWeight(cell, FlowField.Blocked);
                blocked++;
            }
        }

        return blocked;
    }

    /// <summary>What counts as an obstacle: authored world geometry and props.</summary>
    public static uint ObstacleMask => GameLayers.Mask(GameLayers.World, GameLayers.Prop);

    public int RestampObstacles(PhysicsDirectSpaceState3D space, FlowField flow, uint mask, Vector3 centre, float radius,
                                System.Func<GodotObject, bool> blocks)
    {
        using PhysicsShapeQueryParameters3D query = ObstacleQuery.Box(CellSize, mask, out BoxShape3D box);
        using (box)
        {
            int u0 = Mathf.Clamp((int)((centre.X - radius - _min.X) * _inverseCell), 0, GridSize - 1);
            int u1 = Mathf.Clamp((int)((centre.X + radius - _min.X) * _inverseCell), 0, GridSize - 1);
            int v0 = Mathf.Clamp((int)((centre.Z - radius - _min.Z) * _inverseCell), 0, GridSize - 1);
            int v1 = Mathf.Clamp((int)((centre.Z + radius - _min.Z) * _inverseCell), 0, GridSize - 1);
            float radiusSquared = radius * radius;
            int changed = 0;
            for (int v = v0; v <= v1; v++)
                for (int u = u0; u <= u1; u++)
                {
                    int cell = (v * GridSize) + u;
                    Vector3 at = CellCentre(cell);
                    if (new Vector2(at.X - centre.X, at.Z - centre.Z).LengthSquared() > radiusSquared)
                    {
                        continue;
                    }
                    query.Transform = new Transform3D(Basis.Identity, at + new Vector3(0.0f, 1.3f, 0.0f));
                    bool blocked = ObstacleQuery.Blocked(space, query, blocks);
                    bool was = flow.BaseWeightAt(cell) == FlowField.Blocked;
                    if (blocked != was)
                    {
                        // The flats have no slope: free ground weighs one.
                        flow.SetWeight(cell, blocked ? FlowField.Blocked : (byte)1);
                        changed++;
                    }
                }
            return changed;
        }
    }
}
