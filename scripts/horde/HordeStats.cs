using Godot;

namespace DillControl.Horde;

/// <summary>
/// Every arguable number about the horde as a whole: how many, how fast, how
/// they crowd, how far each simulation tier reaches, how they are seeded and
/// how hard they may hit you per second. What each pickle is lives on its own
/// <c>AgentKindStats</c>; this is the crowd.
///
/// Read by <see cref="HordeDirector"/> every frame and pushed into the
/// <see cref="HordeManager"/>, so a change in the inspector or from the bench
/// takes effect live. The capacity, the proxy pool and the ground are read once
/// at load, because they size arrays.
///
/// Defaults are the lab's, measured there at 25,000 on the target machine.
/// </summary>
[GlobalClass]
public partial class HordeStats : Resource
{
    // ---------------- Population ----------------

    [ExportGroup("Population")]
    /// <summary>The base crowd. Extras (thrown cornichons, nest brood) ride on top.</summary>
    [Export(PropertyHint.Range, "0,60000,100")]
    public int AgentCount { get; set; } = 25000;

    /// <summary>Most agents that can ever exist. Read once; it sizes every array.</summary>
    [Export(PropertyHint.Range, "1000,100000,1000")]
    public int AgentCapacity { get; set; } = 60000;

    /// <summary>
    /// A killed agent comes back on the seed ring. On for measuring, where the
    /// load has to hold still; a mission turns it off so the population falls.
    /// </summary>
    [Export]
    public bool RespawnKilledAgents { get; set; } = true;

    [Export(PropertyHint.Range, "1,500,1")]
    public float AgentHealth { get; set; } = 30.0f;

    /// <summary>Off spawns only standards: the baseline for measuring the five-kind split.</summary>
    [Export]
    public bool KindsEnabled { get; set; } = true;

    // ---------------- Seeding ----------------

    [ExportGroup("Seeding")]
    /// <summary>
    /// The horde comes for the player wherever they are. Off, it stands round
    /// the director's own position, which is the garrison.
    /// </summary>
    [Export]
    public bool HuntsPlayer { get; set; } = true;

    /// <summary>Inner edge of the ring a hunting horde is seeded and respawned on, round the player.</summary>
    [Export(PropertyHint.Range, "0,1000,5,suffix:m")]
    public float HuntSeedMinRadius { get; set; } = 150.0f;

    /// <summary>Outer edge of that ring. How long the furthest pack takes to arrive.</summary>
    [Export(PropertyHint.Range, "20,1000,5,suffix:m")]
    public float HuntSeedRadius { get; set; } = 450.0f;

    /// <summary>How far round the director the garrison is seeded, when the horde is not hunting.</summary>
    [Export(PropertyHint.Range, "10,1000,5,suffix:m")]
    public float GarrisonSeedRadius { get; set; } = 160.0f;

    /// <summary>Packs the horde is seeded into. Zero is an even scatter.</summary>
    [Export(PropertyHint.Range, "0,100,1")]
    public int PackCount { get; set; } = 20;

    [Export(PropertyHint.Range, "1,200,1,suffix:m")]
    public float PackRadius { get; set; } = 30.0f;

    // ---------------- Movement ----------------

    [ExportGroup("Movement")]
    [Export(PropertyHint.Range, "0.1,20,0.1,suffix:m/s")]
    public float AgentSpeed { get; set; } = 3.4f;

    /// <summary>Each agent's speed is this fraction either side of the base, so a crowd strings out.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SpeedVariance { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "0.1,60,0.1,suffix:m/s²")]
    public float Acceleration { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0.1,30,0.1")]
    public float TurnRate { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "0,40,0.1,suffix:m/s²")]
    public float Gravity { get; set; } = 9.8f;

    [Export(PropertyHint.Range, "0,10,0.05,suffix:s")]
    public float FlingSeconds { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0,10,0.05")]
    public float FlingDrag { get; set; } = 1.2f;

    [Export(PropertyHint.Range, "0,60,0.5,suffix:m/s")]
    public float LandingSafeSpeed { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "0,20,0.1")]
    public float LandingDamagePerSpeed { get; set; } = 2.5f;

    // ---------------- Crowding ----------------

    [ExportGroup("Crowding")]
    [Export]
    public bool SeparationEnabled { get; set; } = true;

    /// <summary>
    /// How far apart agents push. The spatial hash is sized at twice this, the
    /// least that lets separation find every neighbour in eight cells.
    /// </summary>
    [Export(PropertyHint.Range, "0.2,5,0.05,suffix:m")]
    public float SeparationRadius { get; set; } = 1.4f;

    [Export(PropertyHint.Range, "0,20,0.1")]
    public float SeparationStrength { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "1,32,1")]
    public int MaxSeparationNeighbours { get; set; } = 8;

    /// <summary>Agents a hash cell holds before the ones behind stop walking into it. Zero is no cap.</summary>
    [Export(PropertyHint.Range, "0,32,1")]
    public int CellCapacity { get; set; } = 6;

    /// <summary>How much of its forward drive an agent queued behind others keeps.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CrowdedDrive { get; set; } = 0.05f;

    // ---------------- Tiers ----------------

    [ExportGroup("Tiers")]
    /// <summary>Inside this, agents step every frame and separate.</summary>
    [Export(PropertyHint.Range, "1,100,0.5,suffix:m")]
    public float NearRange { get; set; } = 14.0f;

    /// <summary>Edge of the middle tier, stepped one frame in <see cref="FarStride"/>.</summary>
    [Export(PropertyHint.Range, "10,1000,5,suffix:m")]
    public float MidRange { get; set; } = 120.0f;

    [Export(PropertyHint.Range, "1,60,1")]
    public int FarStride { get; set; } = 6;

    [Export(PropertyHint.Range, "1,120,1")]
    public int DistantStride { get; set; } = 15;

    /// <summary>Past this an agent is drawn with the few-sided far mesh.</summary>
    [Export(PropertyHint.Range, "5,500,1,suffix:m")]
    public float LodDistance { get; set; } = 40.0f;

    /// <summary>Past this an agent is not drawn at all. Zero draws everything; keep it inside the fog.</summary>
    [Export(PropertyHint.Range, "0,2000,10,suffix:m")]
    public float DrawDistance { get; set; } = 420.0f;

    [Export]
    public bool FrustumCull { get; set; } = true;

    /// <summary>Off measures the single-threaded cost: the honest baseline for a dual-core machine.</summary>
    [Export]
    public bool Multithreaded { get; set; } = true;

    // ---------------- Hits ----------------

    [ExportGroup("Hits")]
    /// <summary>Borrowed colliders handed to the nearest agents. Read once at load.</summary>
    [Export(PropertyHint.Range, "0,256,1")]
    public int HitProxyCount { get; set; } = 96;

    /// <summary>How far from the player an agent can be and still wear a collider.</summary>
    [Export(PropertyHint.Range, "5,300,1,suffix:m")]
    public float HitProxyRange { get; set; } = 90.0f;

    /// <summary>Damage multiplier on a pickle's head.</summary>
    [Export(PropertyHint.Range, "1,10,0.05")]
    public float WeakPointMultiplier { get; set; } = 2.5f;

    /// <summary>Most agents one blast resolves against, nearest first. Each costs a sight ray per hash cell.</summary>
    [Export(PropertyHint.Range, "1,4000,1")]
    public int MaxBlastAgents { get; set; } = 384;

    // ---------------- Pressure on the player ----------------

    [ExportGroup("Pressure")]
    /// <summary>
    /// Strikes that may land on the player per second, the whole crowd
    /// together. A surrounded player is hit by dozens at once; this is what
    /// keeps that a threat rather than an instant pickling.
    /// </summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:/s")]
    public float StrikesPerSecond { get; set; } = 6.0f;

    /// <summary>Throws and spits the crowd may make per second, the whole crowd together.</summary>
    [Export(PropertyHint.Range, "0,200,0.5,suffix:/s")]
    public float ShotsPerSecond { get; set; } = 20.0f;

    [Export]
    public bool RangedEnabled { get; set; } = true;

    /// <summary>Thrown cornichons alive at once, at most.</summary>
    [Export(PropertyHint.Range, "0,4000,10")]
    public int MaxThrownCornichons { get; set; } = 400;

    /// <summary>How close a lobbed cornichon has to pass to land on the player.</summary>
    [Export(PropertyHint.Range, "0.2,5,0.05,suffix:m")]
    public float ThrownHitRadius { get; set; } = 1.4f;

    // ---------------- Routing ----------------

    [ExportGroup("Routing")]
    /// <summary>
    /// Flow cell size on the flats. Read once at load. Routing is a global
    /// question and the last stretch is steered straight at the player, so
    /// cells can be coarse; the lab's planet ran on cells thirty metres
    /// across. Six keeps a pillar's footprint near the pillar.
    /// </summary>
    [Export(PropertyHint.Range, "1,32,0.5,suffix:m")]
    public float FlowCellSize { get; set; } = 6.0f;

    /// <summary>Seconds between flow field rebuilds toward where the player now is.</summary>
    [Export(PropertyHint.Range, "0.05,5,0.05,suffix:s")]
    public float FlowRebuildInterval { get; set; } = 0.5f;

    /// <summary>Rebuild on a worker thread. Off charges the whole search to the frame that asks.</summary>
    [Export]
    public bool FlowThreaded { get; set; } = true;
}
