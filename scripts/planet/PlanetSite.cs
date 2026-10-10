using Godot;

namespace DillControl.Planet;

/// <summary>
/// A place on the planet that something authored stands on: the player's
/// spawn, a compound, a cluster of props.
///
/// The ground is generated at runtime, so nothing can be placed on it in the
/// editor. A site is placed roughly -- the direction from the planet's centre
/// is what counts -- and when the planet builds it levels a pad there, moves
/// the site onto the ground and stands it upright, keeping its heading.
/// Everything under it comes along in its own local space, so a cluster
/// arranged in the editor on flat ground stands on the planet the same way.
///
/// A Marker3D, so the player's spawn can be one.
/// </summary>
[GlobalClass]
public partial class PlanetSite : Marker3D
{
    /// <summary>Radius of ground levelled for what stands here. Zero levels nothing.</summary>
    [Export(PropertyHint.Range, "0,400,1,suffix:m")]
    public float PadRadius { get; set; }

    /// <summary>Metres over which the pad blends back into the terrain.</summary>
    [Export(PropertyHint.Range, "0,200,1,suffix:m")]
    public float PadBlend { get; set; } = 20.0f;

    /// <summary>Metres the site stands above the ground once placed. A spawn wants a little.</summary>
    [Export(PropertyHint.Range, "-10,50,0.1,suffix:m")]
    public float Lift { get; set; }

    /// <summary>
    /// Ground within this many metres keeps its colliders wherever the camera
    /// is, so what falls here has something to land on while nobody watches.
    /// Zero streams it like everywhere else.
    /// </summary>
    [Export(PropertyHint.Range, "0,600,1,suffix:m")]
    public float KeepColliderRadius { get; set; }

    /// <summary>The planet's centre is the world origin; this is which way the site lies from it.</summary>
    public Vector3 Direction
    {
        get
        {
            Vector3 position = GlobalPosition;
            return position.LengthSquared() > 1e-6f ? position.Normalized() : Vector3.Up;
        }
    }
}
