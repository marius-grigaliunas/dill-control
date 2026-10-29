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
/// With <see cref="FirstPerson"/> the rider looks out of the pod's own
/// <see cref="Camera"/>, seated inside it, and can turn their head while it
/// falls; after the landing they wait <see cref="ExitDelay"/> and step out.
///
/// It knows nothing of the mission: <see cref="ExtractionMission"/> or
/// <see cref="PodLanding"/> makes it, says where, and listens.
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

    [ExportGroup("First person")]
    /// <summary>The rider sits inside and looks out of <see cref="Camera"/>, turning it with the mouse.</summary>
    [Export]
    public bool FirstPerson { get; set; }

    /// <summary>Seconds between touching down and the rider stepping out.</summary>
    [Export(PropertyHint.Range, "0,10,0.1,suffix:s")]
    public float ExitDelay { get; set; } = 1.8f;

    /// <summary>How far the head is shaken at full speed, in metres.</summary>
    [Export(PropertyHint.Range, "0,0.2,0.005,suffix:m")]
    public float ShakeMetres { get; set; } = 0.025f;

    [Export(PropertyHint.Range, "0.0005,0.01,0.0001")]
    public float LookSensitivity { get; set; } = 0.0022f;

    [Signal]
    public delegate void LandedEventHandler();

    /// <summary>The rider has left the pod and has the controls.</summary>
    [Signal]
    public delegate void SteppedOutEventHandler();

    [Signal]
    public delegate void BoardedEventHandler(PlayerController player);

    [Signal]
    public delegate void GoneEventHandler();

    private enum State { Idle, Falling, Landed, Open, Climbing }

    private State _state;
    private float _yaw;
    private float _pitch;
    private float _landedFor;
    private Vector3 _seat;
    private Vector3 _from;
    private Vector3 _to;
    private float _seconds;
    private float _time;
    private PlayerController _rider;
    private ExtractionRules _rules;

    public bool IsOpen => _state == State.Open;

    public override void _Ready()
    {
        _seat = Camera?.Position ?? Vector3.Zero;
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
        _landedFor = 0.0f;
        _yaw = 0.0f;
        _pitch = 0.0f;
        if (FirstPerson && Camera != null)
        {
            Camera.Rotation = Vector3.Zero;
        }
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

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!FirstPerson || Camera == null || _rider == null || !Camera.Current
            || @event is not InputEventMouseMotion motion || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }
        _yaw = Mathf.Wrap(_yaw - (motion.Relative.X * LookSensitivity), -Mathf.Pi, Mathf.Pi);
        _pitch = Mathf.Clamp(_pitch - (motion.Relative.Y * LookSensitivity), Mathf.DegToRad(-80.0f), Mathf.DegToRad(80.0f));
        Camera.Rotation = new Vector3(_pitch, _yaw, 0.0f);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_state == State.Landed && _rider != null)
        {
            _landedFor += (float)delta;
            if (_landedFor >= ExitDelay)
            {
                StepOut();
            }
            return;
        }
        if (_state is not (State.Falling or State.Climbing))
        {
            return;
        }

        _time += (float)delta;
        float t = Mathf.Clamp(_time / Mathf.Max(_seconds, 0.01f), 0.0f, 1.0f);
        // Falling, it speeds up all the way down; climbing, it starts slow.
        float eased = t * t;
        GlobalPosition = _from.Lerp(_to, eased);
        if (FirstPerson && Camera != null)
        {
            // The fall rattles the cabin, more the faster it goes.
            Camera.Position = _seat + (new Vector3((GD.Randf() - 0.5f), (GD.Randf() - 0.5f), 0.0f) * (ShakeMetres * eased));
        }
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

        if (FirstPerson && _rider != null && IsInstanceValid(_rider) && ExitDelay > 0.0f)
        {
            if (Camera != null)
            {
                Camera.Position = _seat;
            }
            EmitSignal(SignalName.Landed);
            return;
        }
        StepOut();
        EmitSignal(SignalName.Landed);
    }

    /// <summary>The rider leaves the pod by its exit, facing the way they were looking.</summary>
    private void StepOut()
    {
        if (_rider != null && IsInstanceValid(_rider))
        {
            Transform3D exit = ExitPoint?.GlobalTransform ?? GlobalTransform;
            float heading = exit.Basis.GetEuler().Y;
            if (FirstPerson && Camera != null)
            {
                heading = Camera.GlobalTransform.Basis.GetEuler().Y;
            }
            _rider.GlobalTransform = new Transform3D(Basis.Identity.Rotated(Vector3.Up, heading), exit.Origin);
            _rider.SetStowed(false);
            EmitSignal(SignalName.SteppedOut);
        }
        _rider = null;
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
