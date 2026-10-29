using Godot;

namespace DillControl.Mission;

/// <summary>
/// One prompt of an <see cref="IntroGuide"/>: what it says, and what the
/// player does to be done with it. The text names a control as
/// <c>{grapple}</c>, <c>{fire}</c>, and so on, any input action, and shows the
/// key or button the player has it on.
/// </summary>
[GlobalClass]
public partial class GuideStep : Resource
{
    public enum Done
    {
        /// <summary>After <see cref="Seconds"/>.</summary>
        Time,
        /// <summary>The hook latched onto something.</summary>
        Hooked,
        /// <summary>The hook let go.</summary>
        Released,
        /// <summary>The player is <see cref="Metres"/> over where this step began.</summary>
        Height,
        /// <summary>The player has moved <see cref="Metres"/> from where this step began.</summary>
        Travelled,
        /// <summary>The player fired.</summary>
        Fired,
        /// <summary>The jar has taken damage.</summary>
        JarHit,
        /// <summary>A pod of the nest has been destroyed.</summary>
        PodDestroyed,
        /// <summary>The colony has been harmed and is waking.</summary>
        ColonyWoke,
        /// <summary>The player has stepped out of the landing pod (<see cref="PodLanding"/>).</summary>
        Landed,
    }

    /// <summary>A short heading, drawn small over the text.</summary>
    [Export] public string Title { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Text { get; set; } = "";
    [Export] public Done Until { get; set; } = Done.Time;

    /// <summary>For <see cref="Done.Time"/>, how long it shows. For any other,
    /// how long before it gives up waiting and the guide goes on; 0 waits for ever.</summary>
    [Export(PropertyHint.Range, "0,500,0.5")] public float Seconds { get; set; } = 6.0f;
    /// <summary>How far or how high, for the conditions that ask for it.</summary>
    [Export(PropertyHint.Range, "0,500,1,suffix:m")] public float Metres { get; set; }

    /// <summary>A step shows at least this long before its condition can finish
    /// it, so a prompt is never gone before it is read.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:s")] public float ReadSeconds { get; set; } = 3.0f;

    /// <summary>Seconds to wait, once the one before is done, before this shows.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:s")] public float DelaySeconds { get; set; } = 0.5f;
}
