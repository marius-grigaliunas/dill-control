using System;
using System.IO;
using System.Reflection;
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
///
/// Godot's JSON, not System.Text.Json: this runs in the editor too, and
/// System.Text.Json's caches of the project's types keep the editor from
/// unloading the project's assembly when it is rebuilt.
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
        /// <summary>For testing: every weapon kept full (<see cref="NestPlaytest"/>).</summary>
        public bool InfiniteAmmo { get; set; } = true;
        /// <summary>For testing: a hoverbike parked beside the player at the start.</summary>
        public bool Bike { get; set; } = true;
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

    public static void WriteResult(Result result)
    {
        string path = ProjectSettings.GlobalizePath(ResultPath);
        File.WriteAllText(path + ".tmp", ToJson(result));
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
        return FromJson<Result>(File.ReadAllText(path));
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
        File.WriteAllText(ProjectSettings.GlobalizePath(RequestPath), ToJson(request));

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
            Request request = FromJson<Request>(File.ReadAllText(path));
            if (request == null)
                GD.PushWarning($"NestPlay: {RequestPath} could not be read; ignored.");
            return request;
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Each public property by name; seeds as whole numbers (a JSON number is
    // a double, exact to 2^53, and the tool's seeds are ints).
    private static string ToJson(object value)
    {
        var dictionary = new Godot.Collections.Dictionary();
        foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object v = property.GetValue(value);
            dictionary[property.Name] = v switch
            {
                string s => s,
                bool b => b,
                int i => i,
                long l => l,
                ulong u => (long)u,
                float f => f,
                double d => d,
                _ => v?.ToString() ?? "",
            };
        }
        return Json.Stringify(dictionary, "  ");
    }

    private static T FromJson<T>(string text) where T : new()
    {
        if (Json.ParseString(text).Obj is not Godot.Collections.Dictionary dictionary)
            return default;
        var value = new T();
        foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!dictionary.TryGetValue(property.Name, out Variant v))
                continue;
            Type type = property.PropertyType;
            object read = type == typeof(string) ? v.AsString()
                : type == typeof(bool) ? v.AsBool()
                : type == typeof(int) ? (int)v.AsDouble()
                : type == typeof(long) ? (long)v.AsDouble()
                : type == typeof(ulong) ? (ulong)Math.Max(0.0, v.AsDouble())
                : type == typeof(float) ? (float)v.AsDouble()
                : type == typeof(double) ? v.AsDouble()
                : null;
            if (read != null)
                property.SetValue(value, read);
        }
        return value;
    }
}
