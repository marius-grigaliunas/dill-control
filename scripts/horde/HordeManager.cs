using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using DillControl.Audio;
using DillControl.Core;
using DillControl.Enemies;

namespace DillControl.Horde;

/// <summary>
/// The horde, as data rather than as a scene tree.
///
/// Every agent is a struct in one flat array, stepped by one loop, drawn by one
/// MultiMeshInstance3D. There is no Node per agent, no AnimationPlayer, no
/// NavigationAgent3D and no physics body -- all four of which are what makes
/// two hundred enemies impossible on the target machine rather than merely
/// expensive.
///
/// Three choices carry most of the performance:
///
/// Agents never raycast. Standing on the ground is a read of the ground's own
/// data through <see cref="IHordeGround"/> -- one number on the flats, four
/// array reads on a heightfield. Two hundred shape queries a frame would cost
/// more than everything else here combined.
///
/// Transforms go to the GPU as one buffer write. MultiMesh.SetInstanceTransform
/// marshals a Transform3D across the interop boundary per instance, which at a
/// couple of thousand agents is most of a millisecond of pure overhead;
/// RenderingServer.MultimeshSetBuffer is a single call and a memcpy.
///
/// The multimesh gets an explicit CustomAabb the size of the ground. Without it
/// the engine recomputes the bounding box over the whole buffer on every
/// write, which is the hidden cost that makes the fast path no faster than the
/// slow one.
///
/// Ported from the lab's spike folder. The planet it was written on is behind
/// <see cref="IHordeGround"/> now, and every agent, pass and tier is otherwise
/// the lab's, measured there at 25,000 on the target machine.
/// </summary>
public partial class HordeManager : Node3D
{
    /// <summary>Twelve floats per instance: a 3x4 transform, row-major.</summary>
    private const int FloatsPerInstance = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct Agent
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Velocity;
        public float TargetSpeed;
        public float Health;
        public int Cell;

        /// <summary>
        /// Which of the five behaviours this agent runs, as an
        /// <see cref="AgentKind"/>. A byte because this struct is streamed
        /// twice a frame at tens of thousands of instances, and it also
        /// indexes the per-kind instance buffer this agent is drawn from.
        /// </summary>
        public byte Kind;

        /// <summary>
        /// Seconds until this agent's own behaviour may fire again. Written
        /// only by the owning slot, so it is safe in the parallel pass.
        /// </summary>
        public float Cooldown;


        /// <summary>
        /// Generation, bumped whenever this slot is refilled. A hit resolved
        /// against a stale index would otherwise wound whoever took the slot.
        /// </summary>
        public int Id;

        /// <summary>
        /// Seconds left of being thrown. While positive the agent coasts on the
        /// velocity it was given instead of steering, so a throw carries it.
        /// </summary>
        public float Flung;

        /// <summary>
        /// When this agent was last hurt, on the horde's own clock, for the
        /// squash-and-stretch it is drawn with for a moment afterwards. Written
        /// only by a hit and read only by the transform pass, which reads this
        /// agent anyway, so the whole-horde passes never write it.
        /// </summary>
        public float HitAt;

        /// <summary>
        /// Off the ground: held in the manipulator's beam, or thrown and not
        /// yet landed. An airborne agent is not snapped to the heightfield --
        /// the snap is the whole of "grounded", and it is exactly what put a
        /// lifted agent straight back on the ground every frame.
        /// </summary>
        public bool Airborne;

        /// <summary>
        /// Thrown at the player by a thrower and not yet landed. It may hit
        /// the player once on the way down, and it does not take landing
        /// damage: a lob that reaches seventy metres comes down well over the
        /// safe landing speed, and a thrower whose every throw died on arrival
        /// would be throwing gibs.
        /// </summary>
        public bool Lobbed;

        /// <summary>
        /// Seconds left of being held. Renewed by every MoveAgent; while it
        /// runs the agent neither steers, falls nor moves on its own.
        /// </summary>
        public float Held;

        /// <summary>
        /// Where this agent came from. <see cref="SpawnOrigin.Base"/> is the
        /// horde itself; anything else was added on top of it -- a brood, a
        /// gherkin shed by Mother Brine, a pickle thrown out of the hive. An
        /// extra is removed when it dies instead of respawned, and it is not
        /// counted against the agent count the tuning asks for, which used to
        /// trim every one of them off the end of the array on the next frame.
        /// </summary>
        public SpawnOrigin Origin;
    }

    private Agent[] _agents;

    // Written for every agent every frame, so kept out of the struct. The
    // passes that visit the whole horde -- the hash, the classify, the steer's
    // tier check -- used to write into every agent's cache line, which made
    // all four megabytes of the array dirty and had them written back to
    // memory every frame. Out here they are a few hundred kilobytes, and the
    // agent array is only read by anything that does not move an agent.

    /// <summary>The next agent in the same hash bucket, or -1.</summary>
    private int[] _hashNext;

    /// <summary>
    /// Which instance buffer each agent is drawn from this frame -- kind and
    /// level of detail -- or <see cref="NotDrawn"/>.
    /// </summary>
    private byte[] _drawBucket;

    /// <summary>
    /// How many frames' worth of time each agent was stepped by this frame:
    /// one up close, the stride on a further agent's turn, zero on the frames
    /// it sits out.
    /// </summary>
    private byte[] _step;

    /// <summary>Where an agent came from. Extras are counted per origin so each source can be capped on its own.</summary>
    public enum SpawnOrigin : byte
    {
        Base = 0,

        /// <summary>A nest's produce.</summary>
        Brood = 1,

        /// <summary>A gherkin shed by Mother Brine.</summary>
        Shed = 2,

        /// <summary>A pickle thrown out of the compound's hive.</summary>
        Pickle = 3,

        /// <summary>A cornichon a thrower lobbed at the player.</summary>
        Thrown = 4,

        /// <summary>A pickle dropped from its pod when its colony woke (PodField).</summary>
        Pod = 5,
    }

    private readonly int[] _extraByOrigin = new int[6];

    /// <summary>Extras alive from one origin.</summary>
    public int ExtraCount(SpawnOrigin origin) => _extraByOrigin[(int)origin];

    private void CountExtra(SpawnOrigin origin, int delta)
    {
        if (origin == SpawnOrigin.Base)
        {
            return;
        }

        TransientCount += delta;
        _extraByOrigin[(int)origin] += delta;
    }

    private void ClearExtras()
    {
        TransientCount = 0;
        System.Array.Clear(_extraByOrigin, 0, _extraByOrigin.Length);
    }

    /// <summary>
    /// One instance buffer per kind. Five draw calls instead of one, and the
    /// same total bytes uploaded.
    ///
    /// The alternative -- one multimesh with UseColors on -- is cheaper today
    /// and wrong tomorrow: per-instance colour can tint five variants but cannot
    /// give them five *meshes*, and the shipping architecture is explicitly one
    /// MultiMeshInstance3D per variant with its animation baked into a vertex
    /// animation texture. Building the colour version now would mean rewriting
    /// this pack later with real content standing on it, having measured the
    /// wrong thing in between.
    ///
    /// It also buys silhouette for free. Each kind's capsule proportions and
    /// its material come off its own AgentKindStats at build time and never
    /// touch the per-frame buffer at all, which matters because five grey
    /// capsules of identical shape would be unreadable at the horizon and
    /// reading which one is coming is the entire point of having five.
    /// </summary>
    private float[][] _transforms;

    private MultiMeshInstance3D[] _instances;
    private MultiMesh[] _multiMeshes;
    private Rid[] _multiMeshRids;

    /// <summary>
    /// Instances each kind's GPU buffer currently holds. Sized to a power of
    /// two above what is drawn rather than to capacity: see UploadKind.
    /// </summary>
    private int[] _allocated;

    /// <summary>Smallest GPU buffer a kind is given, in instances.</summary>
    private const int MinAllocated = 64;

    /// <summary>How many instances each kind's buffer has room for.</summary>
    private int[] _kindCapacity;

    /// <summary>How many agents of each kind are alive right now.</summary>
    private int[] _kindLive;

    private AgentKindStats[] _kindStats;
    private float[] _kindHalfHeight;
    private HordeComposition _composition;
    private float _maxHalfHeight = 0.9f;

    private int[] _bucketHead;
    private int[] _bucketCount;
    private int _bucketMask;

    /// <summary>
    /// Ceiling on how many agents may be seeded in one call. At 60 Hz this
    /// brings the horde from 200 to its 20,000 ceiling in about a fifth of a
    /// second, which reads as a surge rather than as a stall.
    /// </summary>
    private const int MaxSpawnsPerCall = 2000;

    /// <summary>
    /// Ceiling on chunks a pass is cut into. Far more than there are cores, on
    /// purpose: the array is sorted by where agents stand, so the expensive
    /// ones -- near the player, separating, biting -- sit together, and eight
    /// chunks for eight threads left one thread with all of them while the
    /// rest waited. Small chunks let the thread pool's work stealing even it.
    /// </summary>
    private const int MaxPartitions = 64;

    /// <summary>Fewest agents in one chunk, so scheduling never costs more than the work.</summary>
    private const int MinPartitionSize = 1024;
    private const int ParallelThreshold = 1500;
    private const int KindCount = HordeComposition.KindCount;

    /// <summary>Two instance buffers per kind: the capsule up close, a few triangles far away.</summary>
    private const int BucketCount = KindCount * 2;

    private const byte NotDrawn = byte.MaxValue;

    /// <summary>
    /// Behaviour events a partition raised, drained serially once both passes
    /// are done.
    ///
    /// A strike shoves the player and a throw spawns an agent, both of which
    /// touch state another worker is reading or that only the main thread may
    /// touch. Rather than lock, each
    /// partition appends to its own pre-allocated array with its own count --
    /// the same prefix-sum-free discipline WriteTransforms already proves --
    /// and the main thread walks them afterwards. The cost is bounded by how
    /// many cooldowns expired in one frame, which at the shipped intervals is a
    /// few dozen at worst.
    /// </summary>
    private const int MaxEventsPerPartition = 64;

    private readonly int[] _partitionStranded = new int[MaxPartitions];
    private readonly int[] _partitionNear = new int[MaxPartitions];
    private readonly int[] _partitionPressure = new int[MaxPartitions];

    // Buffer-major: partition * BucketCount + bucket. One multiply-add in the
    // inner loop instead of an increment, and a prefix sum per buffer.
    private readonly int[] _partitionVisible = new int[MaxPartitions * BucketCount];
    private readonly int[] _partitionOffset = new int[MaxPartitions * BucketCount];

    private readonly HordeEvent[][] _partitionEvents = NewEventBuffers();
    private readonly int[] _partitionEventCount = new int[MaxPartitions];

    private static HordeEvent[][] NewEventBuffers()
    {
        HordeEvent[][] buffers = new HordeEvent[MaxPartitions][];
        for (int i = 0; i < MaxPartitions; i++)
        {
            buffers[i] = new HordeEvent[MaxEventsPerPartition];
        }
        return buffers;
    }

    private enum HordeEventType : byte
    {
        Died,

        /// <summary>A bite or a strike that reached the player, or a thrown cornichon landing on them.</summary>
        Strike,

        /// <summary>A thrower or a spitter wants to fire. What it fires is the main thread's call.</summary>
        Ranged,

        /// <summary>
        /// A cornichon launched itself. It exists only so the dash can be heard:
        /// the behaviour itself writes nothing but the agent's own velocity
        /// and cooldown, so it runs inside the parallel steer pass, and audio
        /// cannot be started from a worker thread. Bounded by the same
        /// cooldown every other event here is.
        /// </summary>
        Dash,
    }

    private struct HordeEvent
    {
        public HordeEventType Type;
        public int Index;
        public int Id;
        public Vector3 Position;
        public float Magnitude;
    }

    private IHordeGround _ground;
    private FlowField _flow;

    private int _count;
    private int _capacity;
    private ulong _random = 0x9E3779B97F4A7C15UL;
    private int _nextAgentId = 1;

    private HordeHitProxy[] _proxies;
    private Vector3 _seedCentre;

    /// <summary>
    /// How far round <see cref="_seedCentre"/> agents are seeded. Zero is no
    /// seed at all: on the planet a zero centre meant that, but on the flats
    /// the origin is a perfectly good place to stand.
    /// </summary>
    private float _seedRadius;

    /// <summary>
    /// Ground points each pack is seeded around, rebuilt whenever the
    /// seed centre or radius moves. Held rather than recomputed per agent so a
    /// respawn lands back in a pack instead of dissolving the crowd one death
    /// at a time.
    /// </summary>
    private Vector3[] _packCentres = System.Array.Empty<Vector3>();
    private Vector3 _packCentre;
    private float _packRadius = -1.0f;
    private float _packMinRadius = -1.0f;

    private int _sectionUpdate;
    private int _sectionHash;
    private int _sectionBuffer;
    private int _sectionProxies;
    private int _sectionSteer;
    private int _sectionIntegrate;
    private int _sectionEvents;
    private int _sectionUpload;
    private int _sectionReorder;
    private int _sectionClassify;

    // ---------------- Tunables, pushed in each frame ----------------

    public float AgentSpeed { get; set; } = 3.4f;
    public float SpeedVariance { get; set; } = 0.25f;
    public float Acceleration { get; set; } = 8.0f;
    public float TurnRate { get; set; } = 6.0f;
    public float SeparationRadius { get; set; } = 1.4f;
    public float SeparationStrength { get; set; } = 4.0f;
    public int MaxSeparationNeighbours { get; set; } = 8;
    public bool SeparationEnabled { get; set; } = true;
    public float AgentHalfHeight { get; set; } = 0.9f;
    public float HashCellSize { get; set; } = 2.5f;
    public float AgentHealth { get; set; } = 60.0f;

    /// <summary>Longest a thrown agent coasts before it steers again.</summary>
    public float FlingSeconds { get; set; } = 2.0f;

    /// <summary>Exponential drag on a thrown agent, per second.</summary>
    public float FlingDrag { get; set; } = 1.2f;

    /// <summary>Gravity on an airborne agent, in m/s², down.</summary>
    public float AirGravity { get; set; } = 9.8f;

    /// <summary>Landing speed an agent survives unhurt, in m/s.</summary>
    public float LandingSafeSpeed { get; set; } = 12.0f;

    /// <summary>Damage per m/s of landing speed above <see cref="LandingSafeSpeed"/>.</summary>
    public float LandingDamagePerSpeed { get; set; } = 2.5f;

    /// <summary>Held state outlasts a missed MoveAgent by this much, so a
    /// physics frame landing between two render frames does not drop it.</summary>
    private const float HeldGrace = 0.2f;

    /// <summary>
    /// Spreads the step across cores. Off measures the single-threaded cost,
    /// which is the honest baseline for a minimum-spec dual-core machine.
    /// </summary>
    public bool Multithreaded { get; set; } = true;
    public float HitProxyRange { get; set; } = 60.0f;

    /// <summary>
    /// Packs the horde is seeded into across the seed disc. 1 is one crowd, 0
    /// is the old even scatter. Read at seed time, so changing it takes effect
    /// on the next reseed rather than teleporting a live horde.
    /// </summary>
    public int PackCount { get; set; } = 6;

    /// <summary>Radius of one pack, in metres.</summary>
    public float PackRadius { get; set; } = 18.0f;

    /// <summary>
    /// Inner edge of the seed ring, in metres. Zero seeds the whole disc, which
    /// is the garrison standing round the hive. Above zero packs are rolled on
    /// a ring instead -- the hunt, seeded past the horizon so a crowd arrives
    /// rather than appears -- and the ring follows its centre once that has
    /// moved half this far, so a respawn never lands where the player now is.
    /// </summary>
    public float SeedMinRadius { get; set; }
    public bool RespawnKilledAgents { get; set; } = true;

    /// <summary>
    /// Off spawns every agent as a standard whatever the composition says.
    /// This is the measurement baseline for the five-kind split: the pack, the
    /// buffers and the draw calls are unchanged, only the mix is, so the two
    /// cases are directly comparable on the same build.
    /// </summary>
    public bool KindsEnabled { get; set; } = true;

    /// <summary>
    /// An agent bit, struck or landed on the player: its position, its kind,
    /// whose stats say how hard, and whether it came down from a throw rather
    /// than walking up. Assigned by whoever owns both the horde
    /// and the player -- this class does not know a player exists, only where
    /// the camera is.
    /// </summary>
    public Action<Vector3, int, bool> AgentStruck { get; set; }

    /// <summary>
    /// A thrower or a spitter is in range with its cooldown spent: its
    /// position and its kind. The owner aims, budgets and fires, because the
    /// projectile pool and the throw budget are not the horde's.
    /// </summary>
    public Action<Vector3, int> RangedRequested { get; set; }

    /// <summary>
    /// The point the horde is hunting, live rather than quantised to a flow
    /// cell. The flow field routes everyone to the target's cell, about
    /// thirty metres across, and inside that cell it has no direction to give
    /// -- so without this a hunting crowd arrived and stood still round the
    /// player instead of closing on them.
    /// </summary>
    public Vector3 HuntPoint { get; set; } = Vector3.Up;

    /// <summary>One ally the crowd attacks besides the player: an upright
    /// body of a radius and a height, standing on a point.</summary>
    public struct AllyTarget
    {
        public Vector3 Base;
        public float Radius;
        public float Height;
    }

    private AllyTarget[] _allies = new AllyTarget[8];
    private int _allyCount;

    /// <summary>
    /// How near an ally a pickle has to be to leave the flow and go for it
    /// instead. The rest keep coming for the player.
    /// </summary>
    public float AllyAggroRange { get; set; } = 18.0f;

    /// <summary>
    /// A pickle reached an ally: which one (its place in the list last set),
    /// from where, and what kind. Raised on the main thread.
    /// </summary>
    public Action<int, Vector3, int> AllyStruck { get; set; }

    /// <summary>
    /// The allies this frame, before <see cref="Tick"/>. Read by every worker
    /// during it, so it is only ever set between ticks. Up to eight.
    /// </summary>
    public void SetAllies(System.Collections.Generic.IReadOnlyList<AllyTarget> allies)
    {
        _allyCount = Mathf.Min(allies.Count, _allies.Length);
        for (int i = 0; i < _allyCount; i++)
        {
            _allies[i] = allies[i];
        }
    }

    /// <summary>The nearest ally to a point by the reach to its body, or -1.</summary>
    private int NearestAlly(Vector3 position, out float distanceSquared)
    {
        distanceSquared = float.MaxValue;
        int best = -1;
        for (int i = 0; i < _allyCount; i++)
        {
            float d = DistanceToAllySquared(position, _allies[i]);
            if (d < distanceSquared)
            {
                distanceSquared = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Squared distance from a point to an upright body: the gap
    /// sideways past its radius, and above or below its height.</summary>
    private float DistanceToAllySquared(Vector3 position, in AllyTarget ally)
    {
        Vector3 up = _ground.UpAt(ally.Base);
        Vector3 offset = position - ally.Base;
        float height = offset.Dot(up);
        float side = Mathf.Max(0.0f, (offset - up * height).Length() - ally.Radius);
        float vertical = height < 0.0f ? -height : Mathf.Max(0.0f, height - ally.Height);
        return side * side + vertical * vertical;
    }

    /// <summary>
    /// How near the hunt point an agent with no flow direction steers straight
    /// at it. Zero switches the final approach off, which is the garrison:
    /// standing round the hive rather than piling onto it.
    /// </summary>
    public float FinalApproachRange { get; set; }

    /// <summary>
    /// Not hunting: agents within <see cref="RoamRadius"/> of
    /// <see cref="RoamCentre"/> roam it rather than follow the field to its
    /// middle. Each walks to a spot of its own in the disc, waits there, and
    /// every <see cref="RoamPeriod"/> seconds picks another; one whose spot or
    /// next step is somewhere blocked stands where it is. Following the field,
    /// a garrison of twenty-five thousand walked into one cell and stayed.
    /// </summary>
    public bool Roaming { get; set; }

    public Vector3 RoamCentre { get; set; }

    public float RoamRadius { get; set; }

    public float RoamSpeedScale { get; set; } = 0.35f;

    public float RoamPeriod { get; set; } = 9.0f;

    private float _roamClock;
    private Vector3 _roamAcross;
    private Vector3 _roamAlong;

    /// <summary>How close a lobbed cornichon has to pass the camera to land on the player.</summary>
    public float ThrownHitRadius { get; set; } = 1.4f;

    /// <summary>The stats an agent's kind runs on, for the owner resolving a strike or a throw.</summary>
    public AgentKindStats KindStats(int kind) =>
        _kindStats != null && kind >= 0 && kind < _kindStats.Length ? _kindStats[kind] : null;

    /// <summary>Pushed through to the head hitbox on every proxy.</summary>
    public float WeakPointMultiplier
    {
        set
        {
            if (_proxies == null)
            {
                return;
            }
            foreach (HordeHitProxy proxy in _proxies)
            {
                proxy.SetWeakPointMultiplier(value);
            }
        }
    }

    public int WeakPointKills { get; private set; }

    public int AgentCount => _count;

    /// <summary>
    /// How many of <see cref="AgentCount"/> are extras added by
    /// <see cref="SpawnAt"/>. Whoever drives the count asks for its base
    /// count plus this, so a spawned agent survives until it is killed.
    /// </summary>
    public int TransientCount { get; private set; }
    public int VisibleAgentCount { get; private set; }
    public int NearPlayerCount { get; private set; }

    /// <summary>
    /// Agents standing on <see cref="PressurePoint"/>. This is the overrun
    /// test, and it is a real cost rather than decoration: one extra squared
    /// distance per agent per frame, in the pass that was already parallel and
    /// already computing one.
    /// </summary>
    public int PressureCount { get; private set; }

    /// <summary>What "overrun" is measured around: whatever the horde is besieging.</summary>
    public Vector3 PressurePoint { get; set; } = Vector3.Up;

    public float PressureRadius { get; set; } = 26.0f;
    public int StrandedCount { get; private set; }

    /// <summary>Proxies currently standing in for a shootable agent.</summary>
    public int ActiveProxyCount { get; private set; }

    public int KillCount { get; private set; }

    /// <summary>
    /// Agents of the base crowd killed and not brought back (with
    /// <see cref="RespawnKilledAgents"/> off): whoever drives the count holds
    /// it at its base less these, so the population falls.
    /// </summary>
    public int BaseLost { get; private set; }

    // ---------------- Opaque agent handles ----------------
    //
    // An agent is an index and a generation. Packed into one long, it can be
    // handed to a permanent player tool or to a turret without either of them
    // learning what a horde is -- and the generation check that protects every
    // other damage path protects these too, so a slot refilled by a respawn
    // while something was holding it reports dead rather than handing over a
    // stranger. Zero is "not an agent", which is why the id starts at one.

    private static long Handle(int index, int id) => ((long)index << 32) | (uint)id;

    private bool Unpack(long handle, out int index)
    {
        index = (int)(handle >> 32);
        int id = (int)(handle & 0xFFFFFFFFL);
        HoldOrder();
        return handle != 0 && index >= 0 && index < _count && _agents[index].Id == id;
    }

    /// <summary>
    /// Something outside the horde is holding an agent by its handle, and a
    /// handle is an index: no reorder until it has been left alone for a
    /// while. Called by every handle use, so a held agent, a thrown one being
    /// tracked and a punt all keep the array still for as long as they last.
    /// </summary>
    private void HoldOrder()
    {
        _orderHeldUntil = _frame + ReorderHoldFrames;
    }

    /// <summary>
    /// Turns a borrowed collider back into the agent standing behind it. Zero
    /// when the node is not one of ours.
    /// </summary>
    public long ResolveAgent(Node3D node)
    {
        HoldOrder();
        HordeHitProxy proxy = node as HordeHitProxy ?? node?.GetParent() as HordeHitProxy;
        return proxy != null && proxy.IsAlive ? Handle(proxy.AgentIndex, proxy.AgentId) : 0L;
    }

    public float AgentMassOf(long handle)
    {
        if (!Unpack(handle, out int index))
        {
            return 0.0f;
        }

        AgentKindStats stats = _kindStats[_agents[index].Kind];
        return stats?.Mass ?? 70.0f;
    }

    /// <summary>
    /// Puts a held agent somewhere. False means it died or its slot was
    /// refilled, which is the caller's cue to let go.
    ///
    /// It writes position directly, because an agent has no velocity the solver
    /// integrates -- there is nothing for a servo to servo toward. The velocity
    /// is zeroed so the flow field does not drag it out of your hands.
    /// </summary>
    public bool MoveAgent(long handle, Vector3 position)
    {
        if (!Unpack(handle, out int index))
        {
            return false;
        }

        ref Agent agent = ref _agents[index];

        // Never below the ground: a hold point behind a slope or under the
        // player's feet would otherwise bury what is being carried.
        agent.Position = AboveGround(position, agent.Kind);
        agent.Velocity = Vector3.Zero;
        agent.Airborne = true;
        agent.Held = HeldGrace;
        agent.Flung = 0.0f;
        return true;
    }

    /// <summary>Where an agent is, or null once it died or its slot was refilled.</summary>
    public Vector3? AgentPosition(long handle) =>
        Unpack(handle, out int index) ? _agents[index].Position : null;

    /// <summary>Lets an agent go, optionally with a shove.</summary>
    public void ReleaseAgent(long handle, Vector3 velocity)
    {
        if (Unpack(handle, out int index))
        {
            _agents[index].Velocity = velocity;
            _agents[index].Held = 0.0f;

            // Off the ground until it lands. One that was standing and only
            // shoved along it lands again on the very next integrate.
            _agents[index].Airborne = true;

            // Without this the very next steer pass clamps the agent back to
            // one and a half times its walking speed, and a 42 m/s throw moved
            // it about thirty centimetres.
            _agents[index].Flung = velocity.LengthSquared() > 1e-4f ? FlingSeconds : 0.0f;
        }
    }

    /// <summary>
    /// The nearest agent to a point, through the spatial hash rather than a
    /// physics query.
    ///
    /// It has to be the hash, not a shape query: the borrowed colliders follow
    /// the *player*, so anything asking about a point the player is not
    /// standing on -- a turret holding a lane, a strike landing downrange --
    /// would find an empty pool by construction. That is the same bug the
    /// blast path already had to be rescued from once.
    ///
    /// <paramref name="verticalRange"/> limits the search above and below,
    /// for a caller that only wants the crowd on the ground: the hash is a
    /// cube of cells, and a 60 m range otherwise walks 60 m of empty sky too,
    /// fifty times the cells.
    /// </summary>
    public bool NearestAgent(Vector3 from, float range, out Vector3 position, out long handle, float verticalRange = -1.0f)
    {
        position = Vector3.Zero;
        handle = 0L;

        if (_count == 0 || _bucketHead == null)
        {
            return false;
        }

        int cellRange = Mathf.Max(1, Mathf.CeilToInt(range / HashCellSize));
        int cellRangeY = verticalRange > 0.0f
            ? Mathf.Clamp(Mathf.CeilToInt(verticalRange / HashCellSize), 1, cellRange)
            : cellRange;
        int cx = Mathf.FloorToInt(from.X / HashCellSize);
        int cy = Mathf.FloorToInt(from.Y / HashCellSize);
        int cz = Mathf.FloorToInt(from.Z / HashCellSize);

        float best = range * range;

        for (int dx = -cellRange; dx <= cellRange; dx++)
        {
            for (int dy = -cellRangeY; dy <= cellRangeY; dy++)
            {
                for (int dz = -cellRange; dz <= cellRange; dz++)
                {
                    int index = _bucketHead[CellHash(cx + dx, cy + dy, cz + dz)];

                    while (index != -1)
                    {
                        float distanceSquared = _agents[index].Position.DistanceSquaredTo(from);
                        if (distanceSquared < best)
                        {
                            best = distanceSquared;
                            position = _agents[index].Position;
                            handle = Handle(index, _agents[index].Id);
                        }

                        index = _hashNext[index];
                    }
                }
            }
        }

        return handle != 0;
    }

    /// <summary>
    /// Handles of up to <paramref name="max"/> agents within
    /// <paramref name="radius"/> of a point, nearest first not guaranteed, and
    /// none already held or flying. Through the spatial hash, for the reason
    /// <see cref="NearestAgent"/> records. For the manipulator's vortex.
    /// </summary>
    public int CollectAgents(Vector3 centre, float radius, int max, System.Collections.Generic.List<long> into)
    {
        into.Clear();
        if (_count == 0 || _bucketHead == null || max <= 0)
        {
            return 0;
        }

        int cellRange = Mathf.Max(1, Mathf.CeilToInt(radius / HashCellSize));
        int cx = Mathf.FloorToInt(centre.X / HashCellSize);
        int cy = Mathf.FloorToInt(centre.Y / HashCellSize);
        int cz = Mathf.FloorToInt(centre.Z / HashCellSize);
        float radiusSquared = radius * radius;

        for (int dx = -cellRange; dx <= cellRange; dx++)
        {
            for (int dy = -cellRange; dy <= cellRange; dy++)
            {
                for (int dz = -cellRange; dz <= cellRange; dz++)
                {
                    int index = _bucketHead[CellHash(cx + dx, cy + dy, cz + dz)];
                    while (index != -1)
                    {
                        ref Agent agent = ref _agents[index];
                        if (agent.Held <= 0.0f && !agent.Airborne
                            && agent.Position.DistanceSquaredTo(centre) <= radiusSquared)
                        {
                            into.Add(Handle(index, agent.Id));
                            if (into.Count >= max)
                            {
                                return into.Count;
                            }
                        }
                        index = _hashNext[index];
                    }
                }
            }
        }

        return into.Count;
    }

    /// <summary>Applies damage to an agent named by handle rather than by index.</summary>
    public void DamageAgent(long handle, DamageInfo info)
    {
        int index = (int)(handle >> 32);
        int id = (int)(handle & 0xFFFFFFFFL);
        DamageAgent(index, id, info);
    }

    /// <summary>How many agents of one kind are alive. For the overlay.</summary>
    public int LiveOfKind(int kind) =>
        _kindLive != null && kind >= 0 && kind < _kindLive.Length ? _kindLive[kind] : 0;

    /// <summary>
    /// Raised where an agent died, so the level can throw gibs and brine
    /// without this class having to know that either exists. Carries the
    /// kind's colour, so what sprays out is the colour of what died.
    /// </summary>
    [Signal]
    public delegate void AgentKilledEventHandler(Vector3 position, Vector3 up, Color colour);

    public override void _Ready()
    {
        // Before any perf recording starts, which fixes its columns.
        _ = SectionBlastSight + SectionBlastApply;

        _sectionUpdate = FrameProfiler.Section("horde update");
        _sectionHash = FrameProfiler.Section("horde hash");
        _sectionBuffer = FrameProfiler.Section("horde buffer");
        _sectionProxies = FrameProfiler.Section("hit proxies");
        _sectionSteer = FrameProfiler.Section("horde steer");
        _sectionIntegrate = FrameProfiler.Section("horde integrate");
        _sectionEvents = FrameProfiler.Section("horde events");
        _sectionUpload = FrameProfiler.Section("horde upload");
        _sectionReorder = FrameProfiler.Section("horde reorder");
        _sectionClassify = FrameProfiler.Section("horde classify");

        // See EventBus.AreaBlast's own doc comment for why this is a bus
        // signal rather than Grenade/Rocket calling in directly.
        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast += OnAreaBlast;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast -= OnAreaBlast;
        }
    }

    /// <summary>
    /// Allocates for the worst case once. Agent count is then a runtime dial
    /// that never reallocates -- InstanceCount reallocates the GPU buffer,
    /// VisibleInstanceCount does not.
    /// </summary>
    public void Initialise(IHordeGround ground, FlowField flow, HordeComposition composition,
                           int capacity, int hitProxyCount, float weakPointMultiplier)
    {
        _ground = ground;
        _flow = flow;
        _composition = composition;
        _capacity = Mathf.Max(1, capacity);

        _agents = new Agent[_capacity];
        _hashNext = new int[_capacity];
        _drawBucket = new byte[_capacity];
        _step = new byte[_capacity];
        _stepList = new int[_capacity];
        _listsValid = false;
        _reorderScratch = new Agent[_capacity];
        _cellStart = new int[_ground.CellCount + 1];

        int buckets = Mathf.NearestPo2(Mathf.Max(64, _capacity * 4));
        _bucketHead = new int[buckets];
        _bucketCount = new int[buckets];
        _bucketMask = buckets - 1;

        // Empty, not zero. Until the first Tick rebuilds the hash, a blast or
        // a nearest-agent query walks these chains -- and a zeroed head is
        // agent 0, whose zeroed next is agent 0 again. A rocket that went off
        // before the horde's first tick looped there until the process ran out
        // of memory and died without a word.
        System.Array.Fill(_bucketHead, -1);

        BuildKinds();
        BuildMultiMeshes();
        BuildHitProxies(hitProxyCount, weakPointMultiplier);
        ClearExtras();
        SetAgentCount(0);
    }

    /// <summary>
    /// Reads the composition into flat arrays once, so the step loop never
    /// touches a Resource. A Godot Resource property read inside a loop over
    /// twenty thousand agents is a marshalled call per access.
    /// </summary>
    private void BuildKinds()
    {
        _kindStats = new AgentKindStats[KindCount];
        _kindHalfHeight = new float[KindCount];
        _kindCapacity = new int[KindCount];
        _kindLive = new int[KindCount];
        _maxHalfHeight = AgentHalfHeight;

        for (int kind = 0; kind < KindCount; kind++)
        {
            AgentKindStats stats = _composition?.At(kind);
            _kindStats[kind] = stats;
            _kindHalfHeight[kind] = stats != null ? stats.Height * 0.5f : AgentHalfHeight;
            _maxHalfHeight = Mathf.Max(_maxHalfHeight, _kindHalfHeight[kind]);

            // The standard gets the whole capacity rather than its own share,
            // because it is the fallback: any kind that hits its cap spawns as
            // a standard instead, so a mix that leaves one kind unused has to
            // be absorbable. Every other kind pays only for its share, which is
            // what keeps five buffers from costing five hordes.
            _kindCapacity[kind] = _composition == null || kind == (int)AgentKind.Standard
                ? _capacity
                : Mathf.Min(_capacity, _composition.CapacityFor(kind, _capacity));
        }
    }

    /// <summary>
    /// Allocates the shootable pool once. The count is the ceiling on how many
    /// agents can be hit in a single frame, and a couple of dozen is already
    /// more than can be in front of a player at a 36 m horizon.
    /// </summary>
    private void BuildHitProxies(int count, float weakPointMultiplier)
    {
        if (_proxies != null)
        {
            foreach (HordeHitProxy proxy in _proxies)
            {
                proxy.QueueFree();
            }
        }

        _proxies = new HordeHitProxy[Mathf.Max(0, count)];
        for (int i = 0; i < _proxies.Length; i++)
        {
            HordeHitProxy proxy = new() { Name = $"HitProxy{i}", Horde = this };
            _proxies[i] = proxy;
            AddChild(proxy);
            proxy.Build(0.35f, 1.8f, weakPointMultiplier);
        }
    }

    private void BuildMultiMeshes()
    {
        if (_instances != null)
        {
            foreach (MultiMeshInstance3D old in _instances)
            {
                old?.QueueFree();
            }
        }

        _instances = new MultiMeshInstance3D[BucketCount];
        _multiMeshes = new MultiMesh[BucketCount];
        _multiMeshRids = new Rid[BucketCount];
        _allocated = new int[BucketCount];
        _transforms = new float[BucketCount][];

        Aabb groundSized = _ground.Bounds.Grow(60.0f);

        for (int drawn = 0; drawn < BucketCount; drawn++)
        {
            int kind = drawn / 2;
            bool far = (drawn & 1) == 1;
            AgentKindStats stats = _kindStats[kind];
            int capacity = _kindCapacity[kind];
            float agentRadius = stats != null ? stats.Radius : 0.35f;
            float agentHeight = stats != null ? stats.Height : 1.8f;

            _transforms[drawn] = new float[capacity * FloatsPerInstance];

            StandardMaterial3D material = new()
            {
                AlbedoColor = stats != null ? stats.Colour : new Color(0.46f, 0.52f, 0.38f),
                Roughness = 0.9f,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,

                // Toon: a hard terminator, so each pickle reads as a lit side
                // and a shadow side -- the art direction's bands, free.
                DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,

                // Past the detail distance an agent is a handful of pixels,
                // and there are tens of thousands of them: lit per vertex and
                // with no shadow lookup it reads the same and costs a fraction
                // of a per-pixel fragment with a shadow filter in it.
                ShadingMode = far ? BaseMaterial3D.ShadingModeEnum.PerVertex : BaseMaterial3D.ShadingModeEnum.PerPixel,
                DisableReceiveShadows = far,
            };

            // The far mesh is a five-sided tapered column: a capsule's outline
            // at forty metres and a fifth of its vertices.
            PrimitiveMesh mesh = far
                ? new CylinderMesh
                {
                    TopRadius = agentRadius * 0.75f,
                    BottomRadius = agentRadius,
                    Height = agentHeight,
                    RadialSegments = 5,
                    Rings = 0,
                }
                // One ring, not two: 72 triangles instead of 108, and the same
                // six-sided silhouette with a dome a step coarser. Every near
                // agent is drawn twice -- Forward+ renders a depth prepass --
                // and with a crowd on the player the near capsules measured
                // about a millisecond of GPU on the target machine.
                : new CapsuleMesh
                {
                    Radius = agentRadius,
                    Height = agentHeight,
                    RadialSegments = 6,
                    Rings = 1,
                };
            mesh.Material = material;

            _multiMeshes[drawn] = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = false,
                UseCustomData = false,
                Mesh = mesh,
                InstanceCount = MinAllocated,
                VisibleInstanceCount = 0,

                // The resource's own bounds, not just the instance's: without
                // it every buffer write has the engine walk every transform to
                // recompute an AABB nobody uses.
                CustomAabb = groundSized,
            };

            _instances[drawn] = new MultiMeshInstance3D
            {
                Name = $"Horde{(AgentKind)kind}{(far ? "Far" : "")}",
                Multimesh = _multiMeshes[drawn],

                // Shadow casting on a few thousand capsules is the single most
                // expensive thing this subsystem can do on an integrated GPU.
                // A crowd reads by its ink and its toon bands. Required on
                // every one of them, not just the first: losing it on one
                // brings the whole cost back.
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

                // Explicit and ground-sized, so writing the buffer never
                // triggers an engine-side AABB recompute over every instance.
                // Also required on all of them, for the same reason.
                CustomAabb = groundSized,

                // The buffer holds world positions, so the instance sits at
                // the world origin whatever its parents do. On the flats the
                // director stood at the origin and it made no difference; on
                // the planet the director stands at the garrison, a radius
                // out, and every pickle was drawn a radius up in the sky.
                TopLevel = true,
            };

            AddChild(_instances[drawn]);
            _multiMeshRids[drawn] = _multiMeshes[drawn].GetRid();
            _allocated[drawn] = MinAllocated;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_instances != null)
        {
            foreach (MultiMeshInstance3D instance in _instances)
            {
                if (instance != null)
                {
                    instance.Visible = enabled;
                }
            }
        }

        if (!enabled)
        {
            // Tick is what maintains these, and it stops being called. Leaving
            // the last live values on screen would read as the horde still
            // drawing while it is switched off.
            VisibleAgentCount = 0;
            NearPlayerCount = 0;
            PressureCount = 0;
            StrandedCount = 0;

            // Leaving the proxies live would let you shoot a horde that is
            // switched off, and would keep their colliders in the broadphase.
            if (_proxies != null)
            {
                foreach (HordeHitProxy proxy in _proxies)
                {
                    proxy.Release();
                }
            }
            ActiveProxyCount = 0;
        }
    }

    /// <summary>
    /// Grows or shrinks the live horde. New agents are seeded on a ring around
    /// the target rather than uniformly over the ground, so raising the count
    /// mid-session produces pressure you can actually see. A seed radius of
    /// zero scatters over the whole ground.
    /// </summary>
    public void SetAgentCount(int count, Vector3 seedCentre = default, float seedRadius = 0.0f)
    {
        int wanted = Mathf.Clamp(count, 0, _capacity);
        _seedCentre = seedCentre;
        _seedRadius = seedRadius;
        BuildPacks(seedCentre, seedRadius, false);

        // Grow over frames rather than all at once. Every spawn samples the
        // terrain and the flow grid, so jumping from 200 to 20,000 in a single
        // call is tens of thousands of samples plus a 20,000-instance buffer
        // upload inside one frame -- a hitch long enough that Windows can
        // decide the GPU has stopped responding. Shrinking is free and stays
        // immediate.
        int clamped = wanted > _count
            ? Mathf.Min(wanted, _count + MaxSpawnsPerCall)
            : wanted;

        // Shrinking has to take the dropped agents off the per-kind census, or
        // it drifts upward every time the stress ladder comes back down and
        // kinds start failing their capacity check while their buffers are
        // empty.
        for (int i = clamped; i < _count; i++)
        {
            _kindLive[_agents[i].Kind]--;
            CountExtra(_agents[i].Origin, -1);
        }

        for (int i = _count; i < clamped; i++)
        {
            SpawnAgent(ref _agents[i], seedCentre, seedRadius);
        }

        _count = clamped;
    }

    /// <summary>
    /// Moves where new and respawned agents are seeded, without spawning
    /// anything. Called every frame while the horde hunts, so the seed ring
    /// stays round the player; the packs themselves only move once the centre
    /// has gone far enough to matter.
    /// </summary>
    public void SetSeed(Vector3 seedCentre, float seedRadius)
    {
        _seedCentre = seedCentre;
        _seedRadius = seedRadius;
        BuildPacks(seedCentre, seedRadius, false);
    }

    public void Reseed(Vector3 seedCentre, float seedRadius)
    {
        _seedCentre = seedCentre;
        _seedRadius = seedRadius;
        BuildPacks(seedCentre, seedRadius, true);

        System.Array.Clear(_kindLive, 0, _kindLive.Length);

        // Every slot comes back as an ordinary agent, so the extras are gone
        // and whoever drives the count trims back to its base on the next call.
        ClearExtras();

        for (int i = 0; i < _count; i++)
        {
            SpawnAgent(ref _agents[i], seedCentre, seedRadius);
        }

        KillCount = 0;
        WeakPointKills = 0;
        BaseLost = 0;
    }

    /// <summary>
    /// Adds one agent of a named kind on the ground under a point, for a nest
    /// producing what it produces. Returns false when the horde is already at
    /// capacity.
    /// </summary>
    public bool SpawnAt(Vector3 point, int kind, SpawnOrigin origin = SpawnOrigin.Brood)
    {
        if (_count >= _capacity || _ground == null)
        {
            return false;
        }

        int index = _count;
        SpawnAgent(ref _agents[index], Vector3.Zero, 0.0f, point, kind);
        _agents[index].Origin = origin == SpawnOrigin.Base ? SpawnOrigin.Brood : origin;
        CountExtra(_agents[index].Origin, 1);
        _count = index + 1;
        return true;
    }

    /// <summary>
    /// Adds one agent thrown from a point in the air rather than stood on the
    /// ground -- a gherkin shed off the side of something huge. Returns false
    /// when the horde is already at capacity.
    ///
    /// It is an ordinary spawn followed by exactly what a manipulator release
    /// writes, so it lands, takes landing damage and rejoins the flow field
    /// through code that already exists; nothing here is new behaviour.
    /// </summary>
    public bool SpawnFlung(Vector3 position, Vector3 velocity, int kind, SpawnOrigin origin = SpawnOrigin.Shed,
                           bool lobbed = false)
    {
        if (!SpawnAt(position, kind, origin))
        {
            return false;
        }

        ref Agent agent = ref _agents[_count - 1];

        agent.Position = AboveGround(position, agent.Kind);
        agent.Velocity = velocity;
        agent.Airborne = true;
        agent.Lobbed = lobbed;
        agent.Cooldown = 0.0f;
        agent.Flung = velocity.LengthSquared() > 1e-4f ? FlingSeconds : 0.0f;
        return true;
    }

    private void SpawnAgent(ref Agent agent, Vector3 seedCentre, float seedRadius,
                            Vector3? forcedPoint = null, int forcedKind = -1)
    {
        Vector3 point;

        if (forcedPoint.HasValue)
        {
            point = forcedPoint.Value;
        }
        else if (_packCentres.Length > 0)
        {
            int pack = Mathf.Min(
                (int)(RandomUnit() * _packCentres.Length),
                _packCentres.Length - 1);
            point = _ground.Scatter(_packCentres[pack], PackRadius, 0.0f, RandomUnit(), RandomUnit());
        }
        else if (seedRadius > 0.0f)
        {
            point = _ground.Scatter(seedCentre, seedRadius, Mathf.Min(SeedMinRadius, seedRadius),
                                    RandomUnit(), RandomUnit());
        }
        else
        {
            Aabb bounds = _ground.Bounds;
            point = _ground.Scatter(bounds.GetCenter(), Mathf.Max(bounds.Size.X, bounds.Size.Z) * 0.5f, 0.0f,
                                    RandomUnit(), RandomUnit());
        }

        Vector3 surface = _ground.GroundBelow(point, out int cell);
        Vector3 up = _ground.UpAt(surface);

        int kind = ChooseKind(forcedKind);

        AgentKindStats stats = _kindStats[kind];
        float halfHeight = _kindHalfHeight[kind];

        agent.Kind = (byte)kind;
        // Staggered, so a pack seeded in one frame does not all fire on the
        // same one.
        agent.Cooldown = stats != null ? RandomUnit() * FirstInterval(stats) : 0.0f;
        agent.Position = surface + (up * halfHeight);
        agent.Velocity = Vector3.Zero;
        agent.Flung = 0.0f;
        agent.Airborne = false;
        agent.Lobbed = false;
        agent.Held = 0.0f;
        agent.Origin = SpawnOrigin.Base;

        float speedScale = stats?.SpeedMultiplier ?? 1.0f;
        agent.TargetSpeed = AgentSpeed * speedScale
            * (1.0f + ((RandomUnit() - 0.5f) * 2.0f * SpeedVariance));
        agent.Health = AgentHealth * (stats?.HealthMultiplier ?? 1.0f);
        agent.Id = _nextAgentId++;

        Vector3 tangent = up.Cross(Vector3.Up);
        if (tangent.LengthSquared() < 1e-6f)
        {
            tangent = up.Cross(Vector3.Right);
        }
        agent.Forward = tangent.Normalized();
        agent.Cell = cell;

        _kindLive[kind]++;
    }

    /// <summary>
    /// Rolls a kind for a spawn, honouring each kind's share of the instance
    /// budget. A kind at its cap falls back to the standard rather than failing
    /// to spawn, so the horde is always the size it was asked for -- the caps
    /// are a memory bill, not a population rule.
    /// </summary>
    private int ChooseKind(int forced)
    {
        if (_composition == null || !KindsEnabled)
        {
            return (int)AgentKind.Standard;
        }

        int kind = forced >= 0 && forced < KindCount
            ? forced
            : _composition.Pick(RandomUnit());

        if (_kindStats[kind] == null || _kindLive[kind] >= _kindCapacity[kind])
        {
            return (int)AgentKind.Standard;
        }

        return kind;
    }

    // ---------------- Being shot ----------------

    /// <summary>
    /// Applies damage routed in from a hit proxy.
    ///
    /// The id check is the whole reason agents carry a generation. A shotgun
    /// spread or a grenade resolves several hits against indices captured
    /// before any of them landed, and a slot refilled by a respawn in between
    /// would otherwise inherit the rest of the volley.
    /// </summary>
    public void DamageAgent(int index, int id, DamageInfo info)
    {
        if (info == null || index < 0 || index >= _count || _agents[index].Id != id)
        {
            return;
        }

        ref Agent agent = ref _agents[index];
        agent.Health -= info.Amount;
        agent.HitAt = HordeClock;

        Vector3 position = agent.Position;
        Vector3 up = _ground.UpAt(position);

        // The agent's own share of its kind's health, which only this loop can
        // work out: an agent is a struct in a flat array with no node, so
        // nothing outside here could be asked what is left of one.
        AgentKindStats kind = _kindStats[agent.Kind];

        float full = AgentHealth * (kind?.HealthMultiplier ?? 1.0f);
        info.HealthFraction = full > 0.0f ? Mathf.Max(0.0f, agent.Health) / full : 0.0f;

        // Above its own silhouette rather than at its feet, so a bar sits over
        // the thing it describes and a taller kind carries a higher one.
        info.TargetPosition = position + (up * ((kind?.Height ?? 1.8f) + 0.4f));

        // Set before the hit reaches the bus, not after: RespawnKilledAgents
        // refills this slot immediately below, so the population never
        // visibly shrinks and a kill against the horde would otherwise be
        // indistinguishable from any other hit to anything listening on
        // DamageDealt -- the HUD hitmarker specifically.
        info.Killed = agent.Health <= 0.0f;

        // Straight onto the bus, which is what lights the HUD hitmarker. The
        // horde never learns that a HUD exists. Inside a blast the hits are
        // summed instead and announced once -- see ApplyBlastDamage.
        if (_batch.Active)
        {
            _batch.Add(info, info.TargetPosition, info.Killed, kind);
        }
        else
        {
            EventBus.EmitDamageDealt(this, info);
        }

        if (agent.Health > 0.0f)
        {
            // A hit that does not kill still shoves, so fire reads as having
            // weight even when it does not connect fatally.
            agent.Velocity += info.Direction.Slide(up) * 2.0f;
            if (!_batch.Active)
            {
                PickleAudio.Play(Sfx.AgentHurt, position, kind);
            }
            return;
        }

        if (!_batch.Active)
        {
            PickleAudio.Play(Sfx.AgentDie, position, kind);
        }

        KillCount++;
        if (info.IsWeakPoint)
        {
            WeakPointKills++;
        }
        EmitSignal(SignalName.AgentKilled, position, up, kind?.Colour ?? Colors.Green);
        RemoveAgent(index);
    }

    /// <summary>
    /// Refills the slot from the seed ring, or swap-removes it. A transient
    /// agent is always removed: it was never part of the load being held.
    ///
    /// Refilling is the default because a measurement session wants the load
    /// held constant: if shooting thinned the horde, the frame time would
    /// improve for the wrong reason and the numbers would drift under the
    /// person reading them.
    /// </summary>
    private void RemoveAgent(int index)
    {
        _kindLive[_agents[index].Kind]--;

        bool transient = _agents[index].Origin != SpawnOrigin.Base;
        CountExtra(_agents[index].Origin, -1);

        if (RespawnKilledAgents && !transient)
        {
            SpawnAgent(ref _agents[index], _seedCentre, _seedRadius);
            return;
        }
        if (!transient)
        {
            BaseLost++;
        }

        int last = _count - 1;
        if (index != last)
        {
            _agents[index] = _agents[last];
        }
        _count = last;
    }

    // ---------------- Area damage (blasts) ----------------

    /// <summary>
    /// Cap on agents one blast will resolve against, the same reasoning as
    /// <see cref="Blast.MaxResults"/>: bounded so a blast landing in the
    /// middle of a dense horde cannot cost an unbounded number of raycasts.
    /// </summary>
    /// <summary>
    /// Cap on agents one blast resolves against, nearest first. Every candidate
    /// past the distance test costs a line-of-sight raycast, so this is a real
    /// budget rather than a safety net: a nine-bomb salvo is this many rays on
    /// each of nine frames. Measure before raising it.
    /// </summary>
    public int MaxBlastAgents { get; set; } = 384;

    /// <summary>
    /// The hits of one blast, summed while it resolves and announced once
    /// after. A rocket into a dense crowd reaches up to MaxBlastAgents agents,
    /// and announcing each was a bus signal, a HUD hitmarker, a sound request
    /// and the HUD's health-bar and damage-number bookkeeping per agent:
    /// measured at 4-18 ms of a single blast frame. One DamageDealt carrying
    /// the total says the same thing to everything listening -- a hitmarker,
    /// one big number over the nearest victim, one death sound -- for one of
    /// each.
    /// </summary>
    private struct BlastBatch
    {
        public bool Active;
        public float Damage;
        public int Hits;
        public int Kills;
        public bool WeakPoint;
        public Vector3 NearestTarget;
        public float NearestDistance;
        public DamageInfo First;
        public AgentKindStats NearestKind;

        public void Begin()
        {
            this = default;
            Active = true;
            NearestDistance = float.MaxValue;
        }

        public void Add(DamageInfo info, Vector3 target, bool killed, AgentKindStats kind)
        {
            Damage += info.Amount;
            Hits++;
            WeakPoint |= info.IsWeakPoint;
            if (killed)
            {
                Kills++;
            }
            if (info.Distance < NearestDistance)
            {
                NearestDistance = info.Distance;
                NearestTarget = target;
                First = info;
                NearestKind = kind;
            }
        }
    }

    private BlastBatch _batch;

    /// <summary>
    /// Line of sight per hash cell, for one blast. Agents a hash cell apart
    /// (2.5 m) behind the same cover get the same answer, so a rocket into a
    /// packed crowd casts one ray per cell rather than one per agent: up to
    /// 384 rays became a few dozen. Cover is resolved to the cell, which is
    /// finer than any wall a blast is stopped by.
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<long, bool> _blastSight = new();

    /// <summary>Reused between blasts so a salvo does not allocate per bomb.</summary>
    private readonly List<(int Index, int Id, float DistanceSquared)> _blastCandidates = new();

    private static readonly int SectionBlastSight = FrameProfiler.Section("horde blast sight");
    private static readonly int SectionBlastApply = FrameProfiler.Section("horde blast apply");

    private void OnAreaBlast(BlastInfo blast)
    {
        ApplyBlastDamage(blast);
    }

    /// <summary>
    /// Reached from <see cref="EventBus.AreaBlast"/> instead of a collider.
    ///
    /// <see cref="Blast.ApplyDamage"/>'s sphere-overlap query can only ever
    /// find the agents currently wearing one of the pool's borrowed colliders
    /// -- a couple of dozen, and only the ones nearest the player (see
    /// HitProxyRange/HitProxyCount). An airstrike or a rocket is routinely
    /// called in tens of metres past that pool, which is the entire reason
    /// those weapons exist, so its blast was landing on nothing: "big boom,
    /// nobody dies." This walks the same spatial hash <see cref="Separation"/>
    /// already builds every frame instead of a physics query, so it can
    /// afford to check every agent actually inside the blast rather than only
    /// the ones a collider happens to be following the player onto right now.
    /// </summary>
    /// <returns>How many agents were hurt.</returns>
    public int ApplyBlastDamage(BlastInfo blast)
    {
        if (blast == null || _count == 0 || _bucketHead == null)
        {
            return 0;
        }

        PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return 0;
        }

        if (blast.IsLine)
        {
            return ApplyLineDamage(blast);
        }

        int cellRange = Mathf.Max(1, Mathf.CeilToInt(blast.Radius / HashCellSize));
        int cx = Mathf.FloorToInt(blast.Centre.X / HashCellSize);
        int cy = Mathf.FloorToInt(blast.Centre.Y / HashCellSize);
        int cz = Mathf.FloorToInt(blast.Centre.Z / HashCellSize);
        float radiusSquared = blast.Radius * blast.Radius;

        // Gather everything in range first, cap second.
        //
        // The cap used to be applied inside the dx/dy/dz walk, which meant it
        // spent its whole budget on whichever corner of the scan box the loop
        // started in: a big blast in a dense horde killed the agents furthest
        // in -X -Y -Z and left the ones standing on the detonation alive.
        // Gathering costs nothing -- only the line-of-sight ray below is
        // expensive -- so the cap can mean what it says, the nearest agents.
        _blastCandidates.Clear();

        for (int dx = -cellRange; dx <= cellRange; dx++)
        {
            for (int dy = -cellRange; dy <= cellRange; dy++)
            {
                for (int dz = -cellRange; dz <= cellRange; dz++)
                {
                    int index = _bucketHead[CellHash(cx + dx, cy + dy, cz + dz)];

                    while (index != -1)
                    {
                        int next = _hashNext[index];

                        float distanceSquared = _agents[index].Position.DistanceSquaredTo(blast.Centre);
                        if (distanceSquared <= radiusSquared)
                        {
                            _blastCandidates.Add((index, _agents[index].Id, distanceSquared));
                        }

                        index = next;
                    }
                }
            }
        }

        _blastCandidates.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));

        int hit = 0;
        _batch.Begin();
        _blastSight.Clear();

        for (int i = 0; i < _blastCandidates.Count && hit < MaxBlastAgents; i++)
        {
            (int index, int id, float distanceSquared) = _blastCandidates[i];

            // The id was captured before any of this blast's damage landed, and
            // the sorted list now outlives those mutations -- a slot refilled
            // by a respawn partway through must not inherit the rest of it.
            if (_agents[index].Id != id)
            {
                continue;
            }

            float distance = Mathf.Sqrt(distanceSquared);
            float multiplier = blast.MultiplierAt(distance);
            if (multiplier <= 0.0f)
            {
                continue;
            }

            Vector3 position = _agents[index].Position;

            // Cover means something for the horde too, or an airstrike would
            // strip a building's far side for free -- see Blast.ApplyDamage.
            long cell = SightCellKey(position);
            if (!_blastSight.TryGetValue(cell, out bool seen))
            {
                using (FrameProfiler.Begin(SectionBlastSight))
                {
                    seen = Blast.HasLineOfSight(space, blast.Centre, position, default, blast.SourceRid);
                }
                _blastSight[cell] = seen;
            }
            if (!seen)
            {
                continue;
            }

            using (FrameProfiler.Begin(SectionBlastApply))
            {
                ApplyBlastToAgent(index, position, distance, multiplier, blast);
            }
            hit++;
        }

        BlastBatch batch = _batch;
        _batch = default;
        if (batch.Hits > 0)
        {
            AnnounceBatch(batch, blast);
        }

        return hit;
    }

    private void AnnounceBatch(BlastBatch batch, BlastInfo blast)
    {
        DamageInfo summary = new()
        {
            Amount = batch.Damage,
            BaseAmount = blast.Damage,
            Position = batch.First.Position,
            Normal = batch.First.Normal,
            Direction = batch.First.Direction,
            Distance = batch.NearestDistance,
            Source = blast.Source,
            IsSplash = true,
            IsWeakPoint = batch.WeakPoint,
            Killed = batch.Kills > 0,
            KillCount = batch.Kills,
            TargetPosition = batch.NearestTarget,
            IsSustained = blast.Sustained,
        };
        EventBus.EmitDamageDealt(this, summary);
        PickleAudio.Play(batch.Kills > 0 ? Sfx.AgentDie : Sfx.AgentHurt, batch.NearestTarget, batch.NearestKind);
    }

    /// <summary>Per-agent stamp, so a line visiting overlapping cells takes each agent once.</summary>
    private int[] _lineStamp;
    private int _lineStampValue;

    /// <summary>
    /// <see cref="BlastInfo.IsLine"/>: every agent within the radius of a
    /// segment, nearest the shooter first, capped at
    /// <see cref="MaxBlastAgents"/>. The hash is walked in steps of one cell
    /// along the segment rather than over its bounding box, which for a
    /// 200 m beam would be millions of cells; a few thousand bucket reads a
    /// tick is the whole cost. No sight rays: the caller ended the segment
    /// at the first wall.
    /// </summary>
    private int ApplyLineDamage(BlastInfo blast)
    {
        Vector3 from = blast.Centre;
        Vector3 to = blast.LineEnd;
        Vector3 axis = to - from;
        float length = axis.Length();
        if (length < 0.01f)
        {
            return 0;
        }
        axis /= length;

        if (_lineStamp == null || _lineStamp.Length < _agents.Length)
        {
            _lineStamp = new int[_agents.Length];
            _lineStampValue = 0;
        }
        _lineStampValue++;

        float radiusSquared = blast.Radius * blast.Radius;
        int cellRange = Mathf.Max(1, Mathf.CeilToInt(blast.Radius / HashCellSize));
        int steps = Mathf.CeilToInt(length / HashCellSize);
        long lastCell = long.MinValue;

        _blastCandidates.Clear();

        for (int step = 0; step <= steps; step++)
        {
            Vector3 sample = from + (axis * Mathf.Min(step * HashCellSize, length));
            int cx = Mathf.FloorToInt(sample.X / HashCellSize);
            int cy = Mathf.FloorToInt(sample.Y / HashCellSize);
            int cz = Mathf.FloorToInt(sample.Z / HashCellSize);
            long cell = SightCellKey(sample);
            if (cell == lastCell)
            {
                continue;
            }
            lastCell = cell;

            for (int dx = -cellRange; dx <= cellRange; dx++)
            {
                for (int dy = -cellRange; dy <= cellRange; dy++)
                {
                    for (int dz = -cellRange; dz <= cellRange; dz++)
                    {
                        int index = _bucketHead[CellHash(cx + dx, cy + dy, cz + dz)];

                        while (index != -1)
                        {
                            int next = _hashNext[index];

                            if (_lineStamp[index] != _lineStampValue)
                            {
                                _lineStamp[index] = _lineStampValue;
                                Vector3 offset = _agents[index].Position - from;
                                float along = Mathf.Clamp(offset.Dot(axis), 0.0f, length);
                                if ((offset - (axis * along)).LengthSquared() <= radiusSquared)
                                {
                                    // Sorted by distance along the beam, so the
                                    // cap takes the ones nearest the muzzle.
                                    _blastCandidates.Add((index, _agents[index].Id, along * along));
                                }
                            }

                            index = next;
                        }
                    }
                }
            }
        }

        _blastCandidates.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));

        int hit = 0;
        _batch.Begin();

        for (int i = 0; i < _blastCandidates.Count && hit < MaxBlastAgents; i++)
        {
            (int index, int id, float alongSquared) = _blastCandidates[i];
            if (_agents[index].Id != id)
            {
                continue;
            }

            Vector3 position = _agents[index].Position;
            Vector3 offset = position - from;
            float off = (offset - (axis * Mathf.Clamp(offset.Dot(axis), 0.0f, length))).Length();
            float multiplier = blast.MultiplierAt(off);
            if (multiplier <= 0.0f)
            {
                continue;
            }

            using (FrameProfiler.Begin(SectionBlastApply))
            {
                ApplyBlastToAgent(index, position, Mathf.Sqrt(alongSquared), multiplier, blast);
            }
            hit++;
        }

        BlastBatch batch = _batch;
        _batch = default;
        if (batch.Hits > 0)
        {
            AnnounceBatch(batch, blast);
        }

        return hit;
    }

    private long SightCellKey(Vector3 position)
    {
        long x = Mathf.FloorToInt(position.X / HashCellSize) & 0x1FFFFF;
        long y = Mathf.FloorToInt(position.Y / HashCellSize) & 0x1FFFFF;
        long z = Mathf.FloorToInt(position.Z / HashCellSize) & 0x1FFFFF;
        return (x << 42) | (y << 21) | z;
    }

    private void ApplyBlastToAgent(int index, Vector3 position, float distance, float multiplier, BlastInfo blast)
    {
        Vector3 direction = position - blast.Centre;
        direction = direction.LengthSquared() < 0.0001f ? blast.Up : direction.Normalized();

        DamageInfo info = new()
        {
            Amount = blast.Damage * multiplier,
            BaseAmount = blast.Damage,
            Position = position,
            Normal = -direction,
            Direction = direction,
            Distance = distance,
            Source = blast.Source,
            IsSplash = true,
        };

        DamageAgent(index, _agents[index].Id, info);
    }

    /// <summary>
    /// Hands the pool of colliders to the nearest agents.
    ///
    /// Through the spatial hash first, in a growing cube of cells round the
    /// player, because at tens of thousands of agents a pass over all of them
    /// -- with an insertion into the pool for every one inside the range,
    /// which in a crowd is most of them -- was the single most expensive
    /// serial thing the horde did. A crowd dense enough to cost that much
    /// fills the pool within a few metres. Only a thin one, where the pool is
    /// not full inside <see cref="ProxySearchCells"/>, falls back to the scan,
    /// and a thin crowd is a cheap one to scan.
    /// </summary>
    private void UpdateHitProxies(Vector3 playerPosition)
    {
        if (_proxies == null || _proxies.Length == 0)
        {
            return;
        }

        // Who wears a collider is chosen every few frames; where the
        // colliders are is updated every frame, so a hitbox never trails the
        // agent it stands for. The nearest few dozen in a crowd change slowly
        // and the choosing is the expensive half.
        // A sparse crowd -- too few agents near to fill the pool from the hash
        // round the player -- costs a scan of the whole horde to choose, so it
        // is chosen four times less often. Who is already wearing a collider
        // keeps it in between; only a newcomer waits, a fifth of a second at
        // most. This scan ran every frame while nobody was near, because an
        // empty pool skipped the frame gate: 1.1 ms of an idle frame.
        int reselectEvery = _proxySparse ? ProxyReselectFrames * 4 : ProxyReselectFrames;
        if (_proxyFrame++ % reselectEvery != 0)
        {
            RefreshHitProxies();
            return;
        }

        int slots = _proxies.Length;
        Span<int> best = stackalloc int[slots];
        Span<float> bestDistance = stackalloc float[slots];
        int found = 0;
        float rangeSquared = HitProxyRange * HitProxyRange;

        bool searched = false;
        if (_bucketHead != null && HashCellSize > 0.0f && !_proxySparse)
        {
            int cx = Mathf.FloorToInt(playerPosition.X / HashCellSize);
            int cy = Mathf.FloorToInt(playerPosition.Y / HashCellSize);
            int cz = Mathf.FloorToInt(playerPosition.Z / HashCellSize);

            for (int reach = 3; reach <= ProxySearchCells && !searched; reach += 3)
            {
                found = 0;
                float reachSquared = Mathf.Min(rangeSquared, (reach * HashCellSize) * (reach * HashCellSize));

                for (int dx = -reach; dx <= reach; dx++)
                {
                    for (int dy = -reach; dy <= reach; dy++)
                    {
                        for (int dz = -reach; dz <= reach; dz++)
                        {
                            int index = _bucketHead[CellHash(cx + dx, cy + dy, cz + dz)];
                            while (index != -1)
                            {
                                float distance = _agents[index].Position.DistanceSquaredTo(playerPosition);
                                if (distance <= reachSquared)
                                {
                                    KeepNearest(index, distance, best, bestDistance, ref found);
                                }
                                index = _hashNext[index];
                            }
                        }
                    }
                }

                // Full inside a sphere the cube contains: nothing outside it
                // can be nearer than what was kept, so the answer is exact.
                searched = found >= slots || reachSquared >= rangeSquared;
            }
        }

        if (!searched)
        {
            found = 0;
            for (int i = 0; i < _count; i++)
            {
                float distance = _agents[i].Position.DistanceSquaredTo(playerPosition);
                if (distance <= rangeSquared)
                {
                    KeepNearest(i, distance, best, bestDistance, ref found);
                }
            }

            // Sparse while the pool cannot be filled from inside the hash's
            // reach: the next selection skips straight to the scan rather
            // than walking ten thousand cells to find that out again. Once
            // the scan fills the pool that close, the hash takes over.
            float reach = ProxySearchCells * HashCellSize;
            _proxySparse = found < slots || bestDistance[slots - 1] > reach * reach;
        }

        // Stable assignment: an agent still among the nearest keeps the
        // collider it already wears, and only newcomers take a free one. Slot
        // i used to go to the i-th nearest, so every reselection reshuffled
        // nearly every collider -- and a collider changing hands to another
        // kind resizes its capsule, which Jolt rebuilds. With the bike's gun
        // in a fresh crowd that churn was over a millisecond a frame.
        if (_chosenStamp.Length != _agents.Length)
        {
            _chosenStamp = new int[_agents.Length];
        }
        int stamp = ++_selectStamp;
        if (stamp == int.MaxValue)
        {
            System.Array.Clear(_chosenStamp);
            _selectStamp = stamp = 1;
        }

        for (int k = 0; k < found; k++)
        {
            _chosenStamp[best[k]] = stamp;
        }

        int active = 0;
        Span<bool> free = stackalloc bool[slots];
        for (int slot = 0; slot < slots; slot++)
        {
            HordeHitProxy proxy = _proxies[slot];
            int index = proxy.AgentIndex;
            if (proxy.IsAlive && index >= 0 && index < _count
                && _agents[index].Id == proxy.AgentId && _chosenStamp[index] == stamp)
            {
                // Kept: brought into line, node and all, on the reselection.
                _chosenStamp[index] = -stamp;
                Vector3 position = _agents[index].Position;
                proxy.Assign(index, proxy.AgentId, position, _ground.UpAt(position));
                active++;
            }
            else
            {
                free[slot] = true;
            }
        }

        int next = 0;
        for (int k = 0; k < found; k++)
        {
            int index = best[k];
            if (_chosenStamp[index] != stamp)
            {
                continue;
            }

            while (next < slots && !free[next])
            {
                next++;
            }
            if (next >= slots)
            {
                break;
            }

            free[next] = false;
            Vector3 position = _agents[index].Position;
            int kind = _agents[index].Kind;
            AgentKindStats stats = _kindStats[kind];
            _proxies[next].SetShape(kind, stats?.Radius ?? 0.35f, stats?.Height ?? 1.8f);
            _proxies[next].Assign(index, _agents[index].Id, position, _ground.UpAt(position));
            active++;
        }

        for (int slot = 0; slot < slots; slot++)
        {
            if (free[slot])
            {
                _proxies[slot].Release();
            }
        }

        _proxyChosen = slots;
        ActiveProxyCount = active;
    }

    private int[] _chosenStamp = System.Array.Empty<int>();
    private int _selectStamp;

    /// <summary>
    /// Moves every worn collider to its agent, between selections. An agent
    /// that died or whose slot was refilled since gives its collider up.
    /// </summary>
    private void RefreshHitProxies()
    {
        int active = 0;
        for (int slot = 0; slot < _proxyChosen && slot < _proxies.Length; slot++)
        {
            HordeHitProxy proxy = _proxies[slot];
            if (!proxy.IsAlive)
            {
                continue;
            }

            int index = proxy.AgentIndex;
            if (index < 0 || index >= _count || _agents[index].Id != proxy.AgentId)
            {
                proxy.Release();
                continue;
            }

            Vector3 position = _agents[index].Position;
            proxy.Assign(index, proxy.AgentId, position, _ground.UpAt(position), syncNode: false);
            active++;
        }
        ActiveProxyCount = active;
    }

    private int _proxyFrame;
    private int _proxyChosen;
    private bool _proxySparse;

    /// <summary>Frames between choosing who wears the colliders.</summary>
    private const int ProxyReselectFrames = 3;

    /// <summary>
    /// Hash cells either side of the player the proxy search walks before
    /// giving up on the hash and scanning. At the hash's cell size of about
    /// two metres, nine is a twenty-metre cube: a crowd that has not filled
    /// the pool inside that is thin enough to scan.
    /// </summary>
    private const int ProxySearchCells = 9;

    /// <summary>Keeps the closest few in a small sorted array: an insertion, not a sort of the whole horde.</summary>
    private static void KeepNearest(int index, float distance, Span<int> best, Span<float> bestDistance, ref int found)
    {
        int slots = best.Length;
        int at;
        if (found < slots)
        {
            at = found++;
        }
        else if (distance < bestDistance[slots - 1])
        {
            at = slots - 1;
        }
        else
        {
            return;
        }

        while (at > 0 && bestDistance[at - 1] > distance)
        {
            bestDistance[at] = bestDistance[at - 1];
            best[at] = best[at - 1];
            at--;
        }
        bestDistance[at] = distance;
        best[at] = index;
    }

    // ---------------- The one loop ----------------

    public void Tick(float delta, Vector3 playerPosition, Vector3 eyeDirection, float eyeHeight, bool horizonCull)
    {
        if (_count == 0 || _ground == null)
        {
            VisibleAgentCount = 0;
            return;
        }

        _frame++;
        _lastDelta = delta;
        _roamClock += delta;
        if (Roaming)
        {
            // The disc's own axes, once a tick: on a planet it lies tangent at
            // its centre, and a radius of a few hundred metres is near enough flat.
            Vector3 roamUp = _ground.UpAt(RoamCentre);
            _roamAcross = roamUp.Cross(Mathf.Abs(roamUp.X) < 0.9f ? Vector3.Right : Vector3.Forward).Normalized();
            _roamAlong = roamUp.Cross(_roamAcross);
        }

        if (ReorderInterval > 0 && _frame % ReorderInterval == 0 && _frame > _orderHeldUntil)
        {
            using (FrameProfiler.Begin(_sectionReorder))
            {
                ReorderAgents();
            }
        }

        // Always, not only for separation: blasts, the hit proxies and the
        // density cap all read it.
        using (FrameProfiler.Begin(_sectionHash))
        {
            RebuildHash();
        }

        PrepareView(playerPosition, eyeDirection, eyeHeight, horizonCull);

        using (FrameProfiler.Begin(_sectionUpdate))
        {
            StepAgents(delta, playerPosition);
        }

        using (FrameProfiler.Begin(_sectionBuffer))
        {
            WriteTransforms(playerPosition);
        }

        // After the pack rather than inside the step: the pack's per-partition
        // counts were taken during the integrate pass, and a kill or a spawn
        // reshuffles the array they describe. A death found this frame is
        // drawn for one more.
        using (FrameProfiler.Begin(_sectionEvents))
        {
            DrainEvents();
        }

        using (FrameProfiler.Begin(_sectionProxies))
        {
            UpdateHitProxies(playerPosition);
        }
    }

    /// <summary>
    /// Frames between re-sorting the agent array by flow cell. Zero never
    /// re-sorts.
    /// </summary>
    public int ReorderInterval { get; set; } = 60;

    /// <summary>Frames a handle use holds the order still for.</summary>
    private const int ReorderHoldFrames = 120;

    private Agent[] _reorderScratch;
    private int[] _cellStart;
    private int _orderHeldUntil;

    /// <summary>
    /// Re-sorts the agent array by flow cell, so agents that stand together
    /// sit together in memory.
    ///
    /// Spawn order is scattered order. Every pass here walks the array in
    /// index order and reaches out from each agent to its neighbours, its
    /// hash bucket and the heightfield under it -- and with the array in the
    /// order agents happened to be seeded and respawned in, every one of
    /// those reaches was a cache miss. At fifty thousand agents the horde was
    /// bound by memory rather than arithmetic. A counting sort on the cell is
    /// one pass to count, one to scatter, and a copy of the array, once a
    /// second.
    ///
    /// It renumbers agents, so it never runs while anything outside holds a
    /// handle (see <see cref="HoldOrder"/>), and the hit proxies -- which
    /// remember indices -- are re-chosen on the same frame.
    /// </summary>
    private void ReorderAgents()
    {
        int cells = _cellStart.Length - 1;
        System.Array.Clear(_cellStart, 0, _cellStart.Length);

        for (int i = 0; i < _count; i++)
        {
            int cell = (int)Mathf.Clamp(_agents[i].Cell, 0, cells - 1);
            _cellStart[cell + 1]++;
        }

        for (int cell = 0; cell < cells; cell++)
        {
            _cellStart[cell + 1] += _cellStart[cell];
        }

        for (int i = 0; i < _count; i++)
        {
            int cell = (int)Mathf.Clamp(_agents[i].Cell, 0, cells - 1);
            _reorderScratch[_cellStart[cell]++] = _agents[i];
        }

        (_agents, _reorderScratch) = (_reorderScratch, _agents);
        _proxyChosen = 0;
        _listsValid = false;
    }

    /// <summary>
    /// Rebuilds the spatial hash, and counts what is in each bucket.
    ///
    /// In parallel, with each insert a lock-free push onto its bucket's chain:
    /// at fifty thousand agents the serial version was a millisecond and a
    /// half of the main thread. Chain order changes run to run, which nothing
    /// reads. The counts are what the crowd's density cap steers by.
    /// </summary>
    private void RebuildHash()
    {
        System.Array.Fill(_bucketHead, -1);
        System.Array.Clear(_bucketCount, 0, _bucketCount.Length);

        RunPartitioned(_count, _hashBody ??= (partition, start, end) =>
        {
            for (int i = start; i < end; i++)
            {
                int bucket = BucketOf(_agents[i].Position);
                _hashNext[i] = System.Threading.Interlocked.Exchange(ref _bucketHead[bucket], i);

                // Deliberately not atomic. Two workers landing in one bucket
                // in the same instant can lose an increment, which leaves a
                // crowded cell reading one agent emptier for a frame -- a
                // heuristic off by one -- and an atomic add on every agent,
                // fought over by every worker in the densest buckets, was
                // most of what this rebuild cost.
                _bucketCount[bucket]++;
            }
        });
    }

    private Action<int, int, int> _hashBody;

    private int BucketOf(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.X / HashCellSize);
        int y = Mathf.FloorToInt(position.Y / HashCellSize);
        int z = Mathf.FloorToInt(position.Z / HashCellSize);
        return CellHash(x, y, z);
    }

    private int CellHash(int x, int y, int z)
    {
        // Hash collisions are harmless here: a colliding agent simply fails the
        // distance test a moment later. Verifying cell identity would cost more
        // than the occasional wasted comparison.
        uint hash = (uint)((x * 73856093) ^ (y * 19349663) ^ (z * 83492791));
        return (int)(hash & (uint)_bucketMask);
    }

    /// <summary>
    /// The step, in two passes.
    ///
    /// Splitting it is what makes the horde safe to thread. Separation reads
    /// its neighbours' positions while the agent writes its own, so a single
    /// fused loop has every worker reading data another worker is mutating.
    /// The first pass only ever writes velocity and only ever reads position;
    /// the second only writes position. Neither pass touches a slot it does not
    /// own, so no lock is needed and the result is identical to the serial one.
    /// </summary>
    private void StepAgents(float delta, Vector3 playerPosition)
    {
        float separationSquared = SeparationRadius * SeparationRadius;
        Array.Clear(_partitionStranded, 0, _partitionStranded.Length);
        Array.Clear(_partitionStepped, 0, _partitionStepped.Length);
        Array.Clear(_partitionEventCount, 0, _partitionEventCount.Length);

        // Steer and integrate walk only the agents due this frame, from the
        // list the last classify pass wrote. Walking all fifty thousand to
        // find the six thousand that step was a full pass over the horde
        // spent deciding to do nothing. On the first frame and the frame of a
        // re-sort the indices are not the ones the list was written against,
        // so everything steps once instead.
        bool listed = _listsValid;

        using (FrameProfiler.Begin(_sectionSteer))
        {
            if (listed)
            {
                RunLists((partition, from, to) =>
                {
                    int stranded = 0;
                    int stepped = 0;
                    for (int k = from; k < to; k++)
                    {
                        int i = _stepList[k];
                        if (i < _count)
                        {
                            stranded += SteerAgent(i, delta, separationSquared, partition, playerPosition, true);
                            stepped++;
                        }
                    }
                    _partitionStranded[partition] = stranded;
                    _partitionStepped[partition] = stepped;
                });
            }
            else
            {
                RunPartitioned(_count, (partition, start, end) =>
                {
                    int stranded = 0;
                    int stepped = 0;
                    for (int i = start; i < end; i++)
                    {
                        stranded += SteerAgent(i, delta, separationSquared, partition, playerPosition, false);
                        stepped += _step[i] != 0 ? 1 : 0;
                    }
                    _partitionStranded[partition] = stranded;
                    _partitionStepped[partition] = stepped;
                });
            }
        }

        using (FrameProfiler.Begin(_sectionIntegrate))
        {
            if (listed)
            {
                RunLists((partition, from, to) =>
                {
                    for (int k = from; k < to; k++)
                    {
                        int i = _stepList[k];
                        if (i < _count)
                        {
                            IntegrateAgent(i, delta, partition, playerPosition);
                        }
                    }
                });
            }
            else
            {
                RunPartitioned(_count, (partition, start, end) =>
                {
                    for (int i = start; i < end; i++)
                    {
                        IntegrateAgent(i, delta, partition, playerPosition);
                    }
                });
            }
        }

        // Classify every agent -- what is drawn, and with which mesh -- and
        // write the list of who steps next frame while the agent is in hand.
        Array.Clear(_partitionVisible, 0, _partitionVisible.Length);
        Array.Clear(_partitionNear, 0, _partitionNear.Length);
        Array.Clear(_partitionPressure, 0, _partitionPressure.Length);
        Array.Clear(_listCount, 0, _listCount.Length);

        using (FrameProfiler.Begin(_sectionClassify))
        {
            int nextFrame = _frame + 1;
            RunPartitioned(_count, (partition, start, end) =>
            {
                // Counted on the worker's own stack and written out once.
                // Ten counters per partition are forty bytes, so two
                // partitions shared every cache line of the shared array, and
                // incrementing it per agent had the cores fighting over it.
                ViewCounts counts = default;
                Span<int> visible = stackalloc int[BucketCount];
                int listed = 0;
                for (int i = start; i < end; i++)
                {
                    if (ClassifyAgent(i, visible, ref counts, nextFrame))
                    {
                        _stepList[start + listed] = i;
                        listed++;
                    }
                }

                int bucket = partition * BucketCount;
                for (int drawn = 0; drawn < BucketCount; drawn++)
                {
                    _partitionVisible[bucket + drawn] = visible[drawn];
                }
                _partitionNear[partition] = counts.Near;
                _partitionPressure[partition] = counts.Pressure;
                _listStart[partition] = start;
                _listCount[partition] = listed;
            });
            _listPartitions = _lastPartitions;
            _listsValid = true;
        }

        int total = 0;
        int stepped = 0;
        for (int i = 0; i < _partitionStranded.Length; i++)
        {
            total += _partitionStranded[i];
            stepped += _partitionStepped[i];
        }
        StrandedCount = total;
        SteppedCount = stepped;
    }

    // Who steps next frame, written by the classify pass: partition p's
    // entries sit at [_listStart[p], _listStart[p] + _listCount[p]), inside
    // the index range it classified, so one capacity-sized array holds them all.
    private int[] _stepList;
    private readonly int[] _listStart = new int[MaxPartitions];
    private readonly int[] _listCount = new int[MaxPartitions];
    private int _listPartitions;
    private bool _listsValid;
    private int _lastPartitions = 1;

    /// <summary>Runs a body over each partition's slice of the step list, in parallel when there are several.</summary>
    private void RunLists(Action<int, int, int> body)
    {
        int partitions = Mathf.Max(_listPartitions, 1);
        if (partitions == 1)
        {
            body(0, _listStart[0], _listStart[0] + _listCount[0]);
            return;
        }

        Jobs.For(partitions, partition =>
        {
            int from = _listStart[partition];
            int to = from + _listCount[partition];
            if (from < to)
            {
                body(partition, from, to);
            }
        });
    }

    /// <summary>Agents stepped this frame, across the tiers. For the overlay and the bench.</summary>
    public int SteppedCount { get; private set; }

    private readonly int[] _partitionStepped = new int[MaxPartitions];

    /// <summary>
    /// Walks what the parallel pass recorded, on the main thread.
    ///
    /// Everything in here mutates state a worker was reading -- killing an
    /// agent refills its slot, a thrown cornichon appends to the array being
    /// iterated -- so none of it can happen inside the pass that found it. The
    /// pass only ever writes its own slot and its own event slice.
    /// </summary>
    private void DrainEvents()
    {
        for (int partition = 0; partition < MaxPartitions; partition++)
        {
            int count = _partitionEventCount[partition];
            if (count == 0)
            {
                continue;
            }

            HordeEvent[] events = _partitionEvents[partition];
            for (int i = 0; i < count; i++)
            {
                ref HordeEvent raised = ref events[i];

                // The id was captured before any of these landed, and a slot
                // refilled by an earlier event in this same drain must not
                // inherit the rest of it.
                if (raised.Index >= _count || _agents[raised.Index].Id != raised.Id)
                {
                    continue;
                }

                switch (raised.Type)
                {
                    case HordeEventType.Died:
                        KillCount++;
                        PickleAudio.Play(Sfx.AgentDie, raised.Position, _kindStats[_agents[raised.Index].Kind]);
                        EmitSignal(
                            SignalName.AgentKilled, raised.Position, _ground.UpAt(raised.Position),
                            _kindStats[_agents[raised.Index].Kind]?.Colour ?? Colors.Green);
                        RemoveAgent(raised.Index);
                        break;

                    case HordeEventType.Strike:
                        if (raised.Magnitude < 0.0f)
                        {
                            AllyStruck?.Invoke(-(int)raised.Magnitude - 1, raised.Position, _agents[raised.Index].Kind);
                        }
                        else
                        {
                            AgentStruck?.Invoke(raised.Position, _agents[raised.Index].Kind, raised.Magnitude > 0.0f);
                        }
                        break;

                    case HordeEventType.Ranged:
                        RangedRequested?.Invoke(raised.Position, _agents[raised.Index].Kind);
                        break;

                    case HordeEventType.Dash:
                        AudioDirector.PlayAt(Sfx.CornichonDash, raised.Position);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Appends to this partition's own slice. Silently drops past the cap,
    /// which is correct: the cap exists so one frame cannot raise an unbounded
    /// amount of main-thread work, and a behaviour that missed its turn simply
    /// fires on the next one.
    /// </summary>
    private void RaiseEvent(int partition, HordeEventType type, int index, int id, Vector3 position, float magnitude)
    {
        int count = _partitionEventCount[partition];
        if (count >= MaxEventsPerPartition)
        {
            return;
        }

        _partitionEvents[partition][count] = new HordeEvent
        {
            Type = type,
            Index = index,
            Id = id,
            Position = position,
            Magnitude = magnitude,
        };
        _partitionEventCount[partition] = count + 1;
    }

    /// <summary>
    /// Pass one: decide where this agent wants to go. Writes velocity, its own
    /// cooldown and its own health, and nothing else.
    ///
    /// Every behaviour that only needs those three lives here and runs in
    /// parallel for free. Anything that has to spawn, kill or damage is
    /// recorded as an event instead and drained afterwards.
    /// </summary>
    private int SteerAgent(int index, float delta, float separationSquared, int partition, Vector3 playerPosition,
                           bool scheduled)
    {
        ref Agent agent = ref _agents[index];

        // Near agents, and anything airborne, held or coasting, step every
        // frame; a further one only on its turn, by a whole stride's worth of
        // time.
        bool forced = agent.Airborne || agent.Held > 0.0f || agent.Flung > 0.0f;
        int stride = forced ? 1 : StrideFor(agent.Position.DistanceSquaredTo(playerPosition));
        bool near = stride == 1;
        if (!scheduled && stride > 1 && StepPhase(index, stride) != 0)
        {
            _step[index] = 0;
            return 0;
        }
        byte step = (byte)Mathf.Min(stride, 255);
        _step[index] = step;
        delta *= step;

        // The cell was found by the last integrate, from the same lookup that
        // found the ground; the agent has not moved since.
        Vector3 up = _ground.UpAt(agent.Position);

        Vector3 desired = _flow.FlowAt(agent.Cell);
        int stranded = 0;
        if (desired.LengthSquared() < 1e-6f)
        {
            // In the target's own cell, or nowhere the field reaches. Near
            // the hunt point that is the last thirty metres, so head straight
            // for it; anywhere else it really is stranded.
            Vector3 toHunt = HuntPoint - agent.Position;
            if (FinalApproachRange > 0.0f && toHunt.LengthSquared() <= FinalApproachRange * FinalApproachRange)
            {
                desired = toHunt;
            }
            else if (_flow.IsBeyondSearch(agent.Cell))
            {
                // Past where a bounded field searched (the planet's): open
                // ground a kilometre off, where the straight way along the
                // surface is the route. The field takes over on arrival.
                desired = toHunt;
            }
            else
            {
                // Counted for every frame it sits out, so the overlay's figure
                // does not drop by the stride when the far tier is on.
                stranded = step;
            }
        }

        float pace = 1.0f;
        if (Roaming && (agent.Position - RoamCentre).LengthSquared() <= RoamRadius * RoamRadius)
        {
            desired = Roam(index, agent.Position, up);
            stranded = 0;
            pace = RoamSpeedScale;
        }

        // Near an ally, and nearer it than the player: go for the ally.
        if (_allyCount > 0)
        {
            int ally = NearestAlly(agent.Position, out float allySquared);
            if (ally >= 0 && allySquared <= AllyAggroRange * AllyAggroRange
                && allySquared < DistanceToPlayerSquared(agent.Position, playerPosition))
            {
                desired = _allies[ally].Base - agent.Position;
                stranded = 0;
            }
        }

        // The stored flow vector is tangent at the cell centre, not at the
        // agent, so it has to be re-projected or agents drift off-surface.
        desired = desired.Slide(up);

        // Shallow water slows the wading (decision 0045).
        if (_ground is IGroundPace ground)
        {
            pace *= ground.PaceAt(agent.Cell);
        }

        float speed = agent.TargetSpeed * pace;
        AgentKindStats stats = _kindStats[agent.Kind];

        // Held in the beam: the manipulator places it, nothing else moves it.
        if (agent.Held > 0.0f)
        {
            agent.Held -= delta;
            agent.Velocity = Vector3.Zero;
            return stranded;
        }

        // In the air: a ballistic arc, down, no steering.
        if (agent.Airborne)
        {
            agent.Velocity += up * (-AirGravity * delta);
            return stranded;
        }

        // Thrown and landed: coast along the ground under drag, and neither
        // steer nor behave until the throw has spent itself. Ends early once
        // it is back under the speed steering would have allowed anyway.
        if (agent.Flung > 0.0f)
        {
            agent.Flung -= delta;
            Vector3 coast = agent.Velocity.Slide(up) * Mathf.Exp(-FlingDrag * delta);
            agent.Velocity = coast;
            if (coast.LengthSquared() <= speed * speed * 2.25f)
            {
                agent.Flung = 0.0f;
            }
            return stranded;
        }

        if (stats != null)
        {
            speed = ApplyBehaviour(index, ref agent, stats, delta, partition, playerPosition, speed);
        }

        Vector3 steer = desired.LengthSquared() > 1e-6f
            ? desired.Normalized() * speed
            : Vector3.Zero;

        // The density cap, for every tier: an agent whose next cell is
        // already full stops pushing forward. Separation alone cannot hold a
        // hunting crowd apart -- in the middle of a pile its push cancels from
        // every side while the drive toward the player does not -- and the
        // tiers past the near one do not separate at all, so without this
        // fifty thousand agents walked into one spot and stayed there. With
        // it the crowd queues outward from the player instead: the front
        // ring closes, and everyone behind waits for room.
        if (CellCapacity > 0 && steer.LengthSquared() > 1e-6f)
        {
            Vector3 next = agent.Position + (desired.Normalized() * (HashCellSize * 1.2f));
            if (_bucketCount[BucketOf(next)] >= CellCapacity)
            {
                steer *= CrowdedDrive;
            }
        }

        if (SeparationEnabled && near)
        {
            Vector3 heading = steer.LengthSquared() > 1e-6f ? steer.Normalized() : Vector3.Zero;
            Vector3 push = Separation(index, ref agent, up, separationSquared, heading, out int ahead);

            // Queued behind others, an agent stops shoving forward and only
            // holds its space. Without this a hunting crowd steers every agent
            // at the same point and compresses into it: ten thousand pickles
            // inside twenty-five metres at fifty thousand, a grenade killing
            // hundreds, and every one of them inside the full-rate tier. It is
            // the neighbours *ahead* that count, not all of them: the front
            // row has nobody between it and the player, keeps closing, and
            // the rows behind follow as it moves.
            if (ahead >= BlockedAhead)
            {
                steer *= CrowdedDrive;
            }
            steer += push * SeparationStrength;
        }

        Vector3 velocity = agent.Velocity.Slide(up);
        velocity = velocity.MoveToward(steer, Acceleration * delta);

        float current = velocity.Length();
        float limit = speed * 1.5f;
        if (current > limit)
        {
            velocity *= limit / current;
        }

        agent.Velocity = velocity;
        return stranded;
    }

    /// <summary>
    /// How the crowd stands round a point, for the bench: the share within
    /// <paramref name="radius"/>, the mean and 90th-percentile distance, and
    /// the most agents in any one flow cell. A garrison that piles into one
    /// spot reads thousands there.
    /// </summary>
    public string SpreadAround(Vector3 centre, float radius)
    {
        if (_count == 0)
        {
            return "no agents";
        }
        var distances = new float[_count];
        var perCell = new System.Collections.Generic.Dictionary<int, int>();
        int inside = 0;
        double sum = 0.0;
        for (int i = 0; i < _count; i++)
        {
            float d = _agents[i].Position.DistanceTo(centre);
            distances[i] = d;
            sum += d;
            inside += d <= radius ? 1 : 0;
            int cell = _agents[i].Cell;
            perCell[cell] = perCell.TryGetValue(cell, out int n) ? n + 1 : 1;
        }
        System.Array.Sort(distances);
        int most = 0;
        foreach (int n in perCell.Values)
        {
            most = Mathf.Max(most, n);
        }
        return System.FormattableString.Invariant(
            $"within {radius:F0} m {100.0 * inside / _count:F0}%  mean {sum / _count:F0} m  p90 {distances[(int)(_count * 0.9f)]:F0} m  cells {perCell.Count}  fullest cell {most}");
    }

    /// <summary>
    /// Where a roaming agent heads: its own spot in the garrison's disc for
    /// this period, the periods staggered by agent so the crowd never turns
    /// at once. Zero (stand) when it is there, when its spot or its next step
    /// is blocked, and for one period in four, so the garrison mills rather
    /// than marches. Arithmetic over the ground and the field's weights only:
    /// safe from the parallel passes.
    /// </summary>
    private Vector3 Roam(int index, Vector3 position, Vector3 up)
    {
        float period = Mathf.Max(RoamPeriod, 1.0f);
        uint slot = (uint)((_roamClock / period) + ((index * 0.6180339f) % 1.0f));
        uint h = (uint)index * 0x9E3779B1u ^ slot * 0x85EBCA77u;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        if ((h & 3u) == 0u)
        {
            return Vector3.Zero;
        }

        float angle = ((h >> 2) & 0x3FFFu) / 16383.0f * Mathf.Tau;
        float reach = RoamRadius * Mathf.Sqrt(((h >> 16) & 0xFFFFu) / 65535.0f);
        Vector3 spot = RoamCentre + (((_roamAcross * Mathf.Cos(angle)) + (_roamAlong * Mathf.Sin(angle))) * reach);
        Vector3 to = (spot - position).Slide(up);
        if (to.LengthSquared() < 4.0f)
        {
            return Vector3.Zero;
        }

        Vector3 heading = to.Normalized();
        _ground.GroundBelow(spot, out int spotCell);
        _ground.GroundBelow(position + (heading * (_ground.CellSize * 0.75f)), out int nextCell);
        if ((spotCell >= 0 && _flow.BaseWeightAt(spotCell) == FlowField.Blocked)
            || (nextCell >= 0 && _flow.BaseWeightAt(nextCell) == FlowField.Blocked))
        {
            return Vector3.Zero;
        }
        return heading;
    }

    /// <summary>
    /// The five behaviours, as data rather than five classes: every kind
    /// strikes, throws and dashes by its own ranges, and a range of zero is a
    /// move it does not have. Nearest first -- a pickle in biting range bites
    /// rather than spitting -- and one cooldown, so whatever it did last is
    /// what it waits out.
    ///
    /// Returns this frame's target speed, so a behaviour can express itself as
    /// one; everything here currently keeps what it was given.
    ///
    /// Everything in here writes only this agent's own velocity, cooldown and
    /// flags. A strike reaches the player and a throw spawns or fires, so both
    /// record an event and let the main thread do it.
    /// </summary>
    private float ApplyBehaviour(int index, ref Agent agent, AgentKindStats stats, float delta,
                                 int partition, Vector3 playerPosition, float speed)
    {
        if (agent.Cooldown > 0.0f)
        {
            agent.Cooldown -= delta;
            return speed;
        }

        float distanceSquared = DistanceToPlayerSquared(agent.Position, playerPosition);
        Vector3 targetPoint = playerPosition;

        // An ally nearer than the player is what it strikes and dashes at.
        // The magnitude carries which ally, negative, so the drain can tell
        // it from a strike on the player.
        float strikeTag = 0.0f;
        if (_allyCount > 0)
        {
            int ally = NearestAlly(agent.Position, out float allySquared);
            if (ally >= 0 && allySquared < distanceSquared)
            {
                distanceSquared = allySquared;
                targetPoint = _allies[ally].Base;
                strikeTag = -(ally + 1);
            }
        }

        if (stats.StrikeRange > 0.0f && distanceSquared <= stats.StrikeRange * stats.StrikeRange)
        {
            RaiseEvent(partition, HordeEventType.Strike, index, agent.Id, agent.Position, strikeTag);
            agent.Cooldown = stats.StrikeInterval;
            return speed;
        }

        if (RangedEnabled && stats.RangedRange > 0.0f
            && distanceSquared <= stats.RangedRange * stats.RangedRange
            && distanceSquared >= stats.RangedMinRange * stats.RangedMinRange)
        {
            RaiseEvent(partition, HordeEventType.Ranged, index, agent.Id, agent.Position, 0.0f);
            agent.Cooldown = stats.RangedInterval;
            return speed;
        }

        if (stats.DashRange > 0.0f && distanceSquared <= stats.DashRange * stats.DashRange)
        {
            // A ground dash straight at the player, not an arc. The steer
            // clamp caps velocity at one and a half times walking speed, which
            // used to cut a fourteen metre-a-second dash back to six in the
            // same frame it started; the flung coast is the existing way out
            // of that clamp, and it ends on its own once drag has spent the
            // burst.
            Vector3 up = _ground.UpAt(agent.Position);
            Vector3 toward = (targetPoint - agent.Position).Slide(up);
            toward = toward.LengthSquared() > 1e-4f ? toward.Normalized() : agent.Forward;
            agent.Velocity = agent.Velocity.Slide(up) + (toward * stats.DashSpeed);
            agent.Flung = DashSeconds;
            agent.Cooldown = stats.DashInterval;
            RaiseEvent(partition, HordeEventType.Dash, index, agent.Id, agent.Position, stats.DashSpeed);
        }

        return speed;
    }

    /// <summary>How long a dash coasts before steering takes over, at most.</summary>
    private const float DashSeconds = 0.8f;

    /// <summary>
    /// Squared distance from an agent's middle to the player's body, taken as
    /// the stretch from the camera down to the feet. Against the camera alone a
    /// cornichon at shin height would have to be a metre and a half closer
    /// than a tank to bite the same person.
    /// </summary>
    private float DistanceToPlayerSquared(Vector3 position, Vector3 camera)
    {
        Vector3 down = -_ground.UpAt(camera);
        float along = Mathf.Clamp((position - camera).Dot(down), 0.0f, PlayerBodyLength);
        return position.DistanceSquaredTo(camera + (down * along));
    }

    /// <summary>Camera to feet, near enough. Only reach is measured against it.</summary>
    private const float PlayerBodyLength = 1.6f;

    /// <summary>The first cooldown a fresh agent waits, randomised under it so a pack does not move as one.</summary>
    private static float FirstInterval(AgentKindStats stats)
    {
        if (stats.RangedRange > 0.0f)
        {
            return stats.RangedInterval;
        }
        if (stats.DashRange > 0.0f)
        {
            return stats.DashInterval;
        }
        return stats.StrikeRange > 0.0f ? stats.StrikeInterval : 0.0f;
    }

    /// <summary>Off stops every throw and spit at the source. The instrument toggle for the ranged kinds.</summary>
    public bool RangedEnabled { get; set; } = true;

    // ---------------- Level of detail ----------------

    /// <summary>
    /// Agents within this many metres of the camera are stepped every frame,
    /// with separation. Out to <see cref="MidRange"/> they are stepped one
    /// frame in <see cref="FarStride"/>, past it one in
    /// <see cref="DistantStride"/>, each time by that many frames' worth of
    /// time, and neither separates.
    ///
    /// This is what makes a horde of tens of thousands affordable. A crowd
    /// converging on the player is mostly far from them at any moment, and a
    /// far agent is mostly past the horizon: what it needs is to keep walking
    /// the flow field and stay on the ground, not to avoid its neighbours sixty
    /// times a second. Kept past every kind's bite and strike, so everything
    /// that can touch the player runs at full rate; a throw or a spit from
    /// further out fires on its own step, a few frames late at most.
    /// </summary>
    public float NearRange { get; set; } = 20.0f;

    /// <summary>Edge of the middle tier, in metres.</summary>
    public float MidRange { get; set; } = 120.0f;

    /// <summary>Frames between a middle-tier agent's steps. One switches the tiers off.</summary>
    public int FarStride { get; set; } = 6;

    /// <summary>Frames between a distant agent's steps.</summary>
    public int DistantStride { get; set; } = 15;

    /// <summary>Past this many metres an agent is drawn with the low-detail mesh.</summary>
    public float LodDistance { get; set; } = 40.0f;



    /// <summary>Off draws everything above the horizon, behind the camera included. The instrument switch.</summary>
    public bool FrustumCullEnabled { get; set; } = true;

    private int _frame;
    private float _lastDelta;
    private bool _frustumValid;
    private Vector3 _viewOrigin;
    private Vector3 _viewForward;
    private Vector3 _viewRight;
    private Vector3 _viewUp;
    private float _viewTanX;
    private float _viewTanY;
    private float _viewSecX;
    private float _viewSecY;

    /// <summary>
    /// How many frames apart an agent this far from the camera is stepped.
    /// Anything airborne, held or coasting is never deferred: those are the
    /// states where a stale frame is a visible jump or a missed landing.
    /// </summary>
    private int StrideFor(float distanceSquared)
    {
        if (distanceSquared <= NearRange * NearRange)
        {
            return 1;
        }

        int stride = distanceSquared <= MidRange * MidRange ? FarStride : DistantStride;
        return Mathf.Max(stride, 1);
    }

    /// <summary>
    /// Frames since this agent last stepped, at a given stride. An agent steps
    /// when its index and the frame counter agree modulo the stride, so the
    /// stagger is spread evenly over the frames and costs no field to remember.
    /// </summary>
    private int StepPhase(int index, int stride) => (index + _frame) % stride;

    /// <summary>
    /// The camera's view, pushed each frame before the tick: where it is, how
    /// it is turned, its vertical field of view in degrees and the viewport's
    /// aspect. A field of view of zero switches the cull off for the frame.
    /// </summary>
    public void SetView(Transform3D camera, float verticalFovDegrees, float aspect)
    {
        _frustumValid = verticalFovDegrees > 0.0f && aspect > 0.0f;
        if (!_frustumValid)
        {
            return;
        }

        _viewOrigin = camera.Origin;
        _viewForward = -camera.Basis.Z.Normalized();
        _viewRight = camera.Basis.X.Normalized();
        _viewUp = camera.Basis.Y.Normalized();
        _viewTanY = Mathf.Tan(Mathf.DegToRad(verticalFovDegrees) * 0.5f);
        _viewTanX = _viewTanY * aspect;
        _viewSecX = Mathf.Sqrt(1.0f + (_viewTanX * _viewTanX));
        _viewSecY = Mathf.Sqrt(1.0f + (_viewTanY * _viewTanY));
    }

    /// <summary>Pass two: move, stick to the ground, face the way you moved.</summary>
    private void IntegrateAgent(int index, float delta, int partition, Vector3 playerPosition)
    {
        ref Agent agent = ref _agents[index];

        byte step = _step[index];
        if (step == 0)
        {
            return;
        }
        delta *= step;

        if (agent.Held > 0.0f)
        {
            // Placed by MoveAgent; only the heading needs keeping upright.
            agent.Forward = agent.Forward.Slide(_ground.UpAt(agent.Position)).Normalized();
            return;
        }

        agent.Position += agent.Velocity * delta;

        // One lookup serves the ground under the agent and the flow cell it
        // is in; on the planet they used to cost one each.
        Vector3 surface = _ground.GroundBelow(agent.Position, out int cell);
        agent.Cell = cell;
        Vector3 newUp = _ground.UpAt(surface);
        Vector3 ground = surface + (newUp * _kindHalfHeight[agent.Kind]);

        if (agent.Airborne)
        {
            // A lobbed cornichon that comes down on the player lands on them,
            // once per flight -- the cooldown is not spent while airborne, so
            // setting it is the "once".
            if (agent.Lobbed && agent.Cooldown <= 0.0f
                && DistanceToPlayerSquared(agent.Position, playerPosition) <= ThrownHitRadius * ThrownHitRadius)
            {
                RaiseEvent(partition, HordeEventType.Strike, index, agent.Id, agent.Position, 1.0f);
                agent.Cooldown = 1.0f;
            }

            if ((agent.Position - ground).Dot(newUp) > 0.0f)
            {
                // Still in the air.
                return;
            }

            float impact = agent.Velocity.Length();
            agent.Airborne = false;

            if (agent.Lobbed)
            {
                // A throw lands soft and stays near where it was aimed: it
                // was meant to arrive, not to skid twenty metres past you.
                agent.Lobbed = false;
                agent.Velocity = agent.Velocity.Slide(newUp) * 0.25f;
                agent.Flung = 0.0f;
                agent.Position = ground;
                return;
            }

            // Landed. A hard landing hurts -- a thrown agent that meets the
            // ground at throwing speed does not get up -- and whatever speed
            // is left carries on as a slide along the ground.
            agent.Velocity = agent.Velocity.Slide(newUp);
            agent.Flung = Mathf.Max(agent.Flung, FlingSeconds);

            if (impact > LandingSafeSpeed)
            {
                agent.Health -= (impact - LandingSafeSpeed) * LandingDamagePerSpeed;
                if (agent.Health <= 0.0f)
                {
                    RaiseEvent(partition, HordeEventType.Died, index, agent.Id, ground, 0.0f);
                }
            }
        }

        // Snap back onto the ground. This is the whole of "grounded" -- no
        // floor ray, no character body.
        agent.Position = ground;

        // Re-orthonormalise the heading against the new up, exactly as the
        // player controller does. A scalar heading would be fine within one
        // cube face and would jump at every seam.
        Vector3 forward = agent.Forward.Slide(newUp);
        if (agent.Velocity.LengthSquared() > 1e-4f)
        {
            forward = forward.Lerp(agent.Velocity.Normalized(), Mathf.Min(1.0f, TurnRate * delta));
            forward = forward.Slide(newUp);
        }
        if (forward.LengthSquared() < 1e-8f)
        {
            forward = newUp.Cross(Vector3.Up);
            if (forward.LengthSquared() < 1e-8f)
            {
                forward = newUp.Cross(Vector3.Right);
            }
        }
        agent.Forward = forward.Normalized();
    }

    /// <summary>
    /// Splits a range over the cores, or runs it inline when the horde is small
    /// enough that scheduling would cost more than the work.
    /// </summary>
    private void RunPartitioned(int count, Action<int, int, int> body)
    {
        if (count <= 0)
        {
            return;
        }

        int partitions = Multithreaded && count >= ParallelThreshold
            ? Mathf.Clamp(Mathf.Max(System.Environment.ProcessorCount, count / MinPartitionSize), 1, MaxPartitions)
            : 1;

        _lastPartitions = Mathf.Max(partitions, 1);
        if (partitions <= 1)
        {
            body(0, 0, count);
            return;
        }

        int perPartition = (count + partitions - 1) / partitions;
        Jobs.For(partitions, partition =>
        {
            int start = partition * perPartition;
            int end = Mathf.Min(start + perPartition, count);
            if (start < end)
            {
                body(partition, start, end);
            }
        });
    }

    private Vector3 Separation(int self, ref Agent agent, Vector3 up, float radiusSquared, Vector3 heading,
                               out int ahead)
    {
        Vector3 push = Vector3.Zero;
        int found = 0;
        int visited = 0;
        ahead = 0;

        // The two cells nearest along each axis, not three. A hash cell is at
        // least twice the separation radius, so anything inside the radius is
        // in this cell or in the neighbour on the side the agent is nearer --
        // eight buckets rather than twenty-seven, and every bucket is a cache
        // miss in a table this size. That was most of what a near agent cost.
        float inverse = 1.0f / HashCellSize;
        float fx = agent.Position.X * inverse;
        float fy = agent.Position.Y * inverse;
        float fz = agent.Position.Z * inverse;
        int cx = Mathf.FloorToInt(fx);
        int cy = Mathf.FloorToInt(fy);
        int cz = Mathf.FloorToInt(fz);
        int sx = fx - cx < 0.5f ? -1 : 0;
        int sy = fy - cy < 0.5f ? -1 : 0;
        int sz = fz - cz < 0.5f ? -1 : 0;

        for (int dz = sz; dz <= sz + 1 && found < MaxSeparationNeighbours; dz++)
        {
            for (int dy = sy; dy <= sy + 1 && found < MaxSeparationNeighbours; dy++)
            {
                for (int dx = sx; dx <= sx + 1 && found < MaxSeparationNeighbours; dx++)
                {
                    int other = _bucketHead[CellHash(cx + dx, cy + dy, cz + dz)];

                    while (other != -1 && found < MaxSeparationNeighbours && visited < MaxSeparationVisited)
                    {
                        visited++;
                        if (other != self)
                        {
                            Vector3 offset = agent.Position - _agents[other].Position;
                            float distanceSquared = offset.LengthSquared();
                            if (distanceSquared < radiusSquared && distanceSquared > 1e-6f)
                            {
                                // Falls off with distance, so a crowd shoves
                                // harder than a passer-by.
                                push += offset / distanceSquared;
                                found++;

                                // Ahead: within sixty degrees of where it is
                                // trying to go. offset points away from the
                                // neighbour, so ahead is a negative dot.
                                float along = -offset.Dot(heading);
                                if (along > 0.0f && along * along > 0.25f * distanceSquared)
                                {
                                    ahead++;
                                }
                            }
                        }
                        other = _hashNext[other];
                    }
                }
            }
        }

        return found > 0 ? push.Slide(up) : Vector3.Zero;
    }

    /// <summary>Neighbours ahead, inside the separation radius, that make an agent stop pushing.</summary>
    private const int BlockedAhead = 2;

    /// <summary>
    /// Ceiling on chain entries one separation walk visits. Hash collisions
    /// and a dense crowd can put dozens in a bucket, and the walk stopping
    /// only once enough neighbours are inside the radius made its cost grow
    /// with the density it was meant to relieve.
    /// </summary>
    private const int MaxSeparationVisited = 24;

    /// <summary>
    /// Agents a hash cell holds before the ones behind stop walking into it.
    /// A cell is twice the separation radius a side, about three metres, so
    /// six is a crowd you can see between and a capsule's width apart. Zero
    /// switches the cap off.
    /// </summary>
    public int CellCapacity { get; set; } = 6;

    /// <summary>How much of its forward drive an agent queued behind others keeps.</summary>
    public float CrowdedDrive { get; set; } = 0.05f;

    /// <summary>
    /// Packs visible agents into the multimesh buffers.
    ///
    /// What is visible, and with which mesh, was decided by the classify pass
    /// (<see cref="ClassifyAgent"/>), which also counted it per partition and
    /// per buffer. Instances have to end up contiguous, which is what normally
    /// forces this loop to stay serial; it is done instead by a prefix sum that
    /// hands each partition a slice of each buffer, and then every partition
    /// fills its own slice. No worker writes outside its slice, so there is
    /// nothing to synchronise -- as long as this runs over the same agents,
    /// partitioned the same way, as the classify pass did, which is why the
    /// events that add and remove agents are drained after it.
    /// </summary>
    /// <summary>Seconds on the horde's own clock: real time, for the hit squash.</summary>
    private static float HordeClock => Time.GetTicksMsec() * 0.001f;

    /// <summary>How long a hit pickle wobbles.</summary>
    private const float SquashSeconds = 0.28f;

    private void WriteTransforms(Vector3 playerPosition)
    {
        float clock = HordeClock;
        float frameDelta = _lastDelta;

        // Pass two: one prefix sum per buffer, so each partition knows where
        // its slice of that buffer starts.
        Span<int> written = stackalloc int[BucketCount];
        int nearTotal = 0;

        for (int drawn = 0; drawn < BucketCount; drawn++)
        {
            int running = 0;
            for (int partition = 0; partition < MaxPartitions; partition++)
            {
                int slot = (partition * BucketCount) + drawn;
                _partitionOffset[slot] = running;
                running += _partitionVisible[slot];
            }

            // A kind can never have more live agents than its buffer holds --
            // ChooseKind enforces that at spawn -- but the clamp is kept so a
            // capacity change mid-session cannot write past the end.
            written[drawn] = Mathf.Min(running, _kindCapacity[drawn / 2]);
        }

        int pressureTotal = 0;
        for (int partition = 0; partition < MaxPartitions; partition++)
        {
            nearTotal += _partitionNear[partition];
            pressureTotal += _partitionPressure[partition];
        }

        // Pass three: fill the slices. Each partition holds one cursor per
        // buffer and writes only inside its own slice of each, so there is
        // still nothing to synchronise.
        RunPartitioned(_count, (partition, from, to) =>
        {
            Span<int> cursor = stackalloc int[BucketCount];
            for (int drawn = 0; drawn < BucketCount; drawn++)
            {
                cursor[drawn] = _partitionOffset[(partition * BucketCount) + drawn];
            }

            for (int i = from; i < to; i++)
            {
                ref Agent agent = ref _agents[i];
                int drawn = _drawBucket[i];
                if (drawn == NotDrawn)
                {
                    continue;
                }

                int slot = cursor[drawn];
                if (slot >= _kindCapacity[drawn / 2])
                {
                    continue;
                }
                cursor[drawn] = slot + 1;

                // A far agent between its steps is drawn where it would be by
                // now, so a crowd stepped one frame in four still moves every
                // frame on screen.
                Vector3 position = agent.Position;
                if (!agent.Airborne && agent.Held <= 0.0f && agent.Flung <= 0.0f)
                {
                    int stride = StrideFor(agent.Position.DistanceSquaredTo(playerPosition));
                    int phase = stride > 1 ? StepPhase(i, stride) : 0;
                    if (phase > 0)
                    {
                        position += agent.Velocity * (frameDelta * phase);
                    }
                }

                float[] buffer = _transforms[drawn];
                Vector3 up = _ground.UpAt(position);
                Vector3 back = -agent.Forward;
                Vector3 right = up.Cross(back);

                // Squash and stretch for a moment after a hit: flattened and
                // bulging, then springing tall, then still -- a hit lands on
                // something rubbery. About the feet, so it neither sinks nor
                // floats. One read and a branch for every pickle not hit.
                float since = clock - agent.HitAt;
                if (since >= 0.0f && since < SquashSeconds)
                {
                    float wobble = Mathf.Exp(-since * 12.0f) * Mathf.Cos(since * 38.0f);
                    float tall = 1.0f - (0.32f * wobble);
                    float wide = 1.0f + (0.22f * wobble);
                    position -= up * (_kindHalfHeight[agent.Kind] * (1.0f - tall));
                    up *= tall;
                    right *= wide;
                    back *= wide;
                }

                int offset = slot * FloatsPerInstance;

                buffer[offset + 0] = right.X;
                buffer[offset + 1] = up.X;
                buffer[offset + 2] = back.X;
                buffer[offset + 3] = position.X;

                buffer[offset + 4] = right.Y;
                buffer[offset + 5] = up.Y;
                buffer[offset + 6] = back.Y;
                buffer[offset + 7] = position.Y;

                buffer[offset + 8] = right.Z;
                buffer[offset + 9] = up.Z;
                buffer[offset + 10] = back.Z;
                buffer[offset + 11] = position.Z;
            }
        });

        int total = 0;
        using (FrameProfiler.Begin(_sectionUpload))
        {
            for (int drawn = 0; drawn < BucketCount; drawn++)
            {
                int count = written[drawn];
                total += count;

                UploadBucket(drawn, count);
            }
        }

        VisibleAgentCount = total;
        NearPlayerCount = nearTotal;
        PressureCount = pressureTotal;
    }

    /// <summary>The cull's working for one point, for the bench's aim probe.</summary>
    public string CullProbe(Vector3 position)
    {
        Vector3 offset = position - _viewOrigin;
        return Inv($"view valid={_frustumValid} origin={_viewOrigin} fwd={_viewForward} depth={offset.Dot(_viewForward):F2} side={offset.Dot(_viewRight):F2} rise={offset.Dot(_viewUp):F2} tan {_viewTanX:F2}/{_viewTanY:F2} in={InFrustum(position, 2.0f)}");
    }

    private static string Inv(System.FormattableString text) => System.FormattableString.Invariant(text);

    private struct ViewCounts
    {
        public int Near;
        public int Pressure;
    }

    // The view, prepared once a frame for ClassifyAgent.
    private Vector3 _classifyPlayer;
    private Vector3 _classifyEye;
    private Vector3 _classifyPressure;
    private float _classifyHorizonCosineSquared;
    private float _classifyLodSquared;
    private float _classifyPressureSquared;
    private float _classifyMargin;
    private bool _classifyHorizon;
    private bool _classifyFrustum;
    private float _classifyDrawSquared = float.MaxValue;

    /// <summary>
    /// Past this many metres from the camera an agent is not drawn. Zero
    /// draws to the horizon. On ground with no horizon this is the only far
    /// cull, so it is set just inside where the fog has swallowed a pickle.
    /// </summary>
    public float DrawDistance { get; set; }

    private void PrepareView(Vector3 playerPosition, Vector3 eyeDirection, float eyeHeight, bool horizonCull)
    {
        // A horizon only exists on a sphere. On the flats the draw distance
        // stands in for it, and the fog is what hides where it falls.
        float radius = _ground.HorizonRadius;
        horizonCull &= radius > 0.0f;
        float horizonCosine = radius > 0.0f
            ? Mathf.Cos(
                (2.0f * Mathf.Acos(radius / (radius + Mathf.Max(eyeHeight, 0.1f)))) +
                Mathf.Acos(radius / (radius + (_maxHalfHeight * 2.0f))))
            : -1.0f;

        _classifyPlayer = playerPosition;
        _classifyEye = eyeDirection;
        _classifyPressure = PressurePoint;
        _classifyHorizonCosineSquared = horizonCosine * horizonCosine;
        _classifyLodSquared = LodDistance * LodDistance;
        _classifyPressureSquared = PressureRadius * PressureRadius;
        _classifyMargin = (_maxHalfHeight * 1.5f) + 0.5f;
        _classifyHorizon = horizonCull;
        _classifyFrustum = _frustumValid && FrustumCullEnabled;
        _classifyDrawSquared = DrawDistance > 0.0f ? DrawDistance * DrawDistance : float.MaxValue;
    }

    /// <summary>
    /// Decides whether and with which mesh an agent is drawn, and counts it.
    /// Three culls, cheapest first: past the draw distance or the shared
    /// horizon of the eye and the agent, outside the camera's view, and then
    /// near or far for the level of detail.
    /// </summary>
    private bool ClassifyAgent(int index, Span<int> visibleCounts, ref ViewCounts counts, int nextFrame)
    {
        ref Agent agent = ref _agents[index];
        float distanceSquared = agent.Position.DistanceSquaredTo(_classifyPlayer);
        if (distanceSquared < 625.0f)
        {
            counts.Near++;
        }

        if (agent.Position.DistanceSquaredTo(_classifyPressure) < _classifyPressureSquared)
        {
            counts.Pressure++;
        }

        // dot(eye, p / |p|) >= cos, without the square root: both sides are
        // positive whenever it can pass.
        float along = _classifyEye.Dot(agent.Position);
        bool visible = distanceSquared <= _classifyDrawSquared
                       && (!_classifyHorizon
                           || (along > 0.0f
                               && along * along >= _classifyHorizonCosineSquared * agent.Position.LengthSquared()));
        if (visible && _classifyFrustum)
        {
            visible = InFrustum(agent.Position, _classifyMargin);
        }

        if (visible)
        {
            int drawn = (agent.Kind * 2) + (distanceSquared > _classifyLodSquared ? 1 : 0);
            _drawBucket[index] = (byte)drawn;
            visibleCounts[drawn]++;
        }
        else
        {
            _drawBucket[index] = NotDrawn;
        }

        // Due next frame: anything airborne, held or coasting always, and
        // otherwise on its tier's turn.
        if (agent.Airborne || agent.Held > 0.0f || agent.Flung > 0.0f)
        {
            return true;
        }
        int stride = StrideFor(distanceSquared);
        return stride <= 1 || (index + nextFrame) % stride == 0;
    }

    /// <summary>
    /// Inside the camera's view, give or take a margin: in front of it, and
    /// within its horizontal and vertical half-angles. The far plane is the
    /// horizon's job. Tested against the camera's own axes rather than
    /// Camera3D.GetFrustum's planes, whose convention measured wrong both ways
    /// round -- one sign culled what was straight ahead, the other almost
    /// nothing.
    /// </summary>
    private bool InFrustum(Vector3 position, float margin)
    {
        Vector3 offset = position - _viewOrigin;
        float depth = offset.Dot(_viewForward);
        if (depth < -margin)
        {
            return false;
        }

        float side = Mathf.Abs(offset.Dot(_viewRight));
        if (side - (depth * _viewTanX) > margin * _viewSecX)
        {
            return false;
        }

        float rise = Mathf.Abs(offset.Dot(_viewUp));
        return rise - (depth * _viewTanY) <= margin * _viewSecY;
    }

    /// <summary>
    /// Hands one buffer's packed transforms to the GPU.
    ///
    /// The buffer used to be allocated at full capacity -- twenty thousand
    /// instances for the baseline -- and the whole of it was uploaded every
    /// frame to draw the two hundred or so agents in view: close to a megabyte
    /// a frame through the marshaller, 3.3 ms of the frame at 1080p. The GPU
    /// buffer is now a power of two above what is drawn, and only that slice
    /// goes up -- the engine requires the buffer to match the instance count
    /// exactly, so the slice cannot be smaller than the allocation. It is
    /// reallocated only when the drawn count outgrows it or falls below a
    /// quarter of it, so a crowd hovering on a boundary does not reallocate
    /// every frame; the CPU-side array stays at capacity, so the pack never
    /// allocates.
    /// </summary>
    private void UploadBucket(int drawn, int count)
    {
        int allocated = _allocated[drawn];
        if (count > allocated || (allocated > MinAllocated && count < allocated / 4))
        {
            allocated = Mathf.Min(Mathf.Max(MinAllocated, Mathf.NearestPo2(Mathf.Max(count, 1))), _transforms[drawn].Length / FloatsPerInstance);
            _multiMeshes[drawn].InstanceCount = allocated;
            _allocated[drawn] = allocated;
        }

        _multiMeshes[drawn].VisibleInstanceCount = count;
        RenderingServer.MultimeshSetBuffer(
            _multiMeshRids[drawn],
            new ReadOnlySpan<float>(_transforms[drawn], 0, allocated * FloatsPerInstance));
    }

    // ---------------- Cheap deterministic randomness ----------------

    private float RandomUnit()
    {
        _random ^= _random << 13;
        _random ^= _random >> 7;
        _random ^= _random << 17;
        return (_random >> 40) / (float)(1 << 24);
    }

    /// <summary>
    /// Picks where each pack stands, once per seed rather than once per agent.
    ///
    /// Seeding evenly over the whole disc is what a load measurement wants and
    /// the opposite of what looking at the horde wants: at the shipped seed
    /// radius that is hundreds of square metres per agent against a short
    /// sightline, so almost none of the horde is ever on screen. Packs put the
    /// same count somewhere it can be seen and fought, and change nothing
    /// downstream -- the flow field pulls every pack at the same target, so
    /// they converge into one mass on their own.
    /// </summary>
    private void BuildPacks(Vector3 seedCentre, float seedRadius, bool force)
    {
        if (_ground == null || PackCount <= 0 || seedRadius <= 0.0f)
        {
            _packCentres = System.Array.Empty<Vector3>();
            return;
        }

        // Growing the horde takes several calls -- SetAgentCount seeds at most
        // MaxSpawnsPerCall an invocation -- and re-rolling the pack positions
        // between them would smear one surge across a dozen different crowds.
        // Only a reseed, a moved target or a changed pack count rebuilds.
        bool moved = SeedMinRadius > 0.0f
            ? _packCentre.DistanceSquaredTo(seedCentre) > SeedMinRadius * SeedMinRadius * 0.25f
            : !_packCentre.IsEqualApprox(seedCentre);
        bool stale = force
            || _packCentres.Length != PackCount
            || !Mathf.IsEqualApprox(_packRadius, seedRadius)
            || !Mathf.IsEqualApprox(_packMinRadius, SeedMinRadius)
            || moved;

        if (!stale)
        {
            return;
        }

        _packCentre = seedCentre;
        _packRadius = seedRadius;
        _packMinRadius = SeedMinRadius;

        float inner = Mathf.Min(SeedMinRadius, seedRadius);

        if (_packCentres.Length != PackCount)
        {
            _packCentres = new Vector3[PackCount];
        }

        for (int i = 0; i < PackCount; i++)
        {
            _packCentres[i] = _ground.Scatter(seedCentre, seedRadius, inner, RandomUnit(), RandomUnit());
        }
    }

    /// <summary>
    /// Lifts a point that is under the ground to an agent of this kind
    /// standing on it, and leaves one above the ground where it is.
    /// </summary>
    private Vector3 AboveGround(Vector3 position, int kind)
    {
        Vector3 surface = _ground.GroundBelow(position, out _);
        Vector3 up = _ground.UpAt(surface);
        float below = (_kindHalfHeight[kind]) - (position - surface).Dot(up);
        return below > 0.0f ? position + (up * below) : position;
    }
}
