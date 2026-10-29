#if TOOLS
using System.Linq;
using System.Runtime.Loader;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The asset lab (docs/decisions/0047): a main screen of its own beside 2D,
/// 3D and Vines (<see cref="AssetLabPanel"/>) to look at, tune and test any
/// asset, and its commands on the editor bridge (<see cref="AssetLabCommands"/>).
/// Its asset list is a dock in the left tab group, its Info and Parameters
/// one in the right (with the Inspector), and a walker's timeline a tab of
/// the bottom panel, all open only while the Lab tab is.
///
/// Rebuilt after every reload of the project's code, as the Vines tool is
/// (0043): the editor does not carry a tool's C# signal handlers over a
/// rebuild, so the panel is taken down before the old code unloads and made
/// again from the new.
/// </summary>
[Tool]
public partial class AssetLabPlugin : EditorPlugin
{
    private AssetLabPanel _panel;
    private EditorDock _assets;
    private EditorDock _timeline;
    private EditorDock _tune;
    private bool _hasTimeline;
    private bool _visible;

    // False again after every reload of the project's code.
    private static bool _builtByThisCode;

    public override void _EnterTree()
    {
        TearDown();
        Build();
        AssemblyLoadContext.GetLoadContext(typeof(AssetLabPlugin).Assembly)!.Unloading += OnUnloading;
    }

    public override void _ExitTree()
    {
        AssemblyLoadContext.GetLoadContext(typeof(AssetLabPlugin).Assembly)!.Unloading -= OnUnloading;
        TearDown();
    }

    private void OnUnloading(AssemblyLoadContext context) => TearDown();

    public override void _Process(double delta)
    {
        if (!_builtByThisCode || _panel == null || !IsInstanceValid(_panel))
        {
            TearDown();
            Build();
            _MakeVisible(_visible);
        }
    }

    private void Build()
    {
        // A reload makes a new plugin that does not know the Lab tab was the
        // one showing: the editor remembers it for us.
        _visible = EditorInterface.Singleton.GetEditorSettings().GetProjectMetadata("asset_lab", "visible", false).AsBool()
            || !EditorInterface.Singleton.GetEditorMainScreen().GetChildren().OfType<Control>().Any(c => c.Visible && c is not AssetLabPanel);
        _panel = new AssetLabPanel { Name = "AssetLab", Visible = _visible };
        EditorInterface.Singleton.GetEditorMainScreen().AddChild(_panel);

        _assets = new EditorDock
        {
            Name = "AssetLabAssets",
            Title = "Assets",
            LayoutKey = "AssetLabAssets",
            IconName = "PackedScene",
            DefaultSlot = EditorDock.DockSlot.LeftUr,
            Transient = true,
        };
        _assets.AddChild(_panel.AssetList);
        AddDock(_assets);
        _tune = new EditorDock
        {
            Name = "AssetLabTune",
            Title = "Lab",
            LayoutKey = "AssetLabTune",
            IconName = "Tools",
            DefaultSlot = EditorDock.DockSlot.RightUl,
            Transient = true,
        };
        _tune.AddChild(_panel.TuneBox);
        AddDock(_tune);
        _timeline = new EditorDock
        {
            Name = "AssetLabTimeline",
            Title = "Lab Timeline",
            LayoutKey = "AssetLabTimeline",
            IconName = "Animation",
            DefaultSlot = EditorDock.DockSlot.Bottom,
            AvailableLayouts = EditorDock.DockLayout.Horizontal | EditorDock.DockLayout.Floating,
            Transient = true,
        };
        _timeline.AddChild(_panel.TimelineBox);
        AddDock(_timeline);
        _panel.TimelineWanted += on =>
        {
            _hasTimeline = on;
            ShowDocks();
        };
        AssetLabCommands.Panel = () => _panel != null && IsInstanceValid(_panel) ? _panel : null;
        AssetLabCommands.Register();
        _builtByThisCode = true;
    }

    private void TearDown()
    {
        foreach (AssetLabPanel panel in EditorInterface.Singleton.GetEditorMainScreen().GetChildren().OfType<AssetLabPanel>().ToList())
        {
            panel.GetParent().RemoveChild(panel);
            panel.Free();
        }
        // By name: after a reload this plugin's fields may not name the docks
        // the old code made.
        foreach (EditorDock dock in GetTree().Root.FindChildren("AssetLab*", "EditorDock", true, false).OfType<EditorDock>().ToList())
        {
            RemoveDock(dock);
            dock.Free();
        }
        _panel = null;
        _assets = null;
        _timeline = null;
        _tune = null;
    }

    public override bool _HasMainScreen() => true;

    public override void _MakeVisible(bool visible)
    {
        _visible = visible;
        EditorInterface.Singleton.GetEditorSettings().SetProjectMetadata("asset_lab", "visible", visible);
        if (_panel != null && IsInstanceValid(_panel))
            _panel.Visible = visible;
        ShowDocks();
    }

    /// <summary>The docks follow the Lab tab: the asset list to the front
    /// of the left tab group, and the timeline, for a walker, to the front of
    /// the bottom panel; both gone on any other screen.</summary>
    private void ShowDocks()
    {
        if (_assets == null || !IsInstanceValid(_assets) || _timeline == null || !IsInstanceValid(_timeline)
            || _tune == null || !IsInstanceValid(_tune))
        {
            return;
        }
        if (_visible)
        {
            _assets.MakeVisible();
            _tune.MakeVisible();
        }
        else
        {
            _assets.Close();
            _tune.Close();
        }
        if (_visible && _hasTimeline)
        {
            _timeline.MakeVisible();
        }
        else
        {
            _timeline.Close();
        }
    }

    public override string _GetPluginName() => "Lab";

    public override Texture2D _GetPluginIcon() =>
        EditorInterface.Singleton.GetEditorTheme().GetIcon("Node3D", "EditorIcons");
}
#endif
