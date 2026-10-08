using Godot;
using DillControl.Debug;

namespace DillControl.Levels;

/// <summary>
/// Root of every level. The tree below it is fixed for the project's lifetime:
///
///   Level (this)
///     Geometry    -- static world collision and its meshes
///     Water       -- water surfaces and their volumes
///     Foliage     -- vegetation
///     Props       -- interactive and set-dressing objects
///     Lighting    -- DirectionalLight3D, WorldEnvironment, local lights
///     Audio       -- ambience emitters and reverb areas
///     Spawns      -- player and encounter markers
///     Navigation  -- navigation regions
///
/// Every branch exists in every level, even empty. New content fills branches;
/// it never rebuilds the hierarchy.
///
/// This node instances the player at the spawn marker, so the player scene is
/// never saved into a level and no level has to know a player node path. Until
/// the player is ported (M1) a level may leave <see cref="PlayerScene"/> empty
/// and look through a camera of its own.
///
/// It also adds the perf instruments, so every level can be recorded with
/// <c>U</c> and benched with <c>bench.gd</c> without wiring anything.
/// </summary>
[GlobalClass]
public partial class LevelRoot : Node3D
{
    [ExportGroup("Spawning")]
    [Export]
    public PackedScene PlayerScene { get; set; }

    [Export]
    public Marker3D PlayerSpawn { get; set; }

    /// <summary>The instanced player, for anything in the level that needs it.</summary>
    public Node3D Player { get; private set; }

    /// <summary>Local signal: emitted once the player exists in the tree.</summary>
    [Signal]
    public delegate void PlayerSpawnedEventHandler(Node3D player);

    public override void _Ready()
    {
        AddChild(new PerfDriver { Name = "PerfDriver" });
        SpawnPlayer();
    }

    private void SpawnPlayer()
    {
        if (PlayerScene == null)
        {
            return;
        }

        if (PlayerSpawn == null)
        {
            GD.PushWarning($"LevelRoot '{Name}' has a PlayerScene but no PlayerSpawn; no player spawned.");
            return;
        }

        Player = PlayerScene.Instantiate<Node3D>();
        PlayerSpawn.AddChild(Player);
        Player.GlobalTransform = PlayerSpawn.GlobalTransform;
        EmitSignal(SignalName.PlayerSpawned, Player);
    }
}
