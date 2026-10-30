using Godot;
using DillControl.Levels;
using DillControl.Player;

namespace DillControl.Mission;

/// <summary>
/// The opening of a handbuilt level (decision 0054): every body, the first
/// and each clone, comes down in the company's pod at <see cref="LandingSite"/>
/// and looks out of it until it lands and the hatch is open. The site is a
/// marker in the level, stood on the ground and turned to face what the player
/// is to see first; move it in the editor and the drop follows.
///
/// PLACEHOLDER pod (<c>prp_landing_pod</c> and its cabin): the model, the window's
/// view and the cloning flashes of 0054 are art that does not exist yet.
/// </summary>
[GlobalClass]
public partial class PodLanding : Node
{
    [Export] public ExtractionRules Rules { get; set; }
    [Export] public PackedScene PodScene { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export] public LevelRoot Level { get; set; }
    /// <summary>Where the pod comes down: its -Z is the way the rider faces out of the hatch.</summary>
    [Export] public Marker3D LandingSite { get; set; }

    /// <summary>The rider has stepped out of the current pod and has the controls.</summary>
    [Signal] public delegate void ArrivedEventHandler();

    public bool HasArrived { get; private set; }

    private DropPod _pod;

    public override void _Ready()
    {
        if (Rules == null || Level == null || PodScene == null || LandingSite == null)
        {
            GD.PushError($"PodLanding '{Name}' needs Rules, Level, PodScene and LandingSite; no pod drop.");
            return;
        }
        Level.PlayerSpawned += OnPlayerSpawned;
    }

    public override void _ExitTree()
    {
        if (Level != null)
        {
            Level.PlayerSpawned -= OnPlayerSpawned;
        }
    }

    private void OnPlayerSpawned(Node3D player)
    {
        if (player is not PlayerController rider)
        {
            return;
        }
        // The last body's pod stays where it landed, something to stand on and
        // hook, until the next one needs the spot.
        if (_pod != null && IsInstanceValid(_pod))
        {
            _pod.QueueFree();
        }
        HasArrived = false;
        _pod = PodScene.Instantiate<DropPod>();
        Level.AddChild(_pod);
        _pod.SteppedOut += () =>
        {
            HasArrived = true;
            EmitSignal(SignalName.Arrived);
        };
        // The marker itself, not a ray: on the first frame the ground's
        // colliders are not in the broadphase yet.
        _pod.Drop(LandingSite.GlobalTransform, rider, Rules);
    }
}
