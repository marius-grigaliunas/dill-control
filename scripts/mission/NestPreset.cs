using Godot;

namespace DillControl.Mission;

/// <summary>
/// A nest judged Good in the Vines tool and kept: its kit, seed and
/// population, which grow the same nest on every machine. Saved under
/// <see cref="Folder"/>; the tool's Nest tab loads one back to see or play.
/// Whether a contract picks saved nests or generates at load is
/// docs/decisions/0033.
/// </summary>
[GlobalClass, Tool]
public partial class NestPreset : Resource
{
    public const string Folder = "res://resources/mission/nests";

    [Export] public NestKit Kit { get; set; }
    [Export] public long Seed { get; set; }
    [Export] public int Population { get; set; }
    /// <summary>No vine further than this from the heart; zero is the kit's own.</summary>
    [Export(PropertyHint.Range, "0,2000,10,suffix:m")] public float RadiusM { get; set; }
    /// <summary>Between neighbouring vines, least and most; zero is the kit's own.</summary>
    [Export] public Vector2 SpacingM { get; set; }
    [Export] public bool Horde { get; set; } = true;
    [Export(PropertyHint.MultilineText)] public string Note { get; set; } = "";
    /// <summary>When it was saved, and from which commit of the generator.</summary>
    [Export] public string Saved { get; set; } = "";
    [Export] public string Generator { get; set; } = "";
}
