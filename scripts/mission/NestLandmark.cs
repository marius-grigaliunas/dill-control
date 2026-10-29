using Godot;
using DillControl.Planet;

namespace DillControl.Mission;

/// <summary>
/// A place authored round a nest, where the nest is and the ground is not
/// known until the level builds: <see cref="NestPlacer"/> lays it
/// <see cref="PastEdgeM"/> beyond the nest's outermost vine, at a bearing from
/// the avenue the player comes in by, and the planet stands it on the ground
/// like any site. What is under it is the scene's, drawn flat in the editor.
/// </summary>
[GlobalClass]
public partial class NestLandmark : PlanetSite
{
    /// <summary>Degrees round the nest from the player's avenue: 0 is the way
    /// in, 180 the far side.</summary>
    [Export(PropertyHint.Range, "-180,180,1,suffix:°")]
    public float BearingDegrees { get; set; }

    /// <summary>Metres out from the nest's edge; below zero is inside it.</summary>
    [Export(PropertyHint.Range, "-300,400,1,suffix:m")]
    public float PastEdgeM { get; set; } = 40.0f;

    /// <summary>Its -Z toward the nest's heart; off, away from it.</summary>
    [Export] public bool FaceHeart { get; set; } = true;
}
