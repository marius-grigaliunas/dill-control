using System.Collections.Generic;
using DillControl.Core;
using DillControl.Player;
using DillControl.Voxel;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// The onboarding level's prompts: <see cref="Steps"/> one after another,
/// bottom centre, each until the player does what it asked (hooks on, climbs,
/// fires, hits the jar, lights the colony). It reads the player and the nest,
/// and changes nothing in them.
///
/// PLACEHOLDER look, like <see cref="DillControl.UI.ColonyHud"/>: a plate and
/// a label, no art yet.
/// </summary>
[GlobalClass]
public partial class IntroGuide : Node
{
    [Export] public GuideScript Lines { get; set; }

    private Godot.Collections.Array<GuideStep> Steps => Lines?.Steps ?? new Godot.Collections.Array<GuideStep>();

    [ExportGroup("Scene Wiring")]
    [Export] public Levels.LevelRoot Level { get; set; }
    [Export] public PodField Pods { get; set; }
    /// <summary>The jar's destruction; empty when the level has none.</summary>
    [Export] public VoxelBody Jar { get; set; }
    /// <summary>The pod drop that opens the level; empty when the player starts on foot.</summary>
    [Export] public PodLanding Landing { get; set; }

    private PlayerController _player;
    private Vector3 _start;
    private int _index = -1;
    private float _shown;
    private float _wait;
    private bool _hooked, _released, _fired, _jarHit, _podDestroyed, _woke, _landed;
    private CanvasLayer _layer;
    private PanelContainer _plate;
    private Label _title, _text;

    public override void _Ready()
    {
        if (Level == null)
        {
            GD.PushError($"IntroGuide '{Name}' needs its Level; no prompts.");
            SetProcess(false);
            return;
        }
        Level.PlayerSpawned += OnPlayerSpawned;
        if (Landing != null)
        {
            Landing.Arrived += OnArrived;
        }
        if (Pods != null)
        {
            Pods.PodDestroyed += OnPodDestroyed;
            Pods.Aggravated += OnAggravated;
        }
        if (Jar != null)
        {
            Jar.Damaged += OnJarDamaged;
        }
        Build();
        _plate.Visible = false;
    }

    public override void _ExitTree()
    {
        if (Level != null)
        {
            Level.PlayerSpawned -= OnPlayerSpawned;
        }
        if (Pods != null)
        {
            Pods.PodDestroyed -= OnPodDestroyed;
            Pods.Aggravated -= OnAggravated;
        }
        if (Jar != null && IsInstanceValid(Jar))
        {
            Jar.Damaged -= OnJarDamaged;
        }
        if (Landing != null && IsInstanceValid(Landing))
        {
            Landing.Arrived -= OnArrived;
        }
    }

    private void OnArrived() => _landed = true;
    private void OnPodDestroyed(int pod) => _podDestroyed = true;
    private void OnAggravated(Vector3 at) => _woke = true;
    private void OnJarDamaged(DamageInfo info) => _jarHit = true;

    private void OnPlayerSpawned(Node3D player)
    {
        _player = player as PlayerController;
        if (_player == null)
        {
            return;
        }
        _start = _player.GlobalPosition;
        if (_player.Grapple != null)
        {
            _player.Grapple.Attached += () => _hooked = true;
            _player.Grapple.Released += snapped => _released = true;
        }
        _landed = Landing == null;
        // A clone starts the lesson again only if the first never finished.
        if (_index < 0)
        {
            Next();
        }
    }

    private void Build()
    {
        _layer = new CanvasLayer { Name = "GuideHud", Layer = 6 };
        _plate = new PanelContainer { Name = "Plate" };
        _plate.MouseFilter = Godot.Control.MouseFilterEnum.Ignore;
        _plate.CustomMinimumSize = new Vector2(620.0f, 0.0f);
        _plate.SizeFlagsHorizontal = Godot.Control.SizeFlags.ShrinkCenter;

        var style = new StyleBoxFlat { BgColor = new Color(0.04f, 0.07f, 0.05f, 0.82f), BorderColor = new Color(0.55f, 0.85f, 0.45f, 0.9f) };
        style.SetBorderWidthAll(0);
        style.BorderWidthLeft = 4;
        style.SetContentMarginAll(14.0f);
        style.ContentMarginLeft = 20.0f;
        _plate.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer { MouseFilter = Godot.Control.MouseFilterEnum.Ignore };
        _title = new Label { MouseFilter = Godot.Control.MouseFilterEnum.Ignore };
        _title.AddThemeFontSizeOverride("font_size", 14);
        _title.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 0.45f));
        _text = new Label { MouseFilter = Godot.Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _text.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(_title);
        box.AddChild(_text);
        _plate.AddChild(box);
        // Bottom centre, above the health bars.
        var margin = new MarginContainer { Name = "Margin", MouseFilter = Godot.Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(Godot.Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_bottom", 110);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Godot.Control.MouseFilterEnum.Ignore };
        column.AddChild(_plate);
        margin.AddChild(column);
        _layer.AddChild(margin);
        AddChild(_layer);
    }

    public override void _Process(double delta)
    {
        if (_index < 0 || _index >= Steps.Count)
        {
            return;
        }
        float dt = (float)delta;
        if (_wait > 0.0f)
        {
            _wait -= dt;
            if (_wait <= 0.0f)
            {
                Show(Steps[_index]);
            }
            return;
        }
        _shown += dt;
        GuideStep step = Steps[_index];
        bool gaveUp = step.Until != GuideStep.Done.Time && step.Seconds > 0.0f && _shown >= step.Seconds;
        if (_shown >= step.ReadSeconds && (gaveUp || IsDone(step)))
        {
            Next();
        }
    }

    private void Next()
    {
        _index++;
        _shown = 0.0f;
        _hooked = _released = _fired = false;
        if (_index >= Steps.Count)
        {
            _plate.Visible = false;
            return;
        }
        _plate.Visible = false;
        _wait = Mathf.Max(0.01f, Steps[_index].DelaySeconds);
    }

    private void Show(GuideStep step)
    {
        // Distances count from where the player stands as the step appears.
        if (_player != null && IsInstanceValid(_player))
        {
            _start = _player.GlobalPosition;
        }
        _title.Text = step.Title.ToUpperInvariant();
        _title.Visible = step.Title.Length > 0;
        _text.Text = Fill(step.Text);
        _plate.Visible = true;
    }

    private bool IsDone(GuideStep step)
    {
        if (_player == null || !IsInstanceValid(_player))
        {
            return false;
        }
        Vector3 up = _player.GravityUp;
        switch (step.Until)
        {
            case GuideStep.Done.Time: return _shown >= step.Seconds;
            case GuideStep.Done.Hooked: return _hooked || _player.IsGrappling;
            case GuideStep.Done.Released: return _released;
            case GuideStep.Done.Height: return (_player.GlobalPosition - _start).Dot(up) >= step.Metres;
            case GuideStep.Done.Travelled: return (_player.GlobalPosition - _start).Length() >= step.Metres;
            case GuideStep.Done.Fired: return _fired;
            case GuideStep.Done.JarHit: return _jarHit || Jar == null;
            case GuideStep.Done.PodDestroyed: return _podDestroyed;
            case GuideStep.Done.Landed: return _landed;
            case GuideStep.Done.ColonyWoke: return _woke || Pods?.Colony != PodField.ColonyState.Asleep;
        }
        return true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("fire") && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _fired = true;
        }
    }

    // "{grapple}" to the key or button that action is on.
    private static string Fill(string text)
    {
        int open = text.IndexOf('{');
        while (open >= 0)
        {
            int close = text.IndexOf('}', open);
            if (close < 0)
            {
                break;
            }
            string action = text.Substring(open + 1, close - open - 1);
            text = text.Substring(0, open) + KeyName(action) + text.Substring(close + 1);
            open = text.IndexOf('{');
        }
        return text;
    }

    private static string KeyName(string action)
    {
        if (!InputMap.HasAction(action))
        {
            return action;
        }
        var names = new List<string>();
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
        {
            if (e is InputEventKey or InputEventMouseButton)
            {
                string name = e.AsText();
                // "Space - Physical", "Middle Mouse Button (Mouse Button)": the name only.
                int cut = name.IndexOfAny(new[] { '(', '-' });
                names.Add((cut > 0 ? name.Substring(0, cut) : name).Trim().ToUpperInvariant());
            }
        }
        return names.Count > 0 ? names[0] : action;
    }
}
