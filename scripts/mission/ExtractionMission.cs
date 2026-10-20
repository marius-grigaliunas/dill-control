using Godot;
using DillControl.Core;
using DillControl.Horde;
using DillControl.Levels;
using DillControl.Player;
using DillControl.UI;
using DillControl.Voxel;

namespace DillControl.Mission;

/// <summary>
/// A drop-and-extract contract, on trial (decision 0026). The player comes
/// down in a pod at <see cref="LandingSite"/>; once enough of the horde is
/// dead and the jar's core is cracked, a pod is sent to one of the
/// <see cref="ExtractionSites"/>, and stepping into it ends the contract.
/// Every body that goes down is replaced by a clone landing the same way as
/// the first, until the company stops printing them.
///
/// Placed under Spawns. Sets the horde's tier before the horde builds, as
/// <see cref="CompoundPlacer"/> does on the contract level.
/// </summary>
[GlobalClass]
public partial class ExtractionMission : Node
{
    [ExportGroup("Data")]
    [Export]
    public ExtractionRules Rules { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export]
    public LevelRoot Level { get; set; }

    [Export]
    public HordeDirector Horde { get; set; }

    /// <summary>The jar's destruction.</summary>
    [Export]
    public VoxelBody Jar { get; set; }

    /// <summary>The jar model's <c>Core</c> part: cracked once enough of it is gone.</summary>
    [Export]
    public Node3D JarCore { get; set; }

    /// <summary>Where every pod with a body in it comes down.</summary>
    [Export]
    public Marker3D LandingSite { get; set; }

    /// <summary>Where the pod home may come down. One far enough from the player is picked.</summary>
    [Export]
    public Godot.Collections.Array<Marker3D> ExtractionSites { get; set; } = new();

    [Export]
    public PackedScene PodScene { get; set; }

    public enum Phase { Landing, Fighting, Down, Leaving, Won, Lost }

    public Phase Current { get; private set; } = Phase.Landing;

    private PlayerController _player;
    private DropPod _arrival;
    private DropPod _extraction;
    private MissionHud _hud;
    private int _bodies;
    private float _time;
    private float _phaseTime;
    private float _extractionTimer = -1.0f;
    private bool _jobDone;

    public override void _Ready()
    {
        if (Rules == null || Level == null || Horde == null || PodScene == null || LandingSite == null)
        {
            GD.PushError($"ExtractionMission '{Name}' needs Rules, Level, Horde, PodScene and LandingSite; no contract.");
            SetProcess(false);
            return;
        }

        ApplyTier();
        Level.PlayerSpawned += OnPlayerSpawned;

        var layer = new CanvasLayer { Name = "MissionHud", Layer = 5 };
        _hud = new MissionHud { Name = "Hud" };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hud.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(_hud);
        AddChild(layer);
    }

    public override void _ExitTree()
    {
        if (Level != null)
        {
            Level.PlayerSpawned -= OnPlayerSpawned;
        }
    }

    private void ApplyTier()
    {
        HordeTier tier = Rules.Tier;
        if (tier == null || Horde.Stats == null)
        {
            return;
        }
        HordeStats stats = (HordeStats)Horde.Stats.Duplicate();
        stats.AgentCount = tier.AgentCount;
        stats.RespawnKilledAgents = tier.RespawnKilledAgents;
        stats.AggroRange = tier.AggroRange;
        stats.LeashRange = tier.LeashRange;
        stats.RoamRadius = tier.RoamRadius;
        stats.GarrisonSeedRadius = Mathf.Max(tier.RoamRadius, 10.0f);
        Horde.Stats = stats;
        if (tier.Composition != null)
        {
            Horde.Composition = tier.Composition;
        }
    }

    // ---------------- Bodies ----------------

    /// <summary>Every body, the first and each clone, comes down in a pod at the landing site.</summary>
    private void OnPlayerSpawned(Node3D player)
    {
        _player = player as PlayerController;
        _bodies++;
        if (_player == null)
        {
            return;
        }

        // The last body's pod stays where it landed, something to stand on
        // and hook, until the next one needs the spot.
        if (_arrival != null && IsInstanceValid(_arrival))
        {
            _arrival.QueueFree();
        }
        DropPod pod = MakePod();
        _arrival = pod;
        pod.Landed += () => SetPhase(Phase.Fighting);
        // The marker itself, not a ray: on the first frame the ground's
        // colliders are not in the broadphase yet. Stand it on the ground in
        // the editor (the terrain's button).
        pod.Drop(LandingSite.GlobalTransform, _player, Rules);
        SetPhase(Phase.Landing);
    }

    private void BodyDown()
    {
        _player?.SetStowed(true);
        SetPhase(_bodies >= Rules.Clones ? Phase.Lost : Phase.Down);
    }

    // ---------------- The frame ----------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _phaseTime += dt;
        if (Current is not (Phase.Won or Phase.Lost))
        {
            _time += dt;
        }

        int target = Mathf.CeilToInt((Horde.Stats?.AgentCount ?? 0) * Rules.KillShare);
        int killed = Horde.IsBuilt ? Horde.Manager.BaseLost : 0;
        VoxelPart core = Jar?.PartOf(JarCore);
        float coreLeft = core?.Health01 ?? 1.0f;
        bool cracked = core != null && coreLeft <= 1.0f - Rules.CoreShare;

        switch (Current)
        {
            case Phase.Fighting:
                if (_player != null && IsInstanceValid(_player) && (_player.Health?.IsPickled ?? false))
                {
                    BodyDown();
                }
                break;
            case Phase.Down:
                if (_phaseTime >= Rules.PrintSeconds)
                {
                    Level.RespawnPlayer();
                }
                break;
            case Phase.Won:
            case Phase.Lost:
                if (Input.IsActionJustPressed("reload"))
                {
                    GetTree().ReloadCurrentScene();
                }
                break;
        }

        if (!_jobDone && killed >= target && cracked && Current != Phase.Lost)
        {
            _jobDone = true;
            _extractionTimer = Rules.ExtractionDelay;
        }
        if (_extractionTimer >= 0.0f && Current != Phase.Lost)
        {
            _extractionTimer -= dt;
            if (_extractionTimer < 0.0f)
            {
                SendExtraction();
            }
        }

        _hud.Show(new MissionHud.Readout
        {
            Phase = Current,
            PhaseTime = _phaseTime,
            Time = _time,
            Killed = killed,
            Target = target,
            Alive = Horde.IsBuilt ? Horde.Manager.AgentCount : 0,
            CoreLeft = coreLeft,
            CoreTarget = 1.0f - Rules.CoreShare,
            Cracked = cracked,
            ClonesLeft = Mathf.Max(0, Rules.Clones - _bodies),
            Clones = Rules.Clones,
            Body = _bodies,
            PrintSeconds = Rules.PrintSeconds,
            JobDone = _jobDone,
            ExtractionIn = _extractionTimer,
            ExtractionOpen = _extraction != null && IsInstanceValid(_extraction) && _extraction.IsOpen,
            ExtractionAt = _extraction != null && IsInstanceValid(_extraction) ? _extraction.GlobalPosition : null,
            JarAt = Jar?.Model?.GlobalPosition,
            PlayerAt = _player != null && IsInstanceValid(_player) ? _player.GlobalPosition : null,
        });
    }

    private void SetPhase(Phase phase)
    {
        Current = phase;
        _phaseTime = 0.0f;
        GD.Print(System.FormattableString.Invariant($"[mission] {phase} at {_time:F0} s, body {_bodies} of {Rules.Clones}"));
    }

    // ---------------- Bench ----------------

    /// <summary>For <c>bench.gd</c>'s <c>extract</c>: the job counts as done now.</summary>
    public void BenchJobDone()
    {
        _jobDone = true;
        _extractionTimer = Rules.ExtractionDelay;
    }

    /// <summary>For <c>bench.gd</c>'s <c>extract</c>: the player stands in the pod's hatch.</summary>
    public void BenchToPod()
    {
        if (_extraction != null && IsInstanceValid(_extraction) && _player != null && IsInstanceValid(_player))
        {
            _player.GlobalPosition = _extraction.GlobalPosition + new Vector3(0.0f, 0.5f, 2.6f);
        }
    }

    // ---------------- Extraction ----------------

    private void SendExtraction()
    {
        _extractionTimer = -1.0f;
        Vector3 from = _player != null && IsInstanceValid(_player) ? _player.GlobalPosition : LandingSite.GlobalPosition;
        _extraction = MakePod();
        _extraction.Landed += () => _extraction.Open();
        _extraction.Boarded += player =>
        {
            SetPhase(Phase.Leaving);
            _extraction.LiftOff(player);
        };
        _extraction.Gone += () => SetPhase(Phase.Won);
        // Facing the way the player came from, so they run into the hatch.
        Vector3 site = GroundUnder(PickExtractionSite(from));
        Vector3 back = new Vector3(site.X - from.X, 0.0f, site.Z - from.Z);
        Basis facing = back.LengthSquared() > 1.0f ? Basis.LookingAt(-back.Normalized(), Vector3.Up) : Basis.Identity;
        _extraction.Drop(new Transform3D(facing, site), null, Rules);
    }

    /// <summary>A site at least the minimum distance from the player, rolled; the furthest if none is.</summary>
    private Vector3 PickExtractionSite(Vector3 from)
    {
        var far = new System.Collections.Generic.List<Vector3>();
        Vector3 furthest = LandingSite.GlobalPosition;
        float best = -1.0f;
        foreach (Marker3D site in ExtractionSites)
        {
            if (site == null)
            {
                continue;
            }
            Vector3 at = site.GlobalPosition;
            float d = new Vector2(at.X - from.X, at.Z - from.Z).Length();
            if (d >= Rules.ExtractionMinDistance)
            {
                far.Add(at);
            }
            if (d > best)
            {
                best = d;
                furthest = at;
            }
        }
        if (far.Count == 0)
        {
            return furthest;
        }
        SeededRandom roll = SeededRandom.Stream((ulong)Rules.Seed, "extraction");
        return far[roll.RangeInt(0, far.Count - 1)];
    }

    private DropPod MakePod()
    {
        DropPod pod = PodScene.Instantiate<DropPod>();
        Level.AddChild(pod);
        return pod;
    }

    /// <summary>Whatever is solid under a point: ground, a roof, a tree.</summary>
    private Vector3 GroundUnder(Vector3 point)
    {
        var query = PhysicsRayQueryParameters3D.Create(
            point + (Vector3.Up * 800.0f), point - (Vector3.Up * 800.0f), GameLayers.Mask(GameLayers.World));
        Godot.Collections.Dictionary hit = GetViewport().World3D.DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? hit["position"].AsVector3() : point;
    }
}
