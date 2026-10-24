using Godot;

namespace DillControl.Levels;

/// <summary>
/// What the terrain brush's Forest paints in one stroke: three layers, each
/// the prop scenes whose names start with its prefixes, at its own density
/// and spacing. Tall trees first and furthest apart, then small trees
/// between them, then undergrowth; the brush's Thickness scales every
/// layer's density.
/// </summary>
[Tool]
[GlobalClass]
public partial class ForestStats : Resource
{
    public const string DefaultPath = "res://resources/levels/forest_default.tres";

    [Export] public ForestLayer Tall { get; set; }
    [Export] public ForestLayer Small { get; set; }
    [Export] public ForestLayer Undergrowth { get; set; }

    public ForestLayer[] Layers => new[] { Tall, Small, Undergrowth };
}
