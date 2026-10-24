#if TOOLS
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The Vines tool's two logs (docs/handoffs/vine-tool.md, decision 8): every
/// verdict on a nest, and every verdict on one vine's settings. They are the
/// training data a later generator learns from, so a Bad verdict is kept as
/// carefully as a Good one, and nothing is ever taken out. JSON Lines, one
/// record a line, in <c>tools/vine-logs/</c>, tracked in git. Each file's
/// first line is an <c>"about"</c> record saying in plain words what a line
/// is and what each field means.
///
/// Written with Godot's JSON: System.Text.Json's caches of the project's
/// types keep the editor from unloading the project's assembly on a rebuild.
/// Keys keep the order they are given in.
/// </summary>
public static class VineLogs
{
    /// <summary>Where the logs are. A check of the tool points it elsewhere, so
    /// that nothing it does reaches the real ones.</summary>
    public static string Folder { get; set; } = "res://tools/vine-logs";
    public const string NestVerdicts = "nest_verdicts.jsonl";
    public const string VineVerdicts = "vine_verdicts.jsonl";

    private static readonly Dictionary<string, Dictionary<string, object>> Abouts = new()
    {
        [NestVerdicts] = new()
        {
            ["about"] = "One line per verdict a person gave on a generated vine nest in the Vines tool's Nest tab: Good or Bad, "
                + "with what the nest measured when planned and, if it was played, how the play went. Nothing is removed: "
                + "bad nests are kept as carefully as good ones. A nest is grown the same on every machine from its kit, "
                + "seed and population, given the same generator.",
            ["fields"] = new Dictionary<string, string>
            {
                ["time"] = "When the verdict was given, UTC, ISO 8601.",
                ["verdict"] = "\"good\" or \"bad\".",
                ["note"] = "What the person wrote, if anything.",
                ["kit"] = "The NestKit resource's path: the vine kinds and the layout rules.",
                ["seed"] = "The nest's seed.",
                ["population"] = "Pickles asked for, one pod each.",
                ["horde"] = "Whether it was played with the horde.",
                ["generator"] = "The git commit the generator was at, with \"+changes\" when its code had uncommitted edits.",
                ["plan"] = "What the plan measured: vines, pods, short (pickles left without a pod), radius_m (from the heart), "
                    + "slots and slots_used, avenues, shifted (moved to high-pod kinds to fit), kinds (vines and mean pods per kind), "
                    + "spacing_min_m and spacing_mean_m (to the nearest neighbour), rolls and rejected (vine rolls, and those failing "
                    + "validation), reasons (rejections by reason), grow_seconds.",
                ["play"] = "Null if it was judged without playing. Else: played_seconds, swings (grapple catches), seconds_to_heart "
                    + "(within 30 m of the heart's vine; -1 never), ground_touches (landings on the planet's ground, not a vine), "
                    + "pods, pods_destroyed, pickles_starting, pickles_left, killed_on_pods, woke, outcome (asleep, fighting or "
                    + "cleared), bonus (every pickle killed), times_pickled (the player downed).",
            },
        },
        [VineVerdicts] = new()
        {
            ["about"] = "One line per verdict a person gave on one vine in the Vines tool's range lab: Good or Bad, with every "
                + "setting it was grown from and whether it passed validation. Used to narrow a kind's ranges from evidence. "
                + "Nothing is removed.",
            ["fields"] = new Dictionary<string, string>
            {
                ["time"] = "When the verdict was given, UTC, ISO 8601.",
                ["verdict"] = "\"good\" or \"bad\".",
                ["note"] = "What the person wrote, if anything.",
                ["kind"] = "The VineKind resource's path.",
                ["seed"] = "The seed asked for.",
                ["attempt"] = "Which deterministic re-roll of that seed grew it (0: its own roll).",
                ["generator"] = "The git commit the generator was at, with \"+changes\" when its code had uncommitted edits.",
                ["valid"] = "Whether it passed VineValidator.",
                ["reasons"] = "Why not, if not.",
                ["settings"] = "Every setting it was grown from, by name (VineParams).",
                ["measures"] = "voxels, pods, leaves, height_m, lean_deg, kink_deg, sag_m.",
            },
        },
    };

    /// <summary>Adds one record to a log, its about line first if it is new.</summary>
    public static void Append(string file, Dictionary<string, object> record)
    {
        string folder = ProjectSettings.GlobalizePath(Folder);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, file);
        var lines = new List<string>();
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            lines.Add(Json.Stringify(ToVariant(Abouts[file])));
        lines.Add(Json.Stringify(ToVariant(record)));
        File.AppendAllLines(path, lines);
    }

    /// <summary>The generator's version: the commit, and whether its code has changed since.</summary>
    public static string Generator()
    {
        string root = ProjectSettings.GlobalizePath("res://");
        var output = new Godot.Collections.Array();
        if (OS.Execute("git", new[] { "-C", root, "rev-parse", "--short", "HEAD" }, output) != 0 || output.Count == 0)
            return "unknown";
        string commit = output[0].AsString().Trim();
        output.Clear();
        OS.Execute("git", new[] { "-C", root, "status", "--porcelain", "--", "scripts/foliage", "scripts/mission", "resources/foliage", "resources/mission" }, output);
        bool changed = output.Count > 0 && output[0].AsString().Trim().Length > 0;
        return changed ? commit + "+changes" : commit;
    }

    // Plain values, lists and string-keyed dictionaries, as Godot's.
    private static Variant ToVariant(object value) => value switch
    {
        null => default,
        string s => s,
        bool b => b,
        int i => i,
        long l => l,
        ulong u => (long)u,
        float f => f,
        double d => d,
        IDictionary map => Map(map),
        IEnumerable list => List(list),
        _ => value.ToString(),
    };

    private static Godot.Collections.Dictionary Map(IDictionary map)
    {
        var dictionary = new Godot.Collections.Dictionary();
        foreach (DictionaryEntry entry in map)
            dictionary[entry.Key.ToString()] = ToVariant(entry.Value);
        return dictionary;
    }

    private static Godot.Collections.Array List(IEnumerable list)
    {
        var array = new Godot.Collections.Array();
        foreach (object item in list)
            array.Add(ToVariant(item));
        return array;
    }

    public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
}
#endif
