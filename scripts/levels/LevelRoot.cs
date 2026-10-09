using Godot;
using DillControl.Debug;
using DillControl.Player;
using DillControl.Voxel;
using DillControl.Weapons;

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
/// <c>U</c> and benched with <c>bench.gd</c> without wiring anything, and
/// fires a harmless rocket to pay for the first explosion up front.
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
        Core.ProcessPriority.Raise();
        Core.JitWarmup.Start();
        Core.JitWarmup.WarmGodotClasses();
        AddChild(new PerfDriver { Name = "PerfDriver" });
        AddChild(new FireField { Name = "FireField" });
        AddChild(new VoxelDebris { Name = "VoxelDebris" });
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
        WarmUpExplosionEffects();
    }

    /// <summary>
    /// Fires one real, harmless rocket the instant the player spawns, so
    /// whatever a first explosion costs is paid while the level is still
    /// settling rather than during the player's first real grenade or rocket.
    ///
    /// Measured in the lab, not guessed. A live rocket's very first
    /// detonation cost 50-60 ms of a single physics frame, and it was not the
    /// project's own code: what removed it was replaying the exact same shot --
    /// flight sweep, Blast queries, a real ExplosionEffect with its
    /// GPUParticles3D -- once before the real one. That points at a one-time
    /// engine or driver cost, most likely a shader or particle pipeline
    /// compiled on first use. Here it covers every explosive weapon in every
    /// level, because every level instances its player through this method.
    /// </summary>
    private void WarmUpExplosionEffects()
    {
        if (Player is not PlayerController player)
        {
            return;
        }

        PackedScene rocketScene = GD.Load<PackedScene>(WarmupRocketPath);
        PackedScene explosionScene = GD.Load<PackedScene>(WarmupExplosionPath);
        if (rocketScene == null || explosionScene == null)
        {
            return;
        }

        RocketStats warmupStats = new()
        {
            MuzzleSpeed = 40.0f,
            CruiseSpeed = 40.0f,
            Acceleration = 0.0f,
            GravityScale = 0.0f,
            MaxLifetime = 2.0f,
            DirectHitDamage = 0.0f,
            Damage = 0.0f,
            InnerRadius = 0.1f,
            BlastRadius = 0.5f,
            Impulse = 0.0f,
            DebrisImpulseScale = 0.0f,
        };

        Rocket warmup = rocketScene.Instantiate<Rocket>();
        warmup.ExplosionScene = explosionScene;
        warmup.SilentExplosion = true;
        AddChild(warmup);
        warmup.Launch(
            warmupStats,
            null,
            player.GlobalPosition + (player.GravityUp * 1.5f),
            -player.GravityUp,
            player.GravityUp,
            player.GetGravity(),
            default);
    }

    private const string WarmupRocketPath = "res://scenes/weapons/fx_rocket.tscn";
    private const string WarmupExplosionPath = "res://scenes/weapons/fx_explosion.tscn";
}
