#if TOOLS
using Godot;

namespace DillControl.Editor;

/// <summary>Adds the Vines dock (<see cref="VineGenDock"/>). Vines are baked
/// in the editor, never grown in the game (docs/decisions/0033); the editor is
/// also the only thing that imports the .vox a bake writes.</summary>
[Tool]
public partial class VineGenPlugin : EditorPlugin
{
    private VineGenDock _dock;

    public override void _EnterTree()
    {
        _dock = new VineGenDock { Title = "Vines", DefaultSlot = EditorDock.DockSlot.RightUl, LayoutKey = "vine_gen" };
        AddDock(_dock);
    }

    public override void _ExitTree()
    {
        RemoveDock(_dock);
        _dock.QueueFree();
        _dock = null;
    }
}
#endif
