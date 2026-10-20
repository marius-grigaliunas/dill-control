using Godot;

namespace DillControl.Voxel;

/// <summary>
/// How every voxel model in the game breaks: how much a hit takes out, how
/// pieces come loose, how the loose pieces fly, break and settle, and what it
/// may cost a frame. One resource for all of them, because "everything breaks
/// the same way" is the rule (docs/decisions/0011).
///
/// Sizes are in metres, never voxels, so a rocket takes the same bite out of a
/// 0.1 m mech and a 0.5 m tower.
/// </summary>
[GlobalClass]
public partial class VoxelDestructionStats : Resource
{
    public const string DefaultPath = "res://resources/voxel/voxel_destruction_default.tres";

    [ExportGroup("Carving")]
    /// <summary>Radius every hit takes out, before damage is counted.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:m")]
    public float CarveBase { get; set; } = 0.12f;

    /// <summary>Extra radius per point of damage. A 20-damage round pits about
    /// 0.25 m, a 260-damage rocket blows out about 1.7 m.</summary>
    [Export(PropertyHint.Range, "0,0.05,0.0005,suffix:m")]
    public float CarvePerDamage { get; set; } = 0.006f;

    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:m")]
    public float CarveMax { get; set; } = 3.5f;

    /// <summary>A sustained weapon (flame, beam) carves only every this many
    /// damage points, or it would erase whatever it touched in a second.</summary>
    [Export(PropertyHint.Range, "1,500,1")]
    public float SustainedDamagePerCarve { get; set; } = 40.0f;

    /// <summary>Armour: taken off every single hit before it does anything,
    /// so small arms barely mark it and heavy weapons still tell. Not off
    /// sustained damage (flame, a crowd chewing), which is a rate, not hits,
    /// and not off a beam's melt. 0 for none.</summary>
    [Export(PropertyHint.Range, "0,500,1")]
    public float Armour { get; set; } = 0.0f;

    /// <summary>Hits smaller than this (after armour) are banked, like
    /// sustained damage, and bite once the bank reaches it. 0 for none.</summary>
    [Export(PropertyHint.Range, "0,2000,1")]
    public float MinCarveDamage { get; set; } = 0.0f;

    /// <summary>When above 0, a hit removes this many cubic metres per point
    /// of damage, as a sphere, instead of the radius rule above: what it takes
    /// grows with the damage, not with its cube, so a banked burst of rifle
    /// fire does not come out as one huge bite. For armoured things.</summary>
    [Export(PropertyHint.Range, "0,0.01,0.00001,suffix:m³")]
    public float CarveVolumePerDamage { get; set; } = 0.0f;

    [ExportGroup("Structure")]
    /// <summary>How long after a hit a piece is checked for what has come
    /// loose. The check floods the piece's whole grid, so hits in one burst
    /// share one.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:s")]
    public float IntegrityDelay { get; set; } = 0.12f;

    /// <summary>A part hangs from its hinge: the cells this close to its
    /// pivot are what it hangs from. Shoot them away and it falls off.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.05,suffix:m")]
    public float HingeRadius { get; set; } = 0.3f;

    /// <summary>A building carries its own weight: a layer that has kept less
    /// than this share of the voxels it was built with, under a full load,
    /// gives way and everything above it comes down. Off-centre loss counts
    /// for more (the load hangs over the side that went), and a layer near
    /// the top, with little above it, needs less. So a lobby is brought down
    /// by taking out most of its columns, not every last voxel.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MinSupportFraction { get; set; } = 0.4f;

    /// <summary>Layers of building above a layer that make a full load on it.
    /// A layer with fewer above needs proportionally less support.</summary>
    [Export(PropertyHint.Range, "1,400,1")]
    public float FullLoadLayers { get; set; } = 40.0f;

    /// <summary>Less than this many voxels above a layer is never checked: a
    /// parapet does not collapse.</summary>
    [Export(PropertyHint.Range, "1,5000,1")]
    public int MinLoadVoxels { get; set; } = 150;

    /// <summary>How fast what is above a failed layer starts to tip over,
    /// towards the side that lost its support, at its most off-centre. A
    /// layer that failed evenly all round tips not at all: it comes straight
    /// down.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:rad/s")]
    public float ToppleSpin { get; set; } = 0.22f;

    /// <summary>How much of a building buckles when a layer fails: the storey
    /// from that layer up goes to dust, and what is above drops through it.</summary>
    [Export(PropertyHint.Range, "0.5,20,0.5,suffix:m")]
    public float FailureDepth { get; set; } = 4.0f;

    /// <summary>How long after a crush a part's structure is checked; each
    /// crush puts it off again, so a tower crushing down is checked once it
    /// stops.</summary>
    [Export(PropertyHint.Range, "0,5,0.05,suffix:s")]
    public float CrushIntegrityDelay { get; set; } = 1.0f;

    [ExportGroup("Collapse")]
    /// <summary>A layer that failed this far off-centre (as a fraction of the
    /// footprint's half width) still holds on the side that is left: what is
    /// above hangs on a hinge there, leans over towards the side that went,
    /// and tears free at <see cref="HingeBreakAngle"/>. Less, and it drops.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float HingeOffCentre { get; set; } = 0.15f;

    /// <summary>How far a building leans on what is left of its support
    /// before that tears and it falls free.</summary>
    [Export(PropertyHint.Range, "1,45,0.5,suffix:°")]
    public float HingeBreakAngle { get; set; } = 10.0f;

    /// <summary>Longest a building hangs on its hinge, however far it has
    /// leant: what is left of the support gives way.</summary>
    [Export(PropertyHint.Range, "0.1,20,0.1,suffix:s")]
    public float HingeHoldSeconds { get; set; } = 2.5f;

    /// <summary>A big loose piece landing at least this fast does not stop: it
    /// crushes a slab of itself and of the building it lands on, and keeps
    /// going. A tower dropped one storey comes down storey by storey.</summary>
    [Export(PropertyHint.Range, "0,30,0.1,suffix:m/s")]
    public float CrushThroughSpeed { get; set; } = 2.5f;

    /// <summary>How deep one landing crushes, into the piece and into the
    /// building under it.</summary>
    [Export(PropertyHint.Range, "0.5,20,0.5,suffix:m")]
    public float CrushDepth { get; set; } = 4.0f;

    /// <summary>How far round the contact a crush reaches, past the contacts'
    /// own spread.</summary>
    [Export(PropertyHint.Range, "0,20,0.5,suffix:m")]
    public float CrushWidth { get; set; } = 4.0f;

    /// <summary>Share of its speed a piece keeps through a crush: what it
    /// crushed took the rest.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CrushKeep { get; set; } = 0.7f;

    /// <summary>A piece less than this tall, along the way it is landing,
    /// has too little weight above the contact to crush through: it lands,
    /// and breaks up if it landed hard (<see cref="BreakSpeed"/>).</summary>
    [Export(PropertyHint.Range, "1,100,0.5,suffix:m")]
    public float CrushStopHeight { get; set; } = 16.0f;

    /// <summary>Share of what a crush or a failed storey removes that stays,
    /// heaped round the base (RubblePile); the rest is dust.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float RubbleShare { get; set; } = 0.7f;

    /// <summary>How far a rubble heap spreads past the footprint it came off.</summary>
    [Export(PropertyHint.Range, "2,60,0.5,suffix:m")]
    public float RubbleReach { get; set; } = 14.0f;

    /// <summary>Highest a rubble heap grows.</summary>
    [Export(PropertyHint.Range, "2,60,0.5,suffix:m")]
    public float RubbleHeight { get; set; } = 20.0f;

    /// <summary>Small chunks thrown out from the edge of each crush, for the
    /// eye to follow. Within the loose-piece cap.</summary>
    [Export(PropertyHint.Range, "0,8,1")]
    public int SpallPerCrush { get; set; } = 3;

    /// <summary>Clouds in the ring a crush raises round its footprint, each
    /// about a storey across, rolling outwards.</summary>
    [Export(PropertyHint.Range, "0,16,1")]
    public int CrushDustRing { get; set; } = 6;

    /// <summary>A loose piece at least this big trails dust off its edges
    /// while it falls, so its outline is in a cloud when it is crushed, broken
    /// or swapped. 0 for none.</summary>
    [Export(PropertyHint.Range, "0,2000,1,suffix:m³")]
    public float TrailDustVolume { get; set; } = 40.0f;

    /// <summary>Slowest a piece trails dust at.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float TrailDustSpeed { get; set; } = 4.0f;

    /// <summary>Seconds between one trail of clouds and the next.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.01,suffix:s")]
    public float TrailDustInterval { get; set; } = 0.3f;

    /// <summary>Clouds in each trail, spread along the piece's length.</summary>
    [Export(PropertyHint.Range, "0,8,1")]
    public int TrailDustPoints { get; set; } = 3;

    /// <summary>How big each trail cloud is, as the edge of the material it
    /// stands for (DustField sizes clouds from that).</summary>
    [Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")]
    public float TrailDustSize { get; set; } = 3.0f;

    /// <summary>How much dust this kind of thing raises: every cloud from it
    /// and from the pieces that came off it is this times as wide, after
    /// the cloud's size cap. 0.5 is a quarter of the cover. Read from the
    /// model's own resource, not the default's, and carried by its pieces.</summary>
    [Export(PropertyHint.Range, "0,4,0.05")]
    public float DustScale { get; set; } = 1.0f;

    [ExportGroup("Fracture")]
    /// <summary>A piece bigger than this is heavy: what comes loose of a
    /// building falls as one body and crushes through what it lands on
    /// (<see cref="CrushThroughSpeed"/>), and a thing destroyed outright
    /// fractures into blocks rather than falling as one. In cubic metres, so a
    /// mech's fine voxels never count where a building's coarse ones would.</summary>
    [Export(PropertyHint.Range, "1,2000,1,suffix:m³")]
    public float FractureVolume { get; set; } = 40.0f;

    /// <summary>Width and depth of a fracture block, before the piece count
    /// cap makes them bigger.</summary>
    [Export(PropertyHint.Range, "1,40,0.5,suffix:m")]
    public float FractureWidth { get; set; } = 5.0f;

    /// <summary>Height of a fracture block: about a storey, so a falling
    /// tower comes apart floor by floor.</summary>
    [Export(PropertyHint.Range, "1,40,0.5,suffix:m")]
    public float FractureHeight { get; set; } = 4.0f;

    /// <summary>Most blocks one release fractures into. A bigger piece gets
    /// bigger blocks.</summary>
    [Export(PropertyHint.Range, "2,200,1")]
    public int MaxFracturePieces { get; set; } = 36;

    /// <summary>A heavy piece landing hard on its side snaps across its
    /// length into segments about this long, rather than crumbling.</summary>
    [Export(PropertyHint.Range, "4,100,1,suffix:m")]
    public float SnapLength { get; set; } = 20.0f;

    /// <summary>Cloud a piece of a break-up comes out of, per metre of its
    /// size: the cover for its old shape giving way to its pieces.</summary>
    [Export(PropertyHint.Range, "0,4,0.05")]
    public float CoverDust { get; set; } = 1.6f;

    /// <summary>Biggest loose piece the gravity gun can lift, in voxels.</summary>
    [Export(PropertyHint.Range, "1,20000,1")]
    public int MaxGrabVoxels { get; set; } = 600;

    /// <summary>Radius of the lump the gravity gun tears off a rubble heap.</summary>
    [Export(PropertyHint.Range, "0.3,4,0.1,suffix:m")]
    public float PluckRadius { get; set; } = 1.2f;

    /// <summary>Smallest block a landing piece crumbles into.</summary>
    [Export(PropertyHint.Range, "0.2,10,0.1,suffix:m")]
    public float MinCrumbleSize { get; set; } = 1.5f;

    [ExportGroup("Loose pieces")]
    /// <summary>Most loose pieces in the world at once. Past it the oldest
    /// settled one turns to dust.</summary>
    [Export(PropertyHint.Range, "0,400,1")]
    public int MaxChunks { get; set; } = 120;

    /// <summary>A piece smaller than this many voxels is dust, not a body.</summary>
    [Export(PropertyHint.Range, "1,500,1")]
    public int MinChunkVoxels { get; set; } = 6;

    /// <summary>Kilograms per cubic metre of voxel. Concrete.</summary>
    [Export(PropertyHint.Range, "10,8000,10")]
    public float Density { get; set; } = 2400.0f;

    /// <summary>Friction of a loose piece: rubble grinds to a halt, it does
    /// not skate.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float Friction { get; set; } = 1.0f;

    /// <summary>Spin a loose piece is thrown off with, in rad/s for a piece a
    /// metre across; a bigger one turns slower, by its size. Heavy things do
    /// not tumble like twigs.</summary>
    [Export(PropertyHint.Range, "0,10,0.05,suffix:rad/s")]
    public float LooseSpin { get; set; } = 1.5f;

    /// <summary>Angular damping on a loose piece, so a block that lands on its
    /// corner falls over rather than rolling away.</summary>
    [Export(PropertyHint.Range, "0,10,0.05")]
    public float AngularDamp { get; set; } = 0.6f;

    /// <summary>A piece that loses this much speed in one step breaks apart.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m/s")]
    public float BreakSpeed { get; set; } = 8.0f;

    /// <summary>Only pieces at least this big break on impact; smaller ones
    /// bounce and settle.</summary>
    [Export(PropertyHint.Range, "2,5000,1")]
    public int MinBreakVoxels { get; set; } = 40;

    /// <summary>A piece landing this fast crushes what it lands on.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m/s")]
    public float CrushSpeed { get; set; } = 6.0f;

    /// <summary>Crush damage per kilogram-metre-per-second of impact.</summary>
    [Export(PropertyHint.Range, "0,1,0.001")]
    public float CrushDamagePerMomentum { get; set; } = 0.05f;

    /// <summary>The most one landing does: enough to kill whatever a falling
    /// block lands on, and to bite the most a hit can out of a building. It
    /// never grinds other rubble (VoxelChunk ignores rubble), so piles stay.</summary>
    [Export(PropertyHint.Range, "0,10000,1")]
    public float CrushDamageMax { get; set; } = 1500.0f;

    /// <summary>How hard a piece is thrown off a hit, in m/s.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float BreakawaySpeed { get; set; } = 4.0f;

    /// <summary>Most boxes in one loose piece's collision. Past it the piece
    /// is boxed at half resolution, then a quarter, and so on.</summary>
    [Export(PropertyHint.Range, "1,64,1")]
    public int MaxChunkBoxes { get; set; } = 24;

    /// <summary>A small piece lying still this long joins the rubble heap where
    /// it lies: drawn with the heap's sections instead of on its own, and no
    /// body. A collapse leaves a hundred pieces, and a hundred more draws.</summary>
    [Export(PropertyHint.Range, "0.5,60,0.5,suffix:s")]
    public float BakeAfterSeconds { get; set; } = 1.0f;

    /// <summary>Only pieces up to this many voxels are baked into a heap;
    /// bigger ones (a tower's segments) stay pieces you can blow apart.</summary>
    [Export(PropertyHint.Range, "1,100000,1")]
    public int BakeMaxVoxels { get; set; } = 3000;

    /// <summary>A loose piece found this far under the ground has been
    /// squeezed through it (a tower coming down on it, mass against mass):
    /// up to <see cref="BakeMaxVoxels"/> it goes to dust and its rubble to
    /// the heap there, a bigger one is set back on the surface. 0 is off.</summary>
    [Export(PropertyHint.Range, "0,5,0.05,suffix:m")]
    public float SunkDepth { get; set; } = 0.6f;

    /// <summary>A piece still this long stops being a body and stays as
    /// scenery: no physics, still breakable, and it falls again when what it
    /// rests on goes (<see cref="SupportCheckSeconds"/>). Awake, a tower's
    /// segment carries its 200-odd boxes into every physics step: at 20 s,
    /// eight towers' worth crept on the rubble at a few centimetres a second,
    /// and the pieces awake carried 1 400 boxes; at 3 s, 700.</summary>
    [Export(PropertyHint.Range, "0.5,600,0.5,suffix:s")]
    public float SettleSeconds { get; set; } = 3.0f;

    /// <summary>A piece that has gone less than this far in a second counts
    /// as still, however fast it jitters or creeps: a tower segment grinding
    /// on rubble at 0.1-1 m/s went nowhere for 30 s and never slept.</summary>
    [Export(PropertyHint.Range, "0,2,0.05,suffix:m")]
    public float StillTravel { get; set; } = 0.3f;

    /// <summary>How often a settled piece checks it still has something under
    /// it, and wakes and falls if not. Staggered across pieces.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.1,suffix:s")]
    public float SupportCheckSeconds { get; set; } = 0.5f;

    /// <summary>Fastest a loose piece moves other than falling: up or
    /// sideways. Blasts stacking on one piece threw it at 80 m/s, 600 m into
    /// the sky, where it never came to rest. Above the gravity gun's throw
    /// (42 m/s). 0 for no limit.</summary>
    [Export(PropertyHint.Range, "0,200,1,suffix:m/s")]
    public float MaxThrowSpeed { get; set; } = 45.0f;

    /// <summary>Most boxes in a settled piece's collision. In flight a piece is
    /// boxed coarsely (<see cref="MaxChunkBoxes"/>); once it has settled it is
    /// re-boxed as finely as this allows, or a box spans every hole shot into
    /// it and stops you where nothing is drawn. Woken, it goes back to its
    /// flight boxes: a frozen piece's boxes cost the step nothing, an awake
    /// one's are tested every tick.</summary>
    [Export(PropertyHint.Range, "8,2000,1")]
    public int RestingChunkBoxes { get; set; } = 256;

    [ExportGroup("Budget")]
    /// <summary>Edge of one re-meshed section, in voxels. A hit re-meshes the
    /// section it lands in, not the model.</summary>
    [Export(PropertyHint.Range, "8,64,1")]
    public int SectionEdge { get; set; } = 32;

    /// <summary>Most sections being re-meshed on worker threads at once, for
    /// everything in the world together. The rest wait their turn. Kept below
    /// the cores: the horde's flow field has a worker of its own.</summary>
    [Export(PropertyHint.Range, "1,16,1")]
    public int MeshWorkers { get; set; } = 2;

    /// <summary>Main-thread time a frame may spend handing re-meshed sections
    /// to the renderer and the physics, for everything in the world together.
    /// One section always goes, however long it takes.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:ms")]
    public float ApplyMillisecondsPerFrame { get; set; } = 1.5f;

    /// <summary>Most landing blasts in one frame, world-wide. A crumbling
    /// block's pieces land together and each one's blast weighs everything
    /// in reach; past this, a piece lands without one. Crushing through
    /// always has its blast.</summary>
    [Export(PropertyHint.Range, "1,32,1")]
    public int LandingBlastsPerFrame { get; set; } = 3;

    /// <summary>Most structural checks in one frame, world-wide.</summary>
    [Export(PropertyHint.Range, "1,32,1")]
    public int IntegrityChecksPerFrame { get; set; } = 2;

    [ExportGroup("Chunks")]
    /// <summary>An edge between two chunks holds while it keeps at least this
    /// share of the voxel faces it was built with. 0: a single remaining face
    /// holds, which is how a flood over cells behaves, so a railing built on
    /// two voxels does not fall off at load.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MinContactShare { get; set; } = 0.0f;

    /// <summary>How long an overloaded, already-damaged edge weakens before it
    /// lets go: dust, a creak and chunks shedding off the cut, then the fall.
    /// 0: it lets go on the check that finds it, as in decision 0014.</summary>
    [Export(PropertyHint.Range, "0,8,0.1,suffix:s")]
    public float StressSeconds { get; set; } = 1.4f;

    /// <summary>An edge that has lost this share of its as-built faces, and
    /// still carries weight, starts weakening instead of waiting for the
    /// next hit. A cut clean through has no faces left and does not wait.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01")]
    public float StressLimit { get; set; } = 0.35f;

    /// <summary>How long the gravity gun pulls per voxel face still holding
    /// the chunk it is aimed at.</summary>
    [Export(PropertyHint.Range, "0,0.05,0.0005,suffix:s")]
    public float TearSecondsPerContact { get; set; } = 0.004f;

    /// <summary>Past this long a pull would take, the chunk is refused as
    /// held fast rather than coming out.</summary>
    [Export(PropertyHint.Range, "0.1,8,0.1,suffix:s")]
    public float TearMaxSeconds { get; set; } = 1.6f;

    /// <summary>Camera shake when a strained edge starts to give, falling off
    /// with distance. The collapse itself still shakes through its blasts.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float StrainShake { get; set; } = 0.18f;

    [ExportGroup("Budget")]
    /// <summary>Most loose pieces made in one frame. A collapse fractures into
    /// dozens at once; the rest are made over the next frames, where they
    /// would have got to by then.</summary>
    [Export(PropertyHint.Range, "1,64,1")]
    public int ChunksPerFrame { get; set; } = 8;

    /// <summary>Main-thread time a frame may spend making loose pieces. One is
    /// always made, however long it takes; a tower's upper part takes about
    /// 3 ms, a crumbled block's pieces a tenth of that each.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:ms")]
    public float SpawnMillisecondsPerFrame { get; set; } = 2.0f;
}
