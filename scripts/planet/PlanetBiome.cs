using Godot;
using DillControl.Core;

namespace DillControl.Planet;

/// <summary>
/// How a planet looks: ground colours, sky and fog, and its sun. An authored
/// preset, not generated colours (<c>docs/decisions/0018</c>): a seed picks a
/// biome and shifts it a little, within the bounds the biome declares, so two
/// planets of one biome are cousins rather than twins and neither ever looks
/// like something nobody chose.
///
/// Applied to the terrain material, the level's environment and its sun by
/// <see cref="Apply"/>. Nothing else in the planet changes with the biome.
/// </summary>
[GlobalClass]
public partial class PlanetBiome : Resource
{
    /// <summary>What the contract screen calls it.</summary>
    [Export]
    public string DisplayName { get; set; } = "Brine Flats";

    [ExportGroup("Ground")]
    [Export] public Color LowColor { get; set; } = new(0.22f, 0.31f, 0.15f);
    [Export] public Color HighColor { get; set; } = new(0.58f, 0.55f, 0.45f);
    [Export] public Color RockColor { get; set; } = new(0.32f, 0.30f, 0.29f);
    [Export] public Color ScreeColor { get; set; } = new(0.44f, 0.31f, 0.19f);
    [Export] public Color BaseColor { get; set; } = new(0.46f, 0.44f, 0.35f);
    [Export] public Color SnowColor { get; set; } = new(0.86f, 0.89f, 0.94f);

    /// <summary>How much of the ground is the scree colour, in patches.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float PatchStrength { get; set; } = 0.85f;

    /// <summary>Snow on high flat ground: none at zero.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SnowAmount { get; set; } = 0.75f;

    /// <summary>Where snow starts, as a share of the relief above the mean radius.</summary>
    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float SnowAltitude { get; set; } = 0.55f;

    [ExportGroup("Sky")]
    [Export] public Color SkyTopColor { get; set; } = new(0.3f, 0.52f, 0.84f);
    [Export] public Color SkyHorizonColor { get; set; } = new(0.74f, 0.82f, 0.9f);
    [Export] public Color FogColor { get; set; } = new(0.74f, 0.82f, 0.9f);

    [ExportGroup("Sun")]
    [Export] public Color SunColor { get; set; } = new(1.0f, 0.97f, 0.9f);

    [Export(PropertyHint.Range, "0,4,0.01")]
    public float SunEnergy { get; set; } = 1.0f;

    /// <summary>Lowest the sun stands over the compound's horizon.</summary>
    [Export(PropertyHint.Range, "5,90,1,suffix:°")]
    public float SunElevationMin { get; set; } = 30.0f;

    [Export(PropertyHint.Range, "5,90,1,suffix:°")]
    public float SunElevationMax { get; set; } = 60.0f;

    [ExportGroup("Variation")]
    /// <summary>How far a seed may turn every colour round the hue wheel, as a share of it, either way.</summary>
    [Export(PropertyHint.Range, "0,0.25,0.005")]
    public float HueJitter { get; set; } = 0.03f;

    /// <summary>How far a seed may brighten or darken every colour, as a share, either way.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.01")]
    public float ValueJitter { get; set; } = 0.08f;

    /// <summary>
    /// Paints the planet: the terrain material's colours, the sky, fog and
    /// the sun. <paramref name="reference"/> is up at the place the sun's
    /// elevation is measured from (the compound). Every roll comes from the
    /// seed's "biome" stream.
    /// </summary>
    public void Apply(ulong seed, Vector3 reference, ShaderMaterial terrain, Environment environment,
                      DirectionalLight3D sun)
    {
        SeededRandom random = SeededRandom.Stream(seed, "biome");
        float hue = random.Range(-HueJitter, HueJitter);
        float value = 1.0f + random.Range(-ValueJitter, ValueJitter);
        Color Shift(Color c)
        {
            Color shifted = Color.FromHsv(Mathf.PosMod(c.H + hue, 1.0f), c.S, Mathf.Clamp(c.V * value, 0.0f, 1.0f));
            shifted.A = c.A;
            return shifted;
        }

        if (terrain != null)
        {
            terrain.SetShaderParameter("low_color", Shift(LowColor));
            terrain.SetShaderParameter("high_color", Shift(HighColor));
            terrain.SetShaderParameter("rock_color", Shift(RockColor));
            terrain.SetShaderParameter("scree_color", Shift(ScreeColor));
            terrain.SetShaderParameter("base_color", Shift(BaseColor));
            terrain.SetShaderParameter("line_color", Shift(BaseColor.Darkened(0.25f)));
            terrain.SetShaderParameter("major_line_color", Shift(BaseColor.Lightened(0.25f)));
            terrain.SetShaderParameter("snow_color", SnowColor);
            terrain.SetShaderParameter("patch_strength", PatchStrength);
            terrain.SetShaderParameter("snow_amount", SnowAmount);
            terrain.SetShaderParameter("snow_altitude", SnowAltitude);
        }

        if (environment != null)
        {
            Color horizon = Shift(SkyHorizonColor);
            if (environment.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
            {
                sky.SkyTopColor = Shift(SkyTopColor);
                sky.SkyHorizonColor = horizon;
                sky.GroundHorizonColor = horizon;
                sky.GroundBottomColor = Shift(LowColor.Darkened(0.4f));
            }
            else if (environment.Sky?.SkyMaterial is ShaderMaterial planetSky)
            {
                // The planet sky's zenith is darker than the top colour on
                // purpose: a thin-air sky the stars and bodies read against.
                planetSky.SetShaderParameter("zenith_color", Shift(SkyTopColor).Darkened(0.55f));
                planetSky.SetShaderParameter("horizon_color", horizon);
                planetSky.SetShaderParameter("ground_color", Shift(LowColor.Darkened(0.3f)));
                planetSky.SetShaderParameter("sun_halo_color", SunColor.Lerp(horizon, 0.3f));
            }
            environment.FogLightColor = Shift(FogColor);
        }

        float elevation = Mathf.DegToRad(random.Range(SunElevationMin, SunElevationMax));
        float azimuth = random.Range(0.0f, Mathf.Tau);
        if (sun != null)
        {
            sun.LightColor = SunColor;
            sun.LightEnergy = SunEnergy;

            // The light shines along its -Z: from the sky toward the ground.
            Vector3 up = reference.LengthSquared() > 1e-6f ? reference.Normalized() : Vector3.Up;
            Vector3 east = up.Cross(Mathf.Abs(up.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
            Vector3 north = up.Cross(east);
            Vector3 towardSun = (up * Mathf.Sin(elevation))
                                + (((east * Mathf.Cos(azimuth)) + (north * Mathf.Sin(azimuth))) * Mathf.Cos(elevation));
            Vector3 side = towardSun.Cross(up).Normalized();
            if (side.LengthSquared() < 1e-6f)
            {
                side = east;
            }
            sun.GlobalBasis = new Basis(side, towardSun.Cross(side), towardSun).Orthonormalized();
        }
    }
}
