using Godot;

namespace DillControl.Weapons;

/// <summary>
/// The burned path a held beam leaves behind: a ribbon of char laid mark by
/// mark wherever the beam lands, glowing at first, dissolving after
/// <see cref="Lifetime"/>.
///
/// One multimesh of flat quads in a ring buffer: the whole trail is one draw
/// however long it gets, and the oldest mark is reused when the buffer is
/// full. Each mark runs from the previous one to the new point, so a sweep is
/// one continuous strip; a jump further than <see cref="MaxGap"/> starts a new
/// one. The look is <c>shaders/fx_scorch_trail.gdshader</c>.
///
/// It sits top-level under whatever fires it, so it stays where it was burned
/// while the firer moves on.
/// </summary>
public partial class ScorchTrail : MultiMeshInstance3D
{
    public const string ShaderPath = "res://shaders/fx_scorch_trail.gdshader";

    /// <summary>Most marks alive at once.</summary>
    public int Capacity { get; set; } = 800;

    /// <summary>Width of the strip, in metres.</summary>
    public float Width { get; set; } = 1.6f;

    /// <summary>Seconds a mark lasts, dissolving from its edges over the whole of it.</summary>
    public float Lifetime { get; set; } = 20.0f;

    /// <summary>Seconds a fresh mark's core glows.</summary>
    public float EmberSeconds { get; set; } = 2.5f;

    /// <summary>A new point further than this from the last starts a new strip.</summary>
    public float MaxGap { get; set; } = 6.0f;

    /// <summary>A beam held still lays a fresh mark this often, so the spot it
    /// is burning keeps glowing.</summary>
    public float RestampSeconds { get; set; } = 0.3f;

    private ShaderMaterial _material;
    private int _next;
    private int _used;
    private bool _hasLast;
    private Vector3 _last;
    private Vector3 _lastNormal;
    private float _lastTime;
    private float _clock;

    public override void _Ready()
    {
        TopLevel = true;
        CastShadow = ShadowCastingSetting.Off;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        _material.SetShaderParameter("lifetime", Lifetime);
        _material.SetShaderParameter("ember_seconds", EmberSeconds);
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = _material },
            InstanceCount = Capacity,
            VisibleInstanceCount = 0,
        };
    }

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        _material?.SetShaderParameter("now", _clock);
    }

    /// <summary>The beam has stopped landing: the next mark starts a new strip.</summary>
    public void Break() => _hasLast = false;

    /// <summary>The beam landed here, on a surface facing <paramref name="normal"/>.</summary>
    public void Mark(Vector3 point, Vector3 normal)
    {
        if (Multimesh == null)
        {
            return;
        }
        normal = normal.LengthSquared() > 1e-6f ? normal.Normalized() : Vector3.Up;

        if (_hasLast && (point.DistanceTo(_last) > MaxGap || normal.Dot(_lastNormal) < 0.7f))
        {
            _hasLast = false;
        }

        Vector3 from = _hasLast ? _last : point;
        float length = from.DistanceTo(point);
        if (_hasLast && length < Width * 0.35f)
        {
            // Held still: only refresh the glow now and then.
            if (_clock - _lastTime < RestampSeconds)
            {
                return;
            }
        }

        // Along the strip, or any direction on the surface for a lone spot.
        Vector3 along = length > 1e-3f ? (point - from) / length : normal.Cross(Vector3.Right);
        if (along.LengthSquared() < 1e-6f)
        {
            along = normal.Cross(Vector3.Forward);
        }
        along = (along - normal * along.Dot(normal)).Normalized();
        Vector3 across = along.Cross(normal).Normalized();

        // The quad lies in its XY plane: X across, Y along, Z out of the surface.
        // Overlapping its neighbours by half a width hides the joins; a little
        // lift per slot keeps overlapping marks from fighting for depth.
        float lift = 0.04f + (_next % 4) * 0.006f;
        var basis = new Basis(across * Width, along * (length + Width * 0.5f), normal);
        Vector3 centre = (from + point) * 0.5f + normal * lift;
        Multimesh.SetInstanceTransform(_next, new Transform3D(basis, centre));
        Multimesh.SetInstanceCustomData(_next, new Color(_clock, GD.Randf(), 0.0f, 0.0f));

        _next = (_next + 1) % Capacity;
        _used = Mathf.Min(_used + 1, Capacity);
        Multimesh.VisibleInstanceCount = _used;

        _hasLast = true;
        _last = point;
        _lastNormal = normal;
        _lastTime = _clock;
    }
}
