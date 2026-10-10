using Godot;

namespace DillControl.Mission;

/// <summary>What a cluster is for in a compound, so a kit can ask for one of each it needs.</summary>
public enum ClusterRole
{
    /// <summary>Tall things to swing between: spires, pillars.</summary>
    Anchors,
    /// <summary>Spitter towers, anti-air, things that shoot up.</summary>
    Towers,
    /// <summary>The armoured hive jar the compound is built round.</summary>
    Jar,
    /// <summary>Broken ground and cover.</summary>
    Ruins,
}

/// <summary>
/// The root of a cluster scene: a handful of authored props arranged by hand
/// on flat ground -- "the jar and its court", "a row of towers". A compound
/// is a seeded layout of clusters (<c>docs/decisions/0020</c>); everything
/// inside one stays exactly as the designer placed it.
///
/// Built in the editor like any scene: props instanced under this node at
/// their places, a footprint drawn round them, markers for what the mission
/// needs to find.
/// </summary>
[GlobalClass]
public partial class CompoundCluster : Node3D
{
    [Export]
    public ClusterRole Role { get; set; } = ClusterRole.Anchors;

    /// <summary>
    /// The radius everything in the cluster stands within, round its origin.
    /// Clusters are laid out so their footprints do not overlap.
    /// </summary>
    [Export(PropertyHint.Range, "2,200,0.5,suffix:m")]
    public float FootprintRadius { get; set; } = 20.0f;

    [ExportGroup("Markers")]
    /// <summary>Where the horde gathers when it is not hunting. A compound's nests are every cluster's.</summary>
    [Export]
    public Godot.Collections.Array<Marker3D> Nests { get; set; } = new();

    /// <summary>Where the giant waits, for a cluster that has her arena.</summary>
    [Export]
    public Marker3D Arena { get; set; }

    /// <summary>Where the rearm pod may land, for a cluster that offers one.</summary>
    [Export]
    public Marker3D RearmPad { get; set; }
}
