using Godot;

namespace DillControl.Planet;

/// <summary>
/// The cube-sphere mapping every other planet system is expressed in: six
/// square faces, each a regular 2D grid, projected onto a sphere.
///
/// The reason for a cube-sphere rather than a UV sphere is downstream. The
/// terrain heightfield, the flow field's cost grid and the wetness field all
/// want to be plain 2D arrays with O(1) neighbours. A UV sphere has poles where
/// that breaks; a cube-sphere has six well-behaved grids and twelve seams, and
/// the seams are handled once, here.
///
/// Two invariants hold for every face and remove all per-face special cases:
///
///   AxisA cross AxisB == Normal   (right-handed, so one triangle winding works
///                                  on all six faces)
///   Normal, AxisA, AxisB          are a signed permutation of the basis vectors
///
/// The second is what lets the seam table be *derived* from the axes at static
/// init instead of hand-typed. A hand-written 24-entry table with rotation and
/// flip flags is the classic sign error that surfaces three subsystems later as
/// "agents walk into a seam and vanish".
/// </summary>
public static class CubeSphere
{
    public const int FaceCount = 6;

    /// <summary>Outward normal of each face: +X, -X, +Y, -Y, +Z, -Z.</summary>
    public static readonly Vector3[] Normal =
    {
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>The face's first tangent axis; the a coordinate runs along it.</summary>
    public static readonly Vector3[] AxisA =
    {
        new(0, 1, 0), new(0, 0, 1),
        new(0, 0, 1), new(1, 0, 0),
        new(1, 0, 0), new(0, 1, 0),
    };

    /// <summary>The face's second tangent axis; the b coordinate runs along it.</summary>
    public static readonly Vector3[] AxisB =
    {
        new(0, 0, 1), new(0, 1, 0),
        new(1, 0, 0), new(0, 0, 1),
        new(0, 1, 0), new(1, 0, 0),
    };

    // Direction indices used by Step. 0 = +a, 1 = +b, 2 = -a, 3 = -b.
    public const int DirPlusA = 0;
    public const int DirPlusB = 1;
    public const int DirMinusA = 2;
    public const int DirMinusB = 3;

    private struct SeamLink
    {
        public byte Face;
        public byte IncomingEdge; // which edge of the neighbour we arrive on
        public bool Flip;         // the along-edge parameter runs backwards
    }

    private static readonly SeamLink[] Seams = new SeamLink[FaceCount * 4];

    static CubeSphere()
    {
        BuildSeamTable();
    }

    // ---------------- The mapping ----------------

    /// <summary>
    /// Tangent-adjusted cube-to-sphere warp. Naive normalisation of a uniform
    /// cube grid leaves cells at a face centre 5.2x the area of cells at an
    /// edge midpoint; the tangent warp cuts that to 1.41x.
    ///
    /// That matters more here than it would for pure rendering, because the
    /// same grid carries the flow field: a 2.28:1 spread in linear vertex
    /// spacing means search distances are wrong by a factor of two depending on
    /// where you stand, and agents visibly bunch and stall near face corners.
    /// </summary>
    public static float Warp(float x) => Mathf.Tan(x * Mathf.Pi * 0.25f);

    public static float WarpInverse(float y) => Mathf.Atan(y) * (4.0f / Mathf.Pi);

    /// <summary>Derivative of Warp, needed to scale tangent vectors correctly.</summary>
    public static float WarpDerivative(float warped) => Mathf.Pi * 0.25f * (1.0f + (warped * warped));

    /// <summary>Unit sphere direction for a point on a face, with a, b in [-1, 1].</summary>
    public static Vector3 Direction(int face, float a, float b)
    {
        Vector3 point = Normal[face] + (AxisA[face] * Warp(a)) + (AxisB[face] * Warp(b));
        return point.Normalized();
    }

    /// <summary>
    /// Inverse mapping: which face a direction belongs to, and where on it.
    /// The direction need not be normalised.
    /// </summary>
    public static bool Locate(Vector3 direction, out int face, out float a, out float b)
    {
        float ax = Mathf.Abs(direction.X);
        float ay = Mathf.Abs(direction.Y);
        float az = Mathf.Abs(direction.Z);

        face = ax >= ay && ax >= az
            ? (direction.X > 0.0f ? 0 : 1)
            : ay >= az
                ? (direction.Y > 0.0f ? 2 : 3)
                : (direction.Z > 0.0f ? 4 : 5);

        float major = direction.Dot(Normal[face]);
        if (major < 1e-9f)
        {
            a = 0.0f;
            b = 0.0f;
            return false;
        }

        a = WarpInverse(direction.Dot(AxisA[face]) / major);
        b = WarpInverse(direction.Dot(AxisB[face]) / major);
        return true;
    }

    /// <summary>
    /// The local frame at a point on a face: the outward normal, and the two
    /// tangent vectors along a and b.
    ///
    /// The tangents are deliberately *not* normalised individually. They are
    /// projections of two orthogonal world axes onto a tilted plane, so away
    /// from the face centre they are not orthogonal to each other; normalising
    /// them separately before blending shears the result. Carrying the raw
    /// projections, including the Warp derivative, keeps the metric correct --
    /// dropping the derivative costs up to 19% relative magnitude error near a
    /// face corner, which reads as agents drifting off-line at seams.
    /// </summary>
    public static void Frame(int face, float a, float b, out Vector3 up, out Vector3 tangentA, out Vector3 tangentB)
    {
        float wa = Warp(a);
        float wb = Warp(b);
        Vector3 cube = Normal[face] + (AxisA[face] * wa) + (AxisB[face] * wb);
        up = cube.Normalized();

        float ka = WarpDerivative(wa);
        float kb = WarpDerivative(wb);

        Vector3 axisA = AxisA[face];
        Vector3 axisB = AxisB[face];
        tangentA = (axisA - (up * up.Dot(axisA))) * ka;
        tangentB = (axisB - (up * up.Dot(axisB))) * kb;
    }

    // ---------------- Seams ----------------

    /// <summary>
    /// Moves one cell on a cell-centred grid of gridSize cells per face edge,
    /// crossing a face seam when it has to.
    ///
    /// outDirection comes back rotated into the neighbour's frame, so
    /// repeatedly stepping in the returned direction walks a straight line
    /// right around the planet.
    /// </summary>
    public static bool Step(int gridSize, int face, int u, int v, int direction,
                            out int outFace, out int outU, out int outV, out int outDirection)
    {
        int du = direction == DirPlusA ? 1 : direction == DirMinusA ? -1 : 0;
        int dv = direction == DirPlusB ? 1 : direction == DirMinusB ? -1 : 0;

        int targetU = u + du;
        int targetV = v + dv;

        if (targetU >= 0 && targetU < gridSize && targetV >= 0 && targetV < gridSize)
        {
            outFace = face;
            outU = targetU;
            outV = targetV;
            outDirection = direction;
            return true;
        }

        SeamLink link = Seams[(face * 4) + direction];

        // The coordinate that runs along the edge we are leaving through.
        int along = direction == DirPlusA || direction == DirMinusA ? v : u;
        if (link.Flip)
        {
            along = gridSize - 1 - along;
        }

        outFace = link.Face;
        switch (link.IncomingEdge)
        {
            case DirPlusA:
                outU = gridSize - 1;
                outV = along;
                break;
            case DirMinusA:
                outU = 0;
                outV = along;
                break;
            case DirPlusB:
                outV = gridSize - 1;
                outU = along;
                break;
            default:
                outV = 0;
                outU = along;
                break;
        }

        // We arrived on that edge, so we are travelling away from it.
        outDirection = (link.IncomingEdge + 2) & 3;
        return true;
    }

    /// <summary>
    /// Two orthogonal steps, for the eight-connected neighbourhood the flow
    /// field wants.
    ///
    /// At the eight cube corners only three faces meet, so one of the four
    /// diagonals does not exist and the two orderings disagree. This reports
    /// false there and callers skip that neighbour: eight cells out of tens of
    /// thousands lose one contribution. Building machinery for that case would
    /// cost more than it buys.
    /// </summary>
    public static bool StepDiagonal(int gridSize, int face, int u, int v, int firstDirection, int secondDirection,
                                    out int outFace, out int outU, out int outV)
    {
        outFace = face;
        outU = u;
        outV = v;

        if (!Step(gridSize, face, u, v, firstDirection, out int midFace, out int midU, out int midV, out int midDirection))
        {
            return false;
        }

        // Rotate the second direction by however much the first step turned.
        int turn = (midDirection - firstDirection) & 3;
        int rotatedSecond = (secondDirection + turn) & 3;

        if (!Step(gridSize, midFace, midU, midV, rotatedSecond, out outFace, out outU, out outV, out _))
        {
            return false;
        }

        // A cube corner is exactly the case where the other ordering disagrees.
        if (!Step(gridSize, face, u, v, secondDirection, out int altFace, out int altU, out int altV, out int altDirection))
        {
            return false;
        }

        int altTurn = (altDirection - secondDirection) & 3;
        int rotatedFirst = (firstDirection + altTurn) & 3;
        if (!Step(gridSize, altFace, altU, altV, rotatedFirst, out int checkFace, out int checkU, out int checkV, out _))
        {
            return false;
        }

        return checkFace == outFace && checkU == outU && checkV == outV;
    }

    private static void BuildSeamTable()
    {
        for (int face = 0; face < FaceCount; face++)
        {
            for (int direction = 0; direction < 4; direction++)
            {
                bool alongA = direction == DirPlusA || direction == DirMinusA;
                float sign = direction == DirPlusA || direction == DirPlusB ? 1.0f : -1.0f;

                Vector3 pinned = alongA ? AxisA[face] : AxisB[face];
                Vector3 running = alongA ? AxisB[face] : AxisA[face];

                // Leaving across the edge where the pinned coordinate is +-1
                // means stepping onto the face whose outward normal is that
                // signed axis. The six normals are the six signed basis
                // vectors, so this is unique.
                int neighbour = FaceOfAxis(pinned * sign);

                // On the neighbour, the axis parallel to our own normal is the
                // one that is now pinned, and its extreme says which edge we
                // arrived on.
                bool neighbourPinnedIsA = Mathf.Abs(AxisA[neighbour].Dot(Normal[face])) > 0.5f;
                Vector3 neighbourPinned = neighbourPinnedIsA ? AxisA[neighbour] : AxisB[neighbour];
                Vector3 neighbourRunning = neighbourPinnedIsA ? AxisB[neighbour] : AxisA[neighbour];

                bool atPositiveExtreme = neighbourPinned.Dot(Normal[face]) > 0.0f;
                int incomingEdge = neighbourPinnedIsA
                    ? (atPositiveExtreme ? DirPlusA : DirMinusA)
                    : (atPositiveExtreme ? DirPlusB : DirMinusB);

                Seams[(face * 4) + direction] = new SeamLink
                {
                    Face = (byte)neighbour,
                    IncomingEdge = (byte)incomingEdge,
                    Flip = neighbourRunning.Dot(running) < 0.0f,
                };
            }
        }
    }

    private static int FaceOfAxis(Vector3 axis)
    {
        for (int face = 0; face < FaceCount; face++)
        {
            if (Normal[face].Dot(axis) > 0.5f)
            {
                return face;
            }
        }
        return 0;
    }

    /// <summary>
    /// Proves the derived seam table against geometry: every boundary step must
    /// land on a physically adjacent cell, and stepping back must return to
    /// where it started.
    ///
    /// This is the highest-value check in the planet code. A wrong table does
    /// not announce itself -- it shows up much later as agents stalling at a
    /// seam, by which point three subsystems are suspects.
    /// </summary>
    public static bool SelfTest(int gridSize, out string failure)
    {
        float cellArc = Mathf.Pi * 0.5f / gridSize;
        float tolerance = cellArc * 1.75f;

        for (int face = 0; face < FaceCount; face++)
        {
            for (int direction = 0; direction < 4; direction++)
            {
                for (int along = 0; along < gridSize; along++)
                {
                    int u = direction == DirPlusA ? gridSize - 1 : direction == DirMinusA ? 0 : along;
                    int v = direction == DirPlusB ? gridSize - 1 : direction == DirMinusB ? 0 : along;

                    Step(gridSize, face, u, v, direction, out int nFace, out int nU, out int nV, out int nDirection);

                    Vector3 from = CellDirection(gridSize, face, u, v);
                    Vector3 to = CellDirection(gridSize, nFace, nU, nV);
                    float angle = from.AngleTo(to);
                    if (angle > tolerance)
                    {
                        failure = $"Seam jump at face {face} dir {direction} along {along}: " +
                                  $"{angle:F4} rad exceeds {tolerance:F4}.";
                        return false;
                    }

                    // Stepping back the way we came must return the origin.
                    int back = (nDirection + 2) & 3;
                    Step(gridSize, nFace, nU, nV, back, out int bFace, out int bU, out int bV, out _);
                    if (bFace != face || bU != u || bV != v)
                    {
                        failure = $"Seam not involutive at face {face} dir {direction} along {along}: " +
                                  $"returned ({bFace},{bU},{bV}) not ({face},{u},{v}).";
                        return false;
                    }
                }
            }
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>Unit direction of a cell centre on a cell-centred grid.</summary>
    public static Vector3 CellDirection(int gridSize, int face, int u, int v)
    {
        float a = ((u + 0.5f) / gridSize * 2.0f) - 1.0f;
        float b = ((v + 0.5f) / gridSize * 2.0f) - 1.0f;
        return Direction(face, a, b);
    }
}
