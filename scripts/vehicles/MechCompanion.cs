using DillControl.Levels;
using DillControl.Player;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// The player's ally mech: what tells a <see cref="MechWalker"/> what to do.
///
/// On its own it follows, keeping a spot behind and beside the player, facing
/// the way they face with its turret on whatever they look at. Close enough,
/// <c>interact</c> climbs on: the player is stowed (as on the bike) and carried
/// at the seat, so anything chasing them chases the mech, and the mech's own
/// camera takes over. Riding, WASD walks relative to the camera in any
/// direction, Shift sprints, the body turns to the camera while walking, the
/// turret follows the camera all the way round, jump jumps and held jump is
/// the jets, fire is the arm gun, the flamer or the slam (slots 1/2/3) and
/// alt fire the hull cannon (<see cref="MechGunner"/>, <see cref="MechSlasher"/>).
/// Riding starts in first person from the seat; <c>view_toggle</c> swaps to a
/// chase camera and back; <c>interact</c> climbs off. On its own, its guns
/// pick their own targets.
///
/// The player arrives through <see cref="LevelRoot.PlayerSpawned"/>, because
/// <see cref="LevelRoot"/> instances it at runtime and nothing authored in a
/// level can name it.
/// </summary>
public partial class MechCompanion : Node
{
    [ExportGroup("Scene Wiring")]
    [Export]
    public MechWalker Walker { get; set; }

    [Export]
    public LevelRoot Level { get; set; }

    /// <summary>Where the rider sits; it turns with the turret.</summary>
    [Export]
    public Node3D Seat { get; set; }

    /// <summary>Top-level node the ride camera orbits about.</summary>
    [Export]
    public Node3D CameraPivot { get; set; }

    [Export]
    public SpringArm3D CameraArm { get; set; }

    [Export]
    public Camera3D Camera { get; set; }

    /// <summary>The guns. On its own it picks targets; ridden, the rider fires.</summary>
    [Export]
    public MechGunner Gunner { get; set; }

    /// <summary>The arm slam, if this mech has one. Slot 3 selects it.</summary>
    [Export]
    public MechSlasher Slasher { get; set; }

    /// <summary>The key reminder shown while riding.</summary>
    [Export]
    public CanvasLayer RideHint { get; set; }

    /// <summary>Shown over the mech while the player is close enough to climb on.</summary>
    [Export]
    public Label3D Prompt { get; set; }

    public bool IsRidden { get; private set; }

    /// <summary>For <c>bench.gd</c>, which cannot move the mouse: swings the
    /// ride camera a third of the way round.</summary>
    public void BenchTurnCamera()
    {
        _yaw += Mathf.DegToRad(120.0f);
    }

    private PlayerController _player;
    private float _yaw;
    private float _pitch = -0.25f;
    private bool _seatView;
    private ArmTool _armTool;

    private enum ArmTool
    {
        Gun,
        Flamer,
        Slash,
    }

    private MechTuning Tuning => Walker?.Tuning;

    public override void _Ready()
    {
        if (Walker == null || Tuning == null)
        {
            GD.PushError($"MechCompanion '{Name}' needs a Walker with Tuning.");
            SetPhysicsProcess(false);
            SetProcess(false);
            return;
        }

        // Wired in the level scene normally. If the override is missing, walk
        // up for the LevelRoot the way the bike does: an editor re-save once
        // dropped it, and the mech then never learned who the player was.
        Level ??= FindLevel();
        if (Level != null)
        {
            Level.PlayerSpawned += OnPlayerSpawned;
            if (Level.Player != null)
            {
                OnPlayerSpawned(Level.Player);
            }
        }

        if (CameraPivot != null)
        {
            CameraPivot.TopLevel = true;
        }
        CameraArm?.AddExcludedObject(Walker.GetRid());
        Walker.Destroyed += OnWalkerDestroyed;
        if (Prompt != null)
        {
            Prompt.Visible = false;
        }
        if (RideHint != null)
        {
            RideHint.Visible = false;
        }
        if (Gunner != null)
        {
            Gunner.AimCamera = Camera;
        }
    }

    /// <summary>The mech fell apart: the rider is thrown clear, and nothing
    /// here runs again.</summary>
    private void OnWalkerDestroyed()
    {
        if (IsRidden && _player != null)
        {
            IsRidden = false;
            _player.GlobalPosition = Walker.GlobalPosition + Walker.Up * 1.0f;
            _player.SetStowed(false);
            _player.ApplyBlastImpulse(Walker.Up * 8.0f);
        }
        if (Prompt != null)
        {
            Prompt.Visible = false;
        }
        if (RideHint != null)
        {
            RideHint.Visible = false;
        }
        SetPhysicsProcess(false);
        SetProcess(false);
        SetProcessUnhandledInput(false);
    }

    private void OnPlayerSpawned(Node3D player)
    {
        _player = player as PlayerController;
        if (Gunner != null)
        {
            Gunner.Friendly = _player;
        }
    }

    public override void _ExitTree()
    {
        if (Level != null)
        {
            Level.PlayerSpawned -= OnPlayerSpawned;
        }
    }

    private LevelRoot FindLevel()
    {
        for (Node node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is LevelRoot level)
            {
                return level;
            }
        }
        return null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player == null)
        {
            Walker.MoveIntent = Vector3.Zero;
            Walker.FaceDirection = Vector3.Zero;
            Walker.Sprint = false;
            return;
        }

        if (IsRidden)
        {
            Ride();
        }
        else
        {
            Follow();
        }

        if (IsRidden && Input.IsActionJustPressed("view_toggle"))
        {
            _seatView = !_seatView;
        }
    }

    public override void _Process(double delta)
    {
        if (Prompt != null)
        {
            Prompt.Visible = !IsRidden && _player != null && InRange();
        }

        if (IsRidden)
        {
            PlaceCamera((float)delta);
            // Carried at the seat, so the horde comes for the mech.
            // (A mech with no seat is never ridden: InRange.)
            if (Seat != null)
            {
                _player.GlobalPosition = Seat.GlobalPosition;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // On the event and marked handled, as the hoverbike does, so one press
        // of interact boards one vehicle even standing between two.
        if (@event.IsActionPressed("interact") && _player != null)
        {
            if (IsRidden)
            {
                Dismount();
                GetViewport().SetInputAsHandled();
            }
            else if (InRange() && !CloserAlly())
            {
                Mount();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (!IsRidden || @event is not InputEventMouseMotion motion || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }

        _yaw -= motion.Relative.X * Tuning.MouseSensitivity;
        _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * Tuning.MouseSensitivity, -1.2f, 0.9f);
        GetViewport().SetInputAsHandled();
    }

    // ---------------- Following ----------------

    /// <summary>
    /// What it does when nobody rides it. With a threat near the player it
    /// guards: it takes a stand between the player and the threat, a little
    /// to its own side so allies do not stack, and fights from there. With a
    /// threat near itself only, it holds and fights. Otherwise it follows. It
    /// never strays past the leash.
    /// </summary>
    private void Follow()
    {
        Vector3 up = Walker.Up;
        Basis look = _player.GlobalBasis;
        Vector3 back = look.Z.Slide(up).Normalized();
        Vector3 side = look.X.Slide(up).Normalized();
        Vector3 spot = _player.GlobalPosition + back * Tuning.FollowBehind + side * Tuning.FollowSide;

        Vector3? threat = Gunner?.Target;
        float fromPlayer = (Walker.GlobalPosition - _player.GlobalPosition).Slide(up).Length();
        bool leashed = fromPlayer > Tuning.LeashRange;
        if (threat is Vector3 danger && !leashed)
        {
            Vector3 toThreat = (danger - _player.GlobalPosition).Slide(up);
            if (toThreat.Length() <= Tuning.GuardRadius)
            {
                // Guard: stand off the player toward the threat.
                Vector3 dir = toThreat.Normalized();
                Vector3 across = up.Cross(dir) * Mathf.Sign(Tuning.FollowSide == 0.0f ? 1.0f : Tuning.FollowSide) * 3.0f;
                spot = _player.GlobalPosition + dir * Mathf.Min(Tuning.GuardDistance, toThreat.Length() * 0.5f) + across;
            }
            else
            {
                // Something is on it: hold where it is and fight.
                spot = Walker.GlobalPosition;
            }
        }

        Vector3 toSpot = (spot - Walker.GlobalPosition).Slide(up);
        float distance = toSpot.Length();
        float drive = Mathf.Clamp((distance - Tuning.FollowArriveRadius) / Mathf.Max(Tuning.FollowSlowRadius, 0.01f), 0.0f, 1.0f);
        Vector3 desire = distance > 1e-3f ? toSpot / distance * drive : Vector3.Zero;
        Walker.MoveIntent = AroundPlayer(desire, up);
        Walker.Sprint = Tuning.FollowSprintDistance > 0.0f && distance > Tuning.FollowSprintDistance;

        // Walking, it faces where it is going. Arrived, it turns its hull gun
        // on whatever it is shooting, or else looks the way the player looks.
        // The turret is on the target whenever there is one.
        Vector3? target = Gunner?.Target;
        Vector3 toTarget = target is Vector3 t ? (t - Walker.GlobalPosition).Slide(up) : Vector3.Zero;
        Walker.FaceDirection = drive > 0.3f ? toSpot : (target != null ? toTarget : -back);
        Walker.TurretAim = target != null ? toTarget : -back;
        Walker.JumpHeld = false;
        if (Gunner != null)
        {
            Gunner.Manual = false;
            Gunner.TurretTrigger = false;
            Gunner.CannonTrigger = false;
        }
        if (Slasher != null)
        {
            Slasher.Manual = false;
            Slasher.Trigger = false;
        }
    }

    /// <summary>
    /// Bends a walk so it never goes through the player: inside
    /// <see cref="MechTuning.PersonalSpace"/>, the part of the walk heading at
    /// them is turned sideways, round them, and a push away is added that
    /// grows as it gets closer.
    /// </summary>
    private Vector3 AroundPlayer(Vector3 desire, Vector3 up)
    {
        Vector3 fromPlayer = (Walker.GlobalPosition - _player.GlobalPosition).Slide(up);
        float d = fromPlayer.Length();
        float space = Tuning.PersonalSpace;
        if (d >= space || space <= 0.0f)
        {
            return desire;
        }

        Vector3 away = d > 1e-3f ? fromPlayer / d : -Walker.GlobalBasis.Z;
        float inward = desire.Dot(-away);
        if (inward > 0.0f)
        {
            Vector3 round = up.Cross(away);
            if (round.Dot(desire) < 0.0f)
            {
                round = -round;
            }
            desire += (away + round) * inward;
        }

        desire += away * ((space - d) / space);
        return desire.LimitLength(1.0f);
    }

    private bool InRange()
    {
        if (_player == null || _player.IsStowed || Tuning.MountRange <= 0.0f || Camera == null)
        {
            return false;
        }
        return (_player.GlobalPosition - Walker.GlobalPosition).Slide(Walker.Up).Length() <= Tuning.MountRange;
    }

    /// <summary>True when another climbable ally is nearer than this one.
    /// Interact climbs the closest, not whichever companion node sees the
    /// event first.</summary>
    private bool CloserAlly()
    {
        float mine = (_player.GlobalPosition - Walker.GlobalPosition).Slide(Walker.Up).Length();
        Vector3 up = Walker.Up;
        foreach (Node node in GetTree().GetNodesInGroup(MechWalker.AllyGroup))
        {
            if (node == Walker || node is not MechWalker ally || ally.IsDestroyed || ally.Tuning == null)
            {
                continue;
            }
            if (ally.Tuning.MountRange <= 0.0f)
            {
                continue;
            }
            float d = (ally.GlobalPosition - _player.GlobalPosition).Slide(up).Length();
            if (d <= ally.Tuning.MountRange && d < mine)
            {
                return true;
            }
        }
        return false;
    }

    // ---------------- Riding ----------------

    private void Mount()
    {
        IsRidden = true;
        // First person from the seat: you climb up into it, not out of your
        // own head into a chase camera. V swaps to the chase view.
        _seatView = true;

        // Start the camera behind the player's own view.
        Vector3 forward = -_player.GlobalBasis.Z.Slide(Walker.Up);
        _yaw = Mathf.Atan2(-forward.X, -forward.Z);
        _pitch = -0.25f;

        _player.SetStowed(true);
        Camera?.MakeCurrent();
        PlaceCamera(1.0f);
        if (RideHint != null)
        {
            RideHint.Visible = true;
        }
    }

    private void Dismount()
    {
        IsRidden = false;
        Walker.MoveIntent = Vector3.Zero;
        Walker.JumpHeld = false;
        Walker.Sprint = false;
        if (RideHint != null)
        {
            RideHint.Visible = false;
        }

        // Down beside it, looking where the camera looked.
        Vector3 up = Walker.Up;
        Vector3 side = -Walker.GlobalBasis.X.Slide(up).Normalized();
        _player.GlobalPosition = Walker.GlobalPosition + side * 4.5f + up * 0.5f;
        _player.GlobalBasis = new Basis(up, _yaw);
        _player.SetStowed(false);
    }

    private void Ride()
    {
        Vector3 up = Walker.Up;
        Basis view = new Basis(up, _yaw);
        Vector3 forward = -view.Z;
        Vector3 right = view.X;

        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        Vector3 move = right * input.X - forward * input.Y;
        Walker.MoveIntent = move;
        Walker.Sprint = Input.IsActionPressed("sprint") && move.LengthSquared() > 0.01f;
        Walker.FaceDirection = move.LengthSquared() > 0.01f || Tuning.FaceCameraWhenIdle ? forward : Vector3.Zero;
        Walker.TurretAim = forward;

        if (Input.IsActionJustPressed("jump"))
        {
            Walker.RequestJump();
        }
        Walker.JumpHeld = Input.IsActionPressed("jump");

        if (Gunner != null)
        {
            bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
            // 1 the arm gun, 2 the flamer, 3 the slam, on the same trigger.
            if (Input.IsActionJustPressed("slot_1"))
            {
                _armTool = ArmTool.Gun;
            }
            else if (Gunner.HasFlamer && Input.IsActionJustPressed("slot_2"))
            {
                _armTool = ArmTool.Flamer;
            }
            else if (Slasher != null && Input.IsActionJustPressed("slot_3"))
            {
                _armTool = ArmTool.Slash;
            }

            bool slash = _armTool == ArmTool.Slash && Slasher != null;
            Gunner.Manual = true;
            Gunner.FlamerSelected = _armTool == ArmTool.Flamer;
            Gunner.TurretTrigger = captured && Input.IsActionPressed("fire") && !slash;
            Gunner.CannonTrigger = captured && Input.IsActionPressed("alt_fire");
            if (Slasher != null)
            {
                Slasher.Manual = true;
                Slasher.Trigger = captured && Input.IsActionPressed("fire") && slash;
            }
        }
        else if (Slasher != null)
        {
            Slasher.Manual = true;
            Slasher.Trigger = false;
        }
    }

    private void PlaceCamera(float dt)
    {
        if (CameraPivot == null || CameraArm == null)
        {
            return;
        }

        Basis view = new Basis(Walker.Up, _yaw) * new Basis(Vector3.Right, _pitch);
        Vector3 anchor;
        if (_seatView && Seat != null)
        {
            // The seat rides on whatever it is mounted on (MechWalker.Mounted).
            anchor = Seat.GlobalPosition;
            CameraArm.SpringLength = 0.0f;
        }
        else
        {
            anchor = Walker.GlobalPosition + Walker.Up * Tuning.CameraHeight;
            CameraArm.SpringLength = Tuning.CameraDistance;
        }

        CameraPivot.GlobalTransform = new Transform3D(view, anchor);
    }

    /// <summary>One line of state, for <c>bench.gd</c>'s <c>!call</c>.</summary>
    public void PrintBenchStatus()
    {
        foreach (Node node in GetTree().GetNodesInGroup(MechWalker.AllyGroup))
        {
            if (node is MechWalker walker)
            {
                GD.Print("[bench]   ", walker.BenchStatus(), walker == Walker && IsRidden ? " ridden" : "");
            }
        }
    }
}
