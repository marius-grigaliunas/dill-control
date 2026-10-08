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
    /// towards the side that lost its support, at its most off-centre.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:rad/s")]
    public float ToppleSpin { get; set; } = 0.22f;

    [ExportGroup("Fracture")]
    /// <summary>A loose piece bigger than this does not fall as one body: it
    /// fractures into blocks that start out moving together and come apart
    /// as they land. In cubic metres, so a mech's fine voxels never fracture
    /// where a building's coarse ones would.</summary>
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

    /// <summary>A piece asleep this long stops being a body and stays as
    /// scenery: no physics, still breakable.</summary>
    [Export(PropertyHint.Range, "1,600,1,suffix:s")]
    public float SettleSeconds { get; set; } = 20.0f;

    [ExportGroup("Budget")]
    /// <summary>Edge of one re-meshed section, in voxels. A hit re-meshes the
    /// section it lands in, not the model.</summary>
    [Export(PropertyHint.Range, "8,64,1")]
    public int SectionEdge { get; set; } = 32;

    /// <summary>Most sections re-meshed in one frame, for everything in the
    /// world together. The rest wait a frame.</summary>
    [Export(PropertyHint.Range, "1,64,1")]
    public int SectionsPerFrame { get; set; } = 6;

    /// <summary>Most structural checks in one frame, world-wide.</summary>
    [Export(PropertyHint.Range, "1,32,1")]
    public int IntegrityChecksPerFrame { get; set; } = 2;

    /// <summary>Most loose pieces made in one frame. A collapse fractures into
    /// dozens at once; the rest are made over the next frames, where they
    /// would have got to by then.</summary>
    [Export(PropertyHint.Range, "1,64,1")]
    public int ChunksPerFrame { get; set; } = 8;
}
