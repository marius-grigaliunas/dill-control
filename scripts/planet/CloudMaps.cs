using Godot;

namespace DillControl.Planet;

/// <summary>
/// The two textures the menu planet's cloud deck reads instead of working them
/// out per sample (<c>planet_clouds.gdshader</c>): the weather drawn into a map
/// once a frame, and the value noise's lattice (<see cref="NoiseLattice"/>). Handed
/// to every shader as the globals <c>planet_cloud_flow</c> and <c>planet_noise</c>,
/// so the shared cloud material is never written to. A scene with a cloud deck
/// needs one.
///
/// The weather map is drawn by <c>planet_weather_bake.gdshader</c> in a
/// SubViewport of its own, with the weather settings copied over from
/// <see cref="Clouds"/>' material each frame, so they are tuned in one place.
///
/// A tool node, so the deck is right in the editor too. Its viewport is an
/// internal child with no owner, never saved into the scene.
/// </summary>
[Tool]
[GlobalClass]
public partial class CloudMaps : Node
{
    private const string FlowGlobal = "planet_cloud_flow";

    private static readonly string[] WeatherParameters =
    {
        "drift_degrees_per_second", "jet_swing_degrees", "jets", "storm_size", "storm_twist_degrees",
    };

    /// <summary>The cloud deck whose material holds the weather settings.</summary>
    [Export]
    public PlanetLayer Clouds { get; set; }

    /// <summary>
    /// Texels along each edge of the weather map (a square, octahedral; a texel
    /// is about a third of a degree of the planet). The deck blends between them,
    /// so the storms' spirals stay smooth well below this.
    /// </summary>
    [Export(PropertyHint.Range, "128,2048,1")]
    public int FlowEdge { get; set; } = 768;

    private Vector2I FlowSize => new(FlowEdge, FlowEdge);

    private SubViewport _viewport;
    private ShaderMaterial _bake;

    public override void _Ready()
    {
        NoiseLattice.Publish();

        _bake = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/planet_weather_bake.gdshader") };
        _viewport = new SubViewport
        {
            Name = "WeatherMap",
            Size = FlowSize,
            Disable3D = true,
            // Floating point, so the packed directions keep their precision.
            UseHdr2D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _viewport.AddChild(new ColorRect { Material = _bake, Size = FlowSize });
        AddChild(_viewport, false, InternalMode.Back);
        RenderingServer.GlobalShaderParameterSet(FlowGlobal, _viewport.GetTexture());
    }

    public override void _ExitTree()
    {
        _viewport?.QueueFree();
        _viewport = null;
    }

    public override void _Process(double delta)
    {
        if (_viewport == null)
        {
            return;
        }
        if (_viewport.Size != FlowSize)
        {
            _viewport.Size = FlowSize;
            _viewport.GetChild<ColorRect>(0).Size = FlowSize;
        }
        if (Clouds?.MaterialOverride is ShaderMaterial clouds)
        {
            foreach (string name in WeatherParameters)
            {
                _bake.SetShaderParameter(name, clouds.GetShaderParameter(name));
            }
        }
    }
}
