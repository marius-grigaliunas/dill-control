using Godot;
using ModularMenu;

namespace DillControl.UI;

/// <summary>
/// The title screen: the game's name, its premise, and the way in to the
/// player's ship. The lab's menu module drives it, with a screen authored as
/// data (<c>resources/ui/menu_main.tres</c>); this only answers its one custom
/// row and draws the backdrop. It never pauses and never closes: Escape on the
/// root screen would otherwise leave an empty title.
/// </summary>
[GlobalClass]
public partial class MainMenu : Node
{
    /// <summary>The menu, with the main screen as its root.</summary>
    [Export]
    public MenuSystem Menu { get; set; }

    /// <summary>Drawn behind the menu, over the 3D window: the name.</summary>
    [Export]
    public Control Backdrop { get; set; }

    /// <summary>Where "start" goes: the player's ship.</summary>
    [Export(PropertyHint.File, "*.tscn")]
    public string StartScene { get; set; } = "res://scenes/levels/lvl_ship_hub.tscn";

    private const string StartId = "start";

    public override void _Ready()
    {
        if (Backdrop != null)
        {
            Backdrop.Draw += DrawBackdrop;
        }

        if (Menu == null)
        {
            GD.PushError($"MainMenu '{Name}' has no MenuSystem.");
            return;
        }

        Menu.PauseTree = false;
        Menu.OpenOnCancel = false;
        Menu.ScrimColor = new Color(0, 0, 0, 0);
        Menu.CustomActivated += OnCustom;
        Menu.QuitRequested += OnQuit;

        // Nothing to go back to from the title: reopen if Escape closed it.
        Menu.Closed += () => Callable.From(Menu.Open).CallDeferred();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Callable.From(Menu.Open).CallDeferred();
    }

    private void OnQuit()
    {
        GD.Print("[menu] quit");
        GetTree().Quit();
    }

    private void OnCustom(string id)
    {
        if (id == StartId)
        {
            GetTree().ChangeSceneToFile(StartScene);
        }
    }

    /// <summary>The name, in the display face, over the window onto the planet. The ground is the 3D scene behind.</summary>
    private void DrawBackdrop()
    {
        Vector2 size = Backdrop.Size;
        PickleStyle.TextCentred(Backdrop, new Vector2(size.X * 0.5f, size.Y * 0.16f), "DILL CONTROL", 128, PickleStyle.Brine, -0.04f);
    }
}
