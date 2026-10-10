using Godot;

namespace DillControl.Mission;

/// <summary>
/// What a compound is made of: which clusters and how many of each, on how
/// much levelled ground, and how choosy to be about where. The seed decides
/// where; the kit and its clusters decide what (<c>docs/decisions/0020</c>).
/// Entries are laid out in order, so the ones that must fit (the jar) go first.
/// </summary>
[GlobalClass]
public partial class CompoundKit : Resource
{
    [Export]
    public string DisplayName { get; set; } = "Nest compound";

    [Export]
    public Godot.Collections.Array<CompoundEntry> Entries { get; set; } = new();

    [ExportGroup("Ground")]
    /// <summary>Radius of ground levelled for the compound. Every cluster stands inside it.</summary>
    [Export(PropertyHint.Range, "40,400,5,suffix:m")]
    public float PadRadius { get; set; } = 160.0f;

    [Export(PropertyHint.Range, "0,200,5,suffix:m")]
    public float PadBlend { get; set; } = 60.0f;

    /// <summary>Clear ground kept between two clusters' footprints.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m")]
    public float Spacing { get; set; } = 8.0f;

    [ExportGroup("Site")]
    /// <summary>Places on the planet tried for the compound; the flattest wins.</summary>
    [Export(PropertyHint.Range, "1,128,1")]
    public int SiteCandidates { get; set; } = 32;

    /// <summary>Tries to fit each cluster before it is left out.</summary>
    [Export(PropertyHint.Range, "1,200,1")]
    public int PlacementTries { get; set; } = 40;
}
