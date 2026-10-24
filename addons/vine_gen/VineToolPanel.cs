#if TOOLS
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The Vines tool, a main screen beside 2D, 3D, Script and AssetLib
/// (docs/handoffs/vine-tool.md, "The tool"): the Nest tab
/// (<see cref="NestTab"/>), the range lab and the vine panel.
/// </summary>
[Tool]
public partial class VineToolPanel : VBoxContainer
{
    public override void _Ready()
    {
        if (GetChildCount() > 0)
            return;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(tabs);
        var nest = new NestTab { Name = "Nest", SizeFlagsVertical = SizeFlags.ExpandFill };
        tabs.AddChild(nest);
    }
}
#endif
