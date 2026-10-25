#if TOOLS
using System.Collections.Generic;
using System.Linq;
using DillControl.Planet;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The asset lab's main screen (docs/decisions/0047): every asset on the
/// left, the chosen one in a 3D view of its own in the middle, and on the
/// right what it is, what it costs and the resources it is tuned by, in an
/// inspector. Save writes the shared resource, after naming who else uses it.
/// </summary>
[Tool]
public partial class AssetLabPanel : HSplitContainer
{
    public LabPreview Preview { get; private set; }
    public string Current { get; private set; } = "";
    public AssetInfo Info { get; private set; }
    public List<Resource> Resources { get; private set; } = new();

    private LineEdit _filter;
    private Tree _tree;
    private RichTextLabel _info;
    private OptionButton _resources;
    private EditorInspector _inspector;
    private Label _status;
    private OptionButton _look;
    private ConfirmationDialog _confirm;
    private readonly List<PlanetBiome> _biomes = new();

    private const string MetaSection = "asset_lab";

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
        _filter = new LineEdit { PlaceholderText = "Filter assets", ClearButtonEnabled = true };
        _filter.TextChanged += _ => FillTree();
        left.AddChild(_filter);
        _tree = new Tree { HideRoot = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        _tree.ItemSelected += () =>
        {
            if (_tree.GetSelected()?.GetMetadata(0).AsString() is string path && path != "")
            {
                Open(path);
            }
        };
        left.AddChild(_tree);
        AddChild(left);

        var middleAndRight = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(middleAndRight);

        var middle = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var bar = new HBoxContainer();
        bar.AddChild(Button("Frame", () => Preview.Frame()));
        var ink = new CheckBox { Text = "Ink", ButtonPressed = true };
        ink.Toggled += on => Preview.Ink = on;
        bar.AddChild(ink);
        var map = new CheckBox { Text = "Damage map", ButtonPressed = true };
        map.Toggled += on => Preview.DamageMap = on;
        bar.AddChild(map);
        _look = new OptionButton();
        _look.AddItem("Lab light");
        foreach (string file in DirAccess.GetFilesAt("res://resources/planet/biomes").Where(f => f.EndsWith(".tres")))
        {
            if (GD.Load<Resource>($"res://resources/planet/biomes/{file}") is PlanetBiome biome)
            {
                _biomes.Add(biome);
                _look.AddItem(file.GetBaseName().Replace("biome_", ""));
            }
        }
        _look.ItemSelected += index => Preview.SetLook(index == 0 ? null : _biomes[(int)index - 1]);
        bar.AddChild(_look);
        bar.AddChild(new Label { Text = "Sun" });
        var sun = new HSlider { MinValue = 2, MaxValue = 90, Value = 45, CustomMinimumSize = new Vector2(100, 0) };
        sun.ValueChanged += value => Preview.SunElevation = (float)value;
        bar.AddChild(sun);
        bar.AddChild(new VSeparator());
        var board = new CheckBox { Text = "Ride", ButtonPressed = true, TooltipText = "Test puts you in its seat if it is a vehicle, or steers a walker" };
        bar.AddChild(board);
        bar.AddChild(Button("Test", async () =>
        {
            if (Current != "")
            {
                await AssetLabCommands.StopAndWait();
                AssetLabCommands.Play(Current, board.ButtonPressed);
            }
        }));
        middle.AddChild(bar);
        Preview = new LabPreview();
        middle.AddChild(Preview);
        middleAndRight.AddChild(middle);

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        _info = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SelectionEnabled = true };
        right.AddChild(_info);
        right.AddChild(new HSeparator());
        _resources = new OptionButton();
        _resources.ItemSelected += index => _inspector.Edit(index >= 0 && index < Resources.Count ? Resources[(int)index] : null);
        right.AddChild(_resources);
        _inspector = new EditorInspector { SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(_inspector);
        var buttons = new HBoxContainer();
        buttons.AddChild(Button("Save", AskSave));
        buttons.AddChild(Button("Revert", Revert));
        right.AddChild(buttons);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        right.AddChild(_status);
        middleAndRight.AddChild(right);

        _confirm = new ConfirmationDialog { Title = "Save a shared resource", OkButtonText = "Save" };
        _confirm.Confirmed += Save;
        AddChild(_confirm);

        FillTree();
        string last = EditorInterface.Singleton.GetEditorSettings().GetProjectMetadata(MetaSection, "asset", "").AsString();
        if (last != "" && ResourceLoader.Exists(last))
        {
            CallDeferred(MethodName.Open, last);
        }
    }

    private static Button Button(string text, System.Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        return button;
    }

    private void FillTree()
    {
        _tree.Clear();
        TreeItem root = _tree.CreateItem();
        string filter = _filter.Text.ToLowerInvariant();
        var groups = new Dictionary<string, TreeItem>();
        foreach (AssetCatalog.Asset asset in AssetCatalog.All())
        {
            if (filter != "" && !asset.Path.ToLowerInvariant().Contains(filter))
            {
                continue;
            }
            if (!groups.TryGetValue(asset.Group, out TreeItem group))
            {
                group = _tree.CreateItem(root);
                group.SetText(0, asset.Group);
                group.SetSelectable(0, false);
                groups[asset.Group] = group;
            }
            TreeItem item = _tree.CreateItem(group);
            item.SetText(0, asset.Name);
            item.SetTooltipText(0, asset.Path);
            item.SetMetadata(0, asset.Path);
            if (asset.Path == Current)
            {
                item.Select(0);
            }
        }
    }

    /// <summary>Shows the asset at <paramref name="path"/> and reads it.</summary>
    public void Open(string path)
    {
        var scene = ResourceLoader.Load<PackedScene>(path);
        if (scene == null)
        {
            _status.Text = $"{path} is not a scene or an imported model.";
            return;
        }
        Current = path;
        EditorInterface.Singleton.GetEditorSettings().SetProjectMetadata(MetaSection, "asset", path);
        Preview.Show(scene);
        Info = AssetInfo.Of(path, Preview.Asset, Preview.Bounds);
        _info.Text = Info.Text();
        Resources = AssetCatalog.ResourcesOf(Preview.Asset);
        _resources.Clear();
        foreach (Resource resource in Resources)
        {
            _resources.AddItem(resource.ResourcePath.GetFile());
        }
        _inspector.Edit(Resources.Count > 0 ? Resources[0] : null);
        _status.Text = "";
    }

    private Resource Edited => _inspector.GetEditedObject() as Resource;

    private void AskSave()
    {
        Resource resource = Edited;
        if (resource == null)
        {
            return;
        }
        List<string> others = SharedWith(resource.ResourcePath);
        if (others.Count == 0)
        {
            Save();
            return;
        }
        _confirm.DialogText = $"{resource.ResourcePath.GetFile()} also tunes:\n  "
            + string.Join("\n  ", others.Select(o => o.GetFile())) + "\n\nSave it for all of them?";
        _confirm.PopupCentered();
    }

    /// <summary>Who uses a resource besides the asset shown.</summary>
    public List<string> SharedWith(string path) => AssetCatalog.UsersOf(path).Where(u => u != Current).ToList();

    private void Save()
    {
        Resource resource = Edited;
        if (resource == null)
        {
            return;
        }
        Error error = ResourceSaver.Save(resource, resource.ResourcePath);
        _status.Text = error == Error.Ok ? $"Saved {resource.ResourcePath}." : $"Could not save {resource.ResourcePath}: {error}.";
    }

    private void Revert()
    {
        Resource resource = Edited;
        if (resource == null)
        {
            return;
        }
        ResourceLoader.Load(resource.ResourcePath, "", ResourceLoader.CacheMode.Replace);
        _inspector.Edit(null);
        _inspector.Edit(resource);
        _status.Text = $"{resource.ResourcePath.GetFile()} as saved.";
    }
}
#endif
