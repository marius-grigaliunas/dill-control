using Godot;

namespace DillControl.Mission;

/// <summary>One line of a <see cref="CompoundKit"/>: a cluster scene and how many of it a compound has.</summary>
[GlobalClass]
public partial class CompoundEntry : Resource
{
    /// <summary>A scene whose root is a <see cref="CompoundCluster"/>.</summary>
    [Export]
    public PackedScene Cluster { get; set; }

    [Export(PropertyHint.Range, "0,12,1")]
    public int MinCount { get; set; } = 1;

    [Export(PropertyHint.Range, "0,12,1")]
    public int MaxCount { get; set; } = 1;
}
