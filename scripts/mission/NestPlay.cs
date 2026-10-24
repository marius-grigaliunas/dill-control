using System.IO;
using System.Text.Json;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// What passes between the Vines tool's Nest tab and a played nest
/// (docs/handoffs/vine-tool.md, "The tool"): the tool writes a
/// <see cref="Request"/> and plays <c>lvl_nest_lab</c>; <see cref="NestBuilder"/>
/// takes it (once: it is deleted as it is read, so a later run is not
/// steered by a stale one). While it plays, <see cref="NestPlaytest"/>
/// writes how it is going (<see cref="Result"/>) for the tool to read when
/// play stops. Play writes nothing in the project.
/// </summary>
public static class NestPlay
{
    public const string RequestPath = "user://nest_play.json";

    public sealed class Request
    {
        /// <summary>A <see cref="NestKit"/>'s res:// path.</summary>
        public string Kit { get; set; } = "";
        public ulong Seed { get; set; }
        public int Population { get; set; }
        public bool Horde { get; set; } = true;
    }

    public const string ResultPath = "user://nest_result.json";

    /// <summary>How a played nest went, written by <see cref="NestPlaytest"/>
    /// while it runs (the editor stops a game by ending it, so there may be no
    /// last word) and read by the tool once play stops.</summary>
    public sealed class Result
    {
        public string Kit { get; set; } = "";
        public ulong Seed { get; set; }
        public int Population { get; set; }
        public bool Horde { get; set; }
        public double PlayedSeconds { get; set; }
        /// <summary>Times the grapple caught something.</summary>
        public int Swings { get; set; }
        /// <summary>From being let in to within reach of the heart's vine; below zero, never.</summary>
        public double SecondsToHeart { get; set; } = -1;
        /// <summary>Landings on the planet's ground, as against a vine or a leaf.</summary>
        public int GroundTouches { get; set; }
        public int Pods { get; set; }
        public int PodsDestroyed { get; set; }
        public int PicklesStarting { get; set; }
        public int PicklesLeft { get; set; }
        public int KilledOnPods { get; set; }
        public bool Woke { get; set; }
        /// <summary>"asleep", "fighting" or "cleared".</summary>
        public string Outcome { get; set; } = "asleep";
        public bool Bonus { get; set; }
        public int TimesPickled { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void WriteResult(Result result)
    {
        string path = ProjectSettings.GlobalizePath(ResultPath);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(result, Options));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>The last played nest's result, or null.</summary>
    public static Result ReadResult()
    {
        string path = ProjectSettings.GlobalizePath(ResultPath);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<Result>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void ClearResult()
    {
        string path = ProjectSettings.GlobalizePath(ResultPath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static void Write(Request request) =>
        File.WriteAllText(ProjectSettings.GlobalizePath(RequestPath), JsonSerializer.Serialize(request, Options));

    /// <summary>The request the tool left, or null; it is gone after.</summary>
    public static Request Take()
    {
        string path = ProjectSettings.GlobalizePath(RequestPath);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<Request>(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            GD.PushWarning($"NestPlay: {RequestPath} could not be read ({e.Message}); ignored.");
            return null;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
