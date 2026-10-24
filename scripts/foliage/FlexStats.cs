using Godot;

namespace DillControl.Foliage;

/// <summary>
/// How a kind of plant bends: a vine, a tree. Read by <see cref="PlantFlex"/>,
/// which builds a <see cref="Rod"/> from it and the plant's voxels. The masses
/// and forces are the game's, not nature's: a 117 m vine that bent under a
/// player as much as a real one would not bend at all.
/// </summary>
[GlobalClass]
public partial class FlexStats : Resource
{
    public const string VinePath = "res://resources/foliage/flex_vine.tres";

    [ExportGroup("Shape")]
    /// <summary>Stiff segments from foot to tip. More bends more smoothly.</summary>
    [Export(PropertyHint.Range, "2,16,1")]
    public int Segments { get; set; } = 10;

    /// <summary>
    /// How much the joints' stiffness follows the plant's thickness:
    /// section ^ this. 0 bends like a uniform pole; 2 is a real stem
    /// (stiffness goes with the square of its area), stiff at the foot and
    /// whippy at the tip.
    /// </summary>
    [Export(PropertyHint.Range, "0,3,0.05")]
    public float Taper { get; set; } = 1.0f;

    [ExportGroup("Sway")]
    /// <summary>Seconds for one sway, foot to tip and back. Long is a tall, slow plant.</summary>
    [Export(PropertyHint.Range, "0.5,20,0.1,suffix:s")]
    public float PeriodSeconds { get; set; } = 5.0f;

    /// <summary>How heavy it acts, all of it. What the line and a hit push against.</summary>
    [Export(PropertyHint.Range, "50,200000,10,suffix:kg")]
    public float MassKg { get; set; } = 3000.0f;

    /// <summary>Share of critical damping. Low rings on after a hit; 1 never overshoots.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.01")]
    public float Damping { get; set; } = 0.1f;

    /// <summary>Furthest it ever leans from upright, however weak a cut leaves it.</summary>
    [Export(PropertyHint.Range, "1,80,1,suffix:deg")]
    public float MaxLeanDegrees { get; set; } = 30.0f;

    [ExportGroup("Pushes")]
    /// <summary>Sideways acceleration per m/s of wind, at the top (less toward the foot).</summary>
    [Export(PropertyHint.Range, "0,2,0.005")]
    public float WindPush { get; set; } = 0.1f;

    /// <summary>Impulse per point of an explosion's damage, N s: how hard a
    /// blast shakes it. Rounds, blades, flames and beams do not shake it.</summary>
    [Export(PropertyHint.Range, "0,200,0.5")]
    public float KickPerDamage { get; set; } = 6.0f;

    /// <summary>The most one blast can give it, N s, however big: a heavy
    /// warhead shakes it, it does not fling it. Zero: no limit.</summary>
    [Export(PropertyHint.Range, "0,100000,100")]
    public float MaxKick { get; set; } = 0.0f;

    /// <summary>Scales the pull of a grapple line hanging from it.</summary>
    [Export(PropertyHint.Range, "0,20,0.05")]
    public float LinePull { get; set; } = 1.0f;

    [ExportGroup("Damage")]
    /// <summary>
    /// How fast a cut joint goes soft: stiffness times (section left) ^ this.
    /// 2 is a stem; higher sags sooner.
    /// </summary>
    [Export(PropertyHint.Range, "0,6,0.1")]
    public float Weakening { get; set; } = 2.0f;

    /// <summary>
    /// A joint with less than this share of its stiffness left gives way once
    /// it is kinked past <see cref="GiveWayDegrees"/> or the plant leans on
    /// its limit...
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GiveWayBelow { get; set; } = 0.6f;

    /// <summary>How far a weakened joint bends before it breaks.</summary>
    [Export(PropertyHint.Range, "0.5,30,0.5,suffix:deg")]
    public float GiveWayDegrees { get; set; } = 3.0f;

    /// <summary>...after bending that far for this long.</summary>
    [Export(PropertyHint.Range, "0,5,0.05,suffix:s")]
    public float GiveWaySeconds { get; set; } = 0.6f;

    /// <summary>How fast what gave way starts to turn over, the way it leant.</summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:rad/s")]
    public float ToppleSpin { get; set; } = 0.25f;

    /// <summary>A piece of it at least this long, broken off, falls with <see cref="FallingAngularDamp"/>...</summary>
    [Export(PropertyHint.Range, "1,200,1,suffix:m")]
    public float SlenderMetres { get; set; } = 10.0f;

    /// <summary>...rather than the debris' own, which is for blocks and stops a tall piece turning over.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float FallingAngularDamp { get; set; } = 0.05f;

    [ExportGroup("Collision")]
    /// <summary>Within this of the camera, the sections of its collision near the camera...</summary>
    [Export(PropertyHint.Range, "0,2000,10,suffix:m")]
    public float FollowNearMetres { get; set; } = 60.0f;

    /// <summary>...those within this of it, follow it every tick and carry what stands on them.</summary>
    [Export(PropertyHint.Range, "0,200,1,suffix:m")]
    public float FollowSectionMetres { get; set; } = 30.0f;

    /// <summary>Further off, a section of its collision moves only once it is this far behind...</summary>
    [Export(PropertyHint.Range, "0.05,10,0.05,suffix:m")]
    public float FollowFarStep { get; set; } = 0.5f;

    /// <summary>...and is looked at only every this many ticks.</summary>
    [Export(PropertyHint.Range, "1,60,1")]
    public int FollowFarEvery { get; set; } = 8;

    [ExportGroup("Distance")]
    /// <summary>Beyond this of the camera its spine is stepped every <see cref="StepFarEvery"/> ticks.</summary>
    [Export(PropertyHint.Range, "0,5000,10,suffix:m")]
    public float StepFarMetres { get; set; } = 120.0f;

    [Export(PropertyHint.Range, "1,16,1")]
    public int StepFarEvery { get; set; } = 4;

    [ExportGroup("Falling")]
    /// <summary>When a long piece of it falls (cut, given way or unrooted),
    /// this many big clouds burst round where it broke; and where it comes
    /// down, a cloud every <see cref="FallDustStepM"/> along it. 0: none.</summary>
    [Export(PropertyHint.Range, "0,24,1")]
    public int FallDustPuffs { get; set; }

    /// <summary>Each cloud's width.</summary>
    [Export(PropertyHint.Range, "1,40,0.5,suffix:m")]
    public float FallDustEdgeM { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "2,40,0.5,suffix:m")]
    public float FallDustStepM { get; set; } = 9.0f;

    /// <summary>How long it takes to come down, as this times the square root of
    /// its length over g: a rod toppling from its foot.</summary>
    [Export(PropertyHint.Range, "0.5,4,0.05")]
    public float FallLandK { get; set; } = 1.6f;

    /// <summary>The clouds' colour: dark, so they read against the deep greens.</summary>
    [Export]
    public Color FallDustColour { get; set; } = new(0.24f, 0.24f, 0.17f);

    [ExportGroup("Recount")]
    /// <summary>Seconds between recounts of what is left after damage (worked out on a voxel worker).</summary>
    [Export(PropertyHint.Range, "0.05,2,0.05,suffix:s")]
    public float RecountSeconds { get; set; } = 0.25f;
}
