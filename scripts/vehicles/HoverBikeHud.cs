using Godot;
using DillControl.UI;

namespace DillControl.Vehicles;

/// <summary>
/// Everything the hoverbike draws on screen: the boarding prompt and the
/// instrument panel that stays up for as long as the bike is piloted.
///
/// A plain <see cref="Node"/> that <see cref="HoverBikeSystems"/> builds and
/// <b>pushes</b> state into. It never reads the rig, the player or the level,
/// which is the rule the camera rig and the weapons already follow: locomotion
/// is pushed into them each frame and they never pull it.
///
/// Split out of <see cref="HoverBikeSystems"/> so that file stays about
/// systems. It also keeps the portable module clean -- Vehicles.FlightRig must
/// not own a CanvasLayer, because a host project's HUD is exactly the half it
/// is supposed to bring itself.
///
/// DECISION -- this is a permanent instrument panel, not a tutorial toast. The
/// bike was reported as confusing and too fast in the same breath, and the
/// confusing half was literal: no key confirmed itself and no number said how
/// fast you were going. A legend that fades is a legend you must already have
/// read, so this one is visible the whole time the bike is piloted, the way a
/// crosshair is.
/// </summary>
public partial class HoverBikeHud : Node
{
    private static readonly Color Bright = new(0.95f, 0.96f, 0.9f);
    private static readonly Color Dim = new(0.8f, 0.83f, 0.78f);
    private static readonly Color Warn = new(1.0f, 0.78f, 0.4f);

    private CanvasLayer _promptLayer;
    private Label _promptLabel;

    private Panel _flightPanel;
    private Label _flightLegend;
    private Label _speedLabel;
    private Label _modeLabel;
    private Label _altitudeLabel;
    private Label _attitudeLabel;
    private Label _weaponLabel;

    private ColorRect _hitFlash;
    private float _hitFlashAlpha;

    public override void _Ready()
    {
        BuildPrompt();
        BuildFlightHud();
        BuildHitFlash();
    }

    /// <summary>
    /// A red edge over the whole screen when the bike is hit. The shove and
    /// the shake say something hit you; this says it was a hit and not a
    /// gust, which a pilot needs to know in order to go looking for the tower.
    /// </summary>
    private void BuildHitFlash()
    {
        CanvasLayer layer = new() { Name = "HitFlash", Layer = 89 };
        AddChild(layer);

        _hitFlash = new ColorRect
        {
            Name = "Flash",
            Color = new Color(0.85f, 0.15f, 0.08f, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _hitFlash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_hitFlash);
    }

    public void FlashHit(float strength)
    {
        if (_hitFlash == null)
        {
            return;
        }

        _hitFlashAlpha = Mathf.Min(0.35f, _hitFlashAlpha + (strength * 0.6f));
        _hitFlash.Visible = true;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_hitFlash == null || _hitFlashAlpha <= 0.0f)
        {
            SetProcess(false);
            return;
        }

        _hitFlashAlpha = Mathf.Max(0.0f, _hitFlashAlpha - ((float)delta * 1.4f));
        _hitFlash.Color = new Color(_hitFlash.Color, _hitFlashAlpha);
        _hitFlash.Visible = _hitFlashAlpha > 0.0f;
    }

    // ---------------- Prompt ----------------

    /// <summary>
    /// A ship is the one interactable that has to say something *before* it has
    /// a pilot to tell: "board" must appear the moment you are in range, not
    /// after you already pressed a key nothing told you about. Owned here
    /// rather than routed through the player's Hud, because that cannot exist
    /// until a player does and the prompt has to work on an empty level.
    /// </summary>
    private void BuildPrompt()
    {
        _promptLayer = new CanvasLayer { Name = "BoardPrompt", Layer = 90 };
        AddChild(_promptLayer);

        _promptLabel = new Label
        {
            Name = "Prompt",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _promptLabel.AddThemeFontSizeOverride("font_size", 22);
        _promptLabel.AddThemeColorOverride("font_color", new Color(0.94f, 0.95f, 0.9f));
        _promptLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _promptLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        _promptLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        _promptLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _promptLabel.OffsetLeft = -220;
        _promptLabel.OffsetRight = 220;
        _promptLabel.OffsetTop = -170;
        _promptLabel.OffsetBottom = -130;
        _promptLayer.AddChild(_promptLabel);

        _promptLabel.Visible = false;
    }

    /// <summary>Shown while the player is close enough to board, or aboard.</summary>
    public void SetPrompt(bool piloted, bool inRange, string displayName)
    {
        if (_promptLabel == null)
        {
            return;
        }

        string name = string.IsNullOrEmpty(displayName) ? "ship" : displayName;

        if (piloted)
        {
            _promptLabel.Text = "F  leave " + name;
            _promptLabel.Visible = true;
        }
        else if (inRange)
        {
            _promptLabel.Text = "F  board " + name;
            _promptLabel.Visible = true;
        }
        else
        {
            _promptLabel.Visible = false;
        }
    }

    // ---------------- Flight panel ----------------

    private void BuildFlightHud()
    {
        CanvasLayer layer = new() { Name = "FlightHud", Layer = 90 };
        AddChild(layer);

        // A skewed comic plate with a thick ink border, the HUD's own panel
        // shape, in place of a dark rectangle that read as a debug box.
        StyleBoxFlat plate = new()
        {
            BgColor = PickleStyle.BrineDeep with { A = 0.92f },
            BorderColor = PickleStyle.Ink,
            Skew = new Vector2(-0.06f, 0.0f),
            ShadowColor = PickleStyle.Ink with { A = 0.5f },
            ShadowOffset = new Vector2(5, 5),
        };
        plate.SetBorderWidthAll(4);
        _flightPanel = new Panel
        {
            Name = "Panel",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _flightPanel.AddThemeStyleboxOverride("panel", plate);
        _flightPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _flightPanel.Position = new Vector2(16, 16);
        _flightPanel.Size = new Vector2(272, 150);
        layer.AddChild(_flightPanel);

        _speedLabel = MakeLabel("Speed", 20, Bright, new Vector2(28, 18));
        _modeLabel = MakeLabel("Mode", 14, Dim, new Vector2(28, 46));
        _altitudeLabel = MakeLabel("Altitude", 14, Dim, new Vector2(28, 68));
        _attitudeLabel = MakeLabel("Attitude", 14, Dim, new Vector2(28, 90));
        _weaponLabel = MakeLabel("Weapon", 14, Bright, new Vector2(28, 114));

        Label legend = new()
        {
            Name = "Legend",
            Text = "WASD  thrust        Space / Ctrl  up-down\n"
                 + "Shift  boost         Q / E  roll\n"
                 + "mouse  look          LMB  fire cannon\n"
                 + "H  hover / cruise mode",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        legend.AddThemeFontSizeOverride("font_size", 14);
        legend.AddThemeColorOverride("font_color", Dim);
        legend.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        legend.AddThemeConstantOverride("shadow_offset_x", 1);
        legend.AddThemeConstantOverride("shadow_offset_y", 1);
        legend.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        legend.OffsetLeft = 16;
        legend.OffsetBottom = -16;
        legend.OffsetTop = -110;
        legend.OffsetRight = 460;
        layer.AddChild(legend);
        _flightLegend = legend;

        SetFlightVisible(false);
    }

    /// <summary>Sets the legend text, so the systems layer can list the weapon
    /// keys it actually bound rather than this file guessing at them.</summary>
    public void SetLegend(string text)
    {
        if (_flightLegend != null)
        {
            _flightLegend.Text = text;
        }
    }

    /// <summary>Which LMB slot is live and what it has left. Pushed on a
    /// slot change and whenever the count moves, never polled.</summary>
    public void SetWeapon(string text)
    {
        if (_weaponLabel != null)
        {
            _weaponLabel.Text = text;
        }
    }

    private Label MakeLabel(string name, int size, Color colour, Vector2 position)
    {
        Label label = new()
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        label.AddThemeColorOverride("font_outline_color", PickleStyle.Ink);
        label.AddThemeConstantOverride("outline_size", PickleStyle.OutlineFor(size));
        label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        label.Position = position;
        _flightPanel.AddChild(label);
        return label;
    }

    /// <summary>
    /// Toggled directly on board and disembark rather than on the next physics
    /// tick, so the panel appears and disappears in the same frame the camera
    /// switches.
    /// </summary>
    public void SetFlightVisible(bool visible)
    {
        if (_flightPanel != null)
        {
            _flightPanel.Visible = visible;
        }

        if (_flightLegend != null)
        {
            _flightLegend.Visible = visible;
        }
    }

    /// <summary>
    /// One push per physics frame while piloted. <paramref name="groundDistance"/>
    /// is negative when nothing is within the rig's probe range.
    /// </summary>
    public void SetFlightState(
        float speed,
        bool boosting,
        FlightMode mode,
        bool holding,
        float groundDistance,
        float pitchDegrees,
        float rollDegrees)
    {
        if (_speedLabel == null)
        {
            return;
        }

        // Each write only on a change: assigning a Label's text reshapes it,
        // and a theme override invalidates its theme, and this runs every
        // physics frame for four labels whose readings change a few times a
        // second.
        Show(_speedLabel, boosting ? $"{speed:0} m/s   BOOST" : $"{speed:0} m/s", boosting ? Warn : Bright);

        if (_modeLabel != null)
        {
            Show(_modeLabel, mode == FlightMode.Cruise
                ? "CRUISE  (H to hover)"
                : holding
                    ? "HOVER  holding  (H for cruise)"
                    : "HOVER  (H for cruise)");
        }

        if (_altitudeLabel != null)
        {
            bool known = groundDistance >= 0.0f;
            Show(_altitudeLabel, known ? $"ALT  {groundDistance:0} m" : "ALT  ---",
                known && groundDistance < 15.0f ? Warn : Dim);
        }

        if (_attitudeLabel != null)
        {
            Show(_attitudeLabel, $"PITCH {pitchDegrees:+0;-0;0}°   ROLL {rollDegrees:+0;-0;0}°");
        }
    }

    private readonly System.Collections.Generic.Dictionary<Label, Color> _shownColour = new();
    private readonly System.Collections.Generic.Dictionary<Label, string> _shownText = new();

    private void Show(Label label, string text, Color? colour = null)
    {
        // Compared against what was last written rather than read back:
        // Label.Text is itself a call into the engine and a fresh string.
        if (!_shownText.TryGetValue(label, out string shownText) || shownText != text)
        {
            _shownText[label] = text;
            label.Text = text;
        }

        if (colour is Color c && (!_shownColour.TryGetValue(label, out Color shown) || shown != c))
        {
            _shownColour[label] = c;
            label.AddThemeColorOverride("font_color", c);
        }
    }
}
