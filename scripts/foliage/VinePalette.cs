using System;
using DillControl.Core;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// The colours a baked vine is written in, one per part the generator paints
/// (<see cref="VineGenerator"/>'s indices), and its leaves. Dark deep greens,
/// so muzzle flashes, pickles and fire stand out against them; the pale core
/// is what a cut shows. Judged under the game's toon lighting (bench
/// <c>look</c>), not a flat render. Changing it needs a re-bake, not a rebuild.
///
/// A kind can have its own (<see cref="VineKind.Palette"/>). Every vine rolls
/// a shade of it from its seed (<see cref="Shaded"/>), and every leaf a little
/// more (<see cref="LeafTint"/>), all held inside the green band below: no
/// two vines alike, none neon (docs/handoffs/vine-tool.md, step 5).
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

    /// <summary>How far a vine's shade moves from these, either way: hue (a
    /// share of the wheel), saturation and brightness (shares of their own).</summary>
    [ExportGroup("Each vine")]
    [Export(PropertyHint.Range, "0,0.2,0.005")] public float HueSpread { get; set; } = 0.03f;
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float SaturationSpread { get; set; } = 0.12f;
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float ValueSpread { get; set; } = 0.15f;
    /// <summary>How far each leaf moves from its vine's leaf colour, the same way.</summary>
    [ExportGroup("Each leaf")]
    [Export(PropertyHint.Range, "0,0.1,0.005")] public float LeafHueSpread { get; set; } = 0.012f;
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float LeafValueSpread { get; set; } = 0.1f;

    /// <summary>Every green stays inside: hues from green (no yellow, no lime) to blue-green,
    /// never too bright or too saturated. Not the core.</summary>
    [ExportGroup("Green band")]
    [Export(PropertyHint.Range, "0,1,0.005")] public float HueMin { get; set; } = 0.25f;
    [Export(PropertyHint.Range, "0,1,0.005")] public float HueMax { get; set; } = 0.45f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float SaturationMax { get; set; } = 0.7f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ValueMax { get; set; } = 0.42f;

    /// <summary>The colour of a generator palette index, 1 to 5.</summary>
    public Color Of(int index) => index switch
    {
        VineGenerator.Shell => Shell,
        VineGenerator.Knuckle => Knuckle,
        VineGenerator.Core => Core,
        VineGenerator.Leaf => Leaf,
        VineGenerator.Petiole => Petiole,
        _ => Colors.Magenta,
    };

    /// <summary>This palette moved by one roll for <paramref name="seed"/>:
    /// every green the same way, so the vine stays one plant; the core only
    /// lighter or darker.</summary>
    public VinePalette Shaded(int seed)
    {
        SeededRandom rng = SeededRandom.Stream((uint)seed, "vine:shade");
        float dh = rng.Range(-HueSpread, HueSpread);
        float ds = rng.Range(-SaturationSpread, SaturationSpread);
        float dv = rng.Range(-ValueSpread, ValueSpread);
        // Made, not Duplicate()d: a nest shades its vines on worker threads,
        // and Duplicate calls back into the script from the engine.
        return new VinePalette
        {
            Shell = InBand(Shell, dh, ds, dv),
            Knuckle = InBand(Knuckle, dh, ds, dv),
            Core = Color.FromHsv(Core.H, Core.S, Mathf.Clamp(Core.V * (1 + dv), 0, 1)),
            Leaf = InBand(Leaf, dh, ds, dv),
            Petiole = InBand(Petiole, dh, ds, dv),
            HueSpread = HueSpread, SaturationSpread = SaturationSpread, ValueSpread = ValueSpread,
            LeafHueSpread = LeafHueSpread, LeafValueSpread = LeafValueSpread,
            HueMin = HueMin, HueMax = HueMax, SaturationMax = SaturationMax, ValueMax = ValueMax,
        };
    }

    /// <summary>Leaf <paramref name="leaf"/>'s colour, as a multiplier on the
    /// vine's: near white, inside the band once applied.</summary>
    public Color LeafTint(int seed, int leaf)
    {
        SeededRandom rng = SeededRandom.Stream((uint)seed, "vine:leaftint:" + leaf);
        // Drawn colours are linear (voxel_toon, leaf_toon): the ratio is too.
        Color tinted = InBand(Leaf, rng.Range(-LeafHueSpread, LeafHueSpread), 0, rng.Range(-LeafValueSpread, LeafValueSpread)).SrgbToLinear();
        Color own = Leaf.SrgbToLinear();
        return new Color(Ratio(tinted.R, own.R), Ratio(tinted.G, own.G), Ratio(tinted.B, own.B));
    }

    /// <summary>Whether <paramref name="colour"/> is inside the green band.</summary>
    public bool Inside(Color colour) =>
        colour.H >= HueMin - 1e-3f && colour.H <= HueMax + 1e-3f && colour.S <= SaturationMax + 1e-3f && colour.V <= ValueMax + 1e-3f;

    private Color InBand(Color c, float dh, float ds, float dv) => Color.FromHsv(
        Mathf.Clamp(c.H + dh, HueMin, HueMax),
        Mathf.Clamp(c.S * (1 + ds), 0, SaturationMax),
        Mathf.Clamp(c.V * (1 + dv), 0, ValueMax));

    private static float Ratio(float a, float b) => b > 1e-4f ? a / b : 1.0f;
}
