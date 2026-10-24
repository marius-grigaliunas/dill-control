#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// The Forest brush: one dab paints every layer of <see cref="ForestStats"/>
/// (tall trees, small trees, undergrowth), tallest first so the small ones
/// fill between them; Thickness scales every layer's density, Hardness how
/// far in from the edge it starts to thin.
/// </summary>
public static class ForestBrush
{
    public static ForestStats Stats() => ResourceLoader.Load<ForestStats>(ForestStats.DefaultPath);

    public static void Dab(Scatter scatter, float x, float z, float radius, float thickness, float hardness, bool erase)
    {
        foreach (ForestLayer layer in Stats()?.Layers ?? System.Array.Empty<ForestLayer>())
        {
            if (layer == null)
                continue;
            List<string> palette = Scatter.ScenesStarting(layer.Prefixes);
            var settings = new Scatter.Settings
            {
                RadiusM = radius,
                Density = layer.Density * thickness,
                SpacingM = layer.SpacingM,
                MaxSlopeDeg = layer.MaxSlopeDeg,
                Hardness = hardness,
            };
            if (erase)
                scatter.Erase(x, z, settings, palette.Contains);
            else
                scatter.Paint(x, z, settings, palette);
        }
    }

    /// <summary>Every scene any layer paints.</summary>
    public static List<string> AllScenes() =>
        Scatter.ScenesStarting((Stats()?.Layers ?? System.Array.Empty<ForestLayer>()).Where(l => l != null).SelectMany(l => l.Prefixes));
}
#endif
