using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Placeholder tracer: a stretched box that travels from the muzzle to the
/// impact point at the weapon's tracer speed, then frees itself.
///
/// PLACEHOLDER. Month 3 replaces this with a proper beam or particle trail.
/// It exists now because a hitscan shot with no visible travel gives no read
/// on whether the gun is pointing where the player thinks it is.
/// </summary>
[GlobalClass]
public partial class Tracer : Node3D
{
    [Export]
    public MeshInstance3D Beam { get; set; }

    /// <summary>Visible length of the streak, in metres.</summary>
    [Export(PropertyHint.Range, "0.5,40,0.1,suffix:m")]
    public float StreakLength { get; set; } = 9.0f;

    /// <summary>Seconds the streak lingers at the impact point before freeing.</summary>
    [Export(PropertyHint.Range, "0,1,0.005,suffix:s")]
    public float LingerTime { get; set; } = 0.03f;

    private Vector3 _start;
    private Vector3 _end;
    private float _speed;
    private float _travelled;
    private float _totalDistance;
    private float _lingerRemaining;
    private bool _launched;

    public void Launch(Vector3 start, Vector3 end, float speed)
    {
        _start = start;
        _end = end;
        _speed = Mathf.Max(1.0f, speed);
        _totalDistance = start.DistanceTo(end);
        _travelled = 0.0f;
        _launched = true;

        if (_totalDistance < 0.01f)
        {
            QueueFree();
            return;
        }

        GlobalPosition = start;
        LookAt(end, Mathf.Abs((end - start).Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up);
    }

    public override void _Process(double delta)
    {
        if (!_launched)
        {
            return;
        }

        if (_travelled >= _totalDistance)
        {
            _lingerRemaining -= (float)delta;
            if (_lingerRemaining <= 0.0f)
            {
                QueueFree();
            }
            return;
        }

        _travelled = Mathf.Min(_totalDistance, _travelled + (_speed * (float)delta));

        Vector3 direction = (_end - _start).Normalized();
        float streak = Mathf.Min(StreakLength, _travelled);

        // The box mesh is 1m long on Z; scaling it stretches the streak behind
        // the leading edge without re-authoring the mesh each frame.
        GlobalPosition = _start + (direction * (_travelled - (streak * 0.5f)));
        if (Beam != null)
        {
            Beam.Scale = new Vector3(1.0f, 1.0f, Mathf.Max(0.01f, streak));
        }

        if (_travelled >= _totalDistance)
        {
            _lingerRemaining = LingerTime;
        }
    }
}
