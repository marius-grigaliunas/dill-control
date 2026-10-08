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
    /// <summary>Side of the square of ground the horde can stand on, centred here.</summary>
    [Export(PropertyHint.Range, "50,4000,10,suffix:m")]
    public float GroundSize { get; set; } = 1200.0f;

    public HordeManager Manager { get; private set; }

    public FlatGround Ground { get; private set; }

    public FlowField Flow { get; private set; }

    /// <summary>Flow cells something authored stands in.</summary>
    public int BlockedCells { get; private set; }

    /// <summary>How many strikes reached the player, for the bench.</summary>
    public int StruckCount { get; private set; }

    /// <summary>How many cornichons were thrown, for the bench.</summary>
    public int ThrownCount { get; private set; }

    private PlayerController _player;
    private bool _built;
    private float _flowTimer;
    private float _strikeTokens;
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

        _sectionFlow = FrameProfiler.Section("flow build [worker]");

        Manager = new HordeManager { Name = "Manager" };
        AddChild(Manager);

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
        if (!_built)
        {
            Build();
        }
        SetPhysicsProcess(false);
    }

    private void Build()
    {
        ulong started = Time.GetTicksUsec();

        Ground = new FlatGround(GlobalPosition, GroundSize, Stats.FlowCellSize);
        Flow = new FlowField(Ground);
        BlockedCells = Ground.StampObstacles(GetWorld3D().DirectSpaceState, Flow, FlatGround.ObstacleMask);

        _targetCell = Ground.CellOf(HuntPoint(out _));
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

        _built = true;
        GD.Print(System.FormattableString.Invariant(
            $"[horde] built: {Ground.GridSize}x{Ground.GridSize} cells of {Ground.CellSize:F1} m, {BlockedCells} blocked, {(Time.GetTicksUsec() - started) / 1000.0:F1} ms"));
    }

    public override void _Process(double delta)
    {
        if (!_built)
        {
            return;
        }

        float dt = (float)delta;
        PushStats();
        TickFlow(dt);

        Manager.SeedMinRadius = Seed(out Vector3 centre, out float radius);
        Manager.SetSeed(centre, radius);

        // The base count plus whatever has been added on top. Comparing
        // against the base alone trimmed every extra off the end of the array
        // on the frame after it spawned.
        int wanted = Stats.AgentCount + Manager.TransientCount;
        if (Manager.AgentCount != wanted)
        {
            Manager.SetAgentCount(wanted, centre, radius);
        }

        float strikeRate = Mathf.Max(Stats.StrikesPerSecond, 0.0f);
        _strikeTokens = Mathf.Min(Mathf.Max(strikeRate, 1.0f), _strikeTokens + (strikeRate * dt));
        float shotRate = Mathf.Max(Stats.ShotsPerSecond, 0.0f);
        _shotTokens = Mathf.Min(Mathf.Max(shotRate, 1.0f), _shotTokens + (shotRate * dt));

        Camera3D camera = GetViewport().GetCamera3D();
        Vector2 view = GetViewport().GetVisibleRect().Size;
        Manager.SetView(
            camera?.GlobalTransform ?? Transform3D.Identity,
            camera?.Fov ?? 0.0f,
            view.Y > 0.0f ? view.X / view.Y : 0.0f);

        Vector3 eye = camera?.GlobalPosition ?? GlobalPosition;
        Manager.Tick(dt, eye, Vector3.Up, 0.0f, false);
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
        _targetCell = Ground.CellOf(HuntPoint(out _));
        Flow.RequestBuild(_targetCell, Stats.FlowThreaded);
    }

    /// <summary>Where the horde is going: the ground under the player while it hunts, else here.</summary>
    private Vector3 HuntPoint(out bool hunting)
    {
        hunting = Stats.HuntsPlayer && PlayerAvailable;
        Vector3 point = hunting ? _player.GlobalPosition : GlobalPosition;
        return Ground != null ? Ground.GroundBelow(point, out _) : point;
    }

    private bool PlayerAvailable =>
        _player != null && IsInstanceValid(_player) && _player.IsInsideTree() && !_player.IsStowed;

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

        if (_player.TakeDamage(damage))
        {
            AudioDirector.Play(Sfx.BossSlam);
        }

        // Mostly along the ground. A crowd lands several of these a second,
        // and with the upward share a blast shove has, it juggled the player
        // tens of metres into the air and out of the fight.
        _player.ApplyBlastImpulse((away * impulse) + (up * (impulse * 0.15f)));
        _player.CameraRig?.AddShake(trauma);
        _player.FlashHit(trauma, position);
        AudioDirector.PlayAt(kind == (int)AgentKind.Tank ? Sfx.TankStomp : Sfx.PickleBite, position);
    }

    /// <summary>
    /// A thrower or a spitter wants to fire. The horde decided it is in range
    /// and its cooldown is spent; this decides whether the budget allows it,
    /// where to aim, and what comes out. Over budget, the request is dropped --
    /// its cooldown is already spent, so it simply waits its turn again.
    ///
    /// Spitters have nothing to spit until the shot pool comes across with the
    /// weapons' impact effects; until then their requests are dropped here.
    /// </summary>
    private void OnRangedRequested(Vector3 position, int kind)
    {
        if (_shotTokens < 1.0f || !Stats.RangedEnabled || !PlayerAvailable || kind != (int)AgentKind.Thrower)
        {
            return;
        }

        AgentKindStats stats = Manager.KindStats(kind);
        if (stats == null || Manager.ExtraCount(HordeManager.SpawnOrigin.Thrown) >= Stats.MaxThrownCornichons)
        {
            return;
        }

        Vector3 up = Ground.UpAt(position);
        Vector3 muzzle = position + (up * ((stats.Height * 0.5f) + 0.3f));
        Vector3 target = _player.GlobalPosition + (up * 0.9f);
        float speed = Mathf.Max(stats.ProjectileSpeed, 1.0f);
        float spread = Mathf.DegToRad(stats.ProjectileSpreadDegrees);
        Vector3 lead = _player.Velocity * stats.ProjectileLead;

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
            $"agents={Manager.AgentCount} drawn={Manager.VisibleAgentCount} stepped={Manager.SteppedCount} near={Manager.NearPlayerCount} proxies={Manager.ActiveProxyCount} stranded={Manager.StrandedCount} kills={Manager.KillCount} struck={StruckCount} thrown={ThrownCount} flow={Flow.LastBuildMilliseconds:F1}ms{health}");
    }
}
