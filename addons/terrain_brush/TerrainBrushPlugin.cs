#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;
using DillControl.Foliage;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// The terrain brush (docs/decisions/0044, 0045): with a <see cref="SculptedTerrain"/>
/// selected, a bar on the 3D view's menu picks what the left button does in
/// the 3D view, and a ring shows where:
///
/// - Raise, Lower, Smooth, Flatten: sculpt the heights (<see cref="TerrainSculpt"/>).
/// - Scatter: real instances of the ticked palette's scenes (<see cref="Scatter"/>).
/// - Forest: tall trees, small trees and undergrowth in one stroke (<see cref="ForestBrush"/>).
/// - Vines: new unique vines of a kind, baked and placed when the stroke ends (<see cref="VineBrush"/>).
/// - Flood: a click fills a hollow to Depth over the ground clicked (<see cref="WaterFlood"/>).
/// - Water: paints a pond's water on (<see cref="WaterPaint"/>).
///
/// Shift takes away: what that brush paints, or the water. A stroke or a
/// click is one undo. The terrain's heights and chunks, and the ponds'
/// surfaces, are written to their files when the scene is saved. The
/// bridge's sculpt, scatter, forest, vines, flood and water_paint do the same.
/// </summary>
[Tool]
public partial class TerrainBrushPlugin : EditorPlugin
{
    private static readonly string[] ModeNames =
        { "Off", "Raise", "Lower", "Smooth", "Flatten", "Scatter", "Forest", "Vines", "Flood", "Water" };

    private SculptedTerrain _terrain;
    private HBoxContainer _bar;
    private OptionButton _mode, _kind;
    private SpinBox _radius, _strength, _hardness, _density, _spacing, _slope, _thickness, _edge, _vineDensity, _vineSpacing, _depth;
    private Control _radiusBox, _sculptBox, _scatterBox, _forestBox, _vinesBox, _depthBox;
    private MenuButton _palette;
    private Label _status;
    private MeshInstance3D _ring;
    private List<VineKind> _kinds = new();

    // The stroke under way: a sculpt's heights, a scatter, a vine brush, a water paint.
    private float[] _before, _painting;
    private Scatter _scatter;
    private VineBrush _vines;
    private WaterPaint _water;
    private bool _erasing, _stroking, _baking;
    private Vector3 _lastDab;
    private float _flattenTo;

    private const string SettingsSection = "terrain_brush";

    // False again after every reload of the project's code.
    private static bool _registeredByThisCode;

    public override void _EnterTree()
    {
        _bar = new HBoxContainer { Visible = false };
        _bar.AddChild(new VSeparator());
        _bar.AddChild(new Label { Text = "Terrain" });
        _mode = new OptionButton { TooltipText = "What the left button does in the 3D view. Shift takes away." };
        foreach (string mode in ModeNames)
            _mode.AddItem(mode);
        _mode.ItemSelected += _ => ShowSettings();
        _bar.AddChild(_mode);

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
        _forestBox = Box(_bar);
        _thickness = Spin(_forestBox, "Thickness", 0.1, 5, 0.1, 1, "×");
        _edge = Spin(_forestBox, "Edge", 0, 1, 0.05, 0.5, "");
        _vinesBox = Box(_bar);
        _kind = new OptionButton { TooltipText = "The kind every new vine is grown from." };
        _vinesBox.AddChild(_kind);
        _vineDensity = Spin(_vinesBox, "Density", 0.05, 10, 0.05, 0.5, " /1000m²");
        _vineSpacing = Spin(_vinesBox, "Spacing", 2, 120, 1, 20, " m");
        _depthBox = Box(_bar);
        _depth = Spin(_depthBox, "Depth", 0.1, 3, 0.05, 0.8, " m");
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

    private string Mode => _mode == null || _mode.Selected <= 0 ? null : ModeNames[_mode.Selected];

    private void ShowSettings()
    {
        string mode = Mode;
        _radiusBox.Visible = mode != null && mode != "Flood";
        _sculptBox.Visible = mode is "Raise" or "Lower" or "Smooth" or "Flatten";
        _scatterBox.Visible = mode == "Scatter";
        _forestBox.Visible = mode == "Forest";
        _vinesBox.Visible = mode == "Vines";
        _depthBox.Visible = mode is "Flood" or "Water";
        if (mode == "Vines")
            FillKinds();
        _status.Text = mode switch
        {
            "Scatter" => Counted(Ticked().Contains, "placed"),
            "Forest" => Counted(ForestBrush.AllScenes().Contains, "in forests"),
            _ => "",
        };
    }

    private void FillKinds()
    {
        string was = _kind.Selected >= 0 && _kind.Selected < _kinds.Count ? _kinds[_kind.Selected].KindName : "Shrub";
        _kinds = DirAccess.GetFilesAt(VineKind.Folder).Where(f => f.StartsWith(VineKind.Prefix) && f.EndsWith(".tres"))
            .Select(f => ResourceLoader.Load<VineKind>($"{VineKind.Folder}/{f}")).Where(k => k != null).ToList();
        _kind.Clear();
        foreach (VineKind kind in _kinds)
            _kind.AddItem(kind.KindName);
        _kind.Selected = System.Math.Max(0, _kinds.FindIndex(k => k.KindName == was));
    }

    private VineKind Kind() => _kind.Selected >= 0 && _kind.Selected < _kinds.Count ? _kinds[_kind.Selected] : null;

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
            ShowSettings();
        };
    }

    /// <summary>The ticked palette; the trees until the user picks.</summary>
    public static List<string> Ticked()
    {
        Variant saved = EditorInterface.Singleton.GetEditorSettings().GetProjectMetadata(SettingsSection, "palette", false);
        if (saved.VariantType == Variant.Type.PackedStringArray)
            return saved.AsStringArray().ToList();
        return Scatter.ScenesStarting(new[] { "prp_tree_" });
    }

    private static string Counted(System.Func<string, bool> mine, string what)
    {
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        return root == null ? "" : $"{Scatter.Count(root, mine)} {what}";
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
        Hardness = 1.0f,
    };

    private Scatter.Settings VineSettings() => new()
    {
        RadiusM = (float)_radius.Value,
        Density = (float)_vineDensity.Value,
        SpacingM = (float)_vineSpacing.Value,
        MaxSlopeDeg = 30.0f,
        Hardness = 1.0f,
    };

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent @event)
    {
        string mode = Mode;
        if (_terrain == null || !IsInstanceValid(_terrain) || mode == null || _terrain.Map == null || _baking)
        {
            DropRing();
            return (int)AfterGuiInput.Pass;
        }
        if (@event is not InputEventMouse mouse)
            return (int)AfterGuiInput.Pass;

        Vector3? hit = _terrain.Cast(camera.ProjectRayOrigin(mouse.Position), camera.ProjectRayNormal(mouse.Position));
        ShowRing(hit, mode == "Flood" ? 2.0f : (float)_radius.Value);

        if (mode == "Flood")
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click && hit != null)
            {
                Flood(hit.Value, click.ShiftPressed);
                return (int)AfterGuiInput.Stop;
            }
            return @event is InputEventMouseButton { ButtonIndex: MouseButton.Left } ? (int)AfterGuiInput.Stop : (int)AfterGuiInput.Pass;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed && hit != null)
            {
                BeginStroke(mode, hit.Value, button.ShiftPressed);
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

    private void BeginStroke(string mode, Vector3 at, bool shift)
    {
        _stroking = true;
        _erasing = shift;
        switch (mode)
        {
            case "Scatter":
            case "Forest":
                _scatter = new Scatter(_terrain);
                break;
            case "Vines":
                _scatter = new Scatter(_terrain);
                _vines = Kind() is { } kind ? new VineBrush(_scatter, kind) : null;
                break;
            case "Water":
                _water = new WaterPaint(_terrain, at, (float)_radius.Value, (float)_depth.Value);
                break;
            default:
                _before = _terrain.CopyHeights();
                _painting = (float[])_before.Clone();
                _flattenTo = at.Y - _terrain.GlobalPosition.Y;
                break;
        }
    }

    private void Dab(Vector3 at, string mode)
    {
        _lastDab = at;
        float r = (float)_radius.Value;
        switch (mode)
        {
            case "Scatter":
                if (_erasing)
                    _scatter.Erase(at.X, at.Z, ScatterSettings(), Ticked().Contains);
                else
                    _scatter.Paint(at.X, at.Z, ScatterSettings(), Ticked());
                return;
            case "Forest":
                ForestBrush.Dab(_scatter, at.X, at.Z, r, (float)_thickness.Value, (float)_edge.Value, _erasing);
                return;
            case "Vines":
                if (_erasing)
                    _vines?.Erase(at.X, at.Z, VineSettings());
                else
                    _vines?.Paint(at.X, at.Z, VineSettings());
                _status.Text = _vines != null ? $"{_vines.Waiting} vines to grow" : "no kind";
                return;
            case "Water":
                _water?.Dab(at.X, at.Z, r, _erasing);
                return;
        }
        if (TerrainSculpt.TryMode(mode, out TerrainSculpt.Mode m) && TerrainSculpt.Dab(_terrain, _painting, at.X, at.Z, Brush(m)))
            _terrain.SetHeights((float[])_painting.Clone());
    }

    private async void EndStroke(string mode)
    {
        _stroking = false;
        EditorUndoRedoManager undo = GetUndoRedo();
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        switch (mode)
        {
            case "Scatter":
            case "Forest":
            case "Vines":
                Scatter scatter = _scatter;
                VineBrush vines = _vines;
                _scatter = null;
                _vines = null;
                if (vines != null && vines.Waiting > 0)
                {
                    _baking = true;
                    int placed = await vines.Finish(s => _status.Text = s);
                    _baking = false;
                    _status.Text = $"{placed} new vines placed";
                }
                if (scatter != null && scatter.Changed)
                {
                    undo.CreateAction(_erasing ? $"{mode}: erase" : mode, UndoRedo.MergeMode.Disable, root);
                    scatter.Record(undo);
                    undo.CommitAction(false);
                }
                if (mode != "Vines")
                    ShowSettings();
                return;
            case "Water":
                if (_water != null)
                {
                    undo.CreateAction(_erasing ? "Water: dry" : "Water", UndoRedo.MergeMode.Disable, root);
                    _water.Record(undo);
                    undo.CommitAction(false);
                    _status.Text = $"{_water.Body.Name}: {_water.WetCells} cells";
                }
                _water = null;
                return;
        }
        if (_before == null || _before.SequenceEqual(_painting))
            return;
        undo.CreateAction($"Sculpt: {mode}", UndoRedo.MergeMode.Disable, _terrain);
        TerrainSculpt.Record(undo, _terrain, _before, (float[])_painting.Clone());
        undo.CommitAction(false);
        _before = _painting = null;
    }

    private void Flood(Vector3 at, bool remove)
    {
        EditorUndoRedoManager undo = GetUndoRedo();
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (remove)
        {
            // The ground clicked is under water: that pond goes.
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
        try
        {
            undo.CreateAction("Water: flood", UndoRedo.MergeMode.Disable, root);
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

    // The scene is being saved: its terrains' heights and chunks, and its ponds' surfaces, go to their files.
    public override void _SaveExternalData()
    {
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root == null)
            return;
        foreach (SculptedTerrain terrain in root.FindChildren("*", nameof(StaticBody3D), true, true).OfType<SculptedTerrain>())
            TerrainSculpt.Save(terrain);
        foreach (WaterBody water in root.FindChildren("*", nameof(Node3D), true, true).OfType<WaterBody>())
        {
            if (water.GetNodeOrNull("Surface") is MeshInstance3D { Mesh: { } mesh } && !string.IsNullOrEmpty(mesh.ResourcePath))
                ResourceSaver.Save(mesh);
        }
    }
}
#endif
