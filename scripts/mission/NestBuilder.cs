using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using DillControl.Foliage;
using DillControl.Horde;
using DillControl.Levels;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// Builds a level with a vine nest, behind its loading screen
/// (docs/handoffs/vine-tool.md, step 6): the contract's planet by
/// <see cref="Contract"/>, and the nest on it. No compound: the nest is the
/// place.
///
/// - Once the contract is known, the nest is planned and its vines grown and
///   built on all cores (<see cref="NestLayout"/>) while the planet generates:
///   which vines and how many pods do not depend on the ground.
/// - The planet asks <see cref="Placer"/> where they go and levels their pads.
/// - Once the planet stands, the vines are planted a few a frame, and the
///   level is let in when the frames have settled.
/// - A vine stands on its own foot, not on the ground: when a crater takes
///   the ground from under its foot (<see cref="Kit"/>'s UnrootMarginM), the
///   foot goes too and it falls (<see cref="PlantFlex.Unroot"/>).
///
/// Its population and seed are the contract's, unless the command line says
/// <c>pop=N</c>, <c>nest=SEED</c>; <c>horde=0</c> builds it without the horde,
/// as does a level with none. The horde has no pickles of its own: every one
/// starts on a pod (<see cref="PodField"/>) and drops into it when the colony
/// wakes (docs/decisions/0034).
/// The load is timed by stage, and the memory it took printed: the numbers
/// that decide docs/decisions/0033.
/// </summary>
[GlobalClass]
public partial class NestBuilder : Node, ILevelBuilder
{
    [ExportGroup("Scene Wiring")]
    /// <summary>Builds the contract's planet.</summary>
    [Export] public SystemBuilder Contract { get; set; }
    [Export] public NestPlacer Placer { get; set; }
    /// <summary>Where the vines go: the level's Foliage.</summary>
    [Export] public Node3D Vines { get; set; }
    [Export] public HordeDirector Horde { get; set; }
    /// <summary>Every vine's pods, added as it is planted.</summary>
    [Export] public PodField Pods { get; set; }

    [Export] public NestKit Kit { get; set; }

    [ExportGroup("Planting")]
    /// <summary>Milliseconds a frame spent making vines' nodes. Behind the
    /// loading screen: long frames only slow the bar.</summary>
    [Export(PropertyHint.Range, "1,200,1,suffix:ms")] public float PlantBudgetMs { get; set; } = 40.0f;
    [Export(PropertyHint.Range, "10,50,0.5,suffix:ms")] public float SettledFrameMs { get; set; } = 20.0f;
    [Export(PropertyHint.Range, "1,240,1")] public int SettledFrames { get; set; } = 30;
    [Export(PropertyHint.Range, "1,60,0.5,suffix:s")] public float SettleTimeout { get; set; } = 10.0f;

    public bool LevelReady { get; private set; }
    public float Progress { get; private set; }
    public string Stage { get; private set; } = "";
    public string Title { get; private set; } = "NEST";
    public string Details { get; private set; } = "";

    public NestLayout Layout { get; private set; }
    public int Population { get; private set; }
    public ulong Seed { get; private set; }
    public bool WithHorde { get; private set; } = true;
    public int Planted { get; private set; }
    /// <summary>The pods the plan grew, for the checks.</summary>
    public int PlannedPods => Layout?.Pods ?? 0;

    /// <summary>Seconds per stage and in all, for the bench.</summary>
    public double PlanSeconds { get; private set; }
    public double ModelSeconds { get; private set; }
    public double PlantSeconds { get; private set; }
    /// <summary>Of planting: making the nodes, and their entering the tree (their _Ready).</summary>
    public double MakeSeconds { get; private set; }
    public double EnterSeconds { get; private set; }
    public double LoadSeconds { get; private set; }

    private Task<NestLayout> _plan;
    private Task _models;
    private VineBakeStats _stats;
    private readonly Stopwatch _clock = new();
    private double _plantStarted;
    private int _quiet;
    private float _settling;
    private long _managedBefore;
    private int _physicsSteps = -1;
    private int _plantFrames;
    private double _plantProcessMs, _plantPhysicsMs;
    private ulong _nativeBefore;
    // Each planted vine's spine and where its foot stands: a crater under it unroots it.
    private readonly List<(PlantFlex Flex, Vector3 Foot)> _rooted = new();

    public override void _Ready()
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument == "horde=0")
            {
                WithHorde = false;
            }
        }
        if (!WithHorde && Horde != null)
        {
            // Before the system waits for it to route.
            if (Contract != null)
                Contract.Horde = null;
            Horde.QueueFree();
            Horde = null;
        }
        WithHorde = Horde != null;
        if (Horde?.Stats != null)
        {
            // A copy, as a compound's tier makes one: none of its own, room
            // for the whole nest, and every one it gets hunts.
            var stats = (HordeStats)Horde.Stats.Duplicate();
            stats.AgentCount = 0;
            stats.AgentCapacity = Math.Max(stats.AgentCapacity, (Kit?.PopulationClamp.Y ?? 25000) + 1000);
            stats.RespawnKilledAgents = false;
            stats.AggroRange = 0.0f;
            Horde.Stats = stats;
        }
        _clock.Start();
        _managedBefore = GC.GetTotalMemory(false);
        _nativeBefore = OS.GetStaticMemoryUsage();
    }

    public override void _Process(double delta)
    {
        if (Contract == null || Kit == null)
        {
            GD.PushError($"NestBuilder '{Name}' needs a SystemBuilder and a NestKit; the level cannot load.");
            LevelReady = true;
            SetProcess(false);
            return;
        }
        if (_plan == null)
        {
            if (Contract.Contract == null)
            {
                Mirror(0.0f);
                return;
            }
            Start();
        }

        if (!Contract.LevelReady || !_models.IsCompleted)
        {
            Mirror(0.75f);
            if (_plan.IsCompleted && Layout == null)
            {
                Layout = _plan.Result;
                PlanSeconds = Layout.GrowSeconds;
            }
            return;
        }

        if (_models.IsFaulted)
        {
            GD.PushError($"NestBuilder: building the vines failed: {_models.Exception?.GetBaseException().Message}");
            LevelReady = true;
            SetProcess(false);
            return;
        }

        if (Planted < Layout.Vines.Count)
        {
            Plant();
            return;
        }

        _quiet = delta * 1000.0 <= SettledFrameMs ? _quiet + 1 : 0;
        _settling += (float)delta;
        Progress = 0.95f + (0.05f * Mathf.Min(1.0f, _quiet / (float)SettledFrames));
        Stage = "Warming up";
        if (_quiet >= SettledFrames || _settling > SettleTimeout)
        {
            LoadSeconds = _clock.Elapsed.TotalSeconds;
            LevelReady = true;
            if (_physicsSteps > 0)
            {
                Engine.MaxPhysicsStepsPerFrame = _physicsSteps;
            }
            SetProcess(false);
            Report();
        }
    }

    private void Start()
    {
        _stats = Kit.BakeOrDefault();
        int population = Contract.Contract.Tier?.AgentCount ?? Kit.DefaultPopulation;
        Seed = Contract.Contract.Seed;
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("pop=") && int.TryParse(argument.Substring(4), out int pop))
                population = pop;
            if (argument.StartsWith("nest=") && ulong.TryParse(argument.Substring(5), out ulong seed))
                Seed = seed;
        }
        Population = Math.Clamp(population, Kit.PopulationClamp.X, Kit.PopulationClamp.Y);

        // Read on this thread; everything after runs on the others.
        VineGrower.Context context = VineGrower.Context.For(_stats, Kit.Kinds);
        NestKit kit = Kit;
        ulong nestSeed = Seed;
        int people = Population;
        VineBakeStats stats = _stats;
        _plan = Task.Run(() => NestLayout.Plan(kit, context, nestSeed, people));
        _models = _plan.ContinueWith(plan => plan.Result.BuildModels(stats), TaskContinuationOptions.OnlyOnRanToCompletion);
        if (Placer != null)
        {
            Placer.Layout = () => _plan.Result;
        }
        Title = $"NEST {Seed}";
        Details = string.Create(CultureInfo.InvariantCulture,
            $"{Kit.DisplayName}: {Population:N0} enemies {(WithHorde ? "" : "\nWithout the horde")}");
    }

    // The system's stages, then ours.
    private void Mirror(float share)
    {
        Stage = _plan != null && !_plan.IsCompleted ? "Growing the vines"
            : _models != null && !_models.IsCompleted ? "Building the vines"
            : Contract.Stage;
        Progress = Contract.Progress * share;
    }

    private void Plant()
    {
        if (Planted == 0)
        {
            _plantStarted = _clock.Elapsed.TotalSeconds;
            ModelSeconds = Layout.ModelSeconds;
            // Nobody is in the world yet: a long frame need not be caught up
            // with eight physics ticks, each stepping every vine planted so far.
            _physicsSteps = Engine.MaxPhysicsStepsPerFrame;
            Engine.MaxPhysicsStepsPerFrame = 1;
            Pods?.Reserve(Layout.Pods);
        }
        _plantFrames++;
        _plantProcessMs += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        _plantPhysicsMs += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        var frame = Stopwatch.StartNew();
        Transform3D[] places = Placer?.Places;
        while (Planted < Layout.Vines.Count && frame.Elapsed.TotalMilliseconds < PlantBudgetMs)
        {
            NestLayout.Vine vine = Layout.Vines[Planted];
            // Drawn far off by VoxelBody's coarse copies: no LODs to make here.
            long t0 = Stopwatch.GetTimestamp();
            Node3D node = VineFactory.MakeNodes(vine.Data, _stats, lods: false);
            long t1 = Stopwatch.GetTimestamp();
            if (node != null && places != null && Planted < places.Length)
            {
                node.Name = vine.Data.Name;
                // Placed before it enters: its spine and pads read where it stands.
                node.Transform = Vines.GlobalTransform.AffineInverse() * places[Planted];
                Vines.AddChild(node);
                _rooted.Add((node.GetNodeOrNull<PlantFlex>("Flex"), places[Planted].Origin));
                Pods?.AddVine(node, vine.Data.Pods);
            }
            MakeSeconds += (t1 - t0) / (double)Stopwatch.Frequency;
            EnterSeconds += (Stopwatch.GetTimestamp() - t1) / (double)Stopwatch.Frequency;
            vine.Data = null;
            Planted++;
        }
        Stage = $"Planting the vines ({Planted} of {Layout.Vines.Count})";
        Progress = 0.75f + (0.2f * Planted / Math.Max(1, Layout.Vines.Count));
        if (Planted >= Layout.Vines.Count)
        {
            PlantSeconds = _clock.Elapsed.TotalSeconds - _plantStarted;
            if (Contract.Planet != null)
            {
                Contract.Planet.TerrainDeformed += OnTerrainDeformed;
            }
        }
    }

    public override void _ExitTree()
    {
        if (Contract?.Planet != null && IsInstanceValid(Contract.Planet))
        {
            Contract.Planet.TerrainDeformed -= OnTerrainDeformed;
        }
    }

    // A vine is held up by its own foot, not by the ground: when the ground
    // under the foot is blown out from under it, the foot goes too and it falls.
    private void OnTerrainDeformed(Vector3 centre, float radius)
    {
        Planet.PlanetHeightfield heights = Contract.Planet.Heights;
        for (int i = _rooted.Count - 1; i >= 0; i--)
        {
            (PlantFlex flex, Vector3 foot) = _rooted[i];
            if (flex == null || !IsInstanceValid(flex))
            {
                _rooted.RemoveAt(i);
                continue;
            }
            if (foot.DistanceTo(centre) > radius + Kit.PadRadiusM)
            {
                continue;
            }
            float ground = heights.Radius + heights.SampleHeight(foot.Normalized());
            if (ground < foot.Length() - Kit.UnrootMarginM)
            {
                flex.Unroot();
                _rooted.RemoveAt(i);
            }
        }
    }

    private void Report()
    {
        long managed = GC.GetTotalMemory(true) - _managedBefore;
        ulong native = OS.GetStaticMemoryUsage() - _nativeBefore;
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"[nest] seed {Seed}: {Layout.Vines.Count} vines, {Layout.Pods:N0} pods for {Population:N0} pickles{(Layout.Short > 0 ? $" ({Layout.Short:N0} short)" : "")}, "
            + $"{Layout.RadiusM:F0} m out, {Layout.Vines.Count} of {Layout.Slots} slots, {Layout.Avenues.Length} avenues{(Layout.Shifted ? ", shifted to high-pod kinds" : "")}{(WithHorde ? "" : ", no horde")}\n"
            + $"[nest] kinds: {Layout.KindSummary()}\n"
            + $"[nest] load {LoadSeconds:F1} s: grow {PlanSeconds:F1} s, models {ModelSeconds:F1} s (both on all cores, under the planet), planting {PlantSeconds:F1} s (making {MakeSeconds:F1} s, entering {EnterSeconds:F1} s, {_plantFrames} frames at {_plantProcessMs / Math.Max(1, _plantFrames):F0} ms process and {_plantPhysicsMs / Math.Max(1, _plantFrames):F0} ms physics); "
            + $"memory managed {managed / 1048576.0:F0} MB, engine {native / 1048576.0:F0} MB"));
        Pods?.PrintBenchStatus();
    }

    /// <summary>For bench.gd: a point <paramref name="up"/> metres over the nest's heart.</summary>
    public Vector3 BenchHeart(string up)
    {
        Vector3 heart = Placer?.Heart ?? Vector3.Up;
        float metres = float.Parse(up, CultureInfo.InvariantCulture);
        Planet.PlanetHeightfield heights = Contract?.Planet?.Heights;
        return (heights != null ? heights.SurfacePoint(heart) : heart * 1260.0f) + (heart * metres);
    }

    /// <summary>For bench.gd: a crater under the heart's vine, 14 m in radius and 8 m deep.</summary>
    public void BenchCraterHeart()
    {
        Planet.PlanetHeightfield heights = Contract?.Planet?.Heights;
        if (heights != null)
        {
            Contract.Planet.Crater(heights.SurfacePoint(Placer?.Heart ?? Vector3.Up), 14.0f, 8.0f);
        }
    }

    /// <summary>For bench.gd: the same, 25 m to one side of the heart's stalk:
    /// among the leaves, not inside the vine.</summary>
    public Vector3 BenchBesideHeart(string up)
    {
        Vector3 over = BenchHeart(up);
        Vector3 heart = Placer?.Heart ?? Vector3.Up;
        Vector3 side = heart.Cross(Mathf.Abs(heart.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
        return over + (side * 25.0f);
    }
}
