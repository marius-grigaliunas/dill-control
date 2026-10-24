#if TOOLS
using Godot;

namespace DillControl.Editor;

/// <summary>The Vines dock: a <see cref="VinePanel"/> to hand beside any
/// screen. The Vines tool's Vine tab holds the same.</summary>
[Tool]
public partial class VineGenDock : EditorDock
{
    public override void _Ready()
    {
        if (GetChildCount() > 0)
            return;
        AddChild(new VinePanel { Name = "Vine" });
    }
}
#endif
