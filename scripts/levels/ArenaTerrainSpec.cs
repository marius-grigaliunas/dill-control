using Godot;

namespace DillControl.Levels;

/// <summary>
/// The numbers of an arena's ground that are not where a feature stands: how
/// big, how fine, the rolling noise under everything, the rim round the edge
/// and how steep the horde climbs. Read by <see cref="ArenaTerrain"/>. The
/// hills and mesas themselves are <see cref="TerrainFeature"/> nodes.
/// A tool script because <see cref="ArenaTerrain"/> is one: in the editor a
/// non-tool resource arrives as a bare placeholder and its export fails to cast.
/// </summary>
[Tool]
[GlobalClass]
public partial class ArenaTerrainSpec : Resource
{
    [ExportGroup("Shape")]
    /// <summary>Metres along one side of the square.</summary>
    [Export(PropertyHint.Range, "100,4000,10,suffix:m")]
    public float Size { get; set; } = 1000.0f;

    /// <summary>Metres between height samples. Finer draws and collides more triangles.</summary>
    [Export(PropertyHint.Range, "1,16,0.5,suffix:m")]
    public float Spacing { get; set; } = 4.0f;

    /// <summary>Chunks along one side: what is culled and collided together.</summary>
    [Export(PropertyHint.Range, "1,32,1")]
    public int Chunks { get; set; } = 10;

    /// <summary>The noise's seed. Same seed, same ground (rolled through SeededRandom's "arena-terrain" stream).</summary>
    [Export]
    public long Seed { get; set; } = 1;

    [ExportGroup("Rolling ground")]
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m")]
    public float NoiseAmplitude { get; set; } = 7.0f;

    /// <summary>Metres across the largest swell.</summary>
    [Export(PropertyHint.Range, "20,2000,10,suffix:m")]
    public float NoiseScale { get; set; } = 260.0f;

    [Export(PropertyHint.Range, "1,8,1")]
    public int NoiseOctaves { get; set; } = 4;

    /// <summary>Share of the noise that is ridged (sharp crests) rather than rolling.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float RidgeShare { get; set; } = 0.3f;

    [ExportGroup("Rim")]
    /// <summary>The ground rises this far over the last <see cref="RimWidth"/> to the edge, so the arena ends in hills rather than a drop.</summary>
    [Export(PropertyHint.Range, "0,200,1,suffix:m")]
    public float RimHeight { get; set; } = 45.0f;

    [Export(PropertyHint.Range, "0,400,5,suffix:m")]
    public float RimWidth { get; set; } = 90.0f;

    [ExportGroup("Horde")]
    /// <summary>Width of one of the horde's flow cells.</summary>
    [Export(PropertyHint.Range, "2,20,0.5,suffix:m")]
    public float FlowCellSize { get; set; } = 6.0f;

    /// <summary>Steeper than this and the horde will not walk it: a mesa's cliff is a wall.</summary>
    [Export(PropertyHint.Range, "10,80,1,suffix:deg")]
    public float MaxSlopeDegrees { get; set; } = 42.0f;
}
