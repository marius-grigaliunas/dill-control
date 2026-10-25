using Godot;

namespace DillControl.Debug;

/// <summary>
/// What the asset lab's Test hands the lab level (docs/decisions/0047), as
/// the Nest tab hands <c>lvl_nest_lab</c> its nest: written by the editor to
/// <c>user://lab_play.json</c>, taken and deleted by the level. And where
/// the running lab listens, for the editor's <c>lab_*</c> commands.
/// </summary>
public static class LabPlay
{
    public const string RequestPath = "user://lab_play.json";
    public const string ListenerPath = "user://lab_game.json";
    public const string LevelPath = "res://scenes/levels/lvl_asset_lab.tscn";

    public static void Write(string asset, bool board, string[] with = null)
    {
        var request = new Godot.Collections.Dictionary { ["asset"] = asset, ["board"] = board, ["with"] = with ?? System.Array.Empty<string>() };
        using FileAccess file = FileAccess.Open(RequestPath, FileAccess.ModeFlags.Write);
        file?.StoreString(Json.Stringify(request));
    }

    /// <summary>The request, once: the file is deleted. Null when there is none.</summary>
    public static Godot.Collections.Dictionary Take()
    {
        if (!FileAccess.FileExists(RequestPath))
        {
            return null;
        }
        Variant parsed = Json.ParseString(FileAccess.GetFileAsString(RequestPath));
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(RequestPath));
        return parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : null;
    }
}
