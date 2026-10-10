using Godot;

namespace DillControl.UI;

/// <summary>
/// The loading screen a level shows while its builder works: what is being
/// loaded, a bar, and what is being done now. Shown by <c>LevelRoot</c> over
/// the level itself, so there is no separate loading scene to hand the
/// contract through; it fades once the builder says the level has settled.
///
/// PLACEHOLDER look, in the HUD's palette and face; the art pass dresses it.
/// </summary>
[GlobalClass]
public partial class LoadingOverlay : CanvasLayer
{
    [Export]
    public Control Root { get; set; }

    [Export]
    public Label TitleLabel { get; set; }

    [Export]
    public Label DetailsLabel { get; set; }

    [Export]
    public ProgressBar Bar { get; set; }

    [Export]
    public Label StageLabel { get; set; }

    /// <summary>Seconds the fade out takes.</summary>
    [Export(PropertyHint.Range, "0,3,0.05,suffix:s")]
    public float FadeSeconds { get; set; } = 0.6f;

    private float _shownProgress;
    private bool _fading;

    public override void _Ready()
    {
        Font font = DillControl.UI.PickleStyle.Font;
        if (TitleLabel != null)
        {
            TitleLabel.LabelSettings = new LabelSettings
            {
                Font = font,
                FontSize = 64,
                FontColor = PickleStyle.Brine,
                OutlineSize = PickleStyle.OutlineFor(64),
                OutlineColor = PickleStyle.Ink,
            };
        }
        if (StageLabel != null)
        {
            StageLabel.LabelSettings = new LabelSettings
            {
                Font = font,
                FontSize = 28,
                FontColor = PickleStyle.Cream,
                OutlineSize = PickleStyle.OutlineFor(28),
                OutlineColor = PickleStyle.Ink,
            };
        }
    }

    /// <summary>The current state, every frame while loading. The bar eases toward the progress rather than jumping.</summary>
    public void Show(string title, string details, float progress, string stage, float delta)
    {
        if (TitleLabel != null)
        {
            TitleLabel.Text = title;
        }
        if (DetailsLabel != null)
        {
            DetailsLabel.Text = details;
        }
        if (StageLabel != null)
        {
            StageLabel.Text = stage;
        }

        _shownProgress = Mathf.MoveToward(_shownProgress, Mathf.Clamp(progress, 0.0f, 1.0f), delta * 1.5f);
        if (Bar != null)
        {
            Bar.Value = _shownProgress * Bar.MaxValue;
        }
    }

    /// <summary>Fades out and frees itself.</summary>
    public void FadeOut()
    {
        if (_fading || Root == null)
        {
            QueueFree();
            return;
        }

        _fading = true;
        Tween tween = CreateTween();
        tween.TweenProperty(Root, "modulate:a", 0.0f, FadeSeconds);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
