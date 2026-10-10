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

    /// <summary>Drawn behind the menu: the ground, the name, the premise.</summary>
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

        // Nothing to go back to from the title: reopen if Escape closed it.
        Menu.Closed += () => Callable.From(Menu.Open).CallDeferred();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Callable.From(Menu.Open).CallDeferred();
    }

    private void OnCustom(string id)
    {
        if (id == StartId)
        {
            GetTree().ChangeSceneToFile(StartScene);
        }
    }

    /// <summary>A brine-green ground with inked speed lines, the name in the display face, and the premise.</summary>
    private void DrawBackdrop()
    {
        Vector2 size = Backdrop.Size;
        Backdrop.DrawRect(new Rect2(Vector2.Zero, size), PickleStyle.Ink.Lerp(PickleStyle.BrineDeep, 0.55f));
        for (int i = 0; i < 9; i++)
        {
            float x = (size.X * (i / 8.0f)) - 200.0f;
            Vector2[] stripe =
            {
                new(x, 0), new(x + 90, 0), new(x - 160, size.Y), new(x - 250, size.Y),
            };
            Backdrop.DrawColoredPolygon(stripe, PickleStyle.Ink with { A = 0.12f });
        }

        PickleStyle.TextCentred(Backdrop, new Vector2(size.X * 0.5f, size.Y * 0.16f), "DILL CONTROL", 128, PickleStyle.Brine, -0.04f);
    }
}
