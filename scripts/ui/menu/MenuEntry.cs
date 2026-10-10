using Godot;

namespace ModularMenu;

/// <summary>
/// One row on a menu screen.
///
/// A Resource rather than a scene node so a whole menu is data: screens can be
/// authored as .tres in the inspector, swapped per build, or built in code in
/// half a dozen lines. Nothing here knows what a player or a level is.
/// </summary>
[GlobalClass]
public partial class MenuEntry : Resource
{
    [Export]
    public string Label { get; set; } = "Item";

    [Export]
    public MenuAction Action { get; set; } = MenuAction.Custom;

    /// <summary>
    /// Identifies this entry to the host. It is the settings key for Toggle and
    /// Slider, and the payload of <see cref="MenuSystem.CustomActivated"/> for
    /// Custom. Never shown to the player.
    /// </summary>
    [Export]
    public string Id { get; set; } = string.Empty;

    /// <summary>Screen to push. Only read when Action is OpenScreen.</summary>
    [Export]
    public MenuScreen Target { get; set; }

    [ExportGroup("Slider")]
    [Export]
    public float Minimum { get; set; }

    [Export]
    public float Maximum { get; set; } = 1.0f;

    [Export]
    public float Step { get; set; } = 0.05f;

    /// <summary>
    /// Multiplies the stored value for display only, so a 0..1 setting can be
    /// shown as 0..100. Never applied to what the host is handed.
    /// </summary>
    [Export]
    public float DisplayScale { get; set; } = 1.0f;

    [Export]
    public string Suffix { get; set; } = string.Empty;

    /// <summary>True for the rows that take left/right input.</summary>
    public bool IsAdjustable => Action is MenuAction.Slider or MenuAction.Toggle;
}
