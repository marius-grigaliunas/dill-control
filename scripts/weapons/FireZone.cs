using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// A patch of burning ground left by an incendiary round.
///
/// It denies a position rather than killing what walks through it quickly,
/// which is the point of carrying incendiary at all: the cheap way to hold a
/// lane you cannot stand in.
///
/// It reuses <see cref="Blast.ApplyDamage"/> once per tick rather than growing
/// a damage path of its own. That single call reaches every agent in the whole
/// horde through the bus, respects line of sight, de-duplicates by receiver and
/// builds a fresh DamageInfo per target -- four behaviours it would otherwise
/// have to reimplement, and get subtly wrong.
/// </summary>
public partial class FireZone : Node3D
{
    private float _remaining;
    private float _tickTimer;
    private float _radius;
    private float _damagePerSecond;
    private Vector3 _up = Vector3.Up;
    private GpuParticles3D _flames;

    /// <summary>
    /// How often the fire resolves. Damage is a rate times the elapsed slice,
    /// so what a target takes does not change with this -- it only trades
    /// smoothness against how often the query runs, exactly as the flamethrower
    /// and the bike's cannon do.
    /// </summary>
    public float TickInterval { get; set; } = 0.4f;

    public bool IsBurning => _remaining > 0.0f;

    /// <summary>Radius it burns, as set by the last <see cref="Ignite"/>.</summary>
    public float Radius => _radius;

    private SoundLoop _burnLoop;

    public override void _ExitTree()
    {
        _burnLoop?.Stop();
        _burnLoop = null;
    }

    public void Ignite(Vector3 centre, Vector3 up, float radius, float seconds, float damagePerSecond)
    {
        _up = up.LengthSquared() > 0.0001f ? up.Normalized() : Vector3.Up;
        _radius = Mathf.Max(0.5f, radius);
        _remaining = Mathf.Max(0.0f, seconds);
        _damagePerSecond = Mathf.Max(0.0f, damagePerSecond);
        _tickTimer = 0.0f;

        GlobalPosition = centre;
        BuildMesh();

        _burnLoop ??= AudioDirector.Loop(Sfx.FireLoop, this);
    }

    /// <summary>
    /// Flames across the radius it actually burns, so what you see is what the
    /// query covers: <see cref="ToonFlame.Ground"/>, the same opaque inked
    /// puffs the explosions are drawn with. It was a flat translucent orange
    /// disc, which on the ground read as a puddle of paint rather than fire --
    /// reported as the laser being "not juicy at all".
    /// </summary>
    private void BuildMesh()
    {
        _flames?.QueueFree();
        _flames = ToonFlame.Ground(_radius);
        AddChild(_flames);

        // Lying on the local tangent plane, which on a planet is not world flat.
        Vector3 forward = _up.Cross(Vector3.Right);
        if (forward.LengthSquared() < 1e-6f)
        {
            forward = _up.Cross(Vector3.Forward);
        }
        forward = forward.Normalized();

        GlobalBasis = new Basis(forward.Cross(_up), _up, -forward);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsBurning)
        {
            return;
        }

        float dt = (float)delta;
        _remaining -= dt;

        if (!IsBurning)
        {
            _burnLoop?.Stop();
            _burnLoop = null;
        }

        if (_flames != null)
        {
            // Dies down rather than snapping out, so the lane reopening is
            // something a player can read before they commit to crossing it.
            _flames.AmountRatio = Mathf.Clamp(_remaining / 3.0f, 0.15f, 1.0f);
        }

        _tickTimer += dt;
        if (_tickTimer < TickInterval)
        {
            return;
        }

        float elapsed = _tickTimer;
        _tickTimer = 0.0f;

        if (_damagePerSecond > 0.0f)
        {
            Blast.ApplyDamage(GetWorld3D().DirectSpaceState, new Blast.Settings
            {
                Centre = GlobalPosition,
                InnerRadius = _radius * 0.7f,
                Radius = _radius,
                Damage = _damagePerSecond * elapsed,
                MinMultiplier = 0.6f,
                Impulse = 0.0f,
                ImpulseUpBias = 0.0f,
                Up = _up,
                Source = this,
                SelfImpulseScale = 0.0f,
                Sustained = true,
            });
        }

        if (!IsBurning)
        {
            QueueFree();
        }
    }
}
