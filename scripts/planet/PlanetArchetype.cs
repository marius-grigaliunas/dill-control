using Godot;
using DillControl.Core;

namespace DillControl.Planet;

/// <summary>
/// A kind of planet, as ranges: how big, how rough, how ridged, how cratered.
/// A contract's seed rolls a <see cref="PlanetSpec"/> inside them.
///
/// The ranges are capped by the bench (<c>bench.gd planets</c>): a seed that
/// misses 60 fps is a bug, so a bound only widens once a sweep holds at it.
/// </summary>
[GlobalClass]
public partial class PlanetArchetype : Resource
{
    /// <summary>What the contract screen calls this kind of planet.</summary>
    [Export]
    public string DisplayName { get; set; } = "Rock";

    [ExportGroup("Shape")]
    [Export(PropertyHint.Range, "300,4000,10,suffix:m")]
    public float RadiusMin { get; set; } = 1000.0f;

    [Export(PropertyHint.Range, "300,4000,10,suffix:m")]
    public float RadiusMax { get; set; } = 1400.0f;

    [Export(PropertyHint.Range, "0,200,0.5,suffix:m")]
    public float AmplitudeMin { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "0,200,0.5,suffix:m")]
    public float AmplitudeMax { get; set; } = 32.0f;

    [ExportGroup("Noise")]
    [Export(PropertyHint.Range, "0.5,20,0.1")]
    public float FrequencyMin { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0.5,20,0.1")]
    public float FrequencyMax { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "1,8,1")]
    public int OctavesMin { get; set; } = 4;

    [Export(PropertyHint.Range, "1,8,1")]
    public int OctavesMax { get; set; } = 5;

    [Export(PropertyHint.Range, "0.2,0.8,0.01")]
    public float GainMin { get; set; } = 0.45f;

    [Export(PropertyHint.Range, "0.2,0.8,0.01")]
    public float GainMax { get; set; } = 0.55f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float RidgeShareMin { get; set; } = 0.0f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float RidgeShareMax { get; set; } = 0.7f;

    [ExportGroup("Craters")]
    [Export(PropertyHint.Range, "0,400,1")]
    public int CraterCountMin { get; set; } = 0;

    [Export(PropertyHint.Range, "0,400,1")]
    public int CraterCountMax { get; set; } = 40;

    [Export(PropertyHint.Range, "2,300,1,suffix:m")]
    public float CraterRadiusMin { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "2,300,1,suffix:m")]
    public float CraterRadiusMax { get; set; } = 70.0f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CraterDepthRatio { get; set; } = 0.2f;

    [ExportGroup("Ground")]
    /// <summary>
    /// Pull at the surface. Fixed by default: the line, the jump and the bike
    /// were tuned at 9.8, and a planet that changes how they feel is a design
    /// choice to make on purpose, not a roll.
    /// </summary>
    [Export(PropertyHint.Range, "1,30,0.1,suffix:m/s²")]
    public float GravityMin { get; set; } = 9.8f;

    [Export(PropertyHint.Range, "1,30,0.1,suffix:m/s²")]
    public float GravityMax { get; set; } = 9.8f;

    [Export(PropertyHint.Range, "0,100,0.5,suffix:m")]
    public float DigFloorDepth { get; set; } = 25.0f;

    /// <summary>A planet of this kind for a seed. The same seed always gives the same planet.</summary>
    public PlanetSpec Roll(ulong seed)
    {
        SeededRandom random = SeededRandom.Stream(seed, "planet shape");
        return new PlanetSpec
        {
            Radius = Mathf.Round(random.Range(RadiusMin, RadiusMax) / 10.0f) * 10.0f,
            Amplitude = random.Range(AmplitudeMin, AmplitudeMax),
            Seed = random.NextSeed(),
            NoiseFrequency = random.Range(FrequencyMin, FrequencyMax),
            Octaves = random.RangeInt(OctavesMin, OctavesMax),
            Lacunarity = 2.0f,
            Gain = random.Range(GainMin, GainMax),
            RidgeShare = random.Range(RidgeShareMin, RidgeShareMax),
            CraterCount = random.RangeInt(CraterCountMin, CraterCountMax),
            CraterRadiusMin = CraterRadiusMin,
            CraterRadiusMax = CraterRadiusMax,
            CraterDepthRatio = CraterDepthRatio,
            SurfaceGravity = random.Range(GravityMin, GravityMax),
            DigFloorDepth = DigFloorDepth,
        };
    }
}
