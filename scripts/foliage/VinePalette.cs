using Godot;

namespace DillControl.Foliage;

/// <summary>
/// The colours a baked vine is written in, one per part the generator paints
/// (<see cref="VineGenerator"/>'s indices). Dark deep greens, so muzzle
/// flashes, pickles and fire stand out against them; the pale core is what a
/// cut shows. Judged under the game's toon lighting (bench <c>look</c>), not
/// a flat render. Changing it needs a re-bake, not a rebuild.
/// </summary>
[GlobalClass, Tool]
public partial class VinePalette : Resource
{
    public const string DefaultPath = "res://resources/foliage/vine_palette.tres";

    [Export] public Color Shell { get; set; } = Color.Color8(52, 84, 44);
    /// <summary>The band round each growth ring.</summary>
    [Export] public Color Knuckle { get; set; } = Color.Color8(34, 60, 32);
    /// <summary>Inside the shell: shows only where the vine is cut.</summary>
    [Export] public Color Core { get; set; } = Color.Color8(150, 146, 108);
    [Export] public Color Leaf { get; set; } = Color.Color8(30, 70, 40);
    /// <summary>Pod and leaf stems.</summary>
    [Export] public Color Petiole { get; set; } = Color.Color8(22, 50, 30);
    [Export] public Color Tendril { get; set; } = Color.Color8(64, 96, 54);

    /// <summary>The colour of a generator palette index, 1 to 6.</summary>
    public Color Of(int index) => index switch
    {
        VineGenerator.Shell => Shell,
        VineGenerator.Knuckle => Knuckle,
        VineGenerator.Core => Core,
        VineGenerator.Leaf => Leaf,
        VineGenerator.Petiole => Petiole,
        VineGenerator.Tendril => Tendril,
        _ => Colors.Magenta,
    };
}
