using Godot;

namespace DillControl.Planet;

/// <summary>
/// The contract planet's surroundings: the other bodies in its sky, where the
/// air gives out, and where the ship waits in orbit. One resource for every
/// system; the seed rolls inside it.
/// </summary>
[GlobalClass]
public partial class SystemStats : Resource
{
    [ExportGroup("Bodies")]
    [Export(PropertyHint.Range, "0,4,1")]
    public int BodiesMin { get; set; } = 1;

    [Export(PropertyHint.Range, "0,4,1")]
    public int BodiesMax { get; set; } = 3;

    /// <summary>Angular radius of the smallest body, seen from the planet.</summary>
    [Export(PropertyHint.Range, "0.2,30,0.1,suffix:°")]
    public float BodySizeMin { get; set; } = 1.5f;

    [Export(PropertyHint.Range, "0.2,30,0.1,suffix:°")]
    public float BodySizeMax { get; set; } = 9.0f;

    /// <summary>No body nearer the sun than this, where its glare would hide it and its lit side would face away.</summary>
    [Export(PropertyHint.Range, "0,90,1,suffix:°")]
    public float BodySunClearance { get; set; } = 35.0f;

    /// <summary>The colours a body is drawn from, before the seed shifts one a little.</summary>
    [Export]
    public Color[] BodyColours { get; set; } =
    {
        new(0.62f, 0.58f, 0.52f), new(0.75f, 0.48f, 0.3f), new(0.45f, 0.6f, 0.78f),
        new(0.58f, 0.7f, 0.42f), new(0.85f, 0.8f, 0.62f),
    };

    [ExportGroup("Atmosphere")]
    /// <summary>Altitude, as a share of the radius, where the sky starts to thin toward space.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float AtmosphereFadeStart { get; set; } = 0.1f;

    /// <summary>Altitude, as a share of the radius, where it is space. The gravity well ends about here too.</summary>
    [Export(PropertyHint.Range, "0.1,4,0.01")]
    public float AtmosphereFadeEnd { get; set; } = 0.8f;

    /// <summary>
    /// Metres the fog's start and end recede per metre of altitude, so the
    /// ground stays readable from high up while the air is still there.
    /// </summary>
    [Export(PropertyHint.Range, "0,10,0.1")]
    public float FogRecedePerMetre { get; set; } = 2.0f;

    /// <summary>Zero-g reaches this many radii out, past the gravity well. Beyond it the project's default gravity would pull straight down.</summary>
    [Export(PropertyHint.Range, "2,20,0.5")]
    public float SpaceFieldRadii { get; set; } = 8.0f;

    [ExportGroup("Ship")]
    /// <summary>Where the ship waits, in radii from the planet's centre. Past the gravity well, so it holds still.</summary>
    [Export(PropertyHint.Range, "1.5,6,0.05")]
    public float ShipOrbitRadii { get; set; } = 2.4f;

    /// <summary>How far round from over the compound the ship waits, so the compound is ahead and below rather than straight down.</summary>
    [Export(PropertyHint.Range, "0,60,1,suffix:°")]
    public float ShipOffsetDegrees { get; set; } = 18.0f;
}
