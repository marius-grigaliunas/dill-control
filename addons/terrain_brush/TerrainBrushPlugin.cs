#if TOOLS
using System.Linq;
using Godot;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// The terrain brush (docs/decisions/0044): with a <see cref="SculptedTerrain"/>
/// selected, a bar on the 3D view's menu picks Raise, Lower, Smooth or
/// Flatten, the radius, strength and hardness; holding the left button paints
/// in the 3D view, and a ring shows where. One stroke is one undo. The
/// heights and chunk meshes are written to their files when the scene is
/// saved. The bridge's <c>sculpt</c> uses the same dabs (<see cref="TerrainSculpt"/>).
/// </summary>
[Tool]
public partial class TerrainBrushPlugin : EditorPlugin
{
    private SculptedTerrain _terrain;
    private HBoxContainer _bar;
    private ButtonGroup _modes;
    private SpinBox _radius, _strength, _hardness;
    private MeshInstance3D _ring;

    // A stroke: the heights before it, the copy it paints, where the last dab was.
    private float[] _before, _painting;
    private Vector3 _lastDab;
    private float _flattenTo;
    private bool _stroking;

    // False again after every reload of the project's code.
    private static bool _registeredByThisCode;

    public override void _EnterTree()
    {
        _bar = new HBoxContainer { Visible = false };
        _bar.AddChild(new VSeparator());
        _bar.AddChild(new Label { Text = "Terrain" });
        _modes = new ButtonGroup { AllowUnpress = true };
        foreach (string mode in new[] { "Raise", "Lower", "Smooth", "Flatten" })
            _bar.AddChild(new Button { Text = mode, ToggleMode = true, ButtonGroup = _modes, TooltipText = Tip(mode) });
        _radius = Spin(_bar, "Radius", 1, 300, 1, 20, " m");
        _strength = Spin(_bar, "Strength", 0.05, 10, 0.05, 0.5, "");
        _hardness = Spin(_bar, "Hardness", 0, 0.95, 0.05, 0.3, "");
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, _bar);
        Register();
    }

    public override void _ExitTree()
    {
        DropRing();
        if (_bar != null)
        {
            RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, _bar);
            _bar.QueueFree();
            _bar = null;
        }
    }

    public override void _Process(double delta)
    {
        if (!_registeredByThisCode)
            Register();
    }

    private static void Register()
    {
        TerrainCommands.Register();
        _registeredByThisCode = true;
    }

    private static string Tip(string mode) => mode switch
    {
        "Raise" => "Hold the left button to raise the ground: Strength is metres a dab at the middle.",
        "Lower" => "Hold the left button to lower the ground: Strength is metres a dab at the middle.",
        "Smooth" => "Hold the left button to even the ground out: Strength 0-1, how far a dab goes.",
        _ => "Hold the left button to bring the ground to the height where the stroke began: Strength 0-1.",
    };

    private static SpinBox Spin(Container bar, string label, double min, double max, double step, double value, string suffix)
    {
        bar.AddChild(new Label { Text = label });
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix, CustomMinimumSize = new Vector2(80, 0) };
        bar.AddChild(spin);
        return spin;
    }

    public override bool _Handles(GodotObject @object) => @object is SculptedTerrain;

    public override void _Edit(GodotObject @object)
    {
        _terrain = @object as SculptedTerrain;
        if (_terrain == null)
            DropRing();
    }

    public override void _MakeVisible(bool visible)
    {
        if (_bar != null)
            _bar.Visible = visible;
        if (!visible)
            DropRing();
    }

    private TerrainSculpt.Mode? Mode()
    {
        BaseButton pressed = _modes?.GetPressedButton();
        return pressed is Button b && TerrainSculpt.TryMode(b.Text, out TerrainSculpt.Mode m) ? m : null;
    }

    private TerrainSculpt.Brush Brush(TerrainSculpt.Mode mode) => new()
    {
        Mode = mode,
        RadiusM = (float)_radius.Value,
        Strength = (float)_strength.Value,
        Hardness = (float)_hardness.Value,
        Target = _flattenTo,
    };

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent @event)
    {
        TerrainSculpt.Mode? mode = Mode();
        if (_terrain == null || !IsInstanceValid(_terrain) || mode == null || _terrain.Map == null)
        {
            DropRing();
            return (int)AfterGuiInput.Pass;
        }
        if (@event is not InputEventMouse mouse)
            return (int)AfterGuiInput.Pass;

        Vector3? hit = _terrain.Cast(camera.ProjectRayOrigin(mouse.Position), camera.ProjectRayNormal(mouse.Position));
        ShowRing(hit);

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed && hit != null)
            {
                _stroking = true;
                _before = _terrain.CopyHeights();
                _painting = (float[])_before.Clone();
                _flattenTo = hit.Value.Y - _terrain.GlobalPosition.Y;
                Dab(hit.Value, mode.Value);
                return (int)AfterGuiInput.Stop;
            }
            if (!button.Pressed && _stroking)
            {
                EndStroke(mode.Value);
                return (int)AfterGuiInput.Stop;
            }
        }
        if (@event is InputEventMouseMotion && _stroking)
        {
            // A dab every fifth of the radius along the drag.
            if (hit != null && hit.Value.DistanceTo(_lastDab) >= _radius.Value * 0.2)
                Dab(hit.Value, mode.Value);
            return (int)AfterGuiInput.Stop;
        }
        return (int)AfterGuiInput.Pass;
    }

    private void Dab(Vector3 at, TerrainSculpt.Mode mode)
    {
        _lastDab = at;
        if (TerrainSculpt.Dab(_terrain, _painting, at.X, at.Z, Brush(mode)))
            _terrain.SetHeights((float[])_painting.Clone());
    }

    private void EndStroke(TerrainSculpt.Mode mode)
    {
        _stroking = false;
        if (_before == null || _before.SequenceEqual(_painting))
            return;
        EditorUndoRedoManager undo = GetUndoRedo();
        undo.CreateAction($"Sculpt: {mode}", UndoRedo.MergeMode.Disable, _terrain);
        TerrainSculpt.Record(undo, _terrain, _before, (float[])_painting.Clone());
        undo.CommitAction(false);
        _before = _painting = null;
    }

    // ---------------- The ring ----------------

    private void ShowRing(Vector3? at)
    {
        if (at == null)
        {
            if (_ring != null)
                _ring.Visible = false;
            return;
        }
        if (_ring == null || !IsInstanceValid(_ring))
        {
            _ring = new MeshInstance3D
            {
                TopLevel = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    NoDepthTest = true,
                    AlbedoColor = new Color(1.0f, 0.85f, 0.2f),
                },
            };
            // Not owned: never saved with the scene.
            _terrain.AddChild(_ring, false, InternalMode.Back);
        }
        _ring.Visible = true;
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        float r = (float)_radius.Value;
        ArenaHeights heights = _terrain.Heights;
        for (int i = 0; i <= 64; i++)
        {
            float a = Mathf.Tau * i / 64;
            float x = at.Value.X + (Mathf.Cos(a) * r), z = at.Value.Z + (Mathf.Sin(a) * r);
            float y = heights.Contains(x, z) ? heights.HeightAt(x, z) : at.Value.Y;
            mesh.SurfaceAddVertex(new Vector3(x, y + 0.3f, z));
        }
        mesh.SurfaceEnd();
        _ring.Mesh = mesh;
    }

    private void DropRing()
    {
        if (_ring != null && IsInstanceValid(_ring))
            _ring.QueueFree();
        _ring = null;
    }

    // ---------------- Saving ----------------

    // The scene is being saved: its terrains' heights and chunks go to their files.
    public override void _SaveExternalData()
    {
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root == null)
            return;
        foreach (SculptedTerrain terrain in root.FindChildren("*", nameof(StaticBody3D), true, true).OfType<SculptedTerrain>())
            TerrainSculpt.Save(terrain);
    }
}
#endif
