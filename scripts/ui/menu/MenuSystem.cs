using System.Collections.Generic;
using Godot;

namespace ModularMenu;

/// <summary>
/// A self-contained pause-menu stack. Drop the folder into any Godot 4 C#
/// project, add this node, give it a root <see cref="MenuScreen"/>, and it
/// works.
///
/// The whole point of this class is that it knows nothing about the game it is
/// in. It never names a player, a level, a weapon or a settings singleton. It
/// pauses the tree, releases the mouse, draws a list, and raises signals; the
/// host connects those signals and does the game-specific half. That is the
/// line that makes it portable, and it is worth holding even where reaching
/// into the host directly would be two lines shorter.
///
/// Input is the engine's own built-in ui_* actions -- ui_up, ui_down, ui_left,
/// ui_right, ui_accept, ui_cancel -- because those exist in a blank project.
/// Binding game-specific actions here would mean every host had to add them
/// before this node would respond to anything.
///
/// The UI is built in code rather than loaded from a .tscn so that dropping
/// this in is copying one folder, with no scene to re-path and no broken
/// ExtResource ids.
/// </summary>
[GlobalClass]
public partial class MenuSystem : CanvasLayer
{
    [ExportGroup("Content")]
    [Export]
    public MenuScreen Root { get; set; }

    [ExportGroup("Behaviour")]
    /// <summary>Pauses the scene tree while the menu is open.</summary>
    [Export]
    public bool PauseTree { get; set; } = true;

    /// <summary>
    /// Releases the mouse on open and recaptures it on close. Off for a game
    /// that does not capture the mouse in the first place.
    /// </summary>
    [Export]
    public bool ManageMouse { get; set; } = true;

    /// <summary>Opens the menu when nothing else consumed ui_cancel.</summary>
    [Export]
    public bool OpenOnCancel { get; set; } = true;

    [ExportGroup("Look")]
    [Export]
    public Color ScrimColor { get; set; } = new(0.03f, 0.04f, 0.06f, 0.82f);

    /// <summary>
    /// Optional box drawn behind the title and the rows. Null, the default,
    /// is the bare list on the scrim, as before. A host gives the menu its own
    /// look this way without the module learning anything about the host.
    /// </summary>
    [Export]
    public StyleBox PanelStyle { get; set; }

    [Export]
    public Color TitleColor { get; set; } = new(0.95f, 0.96f, 1.0f);

    [Export]
    public Color ItemColor { get; set; } = new(0.66f, 0.70f, 0.78f);

    [Export]
    public Color SelectedColor { get; set; } = new(1.0f, 0.84f, 0.42f);

    [Export]
    public Color ValueColor { get; set; } = new(0.58f, 0.74f, 0.92f);

    [Export(PropertyHint.Range, "12,64,1")]
    public int TitleSize { get; set; } = 34;

    [Export(PropertyHint.Range, "10,48,1")]
    public int ItemSize { get; set; } = 20;

    // ---------------- Signals the host connects ----------------

    [Signal]
    public delegate void OpenedEventHandler();

    [Signal]
    public delegate void ClosedEventHandler();

    /// <summary>A Custom entry was activated. The argument is its Id.</summary>
    /// <summary>
    /// The highlighted entry changed. Raised for a host project to hang a
    /// navigation sound off; this module deliberately plays nothing itself,
    /// because a portable menu that owns audio brings an audio system with it.
    /// </summary>
    [Signal]
    public delegate void SelectionMovedEventHandler();

    /// <summary>The highlighted entry was activated. Same reason as above.</summary>
    [Signal]
    public delegate void EntryActivatedEventHandler();

    [Signal]
    public delegate void CustomActivatedEventHandler(string id);

    /// <summary>
    /// A Toggle or Slider changed. The host applies it; this class only stores
    /// and displays. Value is a bool for Toggle and a float for Slider.
    /// </summary>
    [Signal]
    public delegate void SettingChangedEventHandler(string id, Variant value);

    /// <summary>
    /// Raised instead of quitting directly, so a host that needs to save or
    /// confirm first can. Unconnected, this class quits by itself.
    /// </summary>
    [Signal]
    public delegate void QuitRequestedEventHandler();

    /// <summary>An action's binding was replaced by a Rebind row. The argument is the action.</summary>
    [Signal]
    public delegate void RebindChangedEventHandler(string action);

    /// <summary>The Rebind row waiting for a key, or null.</summary>
    private MenuEntry _capturing;

    public bool IsOpen { get; private set; }

    // ---------------- State ----------------

    private readonly List<MenuScreen> _stack = new();
    private readonly Dictionary<string, Variant> _settings = new();
    private readonly List<Label> _rows = new();

    private Control _scrim;
    private VBoxContainer _column;
    private Label _titleLabel;
    private Label _hintLabel;
    private int _selected;

    private Input.MouseModeEnum _mouseModeBeforeOpen = Input.MouseModeEnum.Captured;

    public override void _Ready()
    {
        // Runs while the tree is paused -- otherwise pausing would freeze the
        // menu that did the pausing.
        ProcessMode = ProcessModeEnum.Always;
        Layer = 128;

        BuildUi();
        SetShown(false);
    }

    // ---------------- Host-facing API ----------------

    /// <summary>
    /// Seeds a setting's current value without raising SettingChanged. The host
    /// calls this once at startup so the menu shows what the game is actually
    /// doing rather than a default it invented.
    /// </summary>
    public void DefineSetting(string id, Variant value)
    {
        if (!string.IsNullOrEmpty(id))
        {
            _settings[id] = value;
        }
    }

    public Variant GetSetting(string id) =>
        _settings.TryGetValue(id, out Variant value) ? value : default;

    public void Open()
    {
        if (IsOpen || Root == null)
        {
            return;
        }

        IsOpen = true;
        _stack.Clear();
        _stack.Add(Root);
        _selected = FirstSelectable(Root);

        if (ManageMouse)
        {
            _mouseModeBeforeOpen = Input.MouseMode;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        if (PauseTree)
        {
            GetTree().Paused = true;
        }

        SetShown(true);
        Rebuild();
        EmitSignal(SignalName.Opened);
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;

        if (PauseTree)
        {
            GetTree().Paused = false;
        }

        if (ManageMouse)
        {
            Input.MouseMode = _mouseModeBeforeOpen;
        }

        SetShown(false);
        EmitSignal(SignalName.Closed);
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void SetShown(bool shown)
    {
        if (_scrim != null)
        {
            _scrim.Visible = shown;
        }
    }

    // ---------------- Input ----------------

    /// <summary>
    /// _Input rather than _UnhandledInput: while the menu is open it takes
    /// every navigation key before gameplay sees it, which is what stops the
    /// player turning or firing behind an open menu in a game that does not
    /// pause.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!IsOpen)
        {
            if (OpenOnCancel && @event.IsActionPressed("ui_cancel"))
            {
                Open();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_capturing != null)
        {
            Capture(@event);
            GetViewport().SetInputAsHandled();
            return;
        }

        // East face button (B / Circle) goes back only while the menu is open.
        // It is not ui_cancel, so the same button can be crouch in play
        // without opening this menu. Start and Escape are ui_cancel.
        if (@event.IsActionPressed("ui_cancel") || EastButton(@event))
        {
            GoBack();
        }
        else if (@event.IsActionPressed("ui_down"))
        {
            Step(1);
        }
        else if (@event.IsActionPressed("ui_up"))
        {
            Step(-1);
        }
        else if (@event.IsActionPressed("ui_left"))
        {
            Adjust(-1);
        }
        else if (@event.IsActionPressed("ui_right"))
        {
            Adjust(1);
        }
        else if (@event.IsActionPressed("ui_accept"))
        {
            Activate();
            EmitSignal(SignalName.EntryActivated);
        }
        else
        {
            return;
        }

        GetViewport().SetInputAsHandled();
    }

    private MenuScreen Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

    private static int FirstSelectable(MenuScreen screen) =>
        screen != null && screen.Entries != null && screen.Entries.Count > 0 ? 0 : -1;

    private void Step(int direction)
    {
        MenuScreen screen = Current;
        if (screen == null || screen.Entries == null || screen.Entries.Count == 0)
        {
            return;
        }

        _selected = (_selected + direction + screen.Entries.Count) % screen.Entries.Count;
        Paint();
        EmitSignal(SignalName.SelectionMoved);
    }

    private void GoBack()
    {
        if (_stack.Count > 1)
        {
            _stack.RemoveAt(_stack.Count - 1);
            _selected = FirstSelectable(Current);
            Rebuild();
            return;
        }

        // Escape on the root screen closes, which is what everyone expects it
        // to do and saves a Resume press.
        Close();
    }

    private MenuEntry SelectedEntry()
    {
        MenuScreen screen = Current;
        if (screen == null || screen.Entries == null || _selected < 0 || _selected >= screen.Entries.Count)
        {
            return null;
        }
        return screen.Entries[_selected];
    }

    private void Activate()
    {
        MenuEntry entry = SelectedEntry();
        if (entry == null)
        {
            return;
        }

        switch (entry.Action)
        {
            case MenuAction.Resume:
                Close();
                break;

            case MenuAction.OpenScreen:
                if (entry.Target != null)
                {
                    _stack.Add(entry.Target);
                    _selected = FirstSelectable(entry.Target);
                    Rebuild();
                }
                break;

            case MenuAction.Back:
                GoBack();
                break;

            case MenuAction.Rebind:
                if (InputMap.HasAction(entry.Id))
                {
                    _capturing = entry;
                    Paint();
                }
                break;

            case MenuAction.Toggle:
                Adjust(1);
                break;

            case MenuAction.Slider:
                // Accept on a slider does nothing on purpose: it is adjusted
                // with left/right, and having it also jump to an extreme is a
                // good way to lose a setting by pressing Enter to "confirm".
                break;

            case MenuAction.Custom:
                EmitSignal(SignalName.CustomActivated, entry.Id);
                break;

            case MenuAction.Quit:
                // If nothing is listening, quit. A host that connects the
                // signal takes over completely and is expected to quit itself.
                if (GetSignalConnectionList(SignalName.QuitRequested).Count > 0)
                {
                    EmitSignal(SignalName.QuitRequested);
                }
                else
                {
                    GetTree().Quit();
                }
                break;
        }
    }

    private void Adjust(int direction)
    {
        MenuEntry entry = SelectedEntry();
        if (entry == null || !entry.IsAdjustable)
        {
            return;
        }

        if (entry.Action == MenuAction.Toggle)
        {
            bool now = !(bool)GetSetting(entry.Id);
            _settings[entry.Id] = now;
            EmitSignal(SignalName.SettingChanged, entry.Id, now);
        }
        else
        {
            float step = entry.Step > 0.0f ? entry.Step : 0.05f;
            float now = Mathf.Clamp(
                (float)GetSetting(entry.Id) + (step * direction),
                entry.Minimum,
                entry.Maximum);

            // Snapping to the step keeps a value from drifting to 0.30000004
            // after enough presses and showing it to the player.
            now = Mathf.Snapped(now, step);
            _settings[entry.Id] = now;
            EmitSignal(SignalName.SettingChanged, entry.Id, now);
        }

        Paint();
    }

    // ---------------- UI ----------------

    private void BuildUi()
    {
        ColorRect scrim = new()
        {
            Name = "Scrim",
            Color = ScrimColor,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(scrim);
        _scrim = scrim;

        CenterContainer centre = new()
        {
            Name = "Centre",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _scrim.AddChild(centre);

        VBoxContainer frame = new()
        {
            Name = "Frame",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddThemeConstantOverride("separation", 10);

        if (PanelStyle != null)
        {
            PanelContainer panel = new()
            {
                Name = "Panel",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(460, 0),
            };
            panel.AddThemeStyleboxOverride("panel", PanelStyle);
            centre.AddChild(panel);
            panel.AddChild(frame);
        }
        else
        {
            centre.AddChild(frame);
        }

        _titleLabel = new Label
        {
            Name = "Title",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _titleLabel.AddThemeColorOverride("font_color", TitleColor);
        _titleLabel.AddThemeFontSizeOverride("font_size", TitleSize);
        frame.AddChild(_titleLabel);

        frame.AddChild(new Control
        {
            Name = "Gap",
            CustomMinimumSize = new Vector2(0, 14),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _column = new VBoxContainer
        {
            Name = "Items",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _column.AddThemeConstantOverride("separation", 6);
        frame.AddChild(_column);

        frame.AddChild(new Control
        {
            Name = "Gap2",
            CustomMinimumSize = new Vector2(0, 18),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        _hintLabel = new Label
        {
            Name = "Hint",
            Text = "stick or arrows move   A or enter select   B, Start or esc back",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _hintLabel.AddThemeColorOverride("font_color", new Color(0.42f, 0.46f, 0.54f));
        _hintLabel.AddThemeFontSizeOverride("font_size", 13);
        frame.AddChild(_hintLabel);
    }

    /// <summary>
    /// Rebuilds the rows for the current screen. Called on a screen change
    /// only; moving the selection or changing a value repaints instead.
    /// </summary>
    /// <summary>Redraws the current screen, for a host that changed what it shows.</summary>
    public void Rebuild()
    {
        foreach (Label row in _rows)
        {
            row.QueueFree();
        }
        _rows.Clear();

        MenuScreen screen = Current;
        if (screen == null)
        {
            return;
        }

        _titleLabel.Text = screen.Title;

        for (int i = 0; i < screen.Entries.Count; i++)
        {
            Label row = new()
            {
                Name = "Row" + i,
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(360, 0),
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            row.AddThemeFontSizeOverride("font_size", ItemSize);

            // Mouse support costs one lambda per row and makes the menu usable
            // without ever touching the keyboard.
            int index = i;
            row.MouseEntered += () =>
            {
                _selected = index;
                Paint();
            };
            row.GuiInput += @event =>
            {
                if (@event is InputEventMouseButton button
                    && button.Pressed
                    && button.ButtonIndex == MouseButton.Left)
                {
                    _selected = index;
                    Activate();
                }
            };

            _column.AddChild(row);
            _rows.Add(row);
        }

        Paint();
    }

    /// <summary>Writes the text and colour of every row. Allocates no nodes.</summary>
    /// <summary>
    /// The next key, mouse button, pad button or stick axis replaces bindings
    /// of that same kind and leaves the others. A key rebind keeps the pad
    /// binding, and the other way around. Escape or Start cancels. Mouse
    /// motion, releases and a resting stick are ignored. A pad event is stored
    /// for any device, because a pad's index changes when it is replugged.
    /// </summary>
    private void Capture(InputEvent @event)
    {
        bool cancel = @event is InputEventKey { Keycode: Key.Escape }
            or InputEventKey { PhysicalKeycode: Key.Escape }
            or InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.Start };
        bool key = @event is InputEventKey { Pressed: true, Echo: false };
        bool mouse = @event is InputEventMouseButton { Pressed: true };
        bool pad = @event is InputEventJoypadButton { Pressed: true };
        bool stick = @event is InputEventJoypadMotion motion && Mathf.Abs(motion.AxisValue) >= 0.6f;
        if (!cancel && !key && !mouse && !pad && !stick)
        {
            return;
        }

        MenuEntry entry = _capturing;
        _capturing = null;
        if (!cancel)
        {
            InputEvent copy = BindingFrom(@event);
            Godot.Collections.Array<InputEvent> existing = InputMap.ActionGetEvents(entry.Id);
            foreach (InputEvent old in existing)
            {
                if (SameBindingKind(old, copy))
                {
                    InputMap.ActionEraseEvent(entry.Id, old);
                }
            }

            InputMap.ActionAddEvent(entry.Id, copy);
            EmitSignal(SignalName.RebindChanged, entry.Id);
        }
        Paint();
    }

    /// <summary>
    /// A binding worth storing. Stick axes keep their direction as a full
    /// deflection, not the halfway value the stick happened to be at.
    /// </summary>
    private static InputEvent BindingFrom(InputEvent @event)
    {
        if (@event is InputEventJoypadMotion motion)
        {
            return new InputEventJoypadMotion
            {
                Device = -1,
                Axis = motion.Axis,
                AxisValue = Mathf.Sign(motion.AxisValue),
            };
        }

        InputEvent copy = (InputEvent)@event.Duplicate();
        if (copy is InputEventWithModifiers modifiers)
        {
            modifiers.ShiftPressed = false;
            modifiers.CtrlPressed = false;
            modifiers.AltPressed = false;
            modifiers.MetaPressed = false;
        }

        if (copy is InputEventJoypadButton)
        {
            copy.Device = -1;
        }

        return copy;
    }

    private static bool SameBindingKind(InputEvent a, InputEvent b) =>
        (a is InputEventKey && b is InputEventKey)
        || (a is InputEventMouseButton && b is InputEventMouseButton)
        || (a is InputEventJoypadButton && b is InputEventJoypadButton)
        || (a is InputEventJoypadMotion && b is InputEventJoypadMotion);

    private static bool EastButton(InputEvent @event) =>
        @event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B };

    public static string ButtonLabel(JoyButton button)
    {
        bool playstation = PlayStationConnected();
        return button switch
        {
            JoyButton.A => playstation ? "Cross" : "A",
            JoyButton.B => playstation ? "Circle" : "B",
            JoyButton.X => playstation ? "Square" : "X",
            JoyButton.Y => playstation ? "Triangle" : "Y",
            JoyButton.Back => playstation ? "Share" : "Back",
            JoyButton.Start => playstation ? "Options" : "Start",
            JoyButton.Guide => "Guide",
            JoyButton.LeftStick => "L3",
            JoyButton.RightStick => "R3",
            JoyButton.LeftShoulder => playstation ? "L1" : "LB",
            JoyButton.RightShoulder => playstation ? "R1" : "RB",
            JoyButton.DpadUp => "D-pad up",
            JoyButton.DpadDown => "D-pad down",
            JoyButton.DpadLeft => "D-pad left",
            JoyButton.DpadRight => "D-pad right",
            _ => "Button " + (int)button,
        };
    }

    public static string AxisLabel(JoyAxis axis, float axisValue)
    {
        bool playstation = PlayStationConnected();
        bool negative = axisValue < 0.0f;
        return axis switch
        {
            JoyAxis.LeftX => negative ? "Left stick left" : "Left stick right",
            JoyAxis.LeftY => negative ? "Left stick up" : "Left stick down",
            JoyAxis.RightX => negative ? "Right stick left" : "Right stick right",
            JoyAxis.RightY => negative ? "Right stick up" : "Right stick down",
            JoyAxis.TriggerLeft => playstation ? "L2" : "LT",
            JoyAxis.TriggerRight => playstation ? "R2" : "RT",
            _ => "Axis " + (int)axis,
        };
    }

    private static bool PlayStationConnected()
    {
        foreach (int device in Input.GetConnectedJoypads())
        {
            string name = Input.GetJoyName(device);
            if (name.Contains("Dual", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("PlayStation", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("PS4", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("PS5", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("Sony", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The keys bound to an action, as a player reads them.</summary>
    public static string BindingText(string action)
    {
        if (!InputMap.HasAction(action))
        {
            return "-";
        }

        System.Collections.Generic.List<string> names = new();
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
        {
            string name = e switch
            {
                InputEventKey k => OS.GetKeycodeString(k.PhysicalKeycode != Key.None
                    ? DisplayServer.KeyboardGetKeycodeFromPhysical(k.PhysicalKeycode)
                    : k.Keycode),
                InputEventMouseButton m => m.ButtonIndex switch
                {
                    MouseButton.Left => "LMB",
                    MouseButton.Right => "RMB",
                    MouseButton.Middle => "MMB",
                    MouseButton.WheelUp => "Wheel up",
                    MouseButton.WheelDown => "Wheel down",
                    _ => $"Mouse {(int)m.ButtonIndex}",
                },
                InputEventJoypadButton padButton => ButtonLabel(padButton.ButtonIndex),
                InputEventJoypadMotion padMotion => AxisLabel(padMotion.Axis, padMotion.AxisValue),
                _ => string.Empty,
            };
            if (name.Length > 0 && !names.Contains(name))
            {
                names.Add(name);
            }
        }
        return names.Count > 0 ? string.Join(" / ", names) : "-";
    }

    private void Paint()
    {
        MenuScreen screen = Current;
        if (screen == null)
        {
            return;
        }

        for (int i = 0; i < _rows.Count && i < screen.Entries.Count; i++)
        {
            MenuEntry entry = screen.Entries[i];
            bool selected = i == _selected;

            string text = entry.Label;
            if (entry.Action == MenuAction.Toggle)
            {
                text = entry.Label + "    " + ((bool)GetSetting(entry.Id) ? "on" : "off");
            }
            else if (entry.Action == MenuAction.Rebind)
            {
                text = entry.Label + "    " + (entry == _capturing ? "press a button..." : BindingText(entry.Id));
            }
            else if (entry.Action == MenuAction.Slider)
            {
                float shown = (float)GetSetting(entry.Id) * entry.DisplayScale;
                text = entry.Label + "    " + shown.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) + entry.Suffix;
            }

            // The caret is what tells you where you are when the row colour is
            // hard to read against a bright scene behind the scrim.
            _rows[i].Text = selected ? "> " + text + " <" : text;
            _rows[i].AddThemeColorOverride(
                "font_color",
                selected ? SelectedColor : (entry.IsAdjustable ? ValueColor : ItemColor));
        }
    }
}
