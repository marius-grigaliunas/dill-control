using System;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// A plant's spine: a stack of stiff segments joined by bending springs, from
/// the foot (joint 0, which never moves) to the tip. Each joint tilts the
/// segments above it a little in two horizontal directions; summed, the tilts
/// give how far every point of the spine has moved from where the model was
/// built. The mesh is bent by that in the vertex shader (voxel_toon), so the
/// plant leans and sways while its voxels, its collision and its damage stay
/// those of a <see cref="Voxel.VoxelBody"/> (docs/decisions/0032).
///
/// DECISION -- joint springs, not points held together by distance. A chain
/// of points under distance constraints is a rope; making it stiff takes many
/// solver passes. Bending springs are stiff by construction, the static
/// answer is exact (each joint's moment is what hangs above it), and the
/// stiffness can come straight from the voxels: a thick foot, a whippy tip.
///
/// Pure arithmetic over arrays, no nodes: <see cref="FlexField"/> steps every
/// rod once a physics tick.
/// </summary>
public sealed class Rod
{
    public const int MaxSegments = 16;

    /// <summary>Segments; there are <see cref="Segments"/> + 1 points, foot to tip.</summary>
    public readonly int Segments;

    /// <summary>Length of one segment along the plant's up, in metres.</summary>
    public readonly float SegmentLength;

    // Per segment s (from point s to s + 1): what it weighs.
    private readonly float[] _mass;

    // Per joint j (at point j, bending everything above it).
    private readonly float[] _stiffness;
    private readonly float[] _damping;
    private readonly float[] _inertia;
    private readonly Vector2[] _tilt;
    private readonly Vector2[] _rate;

    // Per segment, this tick: horizontal force on it (wind), and the point
    // forces and impulses gathered since the last step.
    private readonly Vector2[] _force;
    private readonly Vector2[] _pointForce;
    private readonly float[] _pointLever;
    private int _points;
    private readonly Vector2[] _kicks;
    private readonly float[] _kickLever;
    private int _kickCount;

    // Scratch: what hangs above each joint, this substep.
    private readonly Vector2[] _aboveForce;
    private readonly Vector2[] _aboveForceMoment;
    private readonly Vector2[] _aboveWeight;
    private readonly float[] _aboveMass;

    // Per point: how far it has moved from rest, sideways and down.
    private readonly Vector2[] _offset;
    private readonly float[] _drop;

    private float _gravity = 9.8f;
    private float _maxLean = 0.4f;
    private float _stiffnessScale = 1.0f;
    private int _substeps = 1;

    // The shape as built, for <see cref="SetProfile"/>: per joint, how much
    // cross-section there was.
    private float[] _builtSection;
    private float _taper = 1.0f;
    private float _weakening = 2.0f;

    public Rod(int segments, float segmentLength)
    {
        Segments = Math.Clamp(segments, 1, MaxSegments);
        SegmentLength = Mathf.Max(0.05f, segmentLength);
        _mass = new float[Segments];
        _stiffness = new float[Segments];
        _damping = new float[Segments];
        _inertia = new float[Segments];
        _tilt = new Vector2[Segments];
        _rate = new Vector2[Segments];
        _force = new Vector2[Segments];
        _pointForce = new Vector2[8];
        _pointLever = new float[8];
        _kicks = new Vector2[8];
        _kickLever = new float[8];
        _aboveForce = new Vector2[Segments];
        _aboveForceMoment = new Vector2[Segments];
        _aboveWeight = new Vector2[Segments];
        _aboveMass = new float[Segments];
        _offset = new Vector2[Segments + 1];
        _drop = new float[Segments + 1];
    }

    public float Height => Segments * SegmentLength;

    public float TotalMass
    {
        get
        {
            float total = 0.0f;
            foreach (float m in _mass)
            {
                total += m;
            }
            return total;
        }
    }

    /// <summary>
    /// The plant as built. <paramref name="mass"/> is per segment, in kg;
    /// <paramref name="section"/> is per joint, how much material crosses
    /// it (voxels in the thinnest layer near it). The joints are made stiff in
    /// proportion to section ^ <paramref name="taper"/>, then all scaled
    /// together so the plant's first sway takes <paramref name="period"/>
    /// seconds; <paramref name="damping"/> is the share of critical.
    /// </summary>
    public void Build(float[] mass, float[] section, float period, float damping, float taper, float weakening,
        float maxLeanRadians, float gravity)
    {
        _taper = taper;
        _weakening = weakening;
        _maxLean = maxLeanRadians;
        _gravity = gravity;
        _builtSection = new float[Segments];
        float foot = Mathf.Max(1.0f, section[0]);
        for (int j = 0; j < Segments; j++)
        {
            _mass[j] = Mathf.Max(0.0f, mass[j]);
            _builtSection[j] = Mathf.Max(section[j], 0.0f);
            _stiffness[j] = Mathf.Pow(Mathf.Max(_builtSection[j], 0.5f) / foot, taper);
        }
        UpdateInertia();

        // The joints' relative stiffness is the plant's shape; its overall
        // stiffness is chosen so the shape sways at the period asked for
        // (Rayleigh's quotient on the shape it bends to under its own weight
        // pushed sideways).
        float omega = Mathf.Tau / Mathf.Max(0.2f, period);
        period = Mathf.Tau / omega;
        long key = Key(period, damping, taper);
        bool known = Calibrated.TryGetValue(key, out float scale);
        if (!known)
        {
            float trial = RayleighOmegaSquared();
            scale = trial > 1e-9f ? omega * omega / trial : 1.0f;
        }
        Scale(scale);
        SetDamping(damping);
        if (known)
        {
            return;
        }

        // Each joint is moved as if what is above it were rigid, which sways
        // faster than the whole chain really does. Timed by swaying it, and
        // scaled until it keeps the period asked for. A nest uses a few baked
        // vines many times over, so each shape is timed once.
        for (int pass = 0; pass < 4; pass++)
        {
            float measured = MeasurePeriod(period);
            if (measured <= 0.0f)
            {
                break;
            }
            float ratio = measured / period;
            Scale(ratio * ratio);
            SetDamping(damping);
            if (Mathf.Abs(ratio - 1.0f) < 0.02f)
            {
                break;
            }
        }
        Calibrated[key] = _stiffnessScale;
    }

    private static readonly System.Collections.Generic.Dictionary<long, float> Calibrated = new();

    private long Key(float period, float damping, float taper)
    {
        var hash = new System.HashCode();
        hash.Add(Segments);
        hash.Add(SegmentLength);
        hash.Add(period);
        hash.Add(damping);
        hash.Add(taper);
        hash.Add(_gravity);
        for (int j = 0; j < Segments; j++)
        {
            hash.Add(_mass[j]);
            hash.Add(_builtSection[j]);
        }
        return ((long)hash.ToHashCode() << 8) ^ Segments;
    }

    private void Scale(float by)
    {
        _stiffnessScale *= by;
        for (int j = 0; j < Segments; j++)
        {
            _stiffness[j] *= by;
        }
    }

    /// <summary>Seconds between the first two times its middle swings back through upright, after a nudge.</summary>
    private float MeasurePeriod(float expected)
    {
        Rest();
        float dt = 1.0f / 60.0f;
        // A nudge small enough that its own weight hardly changes the sway.
        AddImpulse(Height * 0.6f, new Vector2(_inertia[0] * 0.002f / Mathf.Max(Height * 0.6f, 0.1f), 0.0f));
        float previous = 0.0f, first = -1.0f;
        int limit = (int)(expected * 6.0f / dt);
        for (int i = 0; i < limit; i++)
        {
            Step(dt);
            float x = 0.0f;
            for (int s = 0; s < Segments; s++)
            {
                x += _mass[s] * (_offset[s].X + _offset[s + 1].X);
            }
            if (i > 0 && previous < 0.0f && x >= 0.0f)
            {
                float at = (i - 1 + (previous / (previous - x))) * dt;
                if (first >= 0.0f)
                {
                    Rest();
                    return at - first;
                }
                first = at;
            }
            previous = x;
        }
        Rest();
        return -1.0f;
    }

    private void Rest()
    {
        System.Array.Clear(_tilt);
        System.Array.Clear(_rate);
        System.Array.Clear(_force);
        _points = 0;
        _kickCount = 0;
        Shape();
    }

    /// <summary>
    /// After damage: what is left of each segment's mass and each joint's
    /// section. A joint cut half through bends far more easily (section ^
    /// weakening against what it was built with), and what was cut free
    /// weighs nothing.
    /// </summary>
    public void SetProfile(float[] mass, float[] section, float damping)
    {
        if (_builtSection == null)
        {
            return;
        }
        float foot = Mathf.Max(1.0f, _builtSection[0]);
        for (int j = 0; j < Segments; j++)
        {
            _mass[j] = Mathf.Max(0.0f, mass[j]);
            float built = Mathf.Max(_builtSection[j], 0.5f);
            float left = Mathf.Clamp(section[j] / built, 0.0f, 1.0f);
            _stiffness[j] = _stiffnessScale * Mathf.Pow(built / foot, _taper) * Mathf.Pow(Mathf.Max(left, 0.02f), _weakening);
        }
        UpdateInertia();
        SetDamping(damping);
    }

    private void UpdateInertia()
    {
        float L = SegmentLength;
        for (int j = 0; j < Segments; j++)
        {
            float inertia = 0.0f;
            for (int s = j; s < Segments; s++)
            {
                float lever = (s - j + 0.5f) * L;
                inertia += _mass[s] * ((lever * lever) + (L * L / 12.0f));
            }
            _inertia[j] = inertia;
        }
    }

    private void SetDamping(float damping)
    {
        float fastest = 0.0f;
        for (int j = 0; j < Segments; j++)
        {
            float inertia = Mathf.Max(_inertia[j], 1e-3f);
            _damping[j] = 2.0f * damping * Mathf.Sqrt(_stiffness[j] * inertia);
            fastest = Mathf.Max(fastest, Mathf.Sqrt(_stiffness[j] / inertia));
        }
        // Semi-implicit steps hold for omega * dt well under 2; keep it under
        // 0.5 at 60 ticks a second, and never so many that a sliver of a tip
        // left by a cut costs a frame.
        _substeps = Math.Clamp((int)Mathf.Ceil(fastest / 60.0f / 0.5f), 1, 12);
    }

    private float RayleighOmegaSquared()
    {
        // Static bend under each segment's weight acting sideways.
        float L = SegmentLength;
        var tilt = new float[Segments];
        for (int j = 0; j < Segments; j++)
        {
            float moment = 0.0f;
            for (int s = j; s < Segments; s++)
            {
                moment += _mass[s] * _gravity * (s - j + 0.5f) * L;
            }
            tilt[j] = _stiffness[j] > 0.0f ? moment / _stiffness[j] : 0.0f;
        }
        float work = 0.0f, kinetic = 0.0f, cumulative = 0.0f, at = 0.0f;
        for (int s = 0; s < Segments; s++)
        {
            cumulative += tilt[s];
            float mid = at + (cumulative * L * 0.5f);
            at += cumulative * L;
            work += _mass[s] * _gravity * mid;
            kinetic += _mass[s] * mid * mid;
        }
        return kinetic > 1e-12f ? work / kinetic : 0.0f;
    }

    /// <summary>A steady force on segment <paramref name="segment"/> this tick (wind).</summary>
    public void SetSegmentForce(int segment, Vector2 force) => _force[segment] = force;

    /// <summary>A force at <paramref name="height"/> above the foot, for this tick only.</summary>
    public void AddForce(float height, Vector2 force)
    {
        if (_points < _pointForce.Length)
        {
            _pointForce[_points] = force;
            _pointLever[_points] = height;
            _points++;
        }
    }

    /// <summary>An impulse (N s) at <paramref name="height"/>: a hit.</summary>
    public void AddImpulse(float height, Vector2 impulse)
    {
        if (_kickCount < _kicks.Length)
        {
            _kicks[_kickCount] = impulse;
            _kickLever[_kickCount] = height;
            _kickCount++;
        }
    }

    /// <summary>
    /// The lowest joint held at the lean limit in the last step, or -1: past
    /// it, the plant is only propped, not holding itself up.
    /// </summary>
    public int Propped { get; private set; } = -1;

    /// <summary>How far joint <paramref name="joint"/> itself is bent, in radians: a kink there.</summary>
    public float Kink(int joint) => _tilt[joint].Length();

    /// <summary>
    /// At a height above the foot: how far the spine there is tilted from
    /// upright and how fast (radians, radians a second, toward x and z of the
    /// plant's frame), and how fast that point is moving sideways.
    /// </summary>
    public void Motion(float height, out Vector2 tilt, out Vector2 tiltRate, out Vector2 velocity)
    {
        float along = Mathf.Clamp(height / SegmentLength, 0.0f, Segments);
        int at = Math.Min((int)along, Segments - 1);
        Vector2 cumulative = Vector2.Zero, cumulativeRate = Vector2.Zero;
        velocity = Vector2.Zero;
        for (int s = 0; s <= at; s++)
        {
            cumulative += _tilt[s];
            cumulativeRate += _rate[s];
            float share = s < at ? 1.0f : along - at;
            velocity += cumulativeRate * (SegmentLength * share);
        }
        tilt = cumulative;
        tiltRate = cumulativeRate;
    }

    /// <summary>True while it is still moving or being pushed, beyond what a step would show.</summary>
    public bool Restless { get; private set; } = true;

    public void Step(float dt)
    {
        float L = SegmentLength;

        for (int k = 0; k < _kickCount; k++)
        {
            for (int j = 0; j < Segments; j++)
            {
                float lever = _kickLever[k] - (j * L);
                if (lever <= 0.0f || _inertia[j] <= 1e-3f)
                {
                    break;
                }
                _rate[j] += _kicks[k] * (lever / _inertia[j]);
            }
        }
        _kickCount = 0;

        // Substeps were chosen for a 60th of a second; a plant stepped less
        // often (far off) takes as many more.
        int substeps = Math.Max(_substeps, (int)Mathf.Ceil(_substeps * dt * 60.0f - 0.01f));
        float h = dt / substeps;
        float energy = 0.0f;
        Propped = -1;
        for (int step = 0; step < substeps; step++)
        {
            Shape();
            // What hangs above each joint, summed from the tip down, so a
            // joint's moment is a few multiplies rather than a loop over
            // every segment above it.
            Vector2 forceSum = Vector2.Zero, forceMoment = Vector2.Zero, weightMoment = Vector2.Zero;
            float massSum = 0.0f;
            for (int s = Segments - 1; s >= 0; s--)
            {
                float centre = (s + 0.5f) * L;
                Vector2 mid = (_offset[s] + _offset[s + 1]) * 0.5f;
                forceSum += _force[s];
                forceMoment += _force[s] * centre;
                massSum += _mass[s];
                weightMoment += mid * _mass[s];
                _aboveForce[s] = forceSum;
                _aboveForceMoment[s] = forceMoment;
                _aboveMass[s] = massSum;
                _aboveWeight[s] = weightMoment;
            }
            for (int j = 0; j < Segments; j++)
            {
                if (_inertia[j] <= 1e-3f)
                {
                    // Nothing above this joint any more: cut free.
                    _tilt[j] = Vector2.Zero;
                    _rate[j] = Vector2.Zero;
                    continue;
                }
                float hj = j * L;
                // Wind, and its own weight once it leans (it leans further).
                Vector2 torque = _aboveForceMoment[j] - (_aboveForce[j] * hj)
                    + ((_aboveWeight[j] - (_offset[j] * _aboveMass[j])) * _gravity);
                for (int p = 0; p < _points; p++)
                {
                    float lever = _pointLever[p] - hj;
                    if (lever > 0.0f)
                    {
                        torque += _pointForce[p] * lever;
                    }
                }
                Vector2 accel = (torque - (_tilt[j] * _stiffness[j]) - (_rate[j] * _damping[j])) / _inertia[j];
                _rate[j] += accel * h;
                _tilt[j] += _rate[j] * h;
                energy += _rate[j].LengthSquared();
            }
            Limit();
        }
        _points = 0;
        Shape();
        Restless = energy > 1e-10f;
    }

    // A plant never leans past its limit, however weak a cut has left it:
    // past it, it rests there as if propped on what is left.
    private void Limit()
    {
        Vector2 cumulative = Vector2.Zero;
        for (int j = 0; j < Segments; j++)
        {
            Vector2 next = cumulative + _tilt[j];
            float length = next.Length();
            if (length > _maxLean)
            {
                if (Propped < 0)
                {
                    Propped = j;
                }
                Vector2 capped = next * (_maxLean / length);
                _tilt[j] = capped - cumulative;
                next = capped;
                Vector2 outward = capped / _maxLean;
                float away = _rate[j].Dot(outward);
                if (away > 0.0f)
                {
                    _rate[j] -= outward * away;
                }
            }
            cumulative = next;
        }
    }

    private void Shape()
    {
        float L = SegmentLength;
        Vector2 cumulative = Vector2.Zero;
        Vector2 at = Vector2.Zero;
        float drop = 0.0f;
        _offset[0] = Vector2.Zero;
        _drop[0] = 0.0f;
        for (int s = 0; s < Segments; s++)
        {
            cumulative += _tilt[s];
            at += cumulative * L;
            // A segment tilted by a small angle a is shorter upward by about
            // L a^2 / 2: the top comes down as it leans over.
            drop += 0.5f * L * cumulative.LengthSquared();
            _offset[s + 1] = at;
            _drop[s + 1] = drop;
        }
    }

    /// <summary>Point <paramref name="point"/>'s move from rest: sideways (x, z of the plant's frame) and down.</summary>
    public Vector2 Offset(int point) => _offset[point];

    public float Drop(int point) => _drop[point];

    /// <summary>The move at a height above the foot, between points: what the shader does.</summary>
    public void At(float height, out Vector2 offset, out float drop)
    {
        float along = Mathf.Clamp(height / SegmentLength, 0.0f, Segments);
        int i = Math.Min((int)along, Segments - 1);
        float t = along - i;
        offset = _offset[i].Lerp(_offset[i + 1], t);
        drop = Mathf.Lerp(_drop[i], _drop[i + 1], t);
    }

    public int Substeps => _substeps;

    /// <summary>Joint <paramref name="joint"/>'s stiffness: N m per radian of bend.</summary>
    public float Stiffness(int joint) => _stiffness[joint];

    /// <summary>The joint with least of its built section left, and how much: for the bench.</summary>
    public float Softest(out int joint)
    {
        joint = 0;
        float least = 1.0f;
        if (_builtSection == null)
        {
            return least;
        }
        float foot = Mathf.Max(1.0f, _builtSection[0]);
        for (int j = 0; j < Segments; j++)
        {
            float full = _stiffnessScale * Mathf.Pow(Mathf.Max(_builtSection[j], 0.5f) / foot, _taper);
            float left = full > 0.0f ? _stiffness[j] / full : 1.0f;
            if (left < least)
            {
                least = left;
                joint = j;
            }
        }
        return least;
    }
}
