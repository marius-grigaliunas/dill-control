using Godot;

namespace DillControl.Voxel;

/// <summary>
/// How the dust of breaking voxels looks and how much of it there is
/// (<see cref="DustField"/>). The clouds are what hide a piece being swapped
/// for another, so they have to be thick where it happens and last while it
/// does; they are also overdraw, which the integrated GPU pays for by the
/// pixel. Read once, when the field is made.
/// </summary>
[GlobalClass]
public partial class DustStats : Resource
{
    public const string DefaultPath = "res://resources/voxel/voxel_dust.tres";

    [ExportGroup("Budget")]
    /// <summary>Emitters in the pool: the most clouds alive at once.</summary>
    [Export(PropertyHint.Range, "1,128,1")]
    public int PoolSize { get; set; } = 24;

    /// <summary>Most clouds started in one frame. Past it the frame's requests
    /// are merged at twice the distance, and again, until they fit.</summary>
    [Export(PropertyHint.Range, "1,32,1")]
    public int MaxPerFrame { get; set; } = 6;

    /// <summary>Requests closer than this in one frame become one cloud.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")]
    public float MergeDistance { get; set; } = 4.0f;

    /// <summary>Puffs in one cloud. More fill its gaps; each is overdraw.</summary>
    [Export(PropertyHint.Range, "1,64,1")]
    public int ParticlesPerCloud { get; set; } = 16;

    [ExportGroup("Size")]
    /// <summary>Cloud scale per metre of the cube root of the material lost.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.05")]
    public float SizeScale { get; set; } = 1.6f;

    /// <summary>The smallest cloud, as a scale on the emitter: a rifle round's puff.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.05")]
    public float MinScale { get; set; } = 0.6f;

    /// <summary>The biggest cloud: a skyscraper coming down should raise one
    /// the size of a building.</summary>
    [Export(PropertyHint.Range, "1,40,0.5")]
    public float MaxScale { get; set; } = 8.0f;

    /// <summary>One puff's size range, before the cloud's scale.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.05")]
    public float PuffScaleMin { get; set; } = 1.2f;

    [Export(PropertyHint.Range, "0.1,10,0.05")]
    public float PuffScaleMax { get; set; } = 2.4f;

    [ExportGroup("Look")]
    /// <summary>How opaque a puff is at its thickest.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float PeakOpacity { get; set; } = 0.75f;

    /// <summary>Share of its life a puff stays at its thickest before it
    /// starts to thin. 0 thins from the moment it is full.</summary>
    [Export(PropertyHint.Range, "0,0.9,0.01")]
    public float HoldFraction { get; set; } = 0.0f;

    /// <summary>How far a cloud's colour leans from the broken material
    /// toward plain dust.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float DustBlend { get; set; } = 0.45f;

    [ExportGroup("Life")]
    /// <summary>Lifetime of the smallest cloud. Bigger ones linger longer.</summary>
    [Export(PropertyHint.Range, "0.2,20,0.1,suffix:s")]
    public float CloudLifetime { get; set; } = 2.6f;

    /// <summary>Extra seconds of life per unit of scale past one.</summary>
    [Export(PropertyHint.Range, "0,5,0.05,suffix:s")]
    public float LifetimePerScale { get; set; } = 0.45f;

    /// <summary>How fast dust settles with nothing pushing it.</summary>
    [Export(PropertyHint.Range, "0,10,0.05,suffix:m/s²")]
    public float BaseSink { get; set; } = 0.7f;

    /// <summary>How much faster a cloud sinks per m/s its material was
    /// falling: collapse dust drops with the structure it came off.</summary>
    [Export(PropertyHint.Range, "0,5,0.05")]
    public float SinkPerFallSpeed { get; set; } = 0.9f;
}
