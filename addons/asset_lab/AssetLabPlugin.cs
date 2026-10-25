#if TOOLS
using System.Linq;
using System.Runtime.Loader;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The asset lab (docs/decisions/0047): a main screen of its own beside 2D,
/// 3D and Vines (<see cref="AssetLabPanel"/>) to look at, tune and test any
/// asset, and its commands on the editor bridge (<see cref="AssetLabCommands"/>).
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
        _panel = new AssetLabPanel { Name = "AssetLab", Visible = _visible };
        EditorInterface.Singleton.GetEditorMainScreen().AddChild(_panel);
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
        _panel = null;
    }

    public override bool _HasMainScreen() => true;

    public override void _MakeVisible(bool visible)
    {
        _visible = visible;
        if (_panel != null && IsInstanceValid(_panel))
            _panel.Visible = visible;
    }

    public override string _GetPluginName() => "Lab";

    public override Texture2D _GetPluginIcon() =>
        EditorInterface.Singleton.GetEditorTheme().GetIcon("Node3D", "EditorIcons");
}
#endif
