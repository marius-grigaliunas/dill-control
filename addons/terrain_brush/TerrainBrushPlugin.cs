#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// The terrain brush (docs/decisions/0044, 0045): with a <see cref="SculptedTerrain"/>
/// selected, a bar on the 3D view's menu picks what the left button does in
/// the 3D view, and a ring shows where:
///
/// - Raise, Lower, Smooth, Flatten: sculpt the heights (<see cref="TerrainSculpt"/>).
/// - Scatter: paint real instances of the ticked palette's scenes; Shift erases (<see cref="Scatter"/>).
/// - Water: click a hollow to flood it to Depth over the ground clicked; Shift-click on water removes it (<see cref="WaterFlood"/>).
///
/// A stroke or a click is one undo. The terrain's heights and chunks are
/// written to their files when the scene is saved. The bridge's
/// <c>sculpt</c>, <c>scatter</c> and <c>flood</c> do the same things.
/// </summary>
[Tool]
public partial class TerrainBrushPlugin : EditorPlugin
{
    private SculptedTerrain _terrain;
    private HBoxContainer _bar;
    private ButtonGroup _modes;
    private SpinBox _radius, _strength, _hardness, _density, _spacing, _slope, _depth;
    private Control _radiusBox, _sculptBox, _scatterBox, _waterBox;
    private MenuButton _palette;
    private Label _status;
    private MeshInstance3D _ring;

    // A sculpt stroke: the heights before it, the copy it paints.
    private float[] _before, _painting;
    // A scatter stroke.
    private Scatter _scatter;
    private bool _erasing;
    private Vector3 _lastDab;
    private float _flattenTo;
    private bool _stroking;

    private const string SettingsSection = "terrain_brush";

    // False again after every reload of the project's code.
    private static bool _registeredByThisCode;

    public override void _EnterTree()
    {
        _bar = new HBoxContainer { Visible = false };
        _bar.AddChild(new VSeparator());
        _bar.AddChild(new Label { Text = "Terrain" });
        _modes = new ButtonGroup { AllowUnpress = true };
        foreach (string mode in new[] { "Raise", "Lower", "Smooth", "Flatten", "Scatter", "Water" })
        {
            var button = new Button { Text = mode, ToggleMode = true, ButtonGroup = _modes, TooltipText = Tip(mode) };
            button.Toggled += _ => ShowSettings();
            _bar.AddChild(button);
        }

        _radiusBox = Box(_bar);
        _radius = Spin(_radiusBox, "Radius", 1, 300, 1, 20, " m");
        _sculptBox = Box(_bar);
        _strength = Spin(_sculptBox, "Strength", 0.05, 10, 0.05, 0.5, "");
        _hardness = Spin(_sculptBox, "Hardness", 0, 0.95, 0.05, 0.3, "");
        _scatterBox = Box(_bar);
        _density = Spin(_scatterBox, "Density", 0.1, 50, 0.1, 2, " /1000m²");
        _spacing = Spin(_scatterBox, "Spacing", 0.5, 100, 0.5, 12, " m");
        _slope = Spin(_scatterBox, "Max slope", 0, 89, 1, 30, "°");
        _palette = new MenuButton { Text = "Palette", Flat = false, TooltipText = "The scenes Scatter paints and erases." };
        _scatterBox.AddChild(_palette);
        FillPalette();
        _waterBox = Box(_bar);
        _depth = Spin(_waterBox, "Depth", 0.1, 3, 0.05, 0.8, " m");
        _status = new Label { Modulate = new Color(1, 1, 1, 0.75f) };
        _bar.AddChild(_status);
        ShowSettings();

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
        "Flatten" => "Hold the left button to bring the ground to the height where the stroke began: Strength 0-1.",
        "Scatter" => "Hold the left button to paint the palette's scenes onto the ground, up to Density, Spacing apart, nowhere steeper than Max slope or wet. Shift erases them.",
        _ => "Click in a hollow: water fills it to Depth over the ground you clicked, everything lower that connects. Shift-click on water to take it away.",
    };

    private static Control Box(Container bar)
    {
        var box = new HBoxContainer();
        bar.AddChild(box);
        return box;
    }

    private static SpinBox Spin(Control box, string label, double min, double max, double step, double value, string suffix)
    {
        box.AddChild(new Label { Text = label });
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix, CustomMinimumSize = new Vector2(80, 0) };
        box.AddChild(spin);
        return spin;
    }

    private string ModeName() => (_modes?.GetPressedButton() as Button)?.Text;

    private void ShowSettings()
    {
        string mode = ModeName();
        _radiusBox.Visible = mode != null && mode != "Water";
        _sculptBox.Visible = mode is "Raise" or "Lower" or "Smooth" or "Flatten";
        _scatterBox.Visible = mode == "Scatter";
        _waterBox.Visible = mode == "Water";
        _status.Text = mode == "Scatter" ? Counted() : "";
    }

    // ---------------- The palette ----------------

    // Every prop scene; ticked ones are remembered with the project.
    private void FillPalette()
    {
        PopupMenu menu = _palette.GetPopup();
        menu.HideOnCheckableItemSelection = false;
        menu.Clear();
        var ticked = Ticked();
        foreach (string file in DirAccess.GetFilesAt("res://scenes/props").Where(f => f.EndsWith(".tscn")).OrderBy(f => f))
        {
            menu.AddCheckItem(file.GetBaseName());
            menu.SetItemChecked(menu.ItemCount - 1, ticked.Contains($"res://scenes/props/{file}"));
        }
        menu.IndexPressed += index =>
        {
            menu.SetItemChecked((int)index, !menu.IsItemChecked((int)index));
            var now = new List<string>();
            for (int i = 0; i < menu.ItemCount; i++)
            {
                if (menu.IsItemChecked(i))
                    now.Add($"res://scenes/props/{menu.GetItemText(i)}.tscn");
            }
            EditorInterface.Singleton.GetEditorSettings().SetProjectMetadata(SettingsSection, "palette", now.ToArray());
            _status.Text = Counted();
        };
    }

    /// <summary>The ticked palette; the trees until the user picks.</summary>
    public static List<string> Ticked()
    {
        Variant saved = EditorInterface.Singleton.GetEditorSettings().GetProjectMetadata(SettingsSection, "palette", default);
        if (saved.VariantType == Variant.Type.PackedStringArray)
            return saved.AsStringArray().ToList();
        return DirAccess.GetFilesAt("res://scenes/props").Where(f => f.StartsWith("prp_tree_") && f.EndsWith(".tscn"))
            .Select(f => $"res://scenes/props/{f}").ToList();
    }

    private string Counted()
    {
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        List<string> palette = Ticked();
        return root == null ? "" : $"{palette.Count} scenes ticked, {Scatter.Count(root, palette)} placed";
    }

    // ---------------- Input ----------------

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

    private TerrainSculpt.Brush Brush(TerrainSculpt.Mode mode) => new()
    {
        Mode = mode,
        RadiusM = (float)_radius.Value,
        Strength = (float)_strength.Value,
        Hardness = (float)_hardness.Value,
        Target = _flattenTo,
    };

    private Scatter.Settings ScatterSettings() => new()
    {
        RadiusM = (float)_radius.Value,
        Density = (float)_density.Value,
        SpacingM = (float)_spacing.Value,
        MaxSlopeDeg = (float)_slope.Value,
    };

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent @event)
    {
        string mode = ModeName();
        if (_terrain == null || !IsInstanceValid(_terrain) || mode == null || _terrain.Map == null)
        {
            DropRing();
            return (int)AfterGuiInput.Pass;
        }
        if (@event is not InputEventMouse mouse)
            return (int)AfterGuiInput.Pass;

        Vector3? hit = _terrain.Cast(camera.ProjectRayOrigin(mouse.Position), camera.ProjectRayNormal(mouse.Position));
        ShowRing(hit, mode == "Water" ? 2.0f : (float)_radius.Value);

        if (mode == "Water")
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click && hit != null)
            {
                Water(hit.Value, click.ShiftPressed);
                return (int)AfterGuiInput.Stop;
            }
            return @event is InputEventMouseButton { ButtonIndex: MouseButton.Left } ? (int)AfterGuiInput.Stop : (int)AfterGuiInput.Pass;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed && hit != null)
            {
                _stroking = true;
                if (mode == "Scatter")
                {
                    _scatter = new Scatter(_terrain, Ticked());
                    _erasing = button.ShiftPressed;
                }
                else
                {
                    _before = _terrain.CopyHeights();
                    _painting = (float[])_before.Clone();
                    _flattenTo = hit.Value.Y - _terrain.GlobalPosition.Y;
                }
                Dab(hit.Value, mode);
                return (int)AfterGuiInput.Stop;
            }
            if (!button.Pressed && _stroking)
            {
                EndStroke(mode);
                return (int)AfterGuiInput.Stop;
            }
        }
        if (@event is InputEventMouseMotion && _stroking)
        {
            // A dab every fifth of the radius along the drag.
            if (hit != null && hit.Value.DistanceTo(_lastDab) >= _radius.Value * 0.2)
                Dab(hit.Value, mode);
            return (int)AfterGuiInput.Stop;
        }
        return (int)AfterGuiInput.Pass;
    }

    private void Dab(Vector3 at, string mode)
    {
        _lastDab = at;
        if (mode == "Scatter")
        {
            if (_erasing)
                _scatter.Erase(at.X, at.Z, ScatterSettings());
            else
                _scatter.Paint(at.X, at.Z, ScatterSettings());
            return;
        }
        if (TerrainSculpt.TryMode(mode, out TerrainSculpt.Mode m) && TerrainSculpt.Dab(_terrain, _painting, at.X, at.Z, Brush(m)))
            _terrain.SetHeights((float[])_painting.Clone());
    }

    private void EndStroke(string mode)
    {
        _stroking = false;
        EditorUndoRedoManager undo = GetUndoRedo();
        if (mode == "Scatter")
        {
            if (_scatter != null && _scatter.Changed)
            {
                undo.CreateAction(_erasing ? "Scatter: erase" : "Scatter", UndoRedo.MergeMode.Disable, EditorInterface.Singleton.GetEditedSceneRoot());
                _scatter.Record(undo);
                undo.CommitAction(false);
            }
            _scatter = null;
            _status.Text = Counted();
            return;
        }
        if (_before == null || _before.SequenceEqual(_painting))
            return;
        undo.CreateAction($"Sculpt: {mode}", UndoRedo.MergeMode.Disable, _terrain);
        TerrainSculpt.Record(undo, _terrain, _before, (float[])_painting.Clone());
        undo.CommitAction(false);
        _before = _painting = null;
    }

    private void Water(Vector3 at, bool remove)
    {
        EditorUndoRedoManager undo = GetUndoRedo();
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (remove)
        {
            // The ground clicked is under water: that body goes.
            if (WaterBody.DepthAt(at, out WaterBody body) <= 0.0f || body == null)
            {
                _status.Text = "no water there";
                return;
            }
            Node parent = body.GetParent();
            undo.CreateAction($"Water: remove {body.Name}", UndoRedo.MergeMode.Disable, root);
            undo.AddDoMethod(parent, Node.MethodName.RemoveChild, body);
            undo.AddUndoMethod(parent, Node.MethodName.AddChild, body, true);
            undo.AddUndoMethod(parent, Node.MethodName.MoveChild, body, body.GetIndex());
            undo.AddUndoProperty(body, Node.PropertyName.Owner, root);
            foreach (Node child in body.GetChildren())
                undo.AddUndoProperty(child, Node.PropertyName.Owner, root);
            undo.AddUndoReference(body);
            parent.RemoveChild(body);
            undo.CommitAction(false);
            _status.Text = $"removed {body.Name}";
            return;
        }
        undo.CreateAction("Water: flood", UndoRedo.MergeMode.Disable, root);
        try
        {
            WaterBody made = WaterFlood.Flood(_terrain, at.X, at.Z, at.Y + (float)_depth.Value, undo);
            _status.Text = $"{made.Name}: {made.Cells.Count(c => c != 0)} cells at {made.GlobalPosition.Y:0.0} m";
        }
        catch (WaterFlood.Refused e)
        {
            _status.Text = e.Message;
        }
        undo.CommitAction(false);
    }

    // ---------------- The ring ----------------

    private void ShowRing(Vector3? at, float radius)
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
        ArenaHeights heights = _terrain.Heights;
        for (int i = 0; i <= 64; i++)
        {
            float a = Mathf.Tau * i / 64;
            float x = at.Value.X + (Mathf.Cos(a) * radius), z = at.Value.Z + (Mathf.Sin(a) * radius);
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
