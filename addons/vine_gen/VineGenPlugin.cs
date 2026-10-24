#if TOOLS
using System.Linq;
using System.Runtime.Loader;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The Vines tool: a main screen of its own beside 2D, 3D, Script and AssetLib
/// (<see cref="VineToolPanel"/>: generate a nest, see it, play it), and the
/// Vines dock (<see cref="VineGenDock"/>) for one vine. Baked vines are made
/// in the editor (docs/decisions/0033); the editor is also the only thing
/// that imports the .vox a bake writes.
///
/// Rebuilt after every reload of the project's code: the editor does not
/// carry a tool's C# signal handlers over a rebuild (its buttons would call
/// nothing), so the panel and the dock are taken down before the old code
/// is unloaded, which also lets it unload, and made again from the new.
/// </summary>
[Tool]
public partial class VineGenPlugin : EditorPlugin
{
    private VineGenDock _dock;
    private VineToolPanel _panel;
    private bool _visible;

    // False again after every reload of the project's code (statics start
    // over with the new assembly): the first frame after one rebuilds.
    private static bool _builtByThisCode;

    private const string PanelName = "VineTool";
    private const string DockName = "VinesDock";

    public override void _EnterTree()
    {
        TearDown();
        Build();
        AssemblyLoadContext.GetLoadContext(typeof(VineGenPlugin).Assembly)!.Unloading += OnUnloading;
    }

    public override void _ExitTree()
    {
        AssemblyLoadContext.GetLoadContext(typeof(VineGenPlugin).Assembly)!.Unloading -= OnUnloading;
        TearDown();
    }

    // The old code is going: nothing of it may stay in the editor's tree.
    private void OnUnloading(AssemblyLoadContext context) => TearDown();

    public override void _Process(double delta)
    {
        // After a reload: whatever the old code left up goes, and the tool is
        // made again from the new code, so every button is connected to it.
        if (!_builtByThisCode || _panel == null || !IsInstanceValid(_panel))
        {
            TearDown();
            Build();
            _MakeVisible(_visible);
            _rebuilt = true;
        }
        ReloadCheck(delta);
    }

    // For checking that the tool survives a rebuild (--editor -- reload-check):
    // once user://reload_go exists (made after the code is rebuilt), the
    // editor is told it has the focus again, which is when it reloads the
    // project's code; after the reload, the Nest tab's Roll is pressed and
    // whether the seed changed is printed, and the editor quits.
    private bool _rebuilt;
    private double _checkWait;

    private void ReloadCheck(double delta)
    {
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "reload-check") < 0)
            return;
        _checkWait += delta;
        if (!_rebuilt)
        {
            if (FileAccess.FileExists("user://reload_go") && _checkWait > 1.0)
            {
                _checkWait = 0.0;
                GD.Print("[reloadcheck] code rebuilt: telling the editor it has the focus");
                GetTree().Root.PropagateNotification((int)NotificationApplicationFocusIn);
            }
            if (_checkWait > 120.0)
            {
                GD.Print("[reloadcheck] FAIL: no reload in 120 s");
                GetTree().Quit();
            }
            return;
        }
        if (_checkWait < 2.0)
            return;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://reload_go"));
        NestTab nest = _panel.FindChild("Nest", true, false) as NestTab;
        SpinBox seed = nest?.FindChildren("*", nameof(SpinBox), true, false)[0] as SpinBox;
        Button roll = nest?.FindChildren("*", nameof(Button), true, false).Cast<Button>().FirstOrDefault(b => b.Text == "Roll");
        double before = seed?.Value ?? -1;
        roll?.EmitSignal(BaseButton.SignalName.Pressed);
        double after = seed?.Value ?? -1;
        GD.Print($"[reloadcheck] {(roll != null && after != before ? "PASS" : "FAIL")}: after the reload, Roll took the seed from {before} to {after}");
        GetTree().Quit();
    }

    private void Build()
    {
        _dock = new VineGenDock { Name = DockName, Title = "Vines", DefaultSlot = EditorDock.DockSlot.RightUl, LayoutKey = "vine_gen" };
        AddDock(_dock);
        _panel = new VineToolPanel { Name = PanelName };
        EditorInterface.Singleton.GetEditorMainScreen().AddChild(_panel);
        _panel.Visible = _visible;
        _builtByThisCode = true;
    }

    // Ours, and whatever an older load of the code left up: found by type,
    // since after a reload the references to them may be gone, and an older
    // build named its dock nothing in particular.
    private void TearDown()
    {
        foreach (VineGenDock dock in EditorInterface.Singleton.GetBaseControl().FindChildren("*", "", true, false).OfType<VineGenDock>().ToList())
        {
            RemoveDock(dock);
            dock.Free();
        }
        _dock = null;
        foreach (VineToolPanel panel in EditorInterface.Singleton.GetEditorMainScreen().GetChildren().OfType<VineToolPanel>().ToList())
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

    public override string _GetPluginName() => "Vines";

    public override Texture2D _GetPluginIcon() =>
        EditorInterface.Singleton.GetEditorTheme().GetIcon("CurveTexture", "EditorIcons");
}
#endif
