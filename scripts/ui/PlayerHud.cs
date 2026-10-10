using Godot;
using DillControl.Core;
using DillControl.Player;
using DillControl.Weapons;

namespace DillControl.UI;

/// <summary>
/// The working HUD. Everything a fight needs to read, drawn as flat shapes and
/// the engine's fallback font:
///
///   centre        crosshair (closes and turns green while the line would
///                 land, with a ring and HOOK on the surface; a ring while
///                 attached), and a hitmarker on every hit
///   edges         a red flash when hurt, and a wedge pointing at what hurt you
///   top centre    kills, the live combo with its fuse, and the tier word
///   bottom centre gas (a notch per dash charge) and health, PICKLED! at empty
///   bottom right  weapon name, ammo, and the slot strip
///
/// PLACEHOLDER. Layout and information are meant to survive; the look is not.
/// The lab's HUD (the sloshing pickle jar, the styled tally) comes across with
/// real art in M7. Drawn with immediate-mode calls rather than built from
/// Controls: a handful of shapes that change every frame, and one CanvasItem
/// redrawn is cheaper than a tree of nodes re-laid-out.
///
/// It hears hits and ammo on the bus, because the things that deal damage and
/// count rounds cannot name a HUD. Everything else it reads off the player it
/// is wired to.
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
    public Color HurtColour { get; set; } = new(0.9f, 0.12f, 0.08f, 1.0f);

    [Export]
    public Color WeakPointColour { get; set; } = new(1.0f, 0.85f, 0.2f, 1.0f);

    [Export]
    public Color BackingColour { get; set; } = new(0.0f, 0.0f, 0.0f, 0.45f);

    [ExportGroup("Kill Tally")]
    /// <summary>Seconds a kill keeps the combo alive. The lab's number.</summary>
    [Export(PropertyHint.Range, "0.2,10,0.1,suffix:s")]
    public float ComboWindow { get; set; } = 2.5f;

    /// <summary>Seconds the hitmarker stays up after a hit.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01,suffix:s")]
    public float HitmarkerSeconds { get; set; } = 0.18f;

    /// <summary>Combo thresholds and the word each one earns. The lab's ladder.</summary>
    private static readonly (int At, string Word)[] Tiers =
    {
        (5, "KILLING SPREE"),
        (15, "RAMPAGE"),
        (40, "MASSACRE"),
        (100, "UNSTOPPABLE"),
        (250, "EXTERMINATION"),
        (600, "ANNIHILATION"),
        (1500, "EXTINCTION EVENT"),
    };

    private int _inMagazine = -1;
    private int _magazineSize;
    private int _reserve;

    private int _kills;
    private int _combo;
    private int _bestCombo;
    private float _sinceKill = float.MaxValue;

    private float _hitmarker;
    private Color _hitmarkerColour;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        if (EventBus.Instance != null)
        {
            EventBus.Instance.AmmoChanged += OnAmmoChanged;
            EventBus.Instance.DamageDealt += OnDamageDealt;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.AmmoChanged -= OnAmmoChanged;
            EventBus.Instance.DamageDealt -= OnDamageDealt;
        }
    }

    private void OnAmmoChanged(int inMagazine, int magazineSize, int reserve)
    {
        _inMagazine = inMagazine;
        _magazineSize = magazineSize;
        _reserve = reserve;
    }

    /// <summary>
    /// Every hit anywhere lands here. The player is the only thing that hurts
    /// pickles, so every hit and every kill on the bus is the player's. A
    /// blast into a crowd arrives as one report carrying its kill count.
    /// </summary>
    private void OnDamageDealt(Node3D target, DamageInfo info)
    {
        if (info == null || info.IsSustained)
        {
            return;
        }

        int kills = info.KillCount > 0 ? info.KillCount : (info.Killed ? 1 : 0);
        if (kills > 0)
        {
            _kills += kills;
            _combo = _sinceKill <= ComboWindow ? _combo + kills : kills;
            _bestCombo = Mathf.Max(_bestCombo, _combo);
            _sinceKill = 0.0f;
        }

        _hitmarker = HitmarkerSeconds;
        _hitmarkerColour = kills > 0 ? HurtColour : info.IsWeakPoint ? WeakPointColour : CrosshairColour;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _hitmarker = Mathf.Max(0.0f, _hitmarker - dt);
        if (_sinceKill < float.MaxValue)
        {
            _sinceKill += dt;
        }
        if (_sinceKill > ComboWindow)
        {
            _combo = 0;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Player == null)
        {
            return;
        }

        DrawHurt();
        if (!Player.Armed)
        {
            DrawHealth();
            return;
        }

        DrawCrosshair();
        DrawGrapplePrompt();
        DrawHitmarker();
        DrawTally();
        DrawGas();
        DrawHealth();
        DrawWeapon();
    }

    // ---------------- Centre ----------------

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

    /// <summary>
    /// A word under the crosshair. Green HOOK where the line can hold, white
    /// TOO FAR past the throw, red NO HOLD on something too light to. Nothing
    /// on empty air.
    /// </summary>
    private void DrawGrapplePrompt()
    {
        GrappleHook line = Player.Grapple;
        if (line == null || line.Aim == GrappleAim.None)
        {
            return;
        }

        Color colour = line.Aim switch
        {
            GrappleAim.Attach => GrappleReadyColour,
            GrappleAim.NoHold => HurtColour,
            _ => CrosshairColour,
        };
        string word = line.Aim switch
        {
            GrappleAim.Attach => "HOOK",
            GrappleAim.NoHold => "NO HOLD",
            _ => "TOO FAR",
        };

        Vector2 label = (Size * 0.5f) + new Vector2(0.0f, 32.0f);
        DrawShadowed(ThemeDB.FallbackFont, label, word, 16, colour, HorizontalAlignment.Center);
    }

    /// <summary>Four diagonal strokes round the crosshair: white a hit, yellow a weak point, red a kill.</summary>
    private void DrawHitmarker()
    {
        if (_hitmarker <= 0.0f)
        {
            return;
        }

        Vector2 centre = Size * 0.5f;
        float t = _hitmarker / HitmarkerSeconds;
        Color colour = _hitmarkerColour with { A = t };
        float inner = 8.0f + (4.0f * (1.0f - t));
        const float Length = 8.0f;
        for (int i = 0; i < 4; i++)
        {
            Vector2 direction = Vector2.FromAngle((Mathf.Pi * 0.25f) + (i * Mathf.Pi * 0.5f));
            DrawLine(centre + (direction * inner), centre + (direction * (inner + Length)), colour, 3.0f);
        }
    }

    // ---------------- Edges ----------------

    /// <summary>
    /// A red wash on the screen edges while hurt, and a wedge at the edge of a
    /// ring round the crosshair for each recent hit, pointing where it came
    /// from. In a crowd that surrounds you the wash says you are being hurt;
    /// only a direction says by what.
    /// </summary>
    private void DrawHurt()
    {
        float flash = Player.HitFlash;
        if (flash > 0.0f)
        {
            float band = Mathf.Min(Size.X, Size.Y) * 0.08f;
            Color wash = HurtColour with { A = flash };
            DrawRect(new Rect2(0.0f, 0.0f, Size.X, band), wash);
            DrawRect(new Rect2(0.0f, Size.Y - band, Size.X, band), wash);
            DrawRect(new Rect2(0.0f, 0.0f, band, Size.Y), wash);
            DrawRect(new Rect2(Size.X - band, 0.0f, band, Size.Y), wash);
        }

        Camera3D camera = Player.CameraRig?.ActiveCamera;
        if (camera == null)
        {
            return;
        }

        Vector2 centre = Size * 0.5f;
        float radius = Mathf.Min(Size.X, Size.Y) * 0.22f;
        Vector3 up = Player.GravityUp;
        Vector3 forward = (-camera.GlobalBasis.Z).Slide(up).Normalized();
        Vector3 right = camera.GlobalBasis.X.Slide(up).Normalized();

        for (int i = 0; i < PlayerController.HitMarkCount; i++)
        {
            if (!Player.TryGetHitMark(i, out Vector3 from, out float age))
            {
                continue;
            }

            Vector3 toward = (from - Player.GlobalPosition).Slide(up);
            if (toward.LengthSquared() < 0.01f)
            {
                continue;
            }

            // Screen angle: straight ahead is up the screen.
            float angle = Mathf.Atan2(toward.Dot(right), toward.Dot(forward));
            Vector2 direction = new(Mathf.Sin(angle), -Mathf.Cos(angle));
            Vector2 side = new(-direction.Y, direction.X);
            Vector2 tip = centre + (direction * (radius + 22.0f));
            Vector2 baseCentre = centre + (direction * radius);
            Color colour = HurtColour with { A = 0.85f * (1.0f - age) };
            DrawColoredPolygon(new[] { tip, baseCentre + (side * 16.0f), baseCentre - (side * 16.0f) }, colour);
        }
    }

    // ---------------- Top centre ----------------

    /// <summary>
    /// The kill count, and under it the live combo: its count, its tier word
    /// and a fuse that burns down over the combo window.
    /// </summary>
    private void DrawTally()
    {
        Font font = ThemeDB.FallbackFont;
        float x = Size.X * 0.5f;

        DrawShadowed(font, new Vector2(x, 48.0f), $"{_kills} PICKLED", 30, CrosshairColour, HorizontalAlignment.Center);

        if (_combo < 2)
        {
            return;
        }

        string tier = TierWord(_combo);
        string line = tier != null ? $"x{_combo}  {tier}" : $"x{_combo}";
        DrawShadowed(font, new Vector2(x, 84.0f), line, 24, WeakPointColour, HorizontalAlignment.Center);

        float fuse = 1.0f - Mathf.Clamp(_sinceKill / ComboWindow, 0.0f, 1.0f);
        Rect2 bar = new(new Vector2(x - 80.0f, 94.0f), new Vector2(160.0f, 4.0f));
        DrawRect(bar, BackingColour);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fuse, bar.Size.Y)), WeakPointColour);
    }

    private static string TierWord(int combo)
    {
        string word = null;
        foreach ((int at, string tierWord) in Tiers)
        {
            if (combo >= at)
            {
                word = tierWord;
            }
        }
        return word;
    }

    // ---------------- Bottom centre ----------------

    /// <summary>The tank as one bar, with a notch per dash charge it holds.</summary>
    private void DrawGas()
    {
        Rect2 bar = new(new Vector2((Size.X * 0.5f) - 110.0f, Size.Y - 58.0f), new Vector2(220.0f, 8.0f));
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

        Rect2 bar = new(new Vector2((Size.X * 0.5f) - 110.0f, Size.Y - 42.0f), new Vector2(220.0f, 12.0f));
        DrawRect(bar.Grow(2.0f), BackingColour);
        Color colour = health.IsPickled ? HealthColour.Darkened(0.5f) : HealthColour;
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * health.Fraction, bar.Size.Y)), colour);

        if (health.IsPickled)
        {
            DrawShadowed(ThemeDB.FallbackFont, new Vector2(Size.X * 0.5f, Size.Y - 72.0f), "PICKLED!", 34,
                         HurtColour, HorizontalAlignment.Center);
        }
    }

    // ---------------- Bottom right ----------------

    /// <summary>
    /// What is in hand: its name, its ammo (magazine over size, then reserve;
    /// nothing for the machete), and the slot strip with the active one lit.
    /// </summary>
    private void DrawWeapon()
    {
        WeaponLoadout loadout = Player.Loadout;
        if (loadout == null)
        {
            return;
        }

        Font font = ThemeDB.FallbackFont;
        float right = Size.X - 40.0f;
        WeaponBase active = loadout.Active;

        if (active != null)
        {
            DrawShadowed(font, new Vector2(right, Size.Y - 96.0f), active.DisplayName.ToUpperInvariant(), 20,
                         CrosshairColour, HorizontalAlignment.Right);
        }

        if (active is { UsesAmmo: true } && _inMagazine >= 0)
        {
            string ammo = active.IsReloading
                ? "RELOADING"
                : $"{_inMagazine} / {_magazineSize}   {_reserve}";
            DrawShadowed(font, new Vector2(right, Size.Y - 62.0f), ammo, 30, CrosshairColour, HorizontalAlignment.Right);
        }

        // One numbered box per slot, right to left from the last.
        const float Box = 26.0f;
        int count = loadout.Slots.Count;
        for (int i = 0; i < count; i++)
        {
            Vector2 at = new(right - ((count - i) * (Box + 6.0f)) + 6.0f, Size.Y - 38.0f);
            bool lit = i == loadout.ActiveSlot && !loadout.IsSwitching;
            DrawRect(new Rect2(at, new Vector2(Box, Box)), lit ? GasColour : BackingColour);
            DrawString(font, at + new Vector2(0.0f, 19.0f), (i + 1).ToString(), HorizontalAlignment.Center, Box, 16,
                       lit ? Colors.Black : CrosshairColour);
        }
    }

    /// <summary>
    /// A line of text with a drop shadow, anchored at <paramref name="at"/>'s
    /// baseline: centred on it, or ending at it for right alignment.
    /// </summary>
    private void DrawShadowed(Font font, Vector2 at, string text, int size, Color colour, HorizontalAlignment align)
    {
        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        Vector2 position = align switch
        {
            HorizontalAlignment.Center => at - new Vector2(width * 0.5f, 0.0f),
            HorizontalAlignment.Right => at - new Vector2(width, 0.0f),
            _ => at,
        };

        DrawString(font, position + new Vector2(2.0f, 2.0f), text, HorizontalAlignment.Left, -1, size,
                   BackingColour with { A = colour.A * 0.8f });
        DrawString(font, position, text, HorizontalAlignment.Left, -1, size, colour);
    }
}
