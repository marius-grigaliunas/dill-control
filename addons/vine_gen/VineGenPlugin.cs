#if TOOLS
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The Vines tool: a main screen of its own beside 2D, 3D, Script and AssetLib
/// (<see cref="VineToolPanel"/>: generate a nest, see it, play it), and the
/// Vines dock (<see cref="VineGenDock"/>) for one vine. Baked vines are made
/// in the editor (docs/decisions/0033); the editor is also the only thing
/// that imports the .vox a bake writes.
/// </summary>
[Tool]
public partial class VineGenPlugin : EditorPlugin
{
    private VineGenDock _dock;
    private VineToolPanel _panel;

    public override void _EnterTree()
    {
        _dock = new VineGenDock { Title = "Vines", DefaultSlot = EditorDock.DockSlot.RightUl, LayoutKey = "vine_gen" };
        AddDock(_dock);
        _panel = new VineToolPanel { Name = "VineTool" };
        EditorInterface.Singleton.GetEditorMainScreen().AddChild(_panel);
        _MakeVisible(false);
    }

    public override void _ExitTree()
    {
        RemoveDock(_dock);
        _dock.QueueFree();
        _dock = null;
        _panel?.QueueFree();
        _panel = null;
    }

    public override bool _HasMainScreen() => true;

    public override void _MakeVisible(bool visible)
    {
        if (_panel != null)
            _panel.Visible = visible;
    }

    public override string _GetPluginName() => "Vines";

    public override Texture2D _GetPluginIcon() =>
        EditorInterface.Singleton.GetEditorTheme().GetIcon("CurveTexture", "EditorIcons");
}
#endif
