using Godot;
using DillControl.Audio;
using DillControl.Core;
using DillControl.Enemies;
using DillControl.Levels;
using DillControl.Player;

namespace DillControl.Horde;

/// <summary>
/// Puts a horde in a level: builds the ground and the flow field it walks,
/// points them at the player, and turns what the crowd does to the player
/// into hits on the player.
///
/// The one place that knows both a horde and a player exist. The
/// <see cref="HordeManager"/> knows neither -- it raises "an agent struck" and
/// "an agent wants to throw" as delegates and stops there -- and the player
/// knows nothing of crowds. This is the lab's <c>SpikeDirector</c> reduced to
/// the horde's share of it, with its sliders moved to <see cref="HordeStats"/>.
///
/// Placed in the level under <c>Spawns</c>, at the middle of the ground it
/// covers. Its position is also the garrison: where the horde stands when it
/// is not hunting.
/// </summary>
[GlobalClass]
public partial class HordeDirector : Node3D
{
    [ExportGroup("Data")]
    [Export]
    public HordeStats Stats { get; set; }

    [Export]
    public HordeComposition Composition { get; set; }

    [ExportGroup("Scene Wiring")]
    /// <summary>The level, for the player it spawns. The player is never saved into a level.</summary>
    [Export]
    public LevelRoot Level { get; set; }

    [ExportGroup("Ground")]
    /// <summary>
    /// What makes the ground, when the level has one: the planet. It must
    /// implement <see cref="IHordeGroundProvider"/>. Empty lays out a square
    /// of flats round this node instead.
    /// </summary>
    [Export]
    public Node GroundProvider { get; set; }

    /// <summary>Side of the square of flats the horde can stand on, centred here, when there is no provider.</summary>
    [Export(PropertyHint.Range, "50,4000,10,suffix:m")]
    public float GroundSize { get; set; } = 1200.0f;

    public HordeManager Manager { get; private set; }

    public IHordeGround Ground { get; private set; }

    public FlowField Flow { get; private set; }

    /// <summary>True once the ground, the flow field and the crowd exist.</summary>
    public bool IsBuilt => _built;

    /// <summary>Flow cells something authored stands in.</summary>
    public int BlockedCells { get; private set; }

    /// <summary>How many strikes reached the player, for the bench.</summary>
    public int StruckCount { get; private set; }

    /// <summary>How many cornichons were thrown, for the bench.</summary>
    public int ThrownCount { get; private set; }

    /// <summary>The spitters' brine in flight.</summary>
    public ShotPool Brine { get; private set; }

    /// <summary>Brine reads as brine: a sour yellow-green, over-bright so a shot is seen coming.</summary>
    private static readonly Color BrineColour = new(0.72f, 0.82f, 0.2f);

    private PlayerController _player;
    private bool _built;
    private float _flowTimer;
    private float _strikeTokens;
    private float _allyStrikeTokens;
    private readonly System.Collections.Generic.List<IHordeTarget> _allyTargets = new();
    private readonly System.Collections.Generic.List<HordeManager.AllyTarget> _allyData = new();
    private float _shotTokens;
    private int _targetCell = -1;
    private ulong _aimRandom = 0xA0761D6478BD642FUL;

    private int _sectionFlow;

    public override void _Ready()
    {
        if (Stats == null || Composition == null)
        {
            GD.PushError($"HordeDirector '{Name}' needs HordeStats and a HordeComposition; no horde.");
            SetProcess(false);
            SetPhysicsProcess(false);
            return;
        }

        _sectionFlow = FrameProfiler.Section("flow build [worker]", background: true);
        _sectionRestamp = FrameProfiler.Section("horde restamp");
        PerfLog.Counter("horde_agents", () => Manager?.AgentCount ?? 0);
        PerfLog.Counter("horde_drawn", () => Manager?.VisibleAgentCount ?? 0);
        PerfLog.Counter("horde_stepped", () => Manager?.SteppedCount ?? 0);
        PerfLog.Counter("horde_hunting", () => IsHunting ? 1 : 0);
        if (EventBus.Instance != null)
        {
            EventBus.Instance.ObstaclesChanged += OnObstaclesChanged;
        }

        Manager = new HordeManager { Name = "Manager" };
        AddChild(Manager);

        Brine = new ShotPool { Name = "Brine" };
        AddChild(Brine);
        Brine.Target = () => new ShotPool.ShotTarget
        {
            Valid = PlayerAvailable,
            Position = PlayerAvailable ? PlayerChest : Vector3.Zero,
        };
        Brine.Hit = OnBrineHit;
        Brine.ExtraCount = () => _allyTargets.Count;
        Brine.ExtraCentre = i => AllyChest(i);
        Brine.ExtraRadius = i => Mathf.Max(_allyData[i].Radius, _allyData[i].Height * 0.5f) * 0.8f;
        Brine.ExtraHit = OnBrineHitAlly;
        Brine.Struck = point => AudioDirector.PlayAt(Sfx.SpitSplat, point);

        // LevelRoot readies after its children, so this is connected before
        // the player exists.
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
        if (EventBus.Instance != null)
        {
            EventBus.Instance.ObstaclesChanged -= OnObstaclesChanged;
        }
    }

    private int _sectionRestamp;
    private readonly System.Collections.Generic.Queue<(Vector3 centre, float radius)> _restamps = new();

    /// <summary>
    /// Something that stood in the way has changed (a building shot through
    /// or brought down; see EventBus.ObstaclesChanged): its cells are asked
    /// again, one region a frame, before the next flow build reads them.
    /// Stamped once at build and never again, a fallen tower's footprint
    /// stayed a wall the pickles walked round.
    /// </summary>
    private void OnObstaclesChanged(Vector3 centre, float radius)
    {
        foreach ((Vector3 c, float r) in _restamps)
        {
            if (c.DistanceTo(centre) + radius <= r)
            {
                return;
            }
        }
        _restamps.Enqueue((centre, radius));
    }

    /// <summary>Loose pieces and rubble heaps are walked over, not round.</summary>
    private static bool Blocks(GodotObject collider) =>
        collider is not (Voxel.VoxelChunk or Voxel.RubbleCollider);

    private void Restamp()
    {
        if (_restamps.Count == 0 || Ground is not IObstacleGround ground)
        {
            return;
        }
        using FrameProfiler.Scope scope = FrameProfiler.Begin(_sectionRestamp);
        (Vector3 centre, float radius) = _restamps.Dequeue();
        int changed = ground.RestampObstacles(GetWorld3D().DirectSpaceState, Flow, FlatGround.ObstacleMask, centre,
            radius + Ground.CellSize, Blocks);
        if (changed > 0 && PerfLog.Recording)
        {
            PerfLog.Event(System.FormattableString.Invariant($"horde-restamp r={radius:F0}m changed={changed}"));
        }
    }

    private void OnPlayerSpawned(Node3D player)
    {
        _player = player as PlayerController;
    }

    /// <summary>
    /// The build waits for the first physics frame. Stamping obstacles asks
    /// physics about every cell, and on the frame the level enters the tree
    /// its static bodies are not yet in the broadphase to be found.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        IHordeGroundProvider provider = GroundProvider as IHordeGroundProvider;
        if (GroundProvider != null && provider == null)
        {
            GD.PushError($"HordeDirector '{Name}': GroundProvider '{GroundProvider.Name}' is not an IHordeGroundProvider; using flats.");
            GroundProvider = null;
        }
        if (provider != null && !provider.HordeGroundReady)
        {
            // The planet builds behind a loading stage; wait for it.
            return;
        }
        if (!_built)
        {
            Build(provider);
        }
        SetPhysicsProcess(false);
    }

    private void Build(IHordeGroundProvider provider)
    {
        ulong started = Time.GetTicksUsec();
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        // A horde with an aggro range starts at home: its first field leads
        // to the garrison, not to wherever the player arrived.
        _hunting = Stats.AggroRange <= 0.0f;

        if (provider != null)
        {
            Ground = provider.CreateHordeGround();
            Flow = new FlowField((IFlowGraph)Ground);
            BlockedCells = provider.PrepareFlow(Flow, space, GlobalPosition);
        }
        else
        {
            FlatGround flats = new(GlobalPosition, GroundSize, Stats.FlowCellSize);
            Ground = flats;
            Flow = new FlowField(flats);
            BlockedCells = flats.StampObstacles(space, Flow, FlatGround.ObstacleMask);
        }

        Ground.GroundBelow(HuntPoint(out _), out _targetCell);
        Flow.RequestBuild(_targetCell, false);

        Manager.Initialise(Ground, Flow, Composition, Stats.AgentCapacity, Stats.HitProxyCount,
                           Stats.WeakPointMultiplier);
        PushStats();

        Manager.SeedMinRadius = Seed(out Vector3 centre, out float radius);
        Manager.SetAgentCount(Stats.AgentCount, centre, radius);

        // The two behaviours that reach outside the horde. Assigned here so
        // the horde never learns that a player or a projectile exists.
        Manager.AgentStruck = OnAgentStruck;
        Manager.RangedRequested = OnRangedRequested;
        Manager.AllyStruck = OnAllyStruck;

        Brine.Build(Stats.ShotCapacity, BrineColour, 0.22f);
        Brine.Ground = Ground;

        _built = true;
        GD.Print(System.FormattableString.Invariant(
            $"[horde] built: {Ground.CellCount} cells of {Ground.CellSize:F1} m, {BlockedCells} blocked, {(Time.GetTicksUsec() - started) / 1000.0:F1} ms"));
    }

    public override void _Process(double delta)
    {
        if (!_built)
        {
            return;
        }

        float dt = (float)delta;
        UpdateHunt(dt);
        PushStats();
        Restamp();
        TickFlow(dt);

        Manager.SeedMinRadius = Seed(out Vector3 centre, out float radius);
        Manager.SetSeed(centre, radius);

        // The base count plus whatever has been added on top. Comparing
        // against the base alone trimmed every extra off the end of the array
        // on the frame after it spawned. Less the dead that stay dead: without
        // that, a contract's horde was topped back up every frame and never
        // fell.
        int wanted = Stats.AgentCount - Manager.BaseLost + Manager.TransientCount;
        if (Manager.AgentCount != wanted)
        {
            Manager.SetAgentCount(wanted, centre, radius);
        }

        float strikeRate = Mathf.Max(Stats.StrikesPerSecond, 0.0f);
        _strikeTokens = Mathf.Min(Mathf.Max(strikeRate, 1.0f), _strikeTokens + (strikeRate * dt));
        float allyRate = Mathf.Max(Stats.AllyStrikesPerSecond, 0.0f);
        _allyStrikeTokens = Mathf.Min(Mathf.Max(allyRate, 1.0f), _allyStrikeTokens + (allyRate * dt));
        float shotRate = Mathf.Max(Stats.ShotsPerSecond, 0.0f);
        _shotTokens = Mathf.Min(Mathf.Max(shotRate, 1.0f), _shotTokens + (shotRate * dt));

        Camera3D camera = GetViewport().GetCamera3D();
        Vector2 view = GetViewport().GetVisibleRect().Size;
        Manager.SetView(
            camera?.GlobalTransform ?? Transform3D.Identity,
            camera?.Fov ?? 0.0f,
            view.Y > 0.0f ? view.X / view.Y : 0.0f);

        GatherAllies();

        // On a planet the crowd behind the curve is not drawn: the eye's
        // direction from the centre and its height over the ground set the
        // horizon. On the flats there is none and the draw distance culls.
        Vector3 eye = camera?.GlobalPosition ?? GlobalPosition;
        bool horizon = Ground.HorizonRadius > 0.0f;
        Vector3 eyeUp = Ground.UpAt(eye);
        float eyeHeight = horizon ? (eye - Ground.GroundBelow(eye, out _)).Dot(eyeUp) : 0.0f;
        Manager.Tick(dt, eye, horizon ? eyeUp : Vector3.Up, eyeHeight, horizon);
    }

    /// <summary>
    /// The numbers that may change while it runs, every frame. They are plain
    /// C# properties on both sides, so this is a few dozen field writes.
    /// </summary>
    private void PushStats()
    {
        HordeManager horde = Manager;
        horde.AgentSpeed = Stats.AgentSpeed;
        horde.SpeedVariance = Stats.SpeedVariance;
        horde.Acceleration = Stats.Acceleration;
        horde.TurnRate = Stats.TurnRate;
        horde.SeparationRadius = Stats.SeparationRadius;
        horde.SeparationStrength = Stats.SeparationStrength;
        horde.MaxSeparationNeighbours = Stats.MaxSeparationNeighbours;
        horde.SeparationEnabled = Stats.SeparationEnabled;
        horde.CellCapacity = Stats.CellCapacity;
        horde.CrowdedDrive = Stats.CrowdedDrive;
        horde.AgentHealth = Stats.AgentHealth;
        horde.RespawnKilledAgents = Stats.RespawnKilledAgents;
        horde.KindsEnabled = Stats.KindsEnabled;
        horde.HitProxyRange = Stats.HitProxyRange;
        horde.WeakPointMultiplier = Stats.WeakPointMultiplier;
        horde.MaxBlastAgents = Stats.MaxBlastAgents;
        horde.PackCount = Stats.PackCount;
        horde.PackRadius = Stats.PackRadius;
        horde.Multithreaded = Stats.Multithreaded;
        horde.NearRange = Stats.NearRange;
        horde.MidRange = Stats.MidRange;
        horde.FarStride = Stats.FarStride;
        horde.DistantStride = Stats.DistantStride;
        horde.LodDistance = Stats.LodDistance;
        horde.DrawDistance = Stats.DrawDistance;
        horde.FrustumCullEnabled = Stats.FrustumCull;
        horde.RangedEnabled = Stats.RangedEnabled;
        horde.ThrownHitRadius = Stats.ThrownHitRadius;
        horde.AllyAggroRange = Stats.AllyAggroRange;
        horde.FlingSeconds = Stats.FlingSeconds;
        horde.FlingDrag = Stats.FlingDrag;
        horde.AirGravity = Stats.Gravity;
        horde.LandingSafeSpeed = Stats.LandingSafeSpeed;
        horde.LandingDamagePerSpeed = Stats.LandingDamagePerSpeed;

        // Twice the separation radius: the least that lets separation find
        // every neighbour in the eight nearest cells instead of twenty-seven.
        horde.HashCellSize = Mathf.Max(Stats.SeparationRadius * 2.0f, 0.5f);

        Vector3 hunt = HuntPoint(out bool hunting);
        horde.HuntPoint = hunt;
        horde.PressurePoint = hunt;

        // The last stretch, inside the cell the field routes to, is steered
        // straight at the player. Two cells is the target's own and a margin.
        horde.FinalApproachRange = hunting ? Ground.CellSize * 2.0f : 0.0f;

        horde.Roaming = !hunting && Stats.RoamRadius > 0.0f;
        horde.RoamCentre = Ground.GroundBelow(GlobalPosition, out _);
        horde.RoamRadius = Stats.RoamRadius;
        horde.RoamSpeedScale = Stats.RoamSpeedScale;
        horde.RoamPeriod = Stats.RoamPeriod;

        Brine.Gravity = Stats.Gravity * Stats.SpitGravityScale;
        Brine.HitRadius = Stats.ThrownHitRadius;
        AgentKindStats spitter = horde.KindStats((int)AgentKind.Spitter);
        if (spitter != null)
        {
            Brine.Impulse = spitter.ProjectileImpulse;
            Brine.Trauma = spitter.ProjectileTrauma;
            Brine.Lifetime = (spitter.RangedRange * 1.5f / Mathf.Max(spitter.ProjectileSpeed, 1.0f)) + 0.5f;
        }
        if (!Stats.RangedEnabled)
        {
            Brine.Clear();
        }
    }

    /// <summary>
    /// Publishes a finished build and starts the next one toward wherever the
    /// player now is. The search runs on a worker; what is reported is that
    /// worker's time, as background, so it is visible without being charged
    /// to the frame it landed in.
    /// </summary>
    private void TickFlow(float dt)
    {
        Flow.Collect();
        FrameProfiler.SetExternal(_sectionFlow, Flow.LastBuildMilliseconds);

        _flowTimer += dt;
        if (_flowTimer < Stats.FlowRebuildInterval)
        {
            return;
        }

        _flowTimer = 0.0f;
        Ground.GroundBelow(HuntPoint(out _), out _targetCell);
        Flow.RequestBuild(_targetCell, Stats.FlowThreaded);
    }

    /// <summary>Where the horde is going: the ground under the player while it hunts, else here.</summary>
    private Vector3 HuntPoint(out bool hunting)
    {
        // Riding something, the player is still hunted: a mech carries them
        // at its seat, so the crowd comes for the mech.
        hunting = _hunting && Stats.HuntsPlayer && PlayerExists;
        Vector3 point = hunting ? _player.GlobalPosition : GlobalPosition;
        return Ground != null ? Ground.GroundBelow(point, out _) : point;
    }

    /// <summary>One line of how the horde stands round its garrison, for <c>bench.gd</c>.</summary>
    public void PrintBenchSpread()
    {
        if (!_built)
        {
            return;
        }
        float distance = PlayerExists ? _player.GlobalPosition.DistanceTo(GlobalPosition) : -1.0f;
        GD.Print(System.FormattableString.Invariant(
            $"[bench]   garrison hunting={IsHunting} player at {distance:F0} m  {Manager.SpreadAround(Ground.GroundBelow(GlobalPosition, out _), Mathf.Max(Stats.RoamRadius, Stats.GarrisonSeedRadius))}"));
    }

    private bool _hunting = true;
    private float _provoked;
    private int _lastKills;

    /// <summary>True while the horde is coming for the player rather than
    /// keeping to its garrison.</summary>
    public bool IsHunting => _hunting && Stats.HuntsPlayer && PlayerExists;

    /// <summary>
    /// Whether the horde comes for the player this frame. With no
    /// <see cref="HordeStats.AggroRange"/> it always does, as it always did.
    /// With one, it keeps to its garrison until the player comes within that
    /// range of it or kills some of it, and goes home once the player is past
    /// <see cref="HordeStats.LeashRange"/>. Between the two it keeps on, so it
    /// does not turn back and forth at the edge.
    /// </summary>
    private void UpdateHunt(float dt)
    {
        int kills = Manager.KillCount;
        if (kills > _lastKills)
        {
            _provoked = Stats.ProvokeSeconds;
        }
        _lastKills = kills;
        _provoked -= dt;

        if (Stats.AggroRange <= 0.0f)
        {
            _hunting = true;
            return;
        }
        if (!PlayerExists)
        {
            _hunting = false;
            return;
        }

        float distance = _player.GlobalPosition.DistanceTo(GlobalPosition);
        bool was = _hunting;
        if (distance > Stats.LeashRange)
        {
            _hunting = false;
        }
        else if (distance <= Stats.AggroRange || _provoked > 0.0f)
        {
            _hunting = true;
        }
        if (_hunting != was && PerfLog.Recording)
        {
            PerfLog.Event(System.FormattableString.Invariant($"horde-{(_hunting ? "hunts" : "goes-home")} at {distance:F0}m"));
        }
    }

    private bool PlayerAvailable => PlayerExists && !_player.IsStowed;

    private bool PlayerExists => _player != null && IsInstanceValid(_player) && _player.IsInsideTree();

    // ---------------- Allies ----------------

    /// <summary>
    /// The player's allies this frame, from their group: what the crowd may
    /// go for besides the player. Snapshotted before the tick, so an ally's
    /// place in the list is the one the tick's strikes refer to.
    /// </summary>
    private void GatherAllies()
    {
        _allyTargets.Clear();
        _allyData.Clear();
        foreach (Node node in GetTree().GetNodesInGroup(IHordeTarget.GroupName))
        {
            if (node is IHordeTarget target && target.IsTargetable && _allyData.Count < 8)
            {
                _allyTargets.Add(target);
                _allyData.Add(new HordeManager.AllyTarget
                {
                    Base = target.TargetBase,
                    Radius = target.TargetRadius,
                    Height = target.TargetHeight,
                });
            }
        }
        Manager.SetAllies(_allyData);
    }

    private Vector3 AllyChest(int i) =>
        _allyData[i].Base + Ground.UpAt(_allyData[i].Base) * (_allyData[i].Height * 0.5f);

    /// <summary>A pickle reached an ally: a bite or a strike, as hard as its kind's stats say.</summary>
    private void OnAllyStruck(int ally, Vector3 position, int kind)
    {
        if (ally >= _allyTargets.Count || _allyStrikeTokens < 1.0f || !_allyTargets[ally].IsTargetable)
        {
            return;
        }
        AgentKindStats stats = Manager.KindStats(kind);
        if (stats == null)
        {
            return;
        }
        _allyStrikeTokens -= 1.0f;
        StruckCount++;

        Vector3 up = Ground.UpAt(position);
        Vector3 mouth = position + up * (stats.Height * 0.6f);
        Vector3 toward = (AllyChest(ally) - mouth).Slide(up);
        toward = toward.LengthSquared() > 1e-4f ? toward.Normalized() : up;
        _allyTargets[ally].TakeHordeHit(mouth, stats.StrikeDamage, toward * stats.StrikeImpulse);
        AudioDirector.PlayAt(kind == (int)AgentKind.Tank ? Sfx.TankStomp : Sfx.PickleBite, position);
    }

    private void OnBrineHitAlly(int ally, Vector3 point, Vector3 impulse)
    {
        if (ally >= _allyTargets.Count || !_allyTargets[ally].IsTargetable)
        {
            return;
        }
        _allyTargets[ally].TakeHordeHit(point - impulse.Normalized() * 0.5f,
            Manager.KindStats((int)AgentKind.Spitter)?.ProjectileDamage ?? 0.0f, impulse);
        AudioDirector.PlayAt(Sfx.SpitSplat, point);
    }

    /// <summary>What a throw or a spit is aimed at, and what the brine fuses against.</summary>
    private Vector3 PlayerChest => _player.GlobalPosition + (_player.GravityUp * 1.1f);

    /// <summary>
    /// Where agents are seeded and respawned: a ring round the player while
    /// it hunts, far enough out that the crowd is seen arriving rather than
    /// appearing; a disc round the garrison otherwise. Returns the inner edge.
    /// </summary>
    private float Seed(out Vector3 centre, out float radius)
    {
        Vector3 hunt = HuntPoint(out bool hunting);
        if (hunting)
        {
            centre = hunt;
            radius = Mathf.Max(Stats.HuntSeedRadius, Stats.HuntSeedMinRadius + 10.0f);
            return Stats.HuntSeedMinRadius;
        }

        centre = hunt;
        radius = Stats.GarrisonSeedRadius;
        return 0.0f;
    }

    // ---------------- What the crowd does to the player ----------------

    /// <summary>
    /// A pickle reached the player: a bite, a tank's strike, or a thrown
    /// cornichon coming down on them. A shove, a shake, a red flash and a bite
    /// out of the player's health, as hard as the kind's stats say.
    /// </summary>
    private void OnAgentStruck(Vector3 position, int kind, bool fromAbove)
    {
        if (_strikeTokens < 1.0f || !PlayerAvailable)
        {
            return;
        }

        AgentKindStats stats = Manager.KindStats(fromAbove ? (int)AgentKind.Thrower : kind);
        if (stats == null)
        {
            return;
        }

        _strikeTokens -= 1.0f;
        StruckCount++;

        float impulse = fromAbove ? stats.ProjectileImpulse : stats.StrikeImpulse;
        float trauma = fromAbove ? stats.ProjectileTrauma : stats.StrikeTrauma;
        float damage = fromAbove ? stats.ProjectileDamage : stats.StrikeDamage;

        Vector3 up = Ground.UpAt(position);
        Vector3 away = (_player.GlobalPosition - position).Slide(up);
        away = away.LengthSquared() > 1e-4f ? away.Normalized() : up;

        // Mostly along the ground. A crowd lands several of these a second,
        // and with the upward share a blast shove has, it juggled the player
        // tens of metres into the air and out of the fight.
        HitPlayer(position, (away * impulse) + (up * (impulse * 0.15f)), trauma, damage);
        AudioDirector.PlayAt(kind == (int)AgentKind.Tank ? Sfx.TankStomp : Sfx.PickleBite, position);
    }

    /// <summary>A spitter's brine reached the player.</summary>
    private void OnBrineHit(Vector3 point, Vector3 impulse, float trauma)
    {
        if (!PlayerAvailable)
        {
            return;
        }

        HitPlayer(point, impulse, trauma, Manager.KindStats((int)AgentKind.Spitter)?.ProjectileDamage ?? 0.0f);
        AudioDirector.PlayAt(Sfx.SpitSplat, point);
    }

    /// <summary>The one way the crowd hurts the player: health, a shove, a shake and a red flash.</summary>
    private void HitPlayer(Vector3 from, Vector3 impulse, float trauma, float damage)
    {
        if (_player.TakeDamage(damage))
        {
            AudioDirector.Play(Sfx.BossSlam);
        }

        _player.ApplyBlastImpulse(impulse);
        _player.CameraRig?.AddShake(trauma);
        _player.FlashHit(trauma, from);
    }

    /// <summary>
    /// A thrower or a spitter wants to fire. The horde decided it is in range
    /// and its cooldown is spent; this decides whether the budget allows it,
    /// where to aim, and what comes out. Over budget, the request is dropped --
    /// its cooldown is already spent, so it simply waits its turn again.
    /// </summary>
    private void OnRangedRequested(Vector3 position, int kind)
    {
        if (_shotTokens < 1.0f || !Stats.RangedEnabled)
        {
            return;
        }

        AgentKindStats stats = Manager.KindStats(kind);
        if (stats == null)
        {
            return;
        }

        // The nearest thing in reach: the player, or an ally nearer than them.
        bool any = PlayerAvailable;
        Vector3 target = any ? PlayerChest : Vector3.Zero;
        Vector3 lead = any ? _player.Velocity * stats.ProjectileLead : Vector3.Zero;
        float best = any ? position.DistanceSquaredTo(target) : float.MaxValue;
        float reach = stats.RangedRange * 1.2f;
        for (int i = 0; i < _allyTargets.Count; i++)
        {
            Vector3 chest = AllyChest(i);
            float d = position.DistanceSquaredTo(chest);
            if (d < best && d <= reach * reach)
            {
                best = d;
                target = chest;
                lead = Vector3.Zero;
                any = true;
            }
        }
        if (!any)
        {
            return;
        }

        Vector3 up = Ground.UpAt(position);
        Vector3 muzzle = position + (up * ((stats.Height * 0.5f) + 0.3f));
        float speed = Mathf.Max(stats.ProjectileSpeed, 1.0f);
        float spread = Mathf.DegToRad(stats.ProjectileSpreadDegrees);

        if (kind == (int)AgentKind.Spitter)
        {
            Vector3 aim = Ballistics.Intercept(muzzle, target, lead, speed);

            // A little over the line, by the droop the brine will pick up on
            // the way, so a still target is hit rather than undershot.
            float time = muzzle.DistanceTo(aim) / speed;
            aim += up * (0.5f * Brine.Gravity * time * time);

            Vector3 direction = Ballistics.Scatter((aim - muzzle).Normalized(), spread, AimUnit(), AimUnit());
            if (Brine.Fire(muzzle, direction * speed))
            {
                _shotTokens -= 1.0f;
                AudioDirector.PlayAt(Sfx.SpitterSpit, muzzle);
            }
            return;
        }

        if (kind != (int)AgentKind.Thrower
            || Manager.ExtraCount(HordeManager.SpawnOrigin.Thrown) >= Stats.MaxThrownCornichons)
        {
            return;
        }

        // Solved twice: once to learn how long it will be in the air, and
        // again at where the target will be by then.
        Ballistics.Lob(muzzle, target, up, speed, Stats.Gravity, true, out float flight);
        Vector3 velocity = Ballistics.Lob(muzzle, target + (lead * flight), up, speed, Stats.Gravity, true, out _);
        velocity = Ballistics.Scatter(velocity.Normalized(), spread, AimUnit(), AimUnit()) * speed;

        if (Manager.SpawnFlung(muzzle, velocity, (int)AgentKind.Cornichon, HordeManager.SpawnOrigin.Thrown,
                               lobbed: true))
        {
            _shotTokens -= 1.0f;
            ThrownCount++;
            AudioDirector.PlayAt(Sfx.PickleThrow, muzzle);
        }
    }

    private float AimUnit()
    {
        _aimRandom ^= _aimRandom << 13;
        _aimRandom ^= _aimRandom >> 7;
        _aimRandom ^= _aimRandom << 17;
        return (_aimRandom >> 40) / (float)(1UL << 24);
    }

    // ---------------- Bench ----------------

    /// <summary>Every agent back out on the seed ring, so a count set a moment ago starts from one place.</summary>
    public void BenchReseed()
    {
        if (!_built)
        {
            return;
        }

        Manager.SeedMinRadius = Seed(out Vector3 centre, out float radius);
        Manager.Reseed(centre, radius);
    }

    /// <summary>One line of the crowd's state, for <c>bench.gd</c>'s <c>@status</c>.</summary>
    public string BenchStatus()
    {
        if (!_built)
        {
            return "horde not built";
        }

        string health = _player?.Health != null
            ? System.FormattableString.Invariant($" health={_player.Health.Current:F0} pickled={_player.Health.TimesPickled}")
            : string.Empty;
        return System.FormattableString.Invariant(
            $"agents={Manager.AgentCount} drawn={Manager.VisibleAgentCount} stepped={Manager.SteppedCount} near={Manager.NearPlayerCount} proxies={Manager.ActiveProxyCount} stranded={Manager.StrandedCount} kills={Manager.KillCount} struck={StruckCount} thrown={ThrownCount} brine={Brine.FiredCount}/{Brine.HitCount} flow={Flow.LastBuildMilliseconds:F1}ms{health}");
    }
}
