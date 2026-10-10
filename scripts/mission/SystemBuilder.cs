using Godot;
using DillControl.Horde;
using DillControl.Levels;
using DillControl.Planet;

namespace DillControl.Mission;

/// <summary>
/// Builds the contract level behind its loading screen, in stages over
/// frames: which contract, the planet's heights on a background task, the
/// compound and the planet's meshes, the system round it, the horde's
/// routing, then a wait for the frames to settle before the player is let in.
///
/// The contract comes from, in order: a bench's seed (a meta on the tree's
/// root), a <c>seed=N</c> argument after <c>--</c>, the session's accepted
/// contract, and the fallback seed -- so F6 and the bench work without the hub.
/// </summary>
[GlobalClass]
public partial class SystemBuilder : Node, ILevelBuilder
{
    [ExportGroup("Scene Wiring")]
    [Export]
    public PlanetTerrain Planet { get; set; }

    [Export]
    public CompoundPlacer Placer { get; set; }

    [Export]
    public SolarSystem Solar { get; set; }

    [Export]
    public HordeDirector Horde { get; set; }

    [ExportGroup("Without a session")]
    /// <summary>The pools a contract is made from when no session is running (an editor F6).</summary>
    [Export]
    public ContractPools FallbackPools { get; set; }

    [Export]
    public long FallbackSeed { get; set; } = 1;

    [ExportGroup("Settling")]
    /// <summary>Frames in a row under this before the player is let in.</summary>
    [Export(PropertyHint.Range, "10,50,0.5,suffix:ms")]
    public float SettledFrameMs { get; set; } = 20.0f;

    [Export(PropertyHint.Range, "1,240,1")]
    public int SettledFrames { get; set; } = 30;

    /// <summary>Let in anyway after this long settling, so a slow machine is never stuck on the screen.</summary>
    [Export(PropertyHint.Range, "1,30,0.5,suffix:s")]
    public float SettleTimeout { get; set; } = 6.0f;

    /// <summary>The contract being built.</summary>
    public ContractSpec Contract { get; private set; }

    public bool LevelReady { get; private set; }
    public float Progress { get; private set; }
    public string Stage { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Details { get; private set; } = "";

    /// <summary>Seconds from the level's first frame to the player let in, for the bench.</summary>
    public float LoadSeconds { get; private set; }

    private enum Step { Start, Generating, Finish, Routing, Settling, Done }

    private Step _step = Step.Start;
    private float _elapsed;
    private float _stepElapsed;
    private int _quietFrames;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _elapsed += dt;
        _stepElapsed += dt;

        switch (_step)
        {
            case Step.Start:
                Start();
                break;

            case Step.Generating:
                if (!Planet.IsGenerating)
                {
                    // One frame with the new words up before the long one.
                    Next(Step.Finish, "Laying out the nest", 0.45f);
                }
                break;

            case Step.Finish:
                Planet.FinishBuild();
                Next(Step.Routing, "Routing the horde", 0.7f);
                break;

            case Step.Routing:
                if (Horde == null || Horde.IsBuilt)
                {
                    Next(Step.Settling, "Warming up", 0.85f);
                }
                break;

            case Step.Settling:
                _quietFrames = dt * 1000.0f <= SettledFrameMs ? _quietFrames + 1 : 0;
                Progress = 0.85f + (0.15f * Mathf.Min(1.0f, _quietFrames / (float)SettledFrames));
                if ((_quietFrames >= SettledFrames && Planet.PendingBuildCount == 0) || _stepElapsed > SettleTimeout)
                {
                    LoadSeconds = _elapsed;
                    Next(Step.Done, "Go", 1.0f);
                    LevelReady = true;
                    GD.Print(System.FormattableString.Invariant(
                        $"[contract] {Contract?.Summary}: in after {LoadSeconds:F1} s{(_stepElapsed > SettleTimeout ? " (settling timed out)" : "")}"));
                    SetProcess(false);
                }
                break;
        }
    }

    private void Next(Step step, string stage, float progress)
    {
        _step = step;
        _stepElapsed = 0.0f;
        _quietFrames = 0;
        Stage = stage;
        Progress = progress;
    }

    private void Start()
    {
        if (Planet == null)
        {
            GD.PushError($"SystemBuilder '{Name}' has no planet; the level cannot load.");
            LevelReady = true;
            SetProcess(false);
            return;
        }

        Contract = ResolveContract();
        if (Contract?.Planet == null)
        {
            GD.PushError($"SystemBuilder '{Name}': no contract could be made (no pools?); the level cannot load.");
            LevelReady = true;
            SetProcess(false);
            return;
        }

        if (Placer != null)
        {
            Placer.Kit = Contract.Kit ?? Placer.Kit;
            Placer.Tier = Contract.Tier ?? Placer.Tier;
        }

        Title = $"CONTRACT {Contract.Designation}";
        Details = System.FormattableString.Invariant(
            $"A {Contract.SizeClass} {Contract.Archetype?.DisplayName.ToLowerInvariant() ?? "planet"}, {Contract.Biome?.DisplayName ?? "unknown"}.\n{Contract.Tier?.DisplayName ?? "Infested"}: about {Contract.Tier?.AgentCount ?? 0:N0} pickles round a {Contract.Kit?.DisplayName.ToLowerInvariant() ?? "nest"}.");

        Planet.BeginBuild(Contract.Planet, Contract.Biome, Contract.Seed);
        Next(Step.Generating, "Charting the planet", 0.15f);
    }

    private ContractSpec ResolveContract()
    {
        ContractPools pools = Session.Instance?.Pools ?? FallbackPools;

        Window root = GetTree().Root;
        if (root.HasMeta(PlanetTerrain.BenchSeedMeta) && pools != null)
        {
            return ContractGenerator.Generate(pools, (ulong)root.GetMeta(PlanetTerrain.BenchSeedMeta).AsInt64());
        }

        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("seed=") && ulong.TryParse(argument.Substring(5), out ulong seed) && pools != null)
            {
                return ContractGenerator.Generate(pools, seed);
            }
        }

        if (Session.Instance?.Accepted != null)
        {
            return Session.Instance.Accepted;
        }

        return pools != null ? ContractGenerator.Generate(pools, (ulong)FallbackSeed) : null;
    }
}
