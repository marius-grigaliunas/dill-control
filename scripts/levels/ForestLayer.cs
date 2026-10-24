using Godot;

namespace DillControl.Levels;

/// <summary>One layer of a forest.</summary>
[Tool]
[GlobalClass]
public partial class ForestLayer : Resource
{
    /// <summary>Prop scenes (scenes/props) whose file names start with one of these.</summary>
    [Export] public string[] Prefixes { get; set; } = System.Array.Empty<string>();

    /// <summary>Instances per 1,000 m² at Thickness 1.</summary>
    [Export(PropertyHint.Range, "0,50,0.1")] public float Density { get; set; } = 1.0f;

    /// <summary>No closer than this to anything already standing.</summary>
    [Export(PropertyHint.Range, "0.5,100,0.5,suffix:m")] public float SpacingM { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "0,89,1,suffix:deg")] public float MaxSlopeDeg { get; set; } = 30.0f;
}
