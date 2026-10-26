#if TOOLS
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DillControl.Debug;
using DillControl.Vehicles;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The asset lab's commands on the editor bridge (docs/decisions/0047): what
/// the tab does, through the same panel, so Claude sees and changes what the
/// user does. A resource saved here is the shared one; the reply names who
/// else it tunes.
/// </summary>
public static class AssetLabCommands
{
    public static Func<AssetLabPanel> Panel;

    public static void Register()
    {
        Bridge.Register("lab_assets",
            "Every asset the asset lab can show: wrapper scenes in scenes/props, vehicles, enemies, weapons, and bare .vox and .glb models. " +
            "'filter' keeps those whose path contains it.",
            """{"type":"object","properties":{"filter":{"type":"string"}}}""",
            Assets);
        Bridge.Register("lab_open",
            "Show an asset in the asset lab (by path, file name or name) and read it: size, meshes, voxel parts and chunks, a mech's damage map, " +
            "and the resources it is tuned by with who else shares each. 'show': true also brings the Lab tab to the front for the user.",
            """{"type":"object","required":["asset"],"properties":{"asset":{"type":"string"},"show":{"type":"boolean"}}}""",
            Open);
        Bridge.Register("lab_view",
            "A picture of the asset shown in the asset lab. The camera orbits it: 'yaw' and 'pitch' in degrees (0, 0 looks from its front, +Z), " +
            "'distance' in metres; 'frame': true first frames the whole asset. 'damage_map' and 'ink' switch the overlay and outlines, " +
            "'look' is 'lab' or a biome (brine_flats, rust_dunes, vinegar_frost), 'sun' the sun's elevation in degrees. 'width' default 960.",
            """
            {"type":"object","properties":{
              "yaw":{"type":"number"},"pitch":{"type":"number"},"distance":{"type":"number"},"frame":{"type":"boolean"},
              "damage_map":{"type":"boolean"},"ink":{"type":"boolean"},"look":{"type":"string"},"sun":{"type":"number"},
              "width":{"type":"integer"}
            }}
            """,
            View);
        Bridge.Register("lab_resource",
            "Read a resource the shown asset is tuned by (its file name or path), and with 'values' change it, e.g. {\"HardenedCarveScale\":0.3}. " +
            "Changes apply at once to the loaded resource; 'save': true writes the .tres, which is shared: the reply names every other scene " +
            "or resource that uses it.",
            """
            {"type":"object","required":["resource"],"properties":{
              "resource":{"type":"string"},"values":{"type":"object"},"save":{"type":"boolean"}
            }}
            """,
            ResourceCommand);
        Bridge.Register("lab_pose",
            "Pose a mech in the Lab tab, part by part, as the Dill Mech Blender add-on's live mirror does: 'mech' is its scene's name " +
            "(mch_field_fighter), 'parts' maps a part to its transform in the model's space, 12 numbers: the basis' x, y and z columns, then the origin.",
            """{"type":"object","required":["mech","parts"],"properties":{"mech":{"type":"string"},"parts":{"type":"object"}}}""",
            Pose);
        Bridge.Register("lab_walk",
            "Walk the mech shown in the Lab tab on its treadmill, as WASD does there (no physics, no Test): 'forward' and 'right' -1 to 1 " +
            "in its own frame (their length is the share of top speed), 'turn' -1 to 1 (+ to its right), 'sprint'. It keeps walking so until " +
            "told otherwise ({} stops it). 'toggle' names parts (ThighL, ArmR...) whose limbs come off, or go back on if off, as a click on them " +
            "in the tab does; the stance follows. 'click' names parts to click where they are drawn in the view, through the same picking as the mouse. 'seconds' waits that long before answering. Returns its stance, speed and gait.",
            """{"type":"object","properties":{"forward":{"type":"number"},"right":{"type":"number"},"turn":{"type":"number"},"sprint":{"type":"boolean"},"toggle":{"type":"array","items":{"type":"string"}},"click":{"type":"array","items":{"type":"string"}},"seconds":{"type":"number"}}}""",
            Walk);
        Bridge.Register("lab_play",
            "The Lab tab's timeline for the mech shown, as its play bar does (no physics, no Test). 'clip': 'walk' (none), 'combo' (the slash " +
            "combo chained as in play) or a move's short name (smash, slam_r...), read fresh from disk. 'play' true/false; 'time' seconds holds it " +
            "there, paused; 'step' frames on (or back, negative), paused; 'speed' 0.1-1 how fast time runs; 'loop'; 'trails' the fists' paths (default on); 'ghosts' poses either side of the playhead. 'seconds' waits before answering. " +
            "Returns the clip's moves with their starts, blends and markers, and where the playhead is.",
            """{"type":"object","properties":{"clip":{"type":"string"},"play":{"type":"boolean"},"time":{"type":"number"},"step":{"type":"integer"},"speed":{"type":"number"},"loop":{"type":"boolean"},"trails":{"type":"boolean"},"ghosts":{"type":"boolean"},"seconds":{"type":"number"}}}""",
            PlayClip);
        Bridge.Register("lab_timing",
            "Edit the timing of a move in the Lab tab's clip, as its timing row and the timeline's markers do: 'move' its short name (default the " +
            "one under the playhead), 'speed' times as fast as saved, 'blend' seconds (below 0: the player's), 'events' {\"hit\": seconds into the move}, " +
            "snapped to its frames. 'save' writes every edited move's .tres, stamped for the Blender add-on's Read Timing; 'revert' puts them back as saved.",
            """{"type":"object","properties":{"move":{"type":"string"},"speed":{"type":"number"},"blend":{"type":"number"},"events":{"type":"object"},"save":{"type":"boolean"},"revert":{"type":"boolean"}}}""",
            Timing);
        Bridge.Register("lab_test",
            "Play the asset lab level with an asset (default: the one shown in the Lab tab), as the tab's Test does: it stands in front of the player, " +
            "who is put in its seat if it is a vehicle (WASD drives it) or steers it if it is a walker nobody rides ('board': false to stay on foot). 'with' lists more assets stood in a row beside it (a storage bot to mend it). " +
            "Waits until the lab answers, then returns its status. Drive it with lab_do; 'stop' ends it.",
            """{"type":"object","properties":{"asset":{"type":"string"},"board":{"type":"boolean"},"with":{"type":"array","items":{"type":"string"}}}}""",
            Test);
        Bridge.Register("lab_do",
            "Tell the running asset lab (lab_test) to do something, and get its status back: fps, frame, physics and GPU ms, draws, voxels left " +
            "of those built, parts lost, loose pieces, destroyed. 'action': status | tool | time | board | leave | reset | drive | move | wait | shot. " +
            "move: plays the MechMove at 'move' (res://...), read fresh from disk. " +
            "tool: 'kind' bite, blast, cut, break or mend (fits lost limbs new and regrows lost voxels), at the crosshair or at 'at': [x,y,z], a part's name (e.g. Head, ThighL) wherever it is now, " +
            "or 'asset' (aimed from the camera); 'part' breaks a named part away ('next': a walker's next limb, legs first); " +
            "'strength' scales it (default 1). time: 'scale' (0 pauses), 'step' frames. drive: 'actions' held for 'seconds' " +
            "(move_forward, move_back, move_left, move_right, sprint, jump, fire, alt_fire). wait: 'seconds'. " +
            "shot: a picture of the screen; or round the asset where it is now, 'distance' metres away at 'yaw' and 'pitch' degrees (0, 0 is its front); " +
            "or from 'from' [x,y,z] looking at 'at' (a point, a part's name; default the asset). 'width' default 960.",
            """
            {"type":"object","required":["action"],"properties":{
              "action":{"type":"string","enum":["status","tool","time","board","leave","reset","drive","move","wait","shot"]},"move":{"type":"string"},
              "kind":{"type":"string","enum":["bite","blast","cut","break","mend"]},"at":{"description":"[x,y,z], a part's name, or 'asset'"},
              "part":{"type":"string"},"strength":{"type":"number"},"scale":{"type":"number"},"step":{"type":"integer"},
              "actions":{"type":"array","items":{"type":"string"}},"seconds":{"type":"number"},
              "from":{"type":"array","items":{"type":"number"}},"distance":{"type":"number"},"yaw":{"type":"number"},"pitch":{"type":"number"},"width":{"type":"integer"}
            }}
            """,
            Do);
    }

    /// <summary>Plays the lab level with an asset; what the tab's Test button does.</summary>
    public static void Play(string asset, bool board, string[] with = null)
    {
        if (Godot.FileAccess.FileExists(LabPlay.ListenerPath))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(LabPlay.ListenerPath));
        }
        LabPlay.Write(asset, board, with);
        EditorInterface.Singleton.PlayCustomScene(LabPlay.LevelPath);
    }

    private static async Task<JsonNode> Test(JsonObject args)
    {
        string asset = Bridge.Text(args, "asset", null);
        string path = asset == null ? Lab().Current : AssetCatalog.Find(asset)?.Path;
        if (string.IsNullOrEmpty(path))
        {
            throw new Bridge.Refused(asset == null ? "no asset shown in the Lab tab; name one" : $"no asset '{asset}'");
        }
        bool board = Bridge.Flag(args, "board", true);
        string[] with = (args["with"] as JsonArray)?.Select(w => AssetCatalog.Find((string)w)?.Path ?? (string)w).ToArray();
        // D3D12 on the Iris Xe hangs in its first second about one start in
        // three (CLAUDE.md, verified environment): a lab that does not answer
        // is started again.
        string why = "";
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await StopAndWait();
            Play(path, board, with);
            for (int frame = 0; frame < 1800 && !Godot.FileAccess.FileExists(LabPlay.ListenerPath); frame++)
            {
                await Bridge.NextFrame();
            }
            if (!Godot.FileAccess.FileExists(LabPlay.ListenerPath))
            {
                why = "the lab did not start listening; 'output' shows the game's log";
                continue;
            }
            try
            {
                // Twice, a few seconds apart: the hang comes after the first
                // frames, and a lab that answered once may not answer again.
                await Task.Delay(3000);
                await Send(new JsonObject { ["action"] = "status" }, TimeSpan.FromSeconds(10));
                await Task.Delay(2000);
                JsonNode status = await Send(new JsonObject { ["action"] = "status" }, TimeSpan.FromSeconds(10));
                status["attempts"] = attempt;
                return status;
            }
            catch (Bridge.Refused refused)
            {
                why = refused.Message + " (it froze at the start)";
            }
        }
        throw new Bridge.Refused($"three starts, none answered: {why}");
    }

    /// <summary>Stops the playing game and waits until its process has gone:
    /// a game started while the last one was still shutting down froze at
    /// its first frame now and then.</summary>
    public static async Task StopAndWait()
    {
        int pid = 0;
        if (Godot.FileAccess.FileExists(LabPlay.ListenerPath))
        {
            pid = (int?)JsonNode.Parse(Godot.FileAccess.GetFileAsString(LabPlay.ListenerPath))?["pid"] ?? 0;
        }
        if (EditorInterface.Singleton.IsPlayingScene())
        {
            EditorInterface.Singleton.StopPlayingScene();
        }
        for (int i = 0; i < 100 && pid > 0 && OS.IsProcessRunning(pid); i++)
        {
            await Task.Delay(100);
        }
        await Task.Delay(500);
    }

    private static Task<JsonNode> Do(JsonObject args) => Send(args, TimeSpan.FromSeconds(120));

    /// <summary>One request to the running lab: a JSON line with its token, a JSON line back.</summary>
    private static async Task<JsonNode> Send(JsonObject request, TimeSpan wait)
    {
        if (!EditorInterface.Singleton.IsPlayingScene() || !Godot.FileAccess.FileExists(LabPlay.ListenerPath))
        {
            throw new Bridge.Refused("the asset lab is not playing; lab_test starts it");
        }
        JsonNode info = JsonNode.Parse(Godot.FileAccess.GetFileAsString(LabPlay.ListenerPath));
        int port = (int)info["port"];
        request = (JsonObject)request.DeepClone();
        request["token"] = (string)info["token"];
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        using NetworkStream stream = client.GetStream();
        byte[] line = Encoding.UTF8.GetBytes(request.ToJsonString() + "\n");
        await stream.WriteAsync(line);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Task<string> reply = reader.ReadLineAsync();
        if (await Task.WhenAny(reply, Task.Delay(wait)) != reply || reply.Result == null)
        {
            throw new Bridge.Refused("the lab did not answer");
        }
        JsonNode answer = JsonNode.Parse(reply.Result);
        if (answer?["ok"]?.GetValue<bool>() != true)
        {
            throw new Bridge.Refused((string)answer?["error"] ?? "the lab refused");
        }
        return answer;
    }

    private static AssetLabPanel Lab() => Panel?.Invoke() ?? throw new Bridge.Refused("the asset lab is not open: is the asset_lab plugin on?");

    private static JsonNode Pose(JsonObject args)
    {
        AssetLabPanel lab = Lab();
        string mech = Bridge.Text(args, "mech");
        if (lab.Current.GetFile().GetBaseName() != mech)
        {
            AssetCatalog.Asset asset = AssetCatalog.Find(mech + ".tscn") ?? throw new Bridge.Refused($"no asset {mech}");
            lab.Open(asset.Path);
        }
        Node3D shown = lab.Preview.Asset ?? throw new Bridge.Refused("nothing shown");
        int posed = 0;
        foreach ((string name, JsonNode value) in (JsonObject)args["parts"])
        {
            if (value is not JsonArray m || m.Count != 12 || shown.FindChild(name, true, false) is not Node3D part)
            {
                continue;
            }
            float F(int i) => (float)m[i];
            part.Transform = new Transform3D(new Vector3(F(0), F(1), F(2)), new Vector3(F(3), F(4), F(5)), new Vector3(F(6), F(7), F(8)), new Vector3(F(9), F(10), F(11)));
            posed++;
        }
        lab.Preview.Redraw();
        return new JsonObject { ["posed"] = posed, ["asset"] = lab.Current };
    }

    private static async Task<JsonNode> Walk(JsonObject args)
    {
        LabPreview preview = Lab().Preview;
        if (preview.Walker == null)
        {
            throw new Bridge.Refused($"{Lab().Current} is not a walker; lab_open a mech");
        }
        preview.Drive = new Vector2((float)Bridge.Number(args, "right", 0), (float)Bridge.Number(args, "forward", 0));
        preview.DriveTurn = (float)Bridge.Number(args, "turn", 0);
        preview.DriveSprint = Bridge.Flag(args, "sprint", false);
        var toggled = new JsonArray();
        foreach (JsonNode name in args["toggle"] as JsonArray ?? new JsonArray())
        {
            Node3D part = preview.Walker.Model.FindChild((string)name, true, false) as Node3D
                ?? throw new Bridge.Refused($"no part {(string)name}");
            toggled.Add(preview.Toggle(part));
        }
        foreach (JsonNode name in args["click"] as JsonArray ?? new JsonArray())
        {
            Node3D part = preview.Walker.Model.FindChild((string)name, true, false) as Node3D
                ?? throw new Bridge.Refused($"no part {(string)name}");
            Vector3 centre = Voxel.VoxDamageMap.TryRead(part) is Voxel.DamageBake bake ? part.GlobalTransform * bake.Centre : part.GlobalPosition;
            Vector2 at = preview.Camera.UnprojectPosition(centre);
            Node3D hit = preview.PartAt(at);
            toggled.Add($"clicked {(string)name} at {at.X:0},{at.Y:0}: hit {hit?.Name ?? "nothing"}; {preview.Toggle(hit)}");
        }
        ulong until = Time.GetTicksMsec() + (ulong)(Bridge.Number(args, "seconds", 0) * 1000.0);
        while (Time.GetTicksMsec() < until)
        {
            await Bridge.NextFrame();
        }
        return new JsonObject { ["toggled"] = toggled, ["status"] = preview.Walker.BenchStatus(), ["belt"] = Bridge.Json(preview.Walker.BeltTravel) };
    }

    private static async Task<JsonNode> PlayClip(JsonObject args)
    {
        AssetLabPanel lab = Lab();
        LabPreview preview = lab.Preview;
        if (preview.Walker == null)
        {
            throw new Bridge.Refused($"{lab.Current} is not a walker; lab_open a mech");
        }
        if (Bridge.Text(args, "clip", null) is string clip)
        {
            string name = clip == "walk" ? AssetLabPanel.WalkOnly : clip == "combo" ? AssetLabPanel.ComboClip : clip;
            if (!lab.LoadClip(name))
            {
                throw new Bridge.Refused($"no clip '{clip}': walk, combo, or one of " + string.Join(", ", lab.MovesOf(preview.Walker).Select(p => p.GetFile().GetBaseName())));
            }
        }
        LabTransport transport = preview.Transport;
        if (args.ContainsKey("loop"))
        {
            transport.Loop = Bridge.Flag(args, "loop", true);
        }
        if (args.ContainsKey("trails"))
        {
            lab.ShowTrails(Bridge.Flag(args, "trails", true));
        }
        if (args.ContainsKey("ghosts"))
        {
            lab.ShowGhosts(Bridge.Flag(args, "ghosts", false));
        }
        if (args.ContainsKey("speed"))
        {
            preview.TimeScale = Mathf.Clamp((float)Bridge.Number(args, "speed", 1), 0.1f, 1.0f);
        }
        if (args.ContainsKey("time"))
        {
            preview.Seek((float)Bridge.Number(args, "time", 0));
        }
        if (args.ContainsKey("step"))
        {
            preview.StepFrames((int)Bridge.Number(args, "step", 1));
        }
        if (args.ContainsKey("play"))
        {
            transport.Playing = Bridge.Flag(args, "play", true);
        }
        ulong until = Time.GetTicksMsec() + (ulong)(Bridge.Number(args, "seconds", 0) * 1000.0);
        do
        {
            // At least a frame, so what was set is posed before a lab_view.
            await Bridge.NextFrame();
        }
        while (Time.GetTicksMsec() < until);

        var segments = new JsonArray();
        foreach (LabTransport.Segment segment in transport.Segments)
        {
            var events = new JsonObject();
            for (int e = 0; e < segment.Move.EventNames.Length && e < segment.Move.EventTimes.Length; e++)
            {
                events[segment.Move.EventNames[e]] = Math.Round(segment.Start + segment.Move.EventTimes[e], 3);
            }
            segments.Add(new JsonObject
            {
                ["move"] = LabTimeline.ShortName(segment.Move),
                ["start"] = Math.Round(segment.Start, 3),
                ["length"] = Math.Round(segment.Move.Length, 3),
                ["blend"] = Math.Round(segment.Blend, 3),
                ["events"] = events,
            });
        }
        LabTransport.Segment? now = transport.At(transport.Time, out float into);
        return new JsonObject
        {
            ["clip"] = transport.Clip,
            ["time"] = Math.Round(transport.Time, 3),
            ["length"] = Math.Round(transport.Length, 3),
            ["frame"] = Mathf.RoundToInt(transport.Time * transport.Fps),
            ["playing"] = transport.Playing,
            ["loop"] = transport.Loop,
            ["speed"] = preview.TimeScale,
            ["now"] = now == null ? null : FormattableString.Invariant($"{LabTimeline.ShortName(now.Value.Move)} at {into:0.000} s"),
            ["walker_move"] = preview.Walker.PlayingMove == null ? null : LabTimeline.ShortName(preview.Walker.PlayingMove),
            ["segments"] = segments,
        };
    }

    private static JsonNode Timing(JsonObject args)
    {
        AssetLabPanel lab = Lab();
        LabTransport transport = lab.Preview.Transport;
        if (transport.Segments.Count == 0)
        {
            throw new Bridge.Refused("no clip in the Lab tab; lab_play one");
        }
        if (Bridge.Flag(args, "revert", false))
        {
            lab.RevertTiming();
        }
        string name = Bridge.Text(args, "move", null);
        MechMove move = name == null
            ? lab.TimingMove
            : transport.Segments.Select(s => s.Move).FirstOrDefault(m => LabTimeline.ShortName(m) == name)
              ?? throw new Bridge.Refused($"no move '{name}' in the clip: " + string.Join(", ", transport.Segments.Select(s => LabTimeline.ShortName(s.Move))));
        if (args.ContainsKey("speed"))
        {
            lab.SetSpeed(move, Mathf.Clamp((float)Bridge.Number(args, "speed", 1), 0.25f, 4.0f));
        }
        if (args.ContainsKey("blend"))
        {
            float blend = (float)Bridge.Number(args, "blend", -1);
            lab.SetBlend(move, blend < 0.0f ? -1.0f : blend);
        }
        if (args["events"] is JsonObject events)
        {
            foreach ((string key, JsonNode value) in events)
            {
                int index = Array.IndexOf(move.EventNames, key);
                if (index < 0 || index >= move.EventTimes.Length)
                {
                    throw new Bridge.Refused($"{LabTimeline.ShortName(move)} has no marker '{key}'");
                }
                float into = Mathf.Clamp((float)value, 0.0f, move.Length);
                lab.SetMarker(move, index, Mathf.Round(into * move.Fps) / move.Fps);
            }
        }
        var result = new JsonObject();
        if (Bridge.Flag(args, "save", false))
        {
            result["saved"] = new JsonArray(lab.SaveTiming().Select(p => (JsonNode)p).ToArray());
        }
        var marks = new JsonObject();
        for (int e = 0; e < move.EventNames.Length && e < move.EventTimes.Length; e++)
        {
            marks[move.EventNames[e]] = Math.Round(move.EventTimes[e], 4);
        }
        result["move"] = LabTimeline.ShortName(move);
        result["fps"] = Math.Round(move.Fps, 3);
        result["speed"] = Math.Round(lab.SpeedOf(move), 3);
        result["length"] = Math.Round(move.Length, 4);
        result["blend"] = move.Blend;
        result["events"] = marks;
        result["stamp"] = move.TimingStamp;
        result["clip_length"] = Math.Round(transport.Length, 3);
        return result;
    }

    private static JsonNode Assets(JsonObject args)
    {
        string filter = Bridge.Text(args, "filter", "").ToLowerInvariant();
        var list = new JsonArray();
        foreach (AssetCatalog.Asset asset in AssetCatalog.All().Where(a => filter == "" || a.Path.ToLowerInvariant().Contains(filter)))
        {
            list.Add(new JsonObject { ["path"] = asset.Path, ["group"] = asset.Group });
        }
        return new JsonObject { ["count"] = list.Count, ["assets"] = list };
    }

    private static JsonNode Open(JsonObject args)
    {
        AssetLabPanel lab = Lab();
        AssetCatalog.Asset asset = AssetCatalog.Find(Bridge.Text(args, "asset"))
            ?? throw new Bridge.Refused($"no asset '{Bridge.Text(args, "asset")}'; lab_assets lists them");
        lab.Open(asset.Path);
        if (lab.Current != asset.Path)
        {
            throw new Bridge.Refused($"{asset.Path} did not load as a scene");
        }
        if (Bridge.Flag(args, "show", false))
        {
            EditorInterface.Singleton.SetMainScreenEditor("Lab");
        }
        return Described(lab);
    }

    private static JsonObject Described(AssetLabPanel lab)
    {
        JsonObject info = lab.Info.Json();
        var shared = new JsonObject();
        foreach (string resource in lab.Info.Resources)
        {
            shared[resource] = new JsonArray(lab.SharedWith(resource).Select(u => (JsonNode)u).ToArray());
        }
        info["shared_with"] = shared;
        return info;
    }

    private static async Task<JsonNode> View(JsonObject args)
    {
        AssetLabPanel lab = Lab();
        LabPreview preview = lab.Preview;
        if (preview.Asset == null)
        {
            throw new Bridge.Refused("no asset shown; lab_open one first");
        }
        if (Bridge.Flag(args, "frame", false))
        {
            preview.Frame();
        }
        if (args.ContainsKey("yaw"))
        {
            preview.Yaw = Mathf.DegToRad((float)Bridge.Number(args, "yaw", 0));
        }
        if (args.ContainsKey("pitch"))
        {
            preview.Pitch = Mathf.DegToRad((float)Bridge.Number(args, "pitch", 0));
        }
        if (args.ContainsKey("distance"))
        {
            preview.Distance = (float)Bridge.Number(args, "distance", preview.Distance);
        }
        preview.PlaceCamera();
        if (args.ContainsKey("damage_map"))
        {
            preview.DamageMap = Bridge.Flag(args, "damage_map", true);
        }
        if (args.ContainsKey("ink"))
        {
            preview.Ink = Bridge.Flag(args, "ink", true);
        }
        if (args.ContainsKey("look"))
        {
            string look = Bridge.Text(args, "look");
            if (look == "lab")
            {
                preview.SetLook(null);
            }
            else
            {
                string path = $"res://resources/planet/biomes/biome_{look}.tres";
                preview.SetLook(ResourceLoader.Exists(path) ? GD.Load<Planet.PlanetBiome>(path) : throw new Bridge.Refused($"no biome {path}"));
            }
        }
        if (args.ContainsKey("sun"))
        {
            preview.SunElevation = (float)Bridge.Number(args, "sun", 45);
        }
        int width = (int)Bridge.Number(args, "width", 960);
        Image image = await preview.Capture(new Vector2I(width, width * 9 / 16));
        if (image == null || image.IsEmpty())
        {
            throw new Bridge.Refused("the lab drew nothing");
        }
        return new JsonObject
        {
            ["image_png"] = Convert.ToBase64String(image.SavePngToBuffer()),
            ["asset"] = lab.Current,
            ["camera"] = new JsonObject
            {
                ["yaw"] = Math.Round(Mathf.RadToDeg(preview.Yaw), 1),
                ["pitch"] = Math.Round(Mathf.RadToDeg(preview.Pitch), 1),
                ["distance"] = Math.Round(preview.Distance, 2),
                ["target"] = Bridge.Json(preview.Target),
            },
        };
    }

    private static JsonNode ResourceCommand(JsonObject args)
    {
        AssetLabPanel lab = Lab();
        string name = Bridge.Text(args, "resource");
        Resource resource = lab.Resources.FirstOrDefault(r => r.ResourcePath == name || r.ResourcePath.GetFile() == name || r.ResourcePath.GetFile().GetBaseName() == name)
            ?? throw new Bridge.Refused($"'{name}' does not tune {lab.Current}; lab_open lists its resources");
        var changed = new JsonArray();
        if (args["values"] is JsonObject values)
        {
            foreach ((string key, JsonNode value) in values)
            {
                Godot.Collections.Dictionary property = resource.GetPropertyList().FirstOrDefault(p => p["name"].AsString() == key)
                    ?? throw new Bridge.Refused($"{resource.ResourcePath.GetFile()} has no property {key}");
                resource.Set(key, Values.FromJson(value, (Variant.Type)property["type"].AsInt64(), property["class_name"].AsString(), key));
                changed.Add(key);
            }
            resource.EmitChanged();
        }
        var result = new JsonObject { ["resource"] = resource.ResourcePath, ["changed"] = changed };
        if (Bridge.Flag(args, "save", false))
        {
            Error error = ResourceSaver.Save(resource, resource.ResourcePath);
            if (error != Error.Ok)
            {
                throw new Bridge.Refused($"could not save {resource.ResourcePath}: {error}");
            }
            result["saved"] = true;
            result["shared_with"] = new JsonArray(lab.SharedWith(resource.ResourcePath).Select(u => (JsonNode)u).ToArray());
        }
        var properties = new JsonObject();
        foreach (Godot.Collections.Dictionary property in resource.GetPropertyList())
        {
            var usage = (PropertyUsageFlags)property["usage"].AsInt64();
            if ((usage & PropertyUsageFlags.ScriptVariable) != 0 && (usage & PropertyUsageFlags.Storage) != 0)
            {
                string key = property["name"].AsString();
                properties[key] = Values.ToJson(resource.Get(key));
            }
        }
        result["properties"] = properties;
        return result;
    }
}
#endif
