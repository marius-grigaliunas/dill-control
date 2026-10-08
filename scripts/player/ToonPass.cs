using Godot;

namespace DillControl.Player;

/// <summary>
/// The game's ink, hung off a camera: one full-screen quad running
/// <c>shaders/toon_ink.gdshader</c>, which draws silhouettes and creases from
/// the depth buffer alone. The colour bands come from the materials' own toon
/// lighting, not from this pass.
///
/// A quad on the camera rather than a compositor effect or a canvas filter,
/// because only a spatial shader can read the depth buffer in Forward+. It
/// belongs on the player's camera so the viewmodel is inked with the world;
/// until the player exists, a level's own camera carries it.
///
/// <see cref="Enabled"/> is the switch. Off hides the quad outright, since a
/// full-screen pass that does nothing still costs a screen of bandwidth.
///
/// It also hides itself whenever its camera is not the one being looked
/// through. The quad is pinned to whichever camera renders, so every vehicle
/// camera's pass drew on top of the player's: with a tank, a fighter and the
/// bike in the level that was four full-screen passes a frame, measured at
/// 3 ms of GPU against one.
/// </summary>
public partial class ToonPass : MeshInstance3D
{
    [Export]
    public bool Enabled { get; set; } = true;

    private ShaderMaterial _material;

    public override void _Ready()
    {
        Shader shader = GD.Load<Shader>("res://shaders/toon_ink.gdshader");
        if (shader == null)
        {
            GD.PushError("ToonPass could not load res://shaders/toon_ink.gdshader.");
            return;
        }

        _material = new ShaderMaterial { Shader = shader };
        Mesh = new QuadMesh { Size = new Vector2(2.0f, 2.0f) };
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;

        // Drawn last of the transparents and never culled: it is pinned to the
        // screen in the vertex stage, so its real bounds mean nothing.
        ExtraCullMargin = 16384.0f;
        SortingOffset = 1000.0f;
        Visible = Enabled;
    }

    public override void _Process(double delta)
    {
        bool current = GetParent() is not Camera3D camera || camera.IsCurrent();
        bool show = Enabled && current;
        if (Visible != show)
        {
            Visible = show;
        }
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        Visible = enabled;
    }
}
