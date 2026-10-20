using Godot;
using DillControl.Core;
using DillControl.Player;
using DillControl.Weapons;

namespace DillControl.Mission;

/// <summary>
/// The company's pod (decision 0026). Falls from high over a point, carrying
/// the player stowed with its own camera on them, goes off like a bomb where
/// it lands, and lets them out. The same pod comes down empty to take them
/// home: once landed and <see cref="Open"/>, a player stepping into its hatch
/// is <see cref="Boarded"/>, and <see cref="LiftOff"/> takes it back up.
///
/// It knows nothing of the mission: <see cref="ExtractionMission"/> makes
/// it, says where, and listens.
/// </summary>
[GlobalClass]
public partial class DropPod : Node3D
{
    [ExportGroup("Scene Wiring")]
    [Export]
    public Camera3D Camera { get; set; }

    /// <summary>Where the rider steps out.</summary>
    [Export]
    public Marker3D ExitPoint { get; set; }

    /// <summary>Stepping in here boards a pod that is open.</summary>
    [Export]
    public Area3D Hatch { get; set; }

    /// <summary>Solid once landed, so it can be stood on and hooked.</summary>
    [Export]
    public CollisionShape3D Collider { get; set; }

    /// <summary>The burn under it, shown while it falls or climbs.</summary>
    [Export]
    public Node3D Flame { get; set; }

    [Export]
    public PackedScene ExplosionScene { get; set; }

    [Signal]
    public delegate void LandedEventHandler();

    [Signal]
    public delegate void BoardedEventHandler(PlayerController player);

    [Signal]
    public delegate void GoneEventHandler();

    private enum State { Idle, Falling, Landed, Open, Climbing }

    private State _state;
    private Vector3 _from;
    private Vector3 _to;
    private float _seconds;
    private float _time;
    private PlayerController _rider;
    private ExtractionRules _rules;

    public bool IsOpen => _state == State.Open;

    public override void _Ready()
    {
        SetCollider(false);
        if (Hatch != null)
        {
            Hatch.BodyEntered += OnHatchEntered;
        }
    }

    /// <summary>
    /// Starts the fall to <paramref name="landing"/>, turned as it is: the
    /// hatch and the rider face its forward (-Z). With a
    /// <paramref name="rider"/>, the rider is stowed and watched from the pod
    /// until it lands.
    /// </summary>
    public void Drop(Transform3D landing, PlayerController rider, ExtractionRules rules)
    {
        _rules = rules;
        _to = landing.Origin;
        _from = _to + (Vector3.Up * rules.DropHeight);
        _seconds = rules.DropSeconds;
        _time = 0.0f;
        _state = State.Falling;
        GlobalTransform = new Transform3D(landing.Basis.Orthonormalized(), _from);
        SetFlame(true);
        SetCollider(false);

        _rider = rider;
        if (_rider != null)
        {
            _rider.SetStowed(true);
            _rider.GlobalPosition = _from;
            if (Camera != null)
            {
                Camera.Current = true;
            }
        }
    }

    /// <summary>Lets a player board: an extraction pod, once it has landed.</summary>
    public void Open()
    {
        _state = State.Open;
        foreach (Node3D body in Hatch?.GetOverlappingBodies() ?? new Godot.Collections.Array<Node3D>())
        {
            OnHatchEntered(body);
        }
    }

    /// <summary>Climbs back out of sight with whoever is in it, watched from the pod.</summary>
    public void LiftOff(PlayerController rider)
    {
        _rider = rider;
        _rider?.SetStowed(true);
        if (Camera != null)
        {
            Camera.Current = true;
        }
        _from = GlobalPosition;
        _to = _from + (Vector3.Up * (_rules?.DropHeight ?? 400.0f));
        _seconds = (_rules?.DropSeconds ?? 5.0f) * 1.2f;
        _time = 0.0f;
        _state = State.Climbing;
        SetFlame(true);
        SetCollider(false);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_state is not (State.Falling or State.Climbing))
        {
            return;
        }

        _time += (float)delta;
        float t = Mathf.Clamp(_time / Mathf.Max(_seconds, 0.01f), 0.0f, 1.0f);
        // Falling, it speeds up all the way down; climbing, it starts slow.
        float eased = t * t;
        GlobalPosition = _from.Lerp(_to, eased);
        if (_rider != null && IsInstanceValid(_rider))
        {
            _rider.GlobalPosition = GlobalPosition;
        }

        if (t < 1.0f)
        {
            return;
        }

        if (_state == State.Falling)
        {
            Land();
        }
        else
        {
            _state = State.Idle;
            SetFlame(false);
            EmitSignal(SignalName.Gone);
        }
    }

    private void Land()
    {
        _state = State.Landed;
        SetFlame(false);
        SetCollider(true);
        Impact();

        if (_rider != null && IsInstanceValid(_rider))
        {
            Transform3D exit = ExitPoint?.GlobalTransform ?? GlobalTransform;
            _rider.GlobalTransform = new Transform3D(Basis.Identity.Rotated(Vector3.Up, exit.Basis.GetEuler().Y), exit.Origin);
            _rider.SetStowed(false);
        }
        _rider = null;
        EmitSignal(SignalName.Landed);
    }

    /// <summary>The landing is a bomb: whatever was standing there is not any more.</summary>
    private void Impact()
    {
        if (_rules == null || _rules.LandingRadius <= 0.0f)
        {
            return;
        }

        Blast.Settings blast = new()
        {
            Centre = GlobalPosition + (Vector3.Up * 0.5f),
            InnerRadius = _rules.LandingRadius * 0.5f,
            Radius = _rules.LandingRadius,
            Damage = _rules.LandingDamage,
            MinMultiplier = 0.3f,
            Impulse = 12.0f,
            ImpulseUpBias = 0.6f,
            Up = Vector3.Up,
            Source = this,
            SelfImpulseScale = 0.0f,
            NoCrater = true,
        };
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Blast.ApplyDamage(space, blast);
        Blast.ApplyToRigidBodies(space, blast, 1.0f);

        if (ExplosionScene?.Instantiate() is Node3D explosion)
        {
            (explosion as ExplosionEffect)?.Configure(blast.Centre, _rules.LandingRadius, Vector3.Up, Vector3.Up, false);
            GetTree().CurrentScene.AddChild(explosion);
            explosion.GlobalPosition = blast.Centre;
        }
    }

    private void OnHatchEntered(Node3D body)
    {
        if (_state == State.Open && body is PlayerController player && !player.IsStowed)
        {
            _state = State.Landed;
            EmitSignal(SignalName.Boarded, player);
        }
    }

    private void SetFlame(bool on)
    {
        if (Flame != null)
        {
            Flame.Visible = on;
        }
    }

    private void SetCollider(bool on)
    {
        if (Collider != null)
        {
            Collider.SetDeferred(CollisionShape3D.PropertyName.Disabled, !on);
        }
    }
}
