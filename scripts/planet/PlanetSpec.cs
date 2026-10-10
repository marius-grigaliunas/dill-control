using Godot;

namespace DillControl.Planet;

/// <summary>
/// One planet's shape: how big, how rough, which seed. Everything the
/// heightfield is generated from, and nothing about how it is drawn or
/// streamed (<see cref="PlanetStreamingStats"/>).
///
/// Authored as a <c>.tres</c> for a fixed test planet; a contract makes one
/// from its seed (P2). Either way it is read once, at build.
///
/// Defaults are the lab's planet, which held 25,000 pickles on the target
/// machine: 1200 m, 22 m of relief.
/// </summary>
[GlobalClass]
public partial class PlanetSpec : Resource
{
    [ExportGroup("Shape")]
    /// <summary>Mean radius. Vertex spacing stays constant, so chunk count grows with its square.</summary>
    [Export(PropertyHint.Range, "300,4000,10,suffix:m")]
    public float Radius { get; set; } = 1200.0f;

    /// <summary>Height of the highest ridge above the mean radius, and depth of the lowest hollow below it.</summary>
    [Export(PropertyHint.Range, "0,200,0.5,suffix:m")]
    public float Amplitude { get; set; } = 22.0f;

    [Export]
    public int Seed { get; set; } = 1337;

    [ExportGroup("Noise")]
    /// <summary>Features across the planet: the first octave's frequency on the unit sphere.</summary>
    [Export(PropertyHint.Range, "0.5,20,0.1")]
    public float NoiseFrequency { get; set; } = 4.5f;

    [Export(PropertyHint.Range, "1,8,1")]
    public int Octaves { get; set; } = 4;

    [Export(PropertyHint.Range, "1.5,3,0.05")]
    public float Lacunarity { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0.2,0.8,0.01")]
    public float Gain { get; set; } = 0.5f;

    /// <summary>
    /// How much of the relief is ridged rather than rolling. Zero is the lab's
    /// planet; one is all crests and gullies.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float RidgeShare { get; set; } = 0.0f;

    [ExportGroup("Ground")]
    /// <summary>Pull at the surface.</summary>
    [Export(PropertyHint.Range, "1,30,0.1,suffix:m/s²")]
    public float SurfaceGravity { get; set; } = 9.8f;

    /// <summary>
    /// How far below the generated ground a crater can dig. Past it the ground
    /// stops moving and floods as magma. Zero is no floor.
    /// </summary>
    [Export(PropertyHint.Range, "0,100,0.5,suffix:m")]
    public float DigFloorDepth { get; set; } = 25.0f;
}
