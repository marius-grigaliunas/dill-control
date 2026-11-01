using Godot;

namespace DillControl.Planet;

/// <summary>
/// The sun of the menu sky: a directional light that also looks like one. The
/// disc, its rings, its glow and its streaks are drawn into the stars' sky
/// shader, which is told where the light points (by <see cref="PlanetLayer"/>),
/// so turning this node turns the light on the planet and the disc in the sky
/// together. Everything about how it looks is here, in one place, and is passed
/// to the sky as instance uniforms, so it never edits the shared material.
/// </summary>
[Tool]
[GlobalClass]
public partial class SkySun : DirectionalLight3D
{
    /// <summary>The layer that draws the sky (the stars). Without it there is no disc.</summary>
    [Export]
    public PlanetLayer Sky { get; set; }

    [ExportGroup("Disc")]
    /// <summary>The disc's radius, in degrees of sky. The real sun is 0.27.</summary>
    [Export(PropertyHint.Range, "0,10,0.01,or_greater,suffix:°")]
    public float DiscRadius { get; set; } = 0.8f;

    [Export]
    public Color DiscColour { get; set; } = new(1.0f, 0.95f, 0.78f);

    /// <summary>How bright the whole sun is. Zero hides it; the light is not changed.</summary>
    [Export(PropertyHint.Range, "0,4,0.01,or_greater")]
    public float Strength { get; set; } = 1.0f;

    [ExportGroup("Glow")]
    [Export]
    public Color GlowColour { get; set; } = new(1.0f, 0.62f, 0.3f);

    /// <summary>How far the glow reaches, in degrees of sky.</summary>
    [Export(PropertyHint.Range, "0.1,60,0.1,or_greater,suffix:°")]
    public float GlowSize { get; set; } = 4.5f;

    [ExportGroup("Streaks")]
    /// <summary>The cross of light through the sun, as a share of the sky. Zero is none.</summary>
    [Export(PropertyHint.Range, "0,1,0.005,or_greater")]
    public float StreakLength { get; set; } = 0.16f;

    public override void _Process(double delta)
    {
        if (Sky == null)
        {
            return;
        }

        Sky.SetInstanceShaderParameter("sun_radius", DiscRadius);
        Sky.SetInstanceShaderParameter("sun_colour", DiscColour);
        Sky.SetInstanceShaderParameter("sun_strength", Strength);
        Sky.SetInstanceShaderParameter("sun_glow_colour", GlowColour);
        Sky.SetInstanceShaderParameter("sun_glow_size", GlowSize);
        Sky.SetInstanceShaderParameter("sun_streak_length", StreakLength);
    }
}
