using DillControl.Player;
using DillControl.Levels;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// How a nest played from the Vines tool went (<see cref="NestPlay.Result"/>):
/// swings, how long to the heart, how often on the ground, pods destroyed,
/// pickles left and how it ended. Written every couple of seconds while it
/// plays, since the editor stops a game by ending it. Records nothing for
/// a nest the tool did not play.
///
/// What the tool asks for testing it sets up as well: every weapon kept
/// full (resupplied every half second: magazines still reload), and a
/// hoverbike parked beside the player.
///
/// What each measure means is fixed here, not tuned: the verdict log keeps
/// them side by side across nests and must compare like with like.
/// </summary>
[GlobalClass]
public partial class NestPlaytest : Node
{
    /// <summary>Within this of the heart's vine, across the ground, counts as reaching it.</summary>
    public const float HeartReachM = 30.0f;
    /// <summary>On the floor this near the planet's ground is on the ground, not on a vine.</summary>
    public const float GroundMarginM = 2.5f;
    private const float WriteEverySeconds = 2.0f;

    [Export] public LevelRoot Level { get; set; }
    [Export] public NestBuilder Builder { get; set; }
    [Export] public PodField Pods { get; set; }
    [Export] public ColonyMission Mission { get; set; }
    [Export] public PackedScene BikeScene { get; set; }
    /// <summary>Where a parked bike goes: the level's Props.</summary>
    [Export] public Node3D BikeParent { get; set; }

    private const float ResupplyEverySeconds = 0.5f;
    /// <summary>A parked bike stands this far to the player's right.</summary>
    private const float BikeAsideM = 8.0f;
    private float _sinceResupply;
    private bool _infiniteAmmo;

    private NestPlay.Result _result;
    private PlayerController _player;
    private double _playing;
    private bool _wasOnGround;
    private float _sinceWrite;

    public override void _Ready()
    {
        if (Level != null)
        {
            Level.PlayerSpawned += OnPlayerSpawned;
        }
    }

    public override void _ExitTree()
    {
        if (Level != null)
        {
            Level.PlayerSpawned -= OnPlayerSpawned;
        }
        Write();
    }

    private void OnPlayerSpawned(Node3D player)
    {
        NestPlay.Request request = Builder?.PlayedFromTool;
        if (request == null || player is not PlayerController controller)
        {
            return;
        }
        _player = controller;
        _result ??= new NestPlay.Result
        {
            Kit = request.Kit,
            Seed = Builder.Seed,
            Population = Builder.Population,
            Horde = Builder.WithHorde,
        };
        if (_player.Grapple != null)
        {
            _player.Grapple.Attached += () => _result.Swings++;
        }
        _infiniteAmmo = request.InfiniteAmmo;
        if (request.Bike)
        {
            ParkBike();
        }
    }

    // Upright on its own ground, to the player's right, nose the way they face.
    private void ParkBike()
    {
        if (BikeScene == null || BikeParent == null)
        {
            return;
        }
        Vector3 at = _player.GlobalPosition;
        Vector3 up = Core.WorldUp.At(at);
        Vector3 facing = (-_player.GlobalBasis.Z).Slide(up);
        if (facing.LengthSquared() < 1e-6f)
        {
            facing = up.Cross(Vector3.Right);
        }
        facing = facing.Normalized();
        Vector3 place = at + (facing.Cross(up).Normalized() * BikeAsideM) + (up * 1.0f);
        Planet.PlanetHeightfield heights = Builder?.Contract?.Planet?.Heights;
        if (heights != null)
        {
            place = heights.SurfacePoint(place.Normalized()) + (up * 1.0f);
        }
        var bike = BikeScene.Instantiate<Node3D>();
        BikeParent.AddChild(bike);
        bike.GlobalTransform = new Transform3D(Planet.PlanetHeightfield.UprightBasis(up, facing), place);
    }

    // Every weapon the player carries, topped up.
    private void Resupply(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Weapons.WeaponBase weapon)
            {
                weapon.Resupply(1.0f);
            }
            Resupply(child);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_result == null || _player == null || !IsInstanceValid(_player))
        {
            return;
        }
        _playing += delta;
        _result.PlayedSeconds = _playing;
        if (_infiniteAmmo && (_sinceResupply += (float)delta) >= ResupplyEverySeconds)
        {
            _sinceResupply = 0.0f;
            Resupply(_player);
        }
        Vector3 at = _player.GlobalPosition;

        Planet.PlanetHeightfield heights = Builder.Contract?.Planet?.Heights;
        if (heights != null)
        {
            bool onGround = _player.IsOnFloor()
                && at.Length() - (heights.Radius + heights.SampleHeight(at.Normalized())) < GroundMarginM;
            if (onGround && !_wasOnGround)
            {
                _result.GroundTouches++;
            }
            _wasOnGround = onGround;
        }
        if (_result.SecondsToHeart < 0.0 && Builder.Placer != null)
        {
            Vector3 heart = Builder.Placer.Heart;
            if ((at - (heart * at.Length())).Slide(heart).Length() < HeartReachM)
            {
                _result.SecondsToHeart = _playing;
            }
        }

        _sinceWrite += (float)delta;
        if (_sinceWrite >= WriteEverySeconds)
        {
            _sinceWrite = 0.0f;
            Write();
        }
    }

    private void Write()
    {
        if (_result == null)
        {
            return;
        }
        if (Pods != null)
        {
            _result.Pods = Pods.Count;
            _result.PodsDestroyed = Pods.Destroyed;
            _result.KilledOnPods = Pods.Killed;
            _result.Woke = Pods.Colony != PodField.ColonyState.Asleep;
        }
        if (Mission != null)
        {
            _result.PicklesStarting = Mission.Starting;
            _result.PicklesLeft = Mission.Alive;
            _result.Outcome = Mission.Current.ToString().ToLowerInvariant();
            _result.Bonus = Mission.Bonus;
        }
        if (_player != null && IsInstanceValid(_player) && _player.Health != null)
        {
            _result.TimesPickled = _player.Health.TimesPickled;
        }
        NestPlay.WriteResult(_result);
    }
}
