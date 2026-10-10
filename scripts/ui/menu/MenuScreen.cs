using Godot;

namespace ModularMenu;

/// <summary>
/// A titled list of entries. Screens reference each other through
/// <see cref="MenuEntry.Target"/>, which is what makes submenus a data
/// relationship rather than a class hierarchy.
/// </summary>
[GlobalClass]
public partial class MenuScreen : Resource
{
    [Export]
    public string Title { get; set; } = "Menu";

    [Export]
    public Godot.Collections.Array<MenuEntry> Entries { get; set; } = new();
}
