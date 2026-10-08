using Godot;
using DillControl.Core;
using DillControl.Player;

namespace DillControl.UI;

/// <summary>
/// The working HUD: a crosshair that says whether the line would land, the
/// gas charges, the health jar as a bar, and the ammo in hand.
///
/// PLACEHOLDER. The lab's HUD (the sloshing pickle jar, the kill tally, the
/// tier words) comes across with real art in M7. This one exists so the line
/// can be tuned in M1: a throw that silently falls short is a mechanic nobody
/// trusts, and a throw the crosshair said would land is one they will try at
/// speed.
///
/// Drawn with immediate-mode calls rather than built from Controls. It is a
/// handful of shapes that change every frame, and one CanvasItem redrawn is
/// cheaper than a tree of nodes re-laid-out.
/// </summary>
public partial class PlayerHud : Control
{
    [ExportGroup("Scene Wiring")]
    [Export]
    public PlayerController Player { get; set; }

    [ExportGroup("Appearance")]
    [Export]
    public Color CrosshairColour { get; set; } = new(1.0f, 1.0f, 1.0f, 0.85f);

    /// <summary>The crosshair's colour while the line would reach what it is over.</summary>
    [Export]
    public Color GrappleReadyColour { get; set; } = new(0.55f, 0.95f, 0.35f, 0.95f);

    [Export]
    public Color HealthColour { get; set; } = new(0.42f, 0.78f, 0.30f, 0.9f);

    [Export]
    public Color GasColour { get; set; } = new(0.95f, 0.72f, 0.25f, 0.9f);

    [Export]
    public Color BackingColour { get; set; } = new(0.0f, 0.0f, 0.0f, 0.45f);

    private int _inMagazine = -1;
    private int _magazineSize;
    private int _reserve;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        if (EventBus.Instance != null)
        {
            EventBus.Instance.AmmoChanged += OnAmmoChanged;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.AmmoChanged -= OnAmmoChanged;
        }
    }

    private void OnAmmoChanged(int inMagazine, int magazineSize, int reserve)
    {
        _inMagazine = inMagazine;
        _magazineSize = magazineSize;
        _reserve = reserve;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Player == null)
        {
            return;
        }

        DrawCrosshair();
        DrawGas();
        DrawHealth();
        DrawAmmo();
    }

    /// <summary>Magazine over reserve, bottom right. Hidden for anything with nothing to count.</summary>
    private void DrawAmmo()
    {
        if (_inMagazine < 0 || Player.Loadout?.Active is not { UsesAmmo: true })
        {
            return;
        }

        Font font = ThemeDB.FallbackFont;
        string text = $"{_inMagazine} / {_magazineSize}   {_reserve}";
        Vector2 position = new(Size.X - 260.0f, Size.Y - 40.0f);
        DrawString(font, position + new Vector2(2.0f, 2.0f), text, HorizontalAlignment.Left, -1, 28, BackingColour);
        DrawString(font, position, text, HorizontalAlignment.Left, -1, 28, CrosshairColour);
    }

    /// <summary>
    /// A dot with four ticks. The ticks close in and turn green while the
    /// line would land, and become a ring while it is attached.
    /// </summary>
    private void DrawCrosshair()
    {
        Vector2 centre = Size * 0.5f;
        GrappleHook line = Player.Grapple;
        bool ready = line != null && line.TargetInRange;
        bool attached = line != null && line.IsAttached;

        Color colour = ready || attached ? GrappleReadyColour : CrosshairColour;
        DrawCircle(centre, 2.0f, colour);

        if (attached)
        {
            DrawArc(centre, 9.0f, 0.0f, Mathf.Tau, 24, colour, 2.0f, true);
            return;
        }

        float gap = ready ? 6.0f : 10.0f;
        const float Tick = 6.0f;
        DrawLine(centre + new Vector2(gap, 0.0f), centre + new Vector2(gap + Tick, 0.0f), colour, 2.0f);
        DrawLine(centre - new Vector2(gap, 0.0f), centre - new Vector2(gap + Tick, 0.0f), colour, 2.0f);
        DrawLine(centre + new Vector2(0.0f, gap), centre + new Vector2(0.0f, gap + Tick), colour, 2.0f);
        DrawLine(centre - new Vector2(0.0f, gap), centre - new Vector2(0.0f, gap + Tick), colour, 2.0f);
    }

    /// <summary>The tank as one bar, with a notch per dash charge it holds.</summary>
    private void DrawGas()
    {
        Rect2 bar = new(new Vector2(Size.X * 0.5f - 110.0f, Size.Y - 58.0f), new Vector2(220.0f, 8.0f));
        DrawRect(bar.Grow(2.0f), BackingColour);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Player.FlightFuel01, bar.Size.Y)), GasColour);

        int charges = Player.DashChargesMax;
        for (int i = 1; i < charges; i++)
        {
            float x = bar.Position.X + (bar.Size.X * i / charges);
            DrawLine(new Vector2(x, bar.Position.Y), new Vector2(x, bar.End.Y), BackingColour, 2.0f);
        }
    }

    private void DrawHealth()
    {
        PlayerHealth health = Player.Health;
        if (health == null || !health.Enabled)
        {
            return;
        }

        Rect2 bar = new(new Vector2(Size.X * 0.5f - 110.0f, Size.Y - 42.0f), new Vector2(220.0f, 12.0f));
        DrawRect(bar.Grow(2.0f), BackingColour);
        Color colour = health.IsPickled ? HealthColour.Darkened(0.5f) : HealthColour;
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * health.Fraction, bar.Size.Y)), colour);
    }
}
