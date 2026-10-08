using Godot;
using DillControl.Audio;

namespace DillControl.Weapons;

/// <summary>
/// One called-in strike, running in the level after the designator that called
/// it has moved on.
///
/// It is its own node rather than a coroutine on the weapon for one reason: a
/// strike has to survive everything the caller might do next. Switching weapons
/// holsters the designator and stops its processing, and putting the salvo on
/// the weapon would mean a strike could be cancelled by pressing 1. Once it is
/// called it is in the air.
///
/// The bombs are <see cref="Rocket"/> instances aimed downward. A falling bomb
/// and a fired rocket want the same flight integration, the same sweep and the
/// same blast, so there is no second projectile type here -- only a different
/// thing pointing it.
/// </summary>
public partial class Airstrike : Node3D
{
    private AirstrikeStats _stats;
    private RocketStats _ordnance;
    private PackedScene _bombScene;
    private Node3D _source;

    private Vector3 _target;
    private Vector3 _up = Vector3.Up;
    private Vector3 _gravity = Vector3.Down * 9.8f;

    private float _timer;
    private int _dropped;
    private MeshInstance3D _marker;

    private readonly RandomNumberGenerator _rng = new();

    /// <summary>
    /// Arms the strike. Called immediately after the node enters the tree.
    /// </summary>
    public void Begin(
        AirstrikeStats stats,
        RocketStats ordnance,
        PackedScene bombScene,
        Node3D source,
        Vector3 target,
        Vector3 up,
        Vector3 gravity)
    {
        _stats = stats;
        _ordnance = ordnance;
        _bombScene = bombScene;
        _source = source;
        _target = target;
        _up = up.LengthSquared() > 0.0001f ? up.Normalized() : Vector3.Up;
        _gravity = gravity;

        _rng.Randomize();
        _timer = -stats.LeadTime;

        GlobalPosition = target;
        BuildMarker();
    }

    /// <summary>
    /// A ring on the ground where the salvo is going.
    ///
    /// Not decoration. A strike with a lead time and no marker is a weapon that
    /// appears to do nothing for two seconds and then kills you from off
    /// screen; the marker is what turns the delay into a thing you can stand
    /// clear of, which is the decision the lead time exists to create.
    /// </summary>
    private void BuildMarker()
    {
        _marker = new MeshInstance3D
        {
            Name = "Marker",
            Mesh = new TorusMesh
            {
                InnerRadius = Mathf.Max(_stats.SpreadRadius - 1.2f, 0.4f),
                OuterRadius = Mathf.Max(_stats.SpreadRadius, 0.8f),
                RingSegments = 6,
                Rings = 16,
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1.0f, 0.36f, 0.22f, 0.75f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        AddChild(_marker);

        // The torus is built in the XZ plane, so it has to be turned to lie
        // flat against a surface whose up is not world up.
        Vector3 forward = Mathf.Abs(_up.Dot(Vector3.Forward)) > 0.95f
            ? Vector3.Right
            : Vector3.Forward;
        Vector3 right = _up.Cross(forward).Normalized();
        _marker.GlobalTransform = new Transform3D(
            new Basis(right, _up, right.Cross(_up)),
            _target + (_up * 0.35f));
    }

    public override void _Process(double delta)
    {
        if (_stats == null)
        {
            return;
        }

        _timer += (float)delta;

        if (_marker != null)
        {
            // Pulses faster as the salvo gets closer, so the warning reads as a
            // countdown rather than as a decal.
            float urgency = _dropped > 0 ? 14.0f : Mathf.Lerp(3.0f, 12.0f, Countdown());
            float pulse = 0.55f + (0.45f * Mathf.Sin(_timer * urgency));
            ((StandardMaterial3D)_marker.MaterialOverride).AlbedoColor =
                new Color(1.0f, 0.36f, 0.22f, pulse * 0.8f);
        }

        while (_dropped < _stats.BombCount && _timer >= _dropped * _stats.BombInterval)
        {
            DropOne();
            _dropped++;
        }

        // Outlive the last bomb's flight so the marker does not vanish while
        // ordnance is still in the air above it.
        if (_dropped >= _stats.BombCount && _timer > (_stats.BombCount * _stats.BombInterval) + 6.0f)
        {
            QueueFree();
        }
    }

    /// <summary>0 at the call, 1 when the first bomb releases.</summary>
    private float Countdown() =>
        _stats.LeadTime <= 0.0f ? 1.0f : Mathf.Clamp((_timer + _stats.LeadTime) / _stats.LeadTime, 0.0f, 1.0f);

    private void DropOne()
    {
        if (_bombScene == null)
        {
            return;
        }

        Vector3 impact = _target + ScatterOnSurface(_stats.SpreadRadius);

        // Released from directly above its own impact point, with a little
        // lateral offset so the salvo is not a column of bombs down one line.
        Vector3 release = impact + (_up * _stats.DropHeight) + ScatterOnSurface(_stats.SpreadRadius * 0.4f);

        // The whistle is played at the impact point rather than at the
        // release point, and before the bomb has travelled anywhere. It is a
        // warning, and a warning that arrives with the bomb is not one -- the
        // player needs the fall time to get out of the way.
        AudioDirector.PlayAt(Sfx.StrikeIncoming, impact);

        Rocket bomb = _bombScene.Instantiate<Rocket>();
        GetTree().CurrentScene.AddChild(bomb);
        bomb.Launch(
            _ordnance,
            _source,
            release,
            (impact - release).Normalized(),
            _up,
            _gravity,
            default);
    }

    /// <summary>A random offset in the plane perpendicular to up.</summary>
    private Vector3 ScatterOnSurface(float radius)
    {
        Vector3 forward = Mathf.Abs(_up.Dot(Vector3.Forward)) > 0.95f
            ? Vector3.Right
            : Vector3.Forward;
        Vector3 tangent = _up.Cross(forward).Normalized();
        Vector3 bitangent = _up.Cross(tangent);

        float angle = _rng.Randf() * Mathf.Tau;

        // Square root of a uniform sample, or the scatter clusters at the
        // centre and the pattern reads as one big hole rather than a spread.
        float distance = Mathf.Sqrt(_rng.Randf()) * radius;

        return ((tangent * Mathf.Cos(angle)) + (bitangent * Mathf.Sin(angle))) * distance;
    }
}
