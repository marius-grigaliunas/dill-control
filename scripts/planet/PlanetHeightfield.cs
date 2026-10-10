using System;
using System.Collections.Generic;
using Godot;
using DillControl.Core;

namespace DillControl.Planet;

/// <summary>
/// A cube-sphere heightfield: one height per vertex of six square faces, and
/// every question asked of the ground's shape. Pure data -- no nodes -- so it
/// can be generated on workers behind a loading screen and read from the
/// horde's parallel passes.
///
/// Heights live in one shared array indexed by (face, u, v), and the seam rows
/// of adjacent faces are separate entries for the same point. They cannot drift
/// apart: heights come from noise sampled on the 3D direction and craters are
/// applied by 3D distance, both of which give the two copies of a shared point
/// identical answers by construction.
///
/// Ported from the lab's <c>PlanetTerrain</c>, which was this and the chunk
/// node in one class. Split so the shape exists before anything is drawn.
/// The planet is centred on the world origin (<c>docs/decisions/0018</c>):
/// every world point here is also a direction from the centre.
/// </summary>
public sealed class PlanetHeightfield
{
    /// <summary>How much of a vertex's height above a blast counts toward its distance from it.</summary>
    public const float PitWallReach = 0.4f;

    /// <summary>Metres of extra crater radius per metre a pit is already dug, under the blast.</summary>
    public const float PitWidening = 0.9f;

    /// <summary>Once a crater reaches the floor, every vertex under it dug at least this share of the way floods.</summary>
    public const float MagmaFloodShare = 0.35f;

    private readonly float[] _height;

    /// <summary>
    /// The heights as generated, before any crater: what the dig floor is
    /// measured against. Pads are part of the generated ground.
    /// </summary>
    private readonly float[] _baseHeight;

    private readonly List<(int Index, int Face, int U, int V, Vector3 Direction)> _floodCandidates = new();

    public PlanetHeightfield(float radius, float amplitude, int faceVertexEdge, float digFloorDepth)
    {
        Radius = radius;
        Amplitude = amplitude;
        FaceVertexEdge = Mathf.Max(3, faceVertexEdge);
        DigFloorDepth = digFloorDepth;
        _height = new float[CubeSphere.FaceCount * FaceVertexEdge * FaceVertexEdge];
        _baseHeight = new float[_height.Length];
    }

    public float Radius { get; }

    public float Amplitude { get; }

    /// <summary>Vertices along one face edge, sharing a row with each neighbour.</summary>
    public int FaceVertexEdge { get; }

    /// <summary>How far below the generated ground a crater can dig. Zero or less is no floor.</summary>
    public float DigFloorDepth { get; private set; }

    /// <summary>Metres between neighbouring vertices, near enough everywhere.</summary>
    public float VertexSpacing => Mathf.Pi * 0.5f * Radius / Mathf.Max(FaceVertexEdge - 1, 1);

    /// <summary>The radius the last crater actually dug, after widening in a pit.</summary>
    public float LastCraterRadius { get; private set; }

    /// <summary>Vertices the last crater pressed against the floor.</summary>
    public int LastCraterFloorHits { get; private set; }

    /// <summary>Mean position of those vertices. Meaningful only when there were some.</summary>
    public Vector3 LastCraterFloorPoint { get; private set; }

    public int VertexIndex(int face, int u, int v) => (((face * FaceVertexEdge) + v) * FaceVertexEdge) + u;

    public float HeightAt(int index) => _height[index];

    /// <summary>Metres a vertex has been dug below its generated height.</summary>
    public float DugAt(int index) => _baseHeight[index] - _height[index];

    // ---------------- Generation ----------------

    /// <summary>
    /// Fills every height from noise on the 3D direction, row by row on the
    /// job threads. The lab measured this at 529 ms serial and about 200 ms
    /// threaded for a 1200 m planet; it all has to exist before the first
    /// chunk, so it is the part a loading screen covers.
    ///
    /// Safe without a lock: the noise's permutation table is only read, and
    /// each row writes only its own slice.
    /// </summary>
    public void Generate(PlanetSpec spec)
    {
        int edge = FaceVertexEdge;
        PlanetNoise noise = new(spec.Seed);
        float inverse = 2.0f / (edge - 1);
        float[] height = _height;
        float frequency = spec.NoiseFrequency;
        int octaves = Mathf.Max(1, spec.Octaves);
        float lacunarity = spec.Lacunarity;
        float gain = spec.Gain;
        float ridge = Mathf.Clamp(spec.RidgeShare, 0.0f, 1.0f);
        float amplitude = Amplitude;

        Jobs.For(CubeSphere.FaceCount * edge, row =>
        {
            int f = row / edge;
            int v = row - (f * edge);
            float b = (v * inverse) - 1.0f;
            int rowBase = (f * edge * edge) + (v * edge);

            for (int u = 0; u < edge; u++)
            {
                float a = (u * inverse) - 1.0f;
                Vector3 direction = CubeSphere.Direction(f, a, b);
                float rolling = ridge < 1.0f ? noise.Fractal(direction, frequency, octaves, lacunarity, gain) : 0.0f;
                float crest = ridge > 0.0f ? noise.Ridged(direction, frequency, octaves, lacunarity, gain) : 0.0f;
                height[rowBase + u] = Mathf.Lerp(rolling, crest, ridge) * amplitude;
            }
        });

        BakeCraters(spec);
        Array.Copy(_height, _baseHeight, _height.Length);
    }

    /// <summary>
    /// Old impacts, placed from the planet's seed. Small craters are common
    /// and big ones rare (radius drawn toward the low end), as on any
    /// cratered surface. Dug before the base heights are taken, so they are
    /// ground the dig floor measures from, not damage.
    /// </summary>
    private void BakeCraters(PlanetSpec spec)
    {
        if (spec.CraterCount <= 0)
        {
            return;
        }

        SeededRandom random = SeededRandom.Stream((ulong)spec.Seed, "craters");
        float minRadius = Mathf.Min(spec.CraterRadiusMin, spec.CraterRadiusMax);
        float maxRadius = Mathf.Max(spec.CraterRadiusMin, spec.CraterRadiusMax);
        float floorDepth = DigFloorDepth;
        DigFloorDepth = 0.0f;
        for (int i = 0; i < spec.CraterCount; i++)
        {
            Vector3 direction = random.OnSphere();
            float t = random.NextFloat();
            float radius = Mathf.Lerp(minRadius, maxRadius, t * t * t);
            Vector3 centre = SurfacePoint(direction);
            ApplyCrater(centre, radius, radius * spec.CraterDepthRatio, radius * 0.05f, 4.0f, (_, _, _) => { });
        }
        DigFloorDepth = floorDepth;
    }

    /// <summary>
    /// Levels a disc of ground into the plane tangent at its centre, blending
    /// back into the terrain over <paramref name="blend"/> metres: a pad for
    /// something authored to stand on. Part of generation, so it moves the
    /// base heights too and a crater on a pad digs from the pad. Run before
    /// any chunk is built; a built planet would need its chunks queued.
    ///
    /// A plane, not a level ring round the planet. A cluster arranged in the
    /// editor stands on flat ground; on a 1200 m planet a spherical pad falls
    /// away 8 m by 140 m out, and everything at its edge would float.
    /// </summary>
    /// <returns>The pad's height above the mean radius.</returns>
    public float FlattenPad(Vector3 direction, float radius, float blend)
    {
        Vector3 centre = direction.Normalized();
        float level = SampleHeight(centre);
        float outer = Mathf.Min(radius + Mathf.Max(blend, 0.0f), Radius * 0.5f);
        float minDot = Mathf.Cos(outer / Radius);

        ForEachVertexNear(centre, outer, (index, vertexDirection) =>
        {
            float cosine = vertexDirection.Dot(centre);
            if (cosine < minDot)
            {
                return;
            }
            float arc = Mathf.Acos(Mathf.Clamp(cosine, -1.0f, 1.0f)) * Radius;
            float t = blend > 0.0f ? Mathf.Clamp((arc - radius) / blend, 0.0f, 1.0f) : (arc <= radius ? 0.0f : 1.0f);
            t = t * t * (3.0f - (2.0f * t));
            float plane = ((Radius + level) / Mathf.Max(cosine, 0.1f)) - Radius;
            float next = Mathf.Lerp(plane, _height[index], t);
            _height[index] = next;
            _baseHeight[index] = next;
        });

        return level;
    }

    /// <summary>
    /// Calls <paramref name="visit"/> with the index and direction of every
    /// vertex that could be within <paramref name="reach"/> surface metres of
    /// a direction, on every face. Conservative: callers test distance
    /// themselves.
    /// </summary>
    private void ForEachVertexNear(Vector3 centreDirection, float reach, Action<int, Vector3> visit)
    {
        int edge = FaceVertexEdge;
        float inverse = 2.0f / (edge - 1);

        // da/dangle peaks at 4/pi at a face centre and is smaller everywhere
        // else, so this over-covers rather than clipping.
        float angular = Mathf.Min(reach / Radius, Mathf.Pi * 0.5f);
        float halfExtent = (angular * 4.0f / Mathf.Pi * 1.1f) + (inverse * 2.0f);

        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            // A face's own directions are all within 54.7 degrees of its
            // normal, so a disc whose centre is further than that plus its
            // reach cannot touch it. The projection below needs a positive
            // major component, which that also guarantees.
            float major = centreDirection.Dot(CubeSphere.Normal[face]);
            if (major < Mathf.Cos(Mathf.Min(0.9553f + angular, 1.5f)))
            {
                continue;
            }

            float ca = CubeSphere.WarpInverse(centreDirection.Dot(CubeSphere.AxisA[face]) / major);
            float cb = CubeSphere.WarpInverse(centreDirection.Dot(CubeSphere.AxisB[face]) / major);

            int u0 = Mathf.Max(0, (int)Mathf.Floor((ca - halfExtent + 1.0f) * 0.5f * (edge - 1)));
            int u1 = Mathf.Min(edge - 1, (int)Mathf.Ceil((ca + halfExtent + 1.0f) * 0.5f * (edge - 1)));
            int v0 = Mathf.Max(0, (int)Mathf.Floor((cb - halfExtent + 1.0f) * 0.5f * (edge - 1)));
            int v1 = Mathf.Min(edge - 1, (int)Mathf.Ceil((cb + halfExtent + 1.0f) * 0.5f * (edge - 1)));

            int faceBase = face * edge * edge;
            for (int v = v0; v <= v1; v++)
            {
                float b = (v * inverse) - 1.0f;
                for (int u = u0; u <= u1; u++)
                {
                    float a = (u * inverse) - 1.0f;
                    visit(faceBase + (v * edge) + u, CubeSphere.Direction(face, a, b));
                }
            }
        }
    }

    // ---------------- Sampling ----------------

    /// <summary>
    /// Height above the mean radius for a direction from the centre. Bilinear,
    /// clamped at face edges -- continuous across a seam because both faces
    /// store the shared row.
    /// </summary>
    public float SampleHeight(Vector3 direction)
    {
        if (!CubeSphere.Locate(direction, out int face, out float a, out float b))
        {
            return 0.0f;
        }
        return SampleHeight(face, a, b);
    }

    /// <summary>The same sample, for a caller that has already located its direction on the cube.</summary>
    public float SampleHeight(int face, float a, float b) => SampleArray(_height, face, a, b);

    private float SampleBaseHeight(int face, float a, float b) => SampleArray(_baseHeight, face, a, b);

    private float SampleArray(float[] heights, int face, float a, float b)
    {
        int edge = FaceVertexEdge;
        float fu = Mathf.Clamp((a + 1.0f) * 0.5f * (edge - 1), 0.0f, edge - 1.0001f);
        float fv = Mathf.Clamp((b + 1.0f) * 0.5f * (edge - 1), 0.0f, edge - 1.0001f);

        int u0 = (int)fu;
        int v0 = (int)fv;
        int u1 = Mathf.Min(u0 + 1, edge - 1);
        int v1 = Mathf.Min(v0 + 1, edge - 1);
        float tu = fu - u0;
        float tv = fv - v0;

        int faceBase = face * edge * edge;
        float h00 = heights[faceBase + (v0 * edge) + u0];
        float h10 = heights[faceBase + (v0 * edge) + u1];
        float h01 = heights[faceBase + (v1 * edge) + u0];
        float h11 = heights[faceBase + (v1 * edge) + u1];

        return Mathf.Lerp(Mathf.Lerp(h00, h10, tu), Mathf.Lerp(h01, h11, tu), tv);
    }

    /// <summary>World position of the ground below a direction.</summary>
    public Vector3 SurfacePoint(Vector3 direction)
    {
        Vector3 unit = direction.Normalized();
        return unit * (Radius + SampleHeight(unit));
    }

    /// <summary>
    /// Where and how something stands on the ground at a point: upright along
    /// the local radial, facing <paramref name="facing"/> flattened into the
    /// tangent plane, sunk <paramref name="sink"/> metres so its base is in the
    /// ground rather than over it.
    /// </summary>
    public Transform3D SurfaceTransform(Vector3 worldPoint, Vector3 facing, float sink)
    {
        Vector3 localUp = worldPoint.LengthSquared() > 1.0f ? worldPoint.Normalized() : Vector3.Up;
        Vector3 surface = SurfacePoint(localUp);
        return new Transform3D(UprightBasis(localUp, facing), surface - (localUp * sink));
    }

    /// <summary>A basis whose Y is <paramref name="up"/> and whose -Z is <paramref name="facing"/> in the tangent plane.</summary>
    public static Basis UprightBasis(Vector3 up, Vector3 facing)
    {
        Vector3 forward = facing.Slide(up);
        if (forward.LengthSquared() < 1e-6f)
        {
            forward = up.Cross(Vector3.Right);
        }
        if (forward.LengthSquared() < 1e-6f)
        {
            forward = up.Cross(Vector3.Forward);
        }
        forward = forward.Normalized();
        return new Basis(forward.Cross(up), up, -forward);
    }

    /// <summary>Outward ground normal at a direction, from the surface either side of it.</summary>
    public Vector3 NormalAt(Vector3 direction, float sampleArc)
    {
        Vector3 unit = direction.Normalized();
        CubeSphere.Locate(unit, out int face, out float a, out float b);
        CubeSphere.Frame(face, a, b, out Vector3 up, out Vector3 tangentA, out Vector3 tangentB);

        Vector3 ta = tangentA.Normalized() * (sampleArc / Radius);
        Vector3 tb = tangentB.Normalized() * (sampleArc / Radius);

        Vector3 alongA = SurfacePoint(unit + ta) - SurfacePoint(unit - ta);
        Vector3 alongB = SurfacePoint(unit + tb) - SurfacePoint(unit - tb);

        Vector3 normal = alongA.Cross(alongB).Normalized();
        return normal.Dot(up) < 0.0f ? -normal : normal;
    }

    /// <summary>Steepness in radians between the ground normal and straight up.</summary>
    public float SlopeAt(Vector3 direction, float sampleArc)
    {
        Vector3 unit = direction.Normalized();
        return Mathf.Acos(Mathf.Clamp(NormalAt(unit, sampleArc).Dot(unit), -1.0f, 1.0f));
    }

    /// <summary>
    /// Marches a ray against the heightfield. Not a physics ray: colliders
    /// only exist in a ring round the viewer, and this works anywhere on the
    /// planet -- a strike designated across the horizon, a crater far away.
    /// </summary>
    public bool RaycastSurface(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 hit)
    {
        hit = Vector3.Zero;
        Vector3 ray = direction.Normalized();

        // A third of the vertex spacing: a coarser step walks over a ridge.
        float step = Mathf.Max(0.5f, VertexSpacing * 0.33f);

        float travelled = 0.0f;
        float previous = travelled;
        if (!Above(origin))
        {
            return false;
        }

        while (travelled < maxDistance)
        {
            travelled = Mathf.Min(travelled + step, maxDistance);
            Vector3 point = origin + (ray * travelled);

            if (!Above(point))
            {
                float low = previous;
                float high = travelled;
                for (int i = 0; i < 10; i++)
                {
                    float mid = (low + high) * 0.5f;
                    if (Above(origin + (ray * mid)))
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid;
                    }
                }

                hit = origin + (ray * high);
                return true;
            }

            previous = travelled;
        }

        return false;
    }

    /// <summary>True when a point is above the ground.</summary>
    public bool Above(Vector3 point)
    {
        float distance = point.Length();
        if (distance < 0.001f)
        {
            return false;
        }
        return distance > Radius + SampleHeight(point / distance);
    }

    /// <summary>Height of a point above the ground under it; negative underground.</summary>
    public float AltitudeOf(Vector3 point)
    {
        float distance = point.Length();
        return distance < 0.001f ? -Radius : distance - (Radius + SampleHeight(point / distance));
    }

    // ---------------- Deformation ----------------

    /// <summary>
    /// Pushes a bowl into the heightfield and reports every vertex it moved
    /// through <paramref name="moved"/>, so the owner can queue the chunks
    /// those vertices belong to.
    ///
    /// Vertices are found by 3D distance from the blast, not by a rectangle in
    /// face coordinates: distance handles chunk boundaries, seams and cube
    /// corners with one test, and gives both copies of a seam vertex the same
    /// delta.
    /// </summary>
    public void ApplyCrater(Vector3 worldPoint, float radius, float depth, float rimHeight, float rimSharpness,
                            Action<int, int, int> moved)
    {
        Vector3 centreDirection = worldPoint.Normalized();
        float centreRadius = worldPoint.Length();

        // A crater in a pit is wider by however deep the pit already is, so a
        // pit opens into a bowl rather than a slot.
        if (DigFloorDepth > 0.0f && CubeSphere.Locate(centreDirection, out int cf, out float cA, out float cB))
        {
            float dug = SampleBaseHeight(cf, cA, cB) - SampleHeight(cf, cA, cB);
            radius += Mathf.Max(0.0f, dug) * PitWidening;
        }

        int edge = FaceVertexEdge;
        float inverse = 2.0f / (edge - 1);
        float radiusSquared = radius * radius;

        float angular = Mathf.Asin(Mathf.Clamp(radius / Radius, 0.0f, 1.0f));
        float halfExtent = (angular * 4.0f / Mathf.Pi * 1.1f) + (inverse * 2.0f);

        int floorHits = 0;
        Vector3 floorSum = Vector3.Zero;
        _floodCandidates.Clear();

        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            float major = centreDirection.Dot(CubeSphere.Normal[face]);
            if (major <= 0.05f)
            {
                continue;
            }

            float ca = CubeSphere.WarpInverse(centreDirection.Dot(CubeSphere.AxisA[face]) / major);
            float cb = CubeSphere.WarpInverse(centreDirection.Dot(CubeSphere.AxisB[face]) / major);

            int u0 = Mathf.Max(0, (int)Mathf.Floor((ca - halfExtent + 1.0f) * 0.5f * (edge - 1)));
            int u1 = Mathf.Min(edge - 1, (int)Mathf.Ceil((ca + halfExtent + 1.0f) * 0.5f * (edge - 1)));
            int v0 = Mathf.Max(0, (int)Mathf.Floor((cb - halfExtent + 1.0f) * 0.5f * (edge - 1)));
            int v1 = Mathf.Min(edge - 1, (int)Mathf.Ceil((cb + halfExtent + 1.0f) * 0.5f * (edge - 1)));

            if (u0 > u1 || v0 > v1)
            {
                continue;
            }

            int faceBase = face * edge * edge;

            for (int v = v0; v <= v1; v++)
            {
                float b = (v * inverse) - 1.0f;
                for (int u = u0; u <= u1; u++)
                {
                    float a = (u * inverse) - 1.0f;
                    int index = faceBase + (v * edge) + u;

                    Vector3 direction = CubeSphere.Direction(face, a, b);
                    Vector3 point = direction * (Radius + _height[index]);
                    float distanceSquared = point.DistanceSquaredTo(worldPoint);

                    // Ground above the blast is measured mostly sideways, so a
                    // blast at the bottom of a pit digs its walls too. Ground
                    // below keeps the full 3D distance, so an air burst still
                    // leaves it alone.
                    float above = (Radius + _height[index]) - centreRadius;
                    if (DigFloorDepth > 0.0f && distanceSquared - (above * above) < radiusSquared)
                    {
                        _floodCandidates.Add((index, face, u, v, direction));
                    }

                    if (above > 0.0f)
                    {
                        distanceSquared += above * above * ((PitWallReach * PitWallReach) - 1.0f);
                    }

                    if (distanceSquared > radiusSquared)
                    {
                        continue;
                    }

                    float t = Mathf.Sqrt(distanceSquared) / radius;

                    // Quartic bowl, so the profile meets untouched ground with
                    // a matching tangent and leaves no ring.
                    float bowl = 1.0f - (t * t);
                    bowl *= bowl;
                    float lip = t * t * t * t * (1.0f - t) * rimSharpness;

                    float next = _height[index] + (rimHeight * lip) - (depth * bowl);
                    if (DigFloorDepth > 0.0f)
                    {
                        float floor = _baseHeight[index] - DigFloorDepth;
                        if (next <= floor + 0.05f)
                        {
                            next = Mathf.Max(next, floor);
                            floorHits++;
                            floorSum += direction * (Radius + next);
                        }
                    }

                    _height[index] = next;
                    moved(face, u, v);
                }
            }
        }

        // Broke through: the deep part of the pit floods to the floor, a lake
        // rather than one vertex of magma at the bottom of a shaft.
        if (floorHits > 0)
        {
            floorHits = 0;
            floorSum = Vector3.Zero;
            float flood = DigFloorDepth * MagmaFloodShare;
            foreach ((int index, int face, int u, int v, Vector3 direction) in _floodCandidates)
            {
                float floor = _baseHeight[index] - DigFloorDepth;
                if (_baseHeight[index] - _height[index] < flood)
                {
                    continue;
                }

                _height[index] = floor;
                floorHits++;
                floorSum += direction * (Radius + floor);
                moved(face, u, v);
            }
        }

        LastCraterRadius = radius;
        LastCraterFloorHits = floorHits;
        LastCraterFloorPoint = floorHits > 0 ? floorSum / floorHits : Vector3.Zero;
    }

    /// <summary>
    /// A hash of every height, for checking that one seed builds the same
    /// planet twice. FNV-1a over the raw bits.
    /// </summary>
    public ulong Fingerprint()
    {
        ulong hash = 14695981039346656037UL;
        foreach (float h in _height)
        {
            hash ^= (uint)BitConverter.SingleToInt32Bits(h);
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
