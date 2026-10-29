using Godot;

namespace DillControl.Mission;

/// <summary>The prompts of one level's <see cref="IntroGuide"/>, in order.</summary>
[GlobalClass]
public partial class GuideScript : Resource
{
    [Export] public Godot.Collections.Array<GuideStep> Steps { get; set; } = new();
}
