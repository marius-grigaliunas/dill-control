#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using DillControl.Core;
using DillControl.Foliage;
using DillControl.Mission;

namespace DillControl.Editor;

/// <summary>
/// The Vines tool's commands on the editor bridge (docs/decisions/0044):
/// the kinds and their ranges, a range-lab roll, a bake and a verdict,
/// through the same classes the tabs use, never through their buttons.
/// Claude's verdicts carry <c>"by": "claude"</c>.
/// </summary>
public static class VineCommands
{
    public static void Register()
    {
        Bridge.Register("vine_kinds",
            "Every vine kind (resources/foliage/vine_kind_*.tres): its name, file and each setting's range [least, most].",
            """{"type":"object","properties":{}}""",
            _ => Kinds());
        Bridge.Register("vine_kind",
            "Make a vine kind or change one, and save it. A new kind starts as a copy of 'from'. 'ranges' sets settings' ranges, " +
            "e.g. {\"heightM\":[8,12]}; 'chances' sets a yes/no setting's chance, e.g. {\"armTipLeaf\":0.5}. " +
            "A kind is in no nest until a NestKit names it. Changing a kind changes every vine its seeds grow: re-bake its baked vines.",
            """
            {"type":"object","required":["kind"],"properties":{
              "kind":{"type":"string","description":"the kind's name, e.g. Leafy"},
              "from":{"type":"string","description":"for a new kind: the kind to copy"},
              "ranges":{"type":"object","additionalProperties":{"type":"array","items":{"type":"number"}}},
              "chances":{"type":"object","additionalProperties":{"type":"number"}},
              "weight":{"type":"number"},
              "zone":{"type":"array","items":{"type":"number"}}
            }}
            """,
            Kind);
        Bridge.Register("vine_roll",
            "What the range lab does: roll 'count' seeds of a kind as they roll, before any re-roll, and judge each with the validator. " +
            "Returns the share valid on their own roll, why the rest failed, and per seed whether the vine it grows (after re-rolls) is valid, " +
            "its height and where its pods and the bottoms of their hanging pickles are, in metres above its foot.",
            """
            {"type":"object","required":["kind"],"properties":{
              "kind":{"type":"string"},
              "count":{"type":"integer","description":"default 24, at most 200"},
              "from":{"type":"integer","description":"the seeds are drawn from this, as the lab's 'from'; default 1"}
            }}
            """,
            Roll);
        Bridge.Register("vine_bake",
            "Bake one seed of a kind as the Vine tab does: art/voxels/prp_vine_<kind>_<seed>.vox and its wrapper scene, then wait for the import. " +
            "Only a valid vine is baked. 'place' can then put the scene in a level.",
            """{"type":"object","required":["kind","seed"],"properties":{"kind":{"type":"string"},"seed":{"type":"integer"}}}""",
            Bake);
        Bridge.Register("nest_bake",
            "Bake a saved nest (NestPreset .tres) into an authored scene, docs/decisions/0056: every vine baked as 'vine_bake' does, and " +
            "scenes/props/nests/prp_nest_<preset>.tscn with them laid out round the origin, to 'place' in a level and edit by hand. One-way; an existing " +
            "nest scene is never written over unless 'overwrite' (it may hold hand edits).",
            """
            {"type":"object","required":["preset"],"properties":{
              "preset":{"type":"string","description":"res://resources/mission/nests/<file>.tres"},
              "overwrite":{"type":"boolean"}
            }}
            """,
            NestBake);
        Bridge.Register("vine_judge",
            "Log Claude's Good or Bad for a seed's vine to tools/vine-logs/vine_verdicts.jsonl, marked \"by\": \"claude\". " +
            "Only when it can be judged accurately: the logs are training data and are never edited.",
            """
            {"type":"object","required":["kind","seed","good","note"],"properties":{
              "kind":{"type":"string"},"seed":{"type":"integer"},"good":{"type":"boolean"},
              "note":{"type":"string","description":"why, in a sentence"}
            }}
            """,
            Judge);
    }

    private static VineBakeStats Stats() =>
        ResourceLoader.Load(VineBakeStats.DefaultPath, cacheMode: ResourceLoader.CacheMode.Replace) as VineBakeStats
        ?? throw new Bridge.Refused($"{VineBakeStats.DefaultPath} did not load");

    private static List<VineKind> LoadKinds()
    {
        var kinds = new List<VineKind>();
        foreach (string file in DirAccess.GetFilesAt(VineKind.Folder))
        {
            if (file.StartsWith(VineKind.Prefix) && file.EndsWith(".tres")
                && ResourceLoader.Load($"{VineKind.Folder}/{file}", cacheMode: ResourceLoader.CacheMode.Replace) is VineKind kind)
                kinds.Add(kind);
        }
        return kinds;
    }

    private static VineKind Find(string name) =>
        LoadKinds().FirstOrDefault(k => string.Equals(k.KindName, name, StringComparison.OrdinalIgnoreCase) || k.Slug == name.ToLowerInvariant())
        ?? throw new Bridge.Refused($"no vine kind '{name}'; vine_kinds lists them");

    private static JsonObject Describe(VineKind kind)
    {
        var ranges = new JsonObject();
        var chances = new JsonObject();
        foreach (VineParams.Field field in VineParams.RangeFields)
        {
            if (field.Bool)
                chances[field.Key] = kind.ChanceOf(field.Key);
            else
            {
                (double lo, double hi) = kind.RangeOf(field);
                ranges[field.Key] = new JsonArray(lo, hi);
            }
        }
        return new JsonObject
        {
            ["name"] = kind.KindName,
            ["path"] = kind.ResourcePath,
            ["weight"] = kind.Weight,
            ["zone"] = new JsonArray(kind.Zone.X, kind.Zone.Y),
            ["ranges"] = ranges,
            ["chances"] = chances,
        };
    }

    private static JsonNode Kinds() => new JsonArray(LoadKinds().Select(k => (JsonNode)Describe(k)).ToArray());

    private static JsonNode Kind(JsonObject args)
    {
        string name = Bridge.Text(args, "kind") ?? throw new Bridge.Refused("'kind' is missing");
        List<VineKind> kinds = LoadKinds();
        VineKind kind = kinds.FirstOrDefault(k => string.Equals(k.KindName, name, StringComparison.OrdinalIgnoreCase));
        bool made = kind == null;
        if (made)
        {
            string from = Bridge.Text(args, "from") ?? throw new Bridge.Refused($"no kind '{name}': give 'from' to make it as a copy of another");
            VineKind source = kinds.FirstOrDefault(k => string.Equals(k.KindName, from, StringComparison.OrdinalIgnoreCase))
                ?? throw new Bridge.Refused($"no kind '{from}' to copy");
            kind = (VineKind)source.Duplicate();
            kind.KindName = name;
            kind.ResourceName = "VineKind" + name;
            string path = $"{VineKind.Folder}/{VineKind.Prefix}{kind.Slug}.tres";
            if (FileAccess.FileExists(path))
                throw new Bridge.Refused($"{path} is there already");
            kind.ResourcePath = path;
        }

        var changed = new JsonObject();
        if (args["ranges"] is JsonObject ranges)
        {
            foreach ((string key, JsonNode value) in ranges)
            {
                VineParams.Field field = VineParams.RangeFields.FirstOrDefault(f => f.Key == key && !f.Bool)
                    ?? throw new Bridge.Refused($"no range '{key}'");
                if (value is not JsonArray pair || pair.Count != 2)
                    throw new Bridge.Refused($"'{key}' wants [least, most]");
                double lo = pair[0]!.GetValue<double>(), hi = pair[1]!.GetValue<double>();
                if (lo > hi || lo < field.Min || hi > field.Max)
                    throw new Bridge.Refused(Bridge.Invariant($"'{key}' must lie in {field.Min}-{field.Max}, least first"));
                string property = Property(key);
                Vector2 was = kind.Get(property).AsVector2();
                kind.Set(property, new Vector2((float)lo, (float)hi));
                changed[key] = new JsonArray(Math.Round(was.X, 4), Math.Round(was.Y, 4), lo, hi);
            }
        }
        if (args["chances"] is JsonObject chances)
        {
            foreach ((string key, JsonNode value) in chances)
            {
                if (VineParams.RangeFields.FirstOrDefault(f => f.Key == key && f.Bool) == null)
                    throw new Bridge.Refused($"no yes/no setting '{key}'");
                kind.Set(Property(key) + "Chance", (float)Math.Clamp(value!.GetValue<double>(), 0, 1));
                changed[key] = value.GetValue<double>();
            }
        }
        if (args.ContainsKey("weight"))
            kind.Weight = (float)Bridge.Number(args, "weight", kind.Weight);
        if (args["zone"] is JsonArray { Count: 2 } zone)
        {
            kind.Zone = new Vector2((float)zone[0]!.GetValue<double>(), (float)zone[1]!.GetValue<double>());
        }

        Error error = ResourceSaver.Save(kind, kind.ResourcePath);
        if (error != Error.Ok)
            throw new Bridge.Refused($"not saved: {error}");
        if (made)
            EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(kind.ResourcePath);
        JsonObject result = Describe(kind);
        result["made"] = made;
        result["changed"] = changed;
        result["note"] = "the range lab shows it after its Reload";
        return result;
    }

    private static string Property(string key) => char.ToUpperInvariant(key[0]) + key[1..];

    private static async Task<JsonNode> Roll(JsonObject args)
    {
        VineKind kind = Find(Bridge.Text(args, "kind") ?? throw new Bridge.Refused("'kind' is missing"));
        int count = Math.Clamp((int)Bridge.Number(args, "count", 24), 1, 200);
        uint from = (uint)Bridge.Number(args, "from", 1);
        VineBakeStats stats = Stats();
        VineGrower.Context context = VineGrower.Context.For(stats, new[] { kind });
        VineKind.Ranges ranges = context.Kinds[kind];
        double pickle = context.Limits.PickleHeightM;

        var rows = new JsonObject[count];
        var own = new bool[count];
        var reasons = new string[count][];
        await Task.Run(() => Parallel.For(0, count, i =>
        {
            // The lab's seeds, so a roll here is the same set as the lab's.
            int seed = (int)(SeededRandom.Hash(from, "lab:" + i) % 10_000_000UL);
            VineParams p = kind.Roll(seed, context.VoxelSize, ranges);
            VineValidator.Verdict first = VineValidator.Check(VineGenerator.Generate(p, context.Limits), stats, full: true, context.Spine);
            own[i] = first.Ok;
            reasons[i] = first.Reasons.ToArray();
            VineGrower.Grown grown = VineGrower.Grow(kind, seed, context);
            rows[i] = Row(grown, seed, pickle);
        }));

        var why = new SortedDictionary<string, int>();
        foreach (string r in reasons.SelectMany(r => r))
            why[r] = why.GetValueOrDefault(r) + 1;
        int valid = own.Count(o => o);
        return new JsonObject
        {
            ["kind"] = kind.KindName,
            ["rolled"] = count,
            ["valid_on_own_roll"] = valid,
            ["share_valid"] = Math.Round((double)valid / count, 3),
            ["why_not"] = new JsonObject(why.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode)kv.Value))),
            ["no_valid_vine"] = rows.Count(r => !(bool)r["valid"]!),
            ["seeds"] = new JsonArray(rows.Cast<JsonNode>().ToArray()),
        };
    }

    // One seed's vine as it grows (after re-rolls): heights in metres above its foot.
    private static JsonObject Row(VineGrower.Grown grown, int seed, double pickle)
    {
        VineGenerator.Result vine = grown.Vine;
        double vs = vine.VoxelSize;
        var row = new JsonObject { ["seed"] = seed, ["valid"] = grown.Ok, ["attempt"] = grown.Attempt };
        if (vine.Voxels.Count == 0)
        {
            row["error"] = vine.Error ?? "nothing grew";
            return row;
        }
        int foot = vine.Voxels.Min(v => v.Z);
        double[] pods = vine.Markers.Select(m => (m.Z - foot + 0.5) * vs).ToArray();
        row["height_m"] = Math.Round((vine.BoundsZ) * vs, 1);
        row["pods"] = pods.Length;
        row["leaves"] = vine.Counts.Leaves;
        row["voxels"] = vine.Counts.Total;
        row["pods_left_out"] = new JsonObject
        {
            ["blocked"] = vine.Counts.PodsBlocked, ["too_low"] = vine.Counts.PodsGrounded, ["crowded"] = vine.Counts.PodsCrowded,
        };
        if (pods.Length > 0)
        {
            row["pods_m"] = new JsonArray(Math.Round(pods.Min(), 1), Math.Round(pods.Max(), 1));
            row["pickle_bottoms_m"] = new JsonArray(Math.Round(pods.Min() - pickle, 1), Math.Round(pods.Max() - pickle, 1));
        }
        if (!grown.Ok)
            row["why_not"] = new JsonArray(grown.Verdict.Reasons.Select(r => (JsonNode)r).ToArray());
        return row;
    }

    private static async Task<JsonNode> Bake(JsonObject args)
    {
        VineKind kind = Find(Bridge.Text(args, "kind") ?? throw new Bridge.Refused("'kind' is missing"));
        int seed = (int)Bridge.Number(args, "seed", -1);
        if (seed < 0)
            throw new Bridge.Refused("'seed' is missing");
        VineBakeStats stats = Stats();
        VineBaker.Baked baked = VineBaker.Bake(kind, seed, stats);
        if (!baked.Grown.Ok)
            throw new Bridge.Refused("not baked, no valid vine: " + string.Join(" ", baked.Grown.Verdict.Details));

        // The editor imports the .vox; the scene is right once that .vox has
        // been (re)imported, not merely once the scene exists, which a re-bake's does.
        EditorFileSystem files = EditorInterface.Singleton.GetResourceFilesystem();
        bool changed = false;
        void Reimported(string[] paths) => changed |= paths.Contains(baked.VoxPath);
        files.ResourcesReimported += Reimported;
        files.Scan();
        try
        {
            for (int i = 0; i < 1800 && (!changed || files.IsScanning() || !ResourceLoader.Exists(baked.ScenePath)); i++)
                await Bridge.NextFrame();
        }
        finally
        {
            files.ResourcesReimported -= Reimported;
        }
        JsonObject row = Row(baked.Grown, seed, stats.PickleHeightM);
        row["name"] = baked.Name;
        row["scene"] = baked.ScenePath;
        row["scene_written"] = baked.SceneWritten;
        row["imported"] = changed && ResourceLoader.Exists(baked.ScenePath);
        return row;
    }

    private static async Task<JsonNode> NestBake(JsonObject args)
    {
        string path = Bridge.Text(args, "preset") ?? throw new Bridge.Refused("'preset' is missing");
        if (ResourceLoader.Load(path, cacheMode: ResourceLoader.CacheMode.Replace) is not NestPreset preset || preset.Kit == null)
            throw new Bridge.Refused($"{path} is not a NestPreset with a kit");
        VineBakeStats stats = preset.Kit.Bake ?? Stats();
        VineGrower.Context context = VineGrower.Context.For(stats, preset.Kit.Kinds);
        NestLayout layout = await Task.Run(() => NestBaker.Plan(preset, context));
        NestBaker.Result baked;
        try
        {
            baked = NestBaker.Bake(preset, layout, stats, Bridge.Flag(args, "overwrite", false));
        }
        catch (InvalidOperationException e)
        {
            throw new Bridge.Refused(e.Message);
        }

        // Every vine's .vox imported and its scene known before the nest scene is opened.
        EditorFileSystem files = EditorInterface.Singleton.GetResourceFilesystem();
        var waiting = new HashSet<string>(baked.VoxFiles);
        void Reimported(string[] paths) => waiting.ExceptWith(paths);
        files.ResourcesReimported += Reimported;
        files.Scan();
        try
        {
            int idle = 0;
            for (int i = 0; i < 3600; i++)
            {
                await Bridge.NextFrame();
                bool known = baked.VineScenes.All(p => ResourceLoader.Exists(p)) && ResourceLoader.Exists(baked.ScenePath);
                if (files.IsScanning() || !known)
                    idle = 0;
                else if (waiting.Count == 0 || ++idle > 180)
                    break;
            }
        }
        finally
        {
            files.ResourcesReimported -= Reimported;
        }
        return new JsonObject
        {
            ["scene"] = baked.ScenePath,
            ["vines"] = baked.Vines,
            ["unique_vines"] = baked.VineScenes.Count,
            ["pods"] = baked.Pods,
            ["radius_m"] = baked.RadiusM,
            ["imported"] = waiting.Count == 0,
        };
    }

    private static JsonNode Judge(JsonObject args)
    {
        VineKind kind = Find(Bridge.Text(args, "kind") ?? throw new Bridge.Refused("'kind' is missing"));
        int seed = (int)Bridge.Number(args, "seed", -1);
        if (seed < 0 || !args.ContainsKey("good"))
            throw new Bridge.Refused("'seed' and 'good' are needed");
        string note = Bridge.Text(args, "note") ?? throw new Bridge.Refused("a 'note' saying why is needed");
        bool good = Bridge.Flag(args, "good", false);
        VineGrower.Grown grown = VineGrower.Grow(kind, seed, Stats());
        if (grown.Vine.Voxels.Count == 0)
            throw new Bridge.Refused("nothing grew to judge");
        VineLogs.JudgeVine(good, note, kind, seed, grown.Attempt, false, grown.Vine.Params, grown.Vine, grown.Verdict, by: "claude");
        return new JsonObject
        {
            ["logged"] = $"{VineBaker.NameOf(kind, seed, false)} as {(good ? "good" : "bad")}",
            ["log"] = $"{VineLogs.Folder}/{VineLogs.VineVerdicts}",
        };
    }
}
#endif
