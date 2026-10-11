namespace ModularMenu;

/// <summary>
/// What activating a <see cref="MenuEntry"/> does.
///
/// Deliberately small. Anything a specific game needs that is not on this list
/// is <see cref="Custom"/>, which raises a signal carrying the entry's Id and
/// lets the host decide -- so adding a game-specific button never means editing
/// this enum, and this file stays the same in every project it is dropped into.
/// </summary>
public enum MenuAction
{
    /// <summary>Close the whole menu and unpause.</summary>
    Resume,

    /// <summary>Push <see cref="MenuEntry.Target"/> onto the screen stack.</summary>
    OpenScreen,

    /// <summary>Pop one screen. On the root screen this resumes instead.</summary>
    Back,

    /// <summary>A bool the host owns, shown as On/Off.</summary>
    Toggle,

    /// <summary>A float the host owns, shown as a value with left/right arrows.</summary>
    Slider,

    /// <summary>Raise <see cref="MenuSystem.CustomActivated"/> and let the host act.</summary>
    Custom,

    /// <summary>
    /// Rebinds the input action named by the entry's Id: activating it waits
    /// for the next key, mouse button, pad button or stick axis, which
    /// replaces bindings of that same kind. Escape or Start cancels. Raises
    /// RebindChanged.
    /// </summary>
    Rebind,

    /// <summary>Quit to desktop.</summary>
    Quit,
}
