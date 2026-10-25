using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DillControl.Core;
using DillControl.Levels;
using DillControl.Player;
using DillControl.Vehicles;
using DillControl.Voxel;
using DillControl.Weapons;
using Godot;

namespace DillControl.Debug;

/// <summary>
/// The lab level's half of the asset lab (docs/decisions/0047): stands the
/// asset the tab asked for in front of the player, seats the player in it
/// if it is a vehicle (WASD drives it), or steers a walker that cannot be
/// ridden; gives the lab's tools and time controls; shows what it costs;
/// and answers the editor's <c>lab_*</c> commands through <see cref="LabListener"/>.
///
/// Keys (letters only, rule 6): L help, N board/leave or steer, Y next tool,
/// X use it at the crosshair, - and = weaker and stronger, P pause, O one
/// frame, T slower, K put the asset back.
/// </summary>
public partial class AssetLabDirector : Node
{
    [Export]
    public AssetLabStats Stats { get; set; }

    [Export]
    public LevelRoot Level { get; set; }

    /// <summary>Where the asset stands when no player has placed it.</summary>
    [Export]
    public Marker3D Spot { get; set; }

    /// <summary>Shown when the level is played without a request from the tab.</summary>
    [Export(PropertyHint.File, "*.tscn,*.vox,*.glb")]
    public string DefaultAsset { get; set; } = "res://scenes/vehicles/mch_field_fighter.tscn";

    [Export]
    public PackedScene ExplosionScene { get; set; }

    private enum Tool { Bite, Blast, Cut, Break, Mend }

    private string _assetPath;
    private bool _boardAtStart;
    private string[] _withPaths = System.Array.Empty<string>();
    private readonly List<Node3D> _with = new();
    private Node3D _asset;
    private PlayerController _player;
    private LabListener _listener;
    private Tool _tool = Tool.Blast;
    private float _strength = 1.0f;
    private int _slow;
    private bool _paused;
    private bool _help = true;
    private bool _steering;
    private Camera3D _steerCamera;
    private float _steerYaw;
    private float _steerPitch = -0.35f;
    private Label _hud;
    private double _hudTimer;
    private readonly Queue<string> _log = new();

    public override void _Ready()
    {
        Stats ??= GD.Load<AssetLabStats>("res://resources/debug/asset_lab_default.tres");
        Godot.Collections.Dictionary request = LabPlay.Take();
        _assetPath = request?.GetValueOrDefault("asset", DefaultAsset).AsString() ?? DefaultAsset;
        _boardAtStart = request?.GetValueOrDefault("board", true).AsBool() ?? true;
        _withPaths = request?.GetValueOrDefault("with", new string[0]).AsStringArray() ?? System.Array.Empty<string>();

        var layer = new CanvasLayer { Layer = 50 };
        _hud = new Label { Position = new Vector2(16, 16) };
        _hud.AddThemeFontSizeOverride("font_size", 18);
        _hud.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.06f, 0.05f));
        _hud.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_hud);
        AddChild(layer);

        _listener = new LabListener(Handle);
        _listener.Start();
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

        if (Level != null)
        {
            Level.PlayerSpawned += OnPlayerSpawned;
        }
        // The level is still making its children: the asset joins after.
        CallDeferred(MethodName.Begin);
    }

    private async void Begin()
    {
        Spawn();
        if (!_boardAtStart)
        {
            return;
        }
        // The player and the vehicle's own brain settle in their first frames.
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        _player ??= Level?.Player as PlayerController;
        if (_player != null)
        {
            BoardOrSteer();
        }
    }

    public override void _ExitTree()
    {
        _listener?.Stop();
        Engine.TimeScale = 1.0;
        if (Level != null)
        {
            Level.PlayerSpawned -= OnPlayerSpawned;
        }
    }

    private void OnPlayerSpawned(Node3D player) => _player = player as PlayerController;

    // ---------------- The asset ----------------

    private void Spawn()
    {
        if (_asset != null && IsInstanceValid(_asset))
        {
            Leave();
            _asset.QueueFree();
        }
        _asset = null;
        var scene = ResourceLoader.Load<PackedScene>(_assetPath);
        if (scene == null)
        {
            Note($"{_assetPath} does not load");
            return;
        }
        Node made = scene.Instantiate();
        if (_assetPath.EndsWith(".vox") && made is Node3D model)
        {
            // A bare model breaks as a building does, standing where it is.
            model.Name = "Model";
            var holder = new Node3D { Name = _assetPath.GetFile().GetBaseName() };
            holder.AddChild(model);
            holder.AddChild(new VoxelBody { Name = "Voxels", Model = model, Static = true });
            made = holder;
        }
        _asset = made as Node3D ?? new Node3D { Name = "Asset" };
        if (_asset != made)
        {
            _asset.AddChild(made);
        }
        Transform3D at = Spot?.GlobalTransform ?? Transform3D.Identity;
        GetParent().AddChild(_asset);
        _asset.GlobalTransform = at;
        Note($"{_assetPath.GetFile()} placed");

        // Company for it, in a row to its right: a storage bot to mend it.
        foreach (Node3D other in _with)
        {
            if (IsInstanceValid(other))
            {
                other.QueueFree();
            }
        }
        _with.Clear();
        for (int i = 0; i < _withPaths.Length; i++)
        {
            if (ResourceLoader.Load<PackedScene>(_withPaths[i])?.Instantiate() is Node3D other)
            {
                GetParent().AddChild(other);
                other.GlobalTransform = at.Translated(at.Basis.X * (10.0f * (i + 1)));
                _with.Add(other);
            }
        }
    }

    private IRideable Rideable => _asset == null ? null
        : _asset.FindChildren("*", "", true, false).OfType<IRideable>().FirstOrDefault(r => r.CanRide);

    private MechWalker Walker => _asset as MechWalker ?? _asset?.FindChildren("*", nameof(CharacterBody3D), true, false).OfType<MechWalker>().FirstOrDefault();

    private List<VoxelBody> Bodies => _asset == null ? new List<VoxelBody>()
        : _asset.FindChildren("*", "", true, false).OfType<VoxelBody>().ToList();

    private void BoardOrSteer()
    {
        if (Rideable is IRideable ride)
        {
            ride.BoardNow();
            Note(ride.IsRidden ? "in the seat: WASD drives" : "could not board");
        }
        else if (Walker != null)
        {
            Steer(true);
        }
    }

    private void Leave()
    {
        if (Rideable is IRideable { IsRidden: true } ride)
        {
            ride.LeaveNow();
        }
        if (_steering)
        {
            Steer(false);
        }
    }

    private bool InControl => _steering || Rideable is IRideable { IsRidden: true };

    /// <summary>A walker nobody can ride, steered with WASD from a chase camera;
    /// its own brain (a companion's following) is off meanwhile.</summary>
    private void Steer(bool on)
    {
        MechWalker walker = Walker;
        if (walker == null || _player == null)
        {
            return;
        }
        _steering = on;
        foreach (Node brain in walker.GetChildren().Where(c => c is MechCompanion))
        {
            brain.ProcessMode = on ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        }
        _player.SetStowed(on);
        if (on)
        {
            _steerCamera ??= new Camera3D { Fov = 70.0f };
            if (_steerCamera.GetParent() == null)
            {
                AddChild(_steerCamera);
            }
            _steerYaw = walker.GlobalRotation.Y;
            _steerCamera.MakeCurrent();
            Note("steering: WASD walks it");
        }
        else
        {
            walker.MoveIntent = Vector3.Zero;
            walker.Sprint = false;
            _steerCamera?.ClearCurrent(false);
            _player.GlobalPosition = walker.GlobalPosition + walker.GlobalBasis.X * 5.0f + Vector3.Up * 0.5f;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_steering || Walker is not MechWalker walker || walker.IsDestroyed)
        {
            if (_steering)
            {
                Steer(false);
            }
            return;
        }
        Vector2 stick = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var basis = new Basis(Vector3.Up, _steerYaw);
        Vector3 intent = basis * new Vector3(stick.X, 0.0f, stick.Y);
        walker.MoveIntent = intent.LimitLength(1.0f);
        walker.FaceDirection = intent.LengthSquared() > 0.01f ? intent.Normalized() : Vector3.Zero;
        walker.Sprint = Input.IsActionPressed("sprint");
        Vector3 offset = basis * new Basis(Vector3.Right, _steerPitch) * new Vector3(0.0f, 0.0f, 16.0f);
        Vector3 target = walker.GlobalPosition + Vector3.Up * 3.0f;
        _steerCamera.GlobalPosition = target + offset;
        _steerCamera.LookAt(target, Vector3.Up);
    }

    // ---------------- Keys ----------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_steering && @event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _steerYaw -= motion.Relative.X * 0.004f;
            _steerPitch = Mathf.Clamp(_steerPitch - motion.Relative.Y * 0.004f, -1.3f, 0.3f);
        }
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        bool handled = true;
        switch (key.PhysicalKeycode)
        {
            case Key.L: _help = !_help; break;
            case Key.N:
                if (InControl)
                {
                    Leave();
                }
                else
                {
                    BoardOrSteer();
                }
                break;
            case Key.Y: _tool = (Tool)(((int)_tool + 1) % 5); _strength = 1.0f; break;
            case Key.X: Use(_tool, Aim()); break;
            case Key.Minus: _strength /= Stats.StrengthStep; break;
            case Key.Equal: _strength *= Stats.StrengthStep; break;
            case Key.P: SetPaused(!_paused); break;
            case Key.O: _ = StepFrames(1); break;
            case Key.T: _slow = (_slow + 1) % Mathf.Max(1, Stats.SlowScales.Length); ApplyTime(); break;
            case Key.K: Spawn(); break;
            default: handled = false; break;
        }
        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------------- Time ----------------

    private float SlowScale => Stats.SlowScales.Length > 0 ? Stats.SlowScales[_slow % Stats.SlowScales.Length] : 1.0f;

    private void ApplyTime() => Engine.TimeScale = _paused ? 0.0 : SlowScale;

    private void SetPaused(bool paused)
    {
        _paused = paused;
        ApplyTime();
    }

    private async Task StepFrames(int frames)
    {
        _paused = true;
        for (int i = 0; i < Mathf.Max(1, frames); i++)
        {
            Engine.TimeScale = 1.0;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        Engine.TimeScale = 0.0;
    }

    // ---------------- Tools ----------------

    private readonly record struct Hit(Vector3 Point, Vector3 Normal, Vector3 Direction, GodotObject Collider);

    /// <summary>What the crosshair is on: a ray from the camera being looked through.</summary>
    private Hit? Aim()
    {
        Camera3D camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            return null;
        }
        Vector3 from = camera.GlobalPosition;
        Vector3 direction = -camera.GlobalBasis.Z;
        return Ray(from, direction);
    }

    private Hit? Ray(Vector3 from, Vector3 direction)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, from + direction * 600.0f);
        if (_player != null)
        {
            query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
        }
        Godot.Collections.Dictionary hit = GetViewport().World3D.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return null;
        }
        return new Hit(hit["position"].AsVector3(), hit["normal"].AsVector3(), direction, hit["collider"].AsGodotObject());
    }

    private string Use(Tool tool, Hit? aimed, string part = null)
    {
        string result;
        if (tool == Tool.Mend)
        {
            result = Mend();
        }
        else if (tool == Tool.Break && part != null)
        {
            result = BreakNamed(part, aimed?.Direction ?? Vector3.Forward);
        }
        else if (aimed is not Hit hit)
        {
            result = "nothing under the crosshair";
        }
        else
        {
            result = tool switch
            {
                Tool.Bite => Bite(hit),
                Tool.Blast => BlastAt(hit),
                Tool.Cut => Cut(hit),
                _ => BreakNearest(hit),
            };
        }
        Note(result);
        return result;
    }

    private string Bite(Hit hit)
    {
        IDamageable target = hit.Collider as IDamageable;
        if (target is IDamageRelay relay)
        {
            target = relay.Receiver;
        }
        if (target == null)
        {
            return $"bite: {(hit.Collider as Node)?.Name} takes no damage";
        }
        float damage = Stats.BiteDamage * _strength;
        target.ApplyDamage(new DamageInfo { Amount = damage, BaseAmount = damage, Position = hit.Point, Normal = hit.Normal, Direction = hit.Direction, Source = _player });
        return $"bite of {damage:0} on {(hit.Collider as Node)?.Name}";
    }

    private string BlastAt(Hit hit)
    {
        float radius = Stats.BlastRadius * Mathf.Sqrt(_strength);
        float damage = Stats.BlastDamage * _strength;
        var blast = new Blast.Settings
        {
            Centre = hit.Point + hit.Normal * 0.3f,
            InnerRadius = radius * 0.3f,
            Radius = radius,
            Damage = damage,
            MinMultiplier = 0.2f,
            Impulse = Stats.BlastImpulse,
            ImpulseUpBias = 0.5f,
            Up = Vector3.Up,
            Source = _player,
            SelfImpulseScale = 0.0f,
            NoCrater = true,
        };
        PhysicsDirectSpaceState3D space = GetViewport().World3D.DirectSpaceState;
        int hurt = Blast.ApplyDamage(space, blast);
        Blast.ApplyToRigidBodies(space, blast, 1.0f);
        if (ExplosionScene?.Instantiate() is Node3D effect)
        {
            (effect as ExplosionEffect)?.Configure(blast.Centre, radius, Vector3.Up, hit.Normal, false);
            GetParent().AddChild(effect);
            effect.GlobalPosition = blast.Centre;
        }
        return $"blast of {damage:0} over {radius:0.0} m: {hurt} hit";
    }

    /// <summary>A slit right through every part at the aim's height.</summary>
    private string Cut(Hit hit)
    {
        int cut = 0;
        float thickness = Stats.CutThickness * _strength;
        foreach (VoxelPart part in Bodies.SelectMany(b => b.Parts).Where(p => !p.IsEmpty))
        {
            if (part.GetParent() is not Node3D host)
            {
                continue;
            }
            float z = part.Grid.ToVox(host.ToLocal(hit.Point)).Z * part.Grid.VoxelSize;
            float bottom = z - thickness * 0.5f;
            if (bottom > part.Grid.Size.Z * part.Grid.VoxelSize || bottom + thickness < 0.0f)
            {
                continue;
            }
            part.Cut(Mathf.Max(0.0f, bottom), thickness, 1.0f);
            cut++;
        }
        return cut > 0 ? $"cut {thickness:0.0} m through {cut} parts" : "cut: no voxel part at that height";
    }

    /// <summary>Repair played at once (docs/decisions/0046): every lost limb
    /// fitted new, then the voxels gone from the rest regrown, as many as the
    /// strength allows.</summary>
    private string Mend()
    {
        if (Walker is not MechWalker walker)
        {
            int back = 0;
            foreach (VoxelPart part in Bodies.SelectMany(b => b.Parts))
            {
                back += part.Restore((int)(Stats.MendVoxels * _strength));
            }
            return $"mended {back} voxels";
        }
        int limbs = 0, voxels = 0;
        foreach (Node3D limb in walker.LostLimbs())
        {
            voxels += walker.Refit(limb);
            limbs++;
        }
        voxels += walker.Regrow((int)(Stats.MendVoxels * _strength));
        return $"mended: {limbs} limbs fitted, {voxels} voxels back";
    }

    private string BreakNearest(Hit hit)
    {
        VoxelPart nearest = null;
        float best = float.MaxValue;
        foreach (VoxelPart part in Bodies.SelectMany(b => b.Parts).Where(p => !p.IsEmpty))
        {
            float d = part.NearestPoint(hit.Point).DistanceSquaredTo(hit.Point);
            if (d < best)
            {
                best = d;
                nearest = part;
            }
        }
        if (nearest == null)
        {
            return "break: no voxel part";
        }
        string name = (nearest.GetParent() as Node)?.Name ?? "?";
        nearest.BreakAway(hit.Direction * Stats.BreakPush * _strength);
        return $"broke {name} away";
    }

    private string BreakNamed(string name, Vector3 direction)
    {
        if (name == "next")
        {
            return Walker?.BenchBreakNext() == true ? "broke the next limb away" : "break: no limb left";
        }
        foreach (VoxelBody body in Bodies)
        {
            foreach (Node host in body.Model?.FindChildren("*", "", true, false) ?? new Godot.Collections.Array<Node>())
            {
                if (host.Name == name && host is Node3D node && body.PartOf(node) is VoxelPart part && !part.IsEmpty)
                {
                    part.BreakAway(direction * Stats.BreakPush * _strength);
                    return $"broke {name} away";
                }
            }
        }
        return $"break: no part named {name} left";
    }

    // ---------------- What it costs ----------------

    public override void _Process(double delta)
    {
        _listener?.Poll(delta);
        _hudTimer -= delta;
        if (_hudTimer > 0.0)
        {
            return;
        }
        _hudTimer = 0.25;
        _hud.Text = HudText();
    }

    private Godot.Collections.Dictionary Measure()
    {
        int solid = 0, initial = 0, parts = 0, lost = 0;
        foreach (VoxelPart part in Bodies.SelectMany(b => b.Parts))
        {
            parts++;
            lost += part.IsEmpty ? 1 : 0;
            solid += part.Grid?.SolidCount ?? 0;
            initial += part.Grid?.InitialCount ?? 0;
        }
        return new Godot.Collections.Dictionary
        {
            ["asset"] = _assetPath,
            ["fps"] = Performance.GetMonitor(Performance.Monitor.TimeFps),
            ["frame_ms"] = Mathf.Snapped(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0, 0.01),
            ["physics_ms"] = Mathf.Snapped(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0, 0.01),
            ["gpu_ms"] = Mathf.Snapped(RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()), 0.01),
            ["draws"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            ["voxels"] = solid,
            ["voxels_built"] = initial,
            ["parts"] = parts,
            ["parts_lost"] = lost,
            ["loose_pieces"] = VoxelDebris.Current?.ChunkCount ?? 0,
            ["destroyed"] = Walker?.IsDestroyed ?? false,
            ["stance"] = Walker?.CurrentStance.ToString().ToLowerInvariant() ?? "",
            ["drop_m"] = Mathf.Snapped(Walker?.BodyDrop ?? 0.0f, 0.01),
            ["position"] = _asset == null ? new Godot.Collections.Array() : new Godot.Collections.Array
            {
                Mathf.Snapped(_asset.GlobalPosition.X, 0.01), Mathf.Snapped(_asset.GlobalPosition.Y, 0.01), Mathf.Snapped(_asset.GlobalPosition.Z, 0.01),
            },
            ["speed"] = Mathf.Snapped((_asset as CharacterBody3D)?.Velocity.Length() ?? 0.0f, 0.01),
            ["grounded"] = Walker?.IsGrounded ?? false,
            ["blockers"] = Walker?.Blockers ?? "",
            ["missing_voxels"] = Walker?.MissingVoxels() ?? 0,
            ["lost_limbs"] = Walker?.LostLimbs().Count ?? 0,
            ["care"] = Walker?.GetNodeOrNull<MechCare>("Care")?.State ?? "",
            ["with"] = WithStatus(),
            ["ledger"] = LedgerStatus(),
            ["in_control"] = InControl,
            ["tool"] = _tool.ToString().ToLowerInvariant(),
            ["strength"] = Mathf.Snapped(_strength, 0.01),
            ["time_scale"] = Engine.TimeScale,
        };
    }

    private Godot.Collections.Array WithStatus()
    {
        var rows = new Godot.Collections.Array();
        foreach (Node3D other in _with)
        {
            if (!IsInstanceValid(other) || other is not MechWalker walker)
            {
                continue;
            }
            var mender = walker.GetNodeOrNull<MechMender>("Mender");
            rows.Add($"{walker.Name}: {walker.CurrentStance}, missing {walker.MissingVoxels()}, at {walker.GlobalPosition.DistanceTo(_asset?.GlobalPosition ?? Vector3.Zero):0.0} m"
                + (mender?.Patient != null ? $", mending {mender.Patient.Name}" : ""));
        }
        return rows;
    }

    private static Godot.Collections.Array LedgerStatus()
    {
        var rows = new Godot.Collections.Array();
        foreach (Mission.Session.LedgerLine line in Mission.Session.Instance?.Ledger ?? new List<Mission.Session.LedgerLine>())
        {
            rows.Add($"{line.For}: {line.What} x{line.Units} at {line.UnitPrice} = {line.Total}");
        }
        return rows;
    }

    private string HudText()
    {
        Godot.Collections.Dictionary m = Measure();
        var text = new StringBuilder();
        text.AppendLine($"LAB  {_assetPath.GetFile()}   {m["fps"]} fps   frame {m["frame_ms"]} ms   physics {m["physics_ms"]} ms   gpu {m["gpu_ms"]} ms   draws {m["draws"]}");
        if (m["voxels_built"].AsInt32() > 0)
        {
            string stance = m["stance"].AsString() is { Length: > 0 } st ? $"   {st}" : "";
            text.AppendLine($"voxels {m["voxels"]} / {m["voxels_built"]}   parts lost {m["parts_lost"]} / {m["parts"]}   loose pieces {m["loose_pieces"]}{stance}{(m["destroyed"].AsBool() ? "   DESTROYED" : "")}");
        }
        text.AppendLine($"tool {_tool} x{_strength:0.##}   time x{Engine.TimeScale:0.##}{(_paused ? " (paused)" : "")}");
        if (_help)
        {
            text.AppendLine("N board/leave or steer   Y tool   X use at crosshair   - = strength   P pause   O one frame   T slower   K put back   L hide this");
        }
        foreach (string line in _log)
        {
            text.AppendLine(line);
        }
        return text.ToString();
    }

    private void Note(string line)
    {
        _log.Enqueue(line);
        while (_log.Count > 4)
        {
            _log.Dequeue();
        }
        _hudTimer = 0.0;
    }

    // ---------------- The editor's commands ----------------

    private async Task<Godot.Collections.Dictionary> Handle(Godot.Collections.Dictionary request)
    {
        string action = request.GetValueOrDefault("action", "status").AsString();
        var result = new Godot.Collections.Dictionary();
        switch (action)
        {
            case "status":
                break;
            case "tool":
            {
                Tool tool = request.GetValueOrDefault("kind", "blast").AsString() switch
                {
                    "bite" => Tool.Bite,
                    "cut" => Tool.Cut,
                    "break" => Tool.Break,
                    "mend" => Tool.Mend,
                    _ => Tool.Blast,
                };
                _strength = (float)request.GetValueOrDefault("strength", 1.0).AsDouble();
                Hit? hit = Aim();
                if (request.ContainsKey("at"))
                {
                    // A point, or a part of the asset by name wherever it is
                    // now: aimed at from the camera.
                    Vector3 from = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
                    if (request["at"].VariantType == Variant.Type.String)
                    {
                        // Straight onto the part: a ray from a seat inside
                        // the asset would start inside its hull and miss it.
                        Vector3 at = PartCentre(request["at"].AsString());
                        Vector3 direction = (at - from).LengthSquared() > 1e-4f ? (at - from).Normalized() : Vector3.Forward;
                        GodotObject target = _asset as GodotObject is IDamageable ? _asset : Bodies.FirstOrDefault();
                        hit = new Hit(at, -direction, direction, target);
                    }
                    else
                    {
                        Vector3 at = ToVector(request["at"]);
                        hit = Ray(from, (at - from).Normalized());
                    }
                }
                result["result"] = Use(tool, hit, request.GetValueOrDefault("part", "").AsString() is { Length: > 0 } part ? part : null);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                break;
            }
            case "time":
                if (request.ContainsKey("scale"))
                {
                    float scale = (float)request["scale"].AsDouble();
                    _slow = 0;
                    _paused = scale <= 0.0f;
                    Engine.TimeScale = Mathf.Max(0.0f, scale);
                }
                if (request.ContainsKey("step"))
                {
                    await StepFrames((int)request["step"].AsInt64());
                }
                break;
            case "board":
                BoardOrSteer();
                break;
            case "leave":
                Leave();
                break;
            case "reset":
                Spawn();
                break;
            case "drive":
                await Drive(request);
                break;
            case "wait":
                await ToSignal(GetTree().CreateTimer(request.GetValueOrDefault("seconds", 1.0).AsDouble(), true, false, true), SceneTreeTimer.SignalName.Timeout);
                break;
            case "shot":
                result["image_png"] = await Shot(request);
                break;
            default:
                throw new System.ArgumentException($"no action {action}: status, tool, time, board, leave, reset, drive, wait, shot");
        }
        foreach ((Variant key, Variant value) in Measure())
        {
            result[key] = value;
        }
        result["log"] = new Godot.Collections.Array(_log.Select(l => Variant.From(l)).ToArray());
        return result;
    }

    /// <summary>Holds the given input actions (move_forward, sprint, jump,
    /// fire...) for some seconds, as if pressed: drives whatever has control.</summary>
    private async Task Drive(Godot.Collections.Dictionary request)
    {
        var actions = request.GetValueOrDefault("actions", new Godot.Collections.Array { "move_forward" }).AsGodotArray().Select(a => a.AsString()).ToList();
        foreach (string action in actions)
        {
            Input.ActionPress(action);
        }
        try
        {
            await ToSignal(GetTree().CreateTimer(request.GetValueOrDefault("seconds", 2.0).AsDouble(), true, false, true), SceneTreeTimer.SignalName.Timeout);
        }
        finally
        {
            foreach (string action in actions)
            {
                Input.ActionRelease(action);
            }
        }
    }

    /// <summary>Where a part of the asset is now, by its node's name ("asset"
    /// for the whole): its centre of mass if it has a damage map.</summary>
    private Vector3 PartCentre(string name)
    {
        if (_asset == null)
        {
            throw new System.ArgumentException("no asset");
        }
        if (name == "asset")
        {
            return _asset.GlobalPosition + Vector3.Up;
        }
        foreach (Node node in _asset.FindChildren("*", "", true, false))
        {
            if (node is Node3D part && node.Name == name)
            {
                return VoxDamageMap.TryRead(part) is DamageBake bake ? part.GlobalTransform * bake.Centre : part.GlobalPosition;
            }
        }
        throw new System.ArgumentException($"the asset has no part named {name}");
    }

    /// <summary>What is on screen, or from a point of view given (drawn into
    /// a viewport of its own, so the game's cameras are left alone), as a PNG
    /// in base64.</summary>
    private async Task<string> Shot(Godot.Collections.Dictionary request)
    {
        int width = (int)request.GetValueOrDefault("width", 960).AsInt64();
        SubViewport own = null;
        if (request.ContainsKey("distance") && !request.ContainsKey("from"))
        {
            // Round the asset where it is now: yaw 0 looks at its front.
            Vector3 centre = PartCentre("asset");
            float yaw = Mathf.DegToRad((float)request.GetValueOrDefault("yaw", 30.0).AsDouble()) + (_asset?.GlobalRotation.Y ?? 0.0f);
            float pitch = Mathf.DegToRad((float)request.GetValueOrDefault("pitch", 15.0).AsDouble());
            float distance = (float)request["distance"].AsDouble();
            Vector3 offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * distance;
            request["from"] = new Godot.Collections.Array { centre.X + offset.X, centre.Y + offset.Y, centre.Z + offset.Z };
            request["at"] = new Godot.Collections.Array { centre.X, centre.Y, centre.Z };
        }
        if (request.ContainsKey("from"))
        {
            Vector2I size = GetViewport().GetVisibleRect().Size is var s && s.X > 0 ? (Vector2I)s : new Vector2I(1920, 1080);
            own = new SubViewport { World3D = GetViewport().World3D, Size = size, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            var view = new Camera3D { Fov = 60.0f, Current = true };
            own.AddChild(view);
            AddChild(own);
            Vector3 from = ToVector(request["from"]);
            Vector3 at = !request.ContainsKey("at") ? PartCentre("asset")
                : request["at"].VariantType == Variant.Type.String ? PartCentre(request["at"].AsString()) : ToVector(request["at"]);
            view.GlobalPosition = from;
            view.LookAt(at, Vector3.Up);
            if (GD.Load<Shader>("res://shaders/toon_ink.gdshader") is Shader ink)
            {
                view.AddChild(new MeshInstance3D
                {
                    Mesh = new QuadMesh { Size = new Vector2(2.0f, 2.0f) },
                    MaterialOverride = new ShaderMaterial { Shader = ink },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    ExtraCullMargin = 16384.0f,
                    SortingOffset = 1000.0f,
                });
            }
        }
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image image = (own != null ? own.GetTexture() : GetViewport().GetTexture()).GetImage();
            if (image.GetWidth() > width)
            {
                image.Resize(width, image.GetHeight() * width / image.GetWidth(), Image.Interpolation.Lanczos);
            }
            return System.Convert.ToBase64String(image.SavePngToBuffer());
        }
        finally
        {
            own?.QueueFree();
        }
    }

    private static Vector3 ToVector(Variant v)
    {
        Godot.Collections.Array a = v.AsGodotArray();
        return new Vector3((float)a[0].AsDouble(), (float)a[1].AsDouble(), (float)a[2].AsDouble());
    }
}
