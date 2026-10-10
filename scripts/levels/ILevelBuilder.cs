namespace DillControl.Levels;

/// <summary>
/// Something that builds a level after it loads -- the contract level's
/// planet, compound and horde -- and says when the player can be let in.
/// While a <see cref="LevelRoot"/>'s builder is not ready, the level shows a
/// loading screen and holds the player back.
/// </summary>
public interface ILevelBuilder
{
    /// <summary>True once the level is built and its frames have settled.</summary>
    bool LevelReady { get; }

    /// <summary>0 to 1, for the bar.</summary>
    float Progress { get; }

    /// <summary>What is being done now, in a few words.</summary>
    string Stage { get; }

    /// <summary>The loading screen's heading: what is being loaded.</summary>
    string Title { get; }

    /// <summary>A line or two under the heading.</summary>
    string Details { get; }
}
