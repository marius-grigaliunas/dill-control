using System.IO;
using System.Text.Json;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// What passes between the Vines tool's Nest tab and a played nest
/// (docs/handoffs/vine-tool.md, "The tool"): the tool writes a
/// <see cref="Request"/> and plays <c>lvl_nest_lab</c>; <see cref="NestBuilder"/>
/// takes it (once: it is deleted as it is read, so a later run is not
/// steered by a stale one). Play writes nothing else.
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

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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
