using Godot;

namespace DillControl.Planet;

/// <summary>
/// How a planet is cut up, drawn, streamed and walked, whatever its shape:
/// vertex spacing, the collision ring, the build budget, the far shell, the
/// gravity well, how blasts dig, and what slope the horde will climb.
///
/// One resource for every planet. Arguable numbers about what a planet *is*
/// are on <see cref="PlanetSpec"/>.
/// </summary>
[GlobalClass]
public partial class PlanetStreamingStats : Resource
{
    [ExportGroup("Mesh")]
    /// <summary>
    /// Metres between heightfield vertices. Held constant as the radius
    /// changes, which is what keeps a chunk's cost the same on every planet.
    /// The lab's 4.91 m held its budget.
    /// </summary>
    [Export(PropertyHint.Range, "1,12,0.01,suffix:m")]
    public float VertexSpacing { get; set; } = 4.91f;

    /// <summary>Vertices along a chunk's edge. 33 costs about 1 ms to build.</summary>
    [Export(PropertyHint.Range, "9,65,8")]
    public int ChunkVertexEdge { get; set; } = 33;

    /// <summary>Vertices along a face edge of the far shell, the whole planet seen from orbit.</summary>
    [Export(PropertyHint.Range, "9,129,8")]
    public int FarShellVertexEdge { get; set; } = 65;

    /// <summary>
    /// How far under the near chunks the far shell sits, below the lowest
    /// ground its triangle covers. Enough that it never shows through.
    /// </summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:m")]
    public float FarShellSink { get; set; } = 1.5f;

    /// <summary>
    /// A coarser chunk level's error, as a share of its vertex spacing. Godot
    /// switches to a level once that error is under a pixel; smaller switches
    /// later. 0.05 puts the first step near 350 m and the third near 1.4 km.
    /// </summary>
    [Export(PropertyHint.Range, "0.005,1,0.005")]
    public float LodErrorScale { get; set; } = 0.05f;

    [ExportGroup("Streaming")]
    /// <summary>
    /// Near chunks further than this from the camera are not drawn; the far
    /// shell stands in. From the surface the horizon is nearer than this, so
    /// it only bites from altitude.
    /// </summary>
    [Export(PropertyHint.Range, "200,6000,10,suffix:m")]
    public float NearDrawDistance { get; set; } = 1800.0f;

    /// <summary>Chunks either side of the viewer's that carry a collider.</summary>
    [Export(PropertyHint.Range, "0,4,1")]
    public int CollisionRingChunks { get; set; } = 1;

    /// <summary>Most chunk meshes built in one frame.</summary>
    [Export(PropertyHint.Range, "1,64,1")]
    public int MaxChunkBuildsPerFrame { get; set; } = 6;

    /// <summary>Stop starting chunk builds once a frame has spent this much on them.</summary>
    [Export(PropertyHint.Range, "0.5,10,0.1,suffix:ms")]
    public float ChunkBuildBudgetMs { get; set; } = 3.0f;

    [ExportGroup("Gravity")]
    /// <summary>How far the gravity well reaches, as a multiple of the radius. Past it is zero-g.</summary>
    [Export(PropertyHint.Range, "1.1,6,0.05")]
    public float GravityFieldMultiplier { get; set; } = 1.8f;

    [ExportGroup("Craters")]
    /// <summary>A blast that names no crater digs one this share of its radius.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float BlastCraterScale { get; set; } = 0.45f;

    /// <summary>The crater's depth as a share of its radius.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float BlastCraterDepthRatio { get; set; } = 0.3f;

    /// <summary>Smaller blasts dig nothing.</summary>
    [Export(PropertyHint.Range, "0,50,0.5,suffix:m")]
    public float BlastCraterMinRadius { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0,5,0.05,suffix:m")]
    public float CraterRimHeight { get; set; } = 0.7f;

    [Export(PropertyHint.Range, "0,10,0.1")]
    public float CraterRimSharpness { get; set; } = 4.0f;

    /// <summary>The depth at which a crater gets its full rim.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:m")]
    public float CraterReferenceDepth { get; set; } = 2.6f;

    [ExportGroup("Horde")]
    /// <summary>
    /// Wanted width of a horde flow cell. The grid is a power of two per face
    /// edge, so the real width is the nearest that fits.
    /// </summary>
    [Export(PropertyHint.Range, "4,60,0.5,suffix:m")]
    public float FlowCellSize { get; set; } = 15.0f;

    /// <summary>Ground at least this steep is a wall to the horde; below it, the steeper the dearer.</summary>
    [Export(PropertyHint.Range, "10,89,1,suffix:°")]
    public float MaxSlopeDegrees { get; set; } = 46.0f;

    /// <summary>Metres round a horde's garrison in which authored props are stamped into its routing.</summary>
    [Export(PropertyHint.Range, "0,2000,10,suffix:m")]
    public float ObstacleStampRadius { get; set; } = 500.0f;
}
