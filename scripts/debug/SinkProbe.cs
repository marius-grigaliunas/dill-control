using System.Collections.Generic;
using System.Text;
using Godot;
using DillControl.Core;
using DillControl.Voxel;

namespace DillControl.Debug;

/// <summary>
/// Development views for watching destruction, switched by words after
/// <c>--</c> on the command line (any order, any bench mode):
/// <c>sink</c> follows every loose piece's collision boxes and prints the
/// frames before one goes more than <see cref="Depth"/> under the ground;
/// <c>nodust</c> hides the dust (collision boxes are DustLab's <c>boxes</c>);
/// <c>noprobe</c> switches it off for measuring. While U records it also
/// writes a census of the loose pieces every 5 s (awake, asleep, frozen, their
/// boxes) and of every body in the physics world every 30 s. It runs last in
/// the physics tick, so its time is billed to "physics step [engine]": about
/// 4 ms a tick with a hundred pieces, a census frame 30-45 ms.
/// Not part of the game: <see cref="PerfDriver"/> adds it only when asked.
/// </summary>
public partial class SinkProbe : Node
{
    /// <summary>How far under the ground plane a box corner may go before it counts.</summary>
    private const float Depth = 1.0f;
    private const int History = 40;

    private bool _sink, _noDust, _off;
    private float _groundY;
    private int _frame;
    private readonly Dictionary<ulong, Queue<string>> _history = new();
    private readonly HashSet<ulong> _reported = new();
    private readonly HashSet<ulong> _lost = new();
    private readonly Dictionary<ulong, int> _stuck = new();
    private readonly Dictionary<ulong, int> _hinged = new();
    private readonly Dictionary<ulong, int> _airborne = new();
    private readonly HashSet<ulong> _misfit = new();
    private readonly Dictionary<ulong, int> _misfitFrames = new();
    private readonly Dictionary<ulong, int> _under = new();
    private readonly Dictionary<ulong, (int voxels, int shapes)> _last = new();

    public static bool Wanted(string[] args)
    {
        // Always there: with no words it only watches while U records, and
        // writes what it finds beside the recording.
        return true;
        foreach (string a in args)
        {
            if (a is "sink" or "nodust" or "drops")
            {
                return true;
            }
        }
        return false;
    }

    public override void _Ready()
    {
        var args = new HashSet<string>(OS.GetCmdlineUserArgs());
        _sink = args.Contains("sink");
        _noDust = args.Contains("nodust");
        // Measuring: the probe runs last in the physics tick and is billed to
        // "physics step [engine]" (a census walks every piece, 1-45 ms).
        _off = args.Contains("noprobe");
        VoxelPart.WatchDrops = args.Contains("drops");
        if (_sink)
        {
            VoxelDebris.SpawnedBy = new();
        }
        ProcessPhysicsPriority = int.MaxValue;
        GD.Print($"[sink] probe on: sink={_sink} nodust={_noDust}");
    }

    public override void _Process(double delta)
    {
        if (_noDust && VoxelDebris.Current?.GetNodeOrNull<Node3D>("Dust") is Node3D dust && dust.Visible)
        {
            dust.Visible = false;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;
        bool watching = !_off && (_sink || PerfLog.Recording);
        if (!watching || VoxelDebris.Current == null)
        {
            return;
        }
        if (_frame % 60 == 0)
        {
            WatchStrays();
        }
        if (_frame % 300 == 0)
        {
            Census();
        }
        foreach (Node child in VoxelDebris.Current.GetChildren())
        {
            if (child is VoxelChunk chunk)
            {
                Watch(chunk);
            }
        }
    }

    private void Watch(VoxelChunk chunk)
    {
        ulong id = chunk.GetInstanceId();
        float lowest = float.MaxValue, top = float.MinValue;
        int shapes = 0;
        Transform3D body = chunk.GlobalTransform;
        foreach (uint owner in chunk.GetShapeOwners())
        {
            if (chunk.IsShapeOwnerDisabled(owner))
            {
                continue;
            }
            Transform3D at = body * chunk.ShapeOwnerGetTransform(owner);
            for (int s = 0; s < chunk.ShapeOwnerGetShapeCount(owner); s++)
            {
                if (chunk.ShapeOwnerGetShape(owner, s) is not BoxShape3D box)
                {
                    continue;
                }
                shapes++;
                Vector3 h = box.Size * 0.5f;
                for (int k = 0; k < 8; k++)
                {
                    var corner = new Vector3((k & 1) == 0 ? -h.X : h.X, (k & 2) == 0 ? -h.Y : h.Y, (k & 4) == 0 ? -h.Z : h.Z);
                    lowest = Mathf.Min(lowest, (at * corner).Y);
                    top = Mathf.Max(top, (at * corner).Y);
                }
            }
        }

        int voxels = chunk.VoxelCount;
        // Its boxes against its own voxels: they must cover the same space.
        if (shapes > 0 && chunk.Grid != null && chunk.Grid.SolidBounds(out Aabb cells) && !_misfit.Contains(id))
        {
            float vlow = float.MaxValue, vtop = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                float y = (body * cells.GetEndpoint(k)).Y;
                vlow = Mathf.Min(vlow, y);
                vtop = Mathf.Max(vtop, y);
            }
            float off = Mathf.Max(Mathf.Abs(vlow - lowest), Mathf.Abs(vtop - top));
            // Collision is built over a few frames; a second of it is not that.
            _misfitFrames[id] = off > 8.0f * chunk.Grid.VoxelSize + 0.5f ? _misfitFrames.GetValueOrDefault(id) + 1 : 0;
            if (_misfitFrames[id] >= 60)
            {
                _misfit.Add(id);
                var detail = new StringBuilder(); detail.AppendLine($"  node-space voxels {cells.Position}..{cells.End}");
                foreach (uint o in chunk.GetShapeOwners())
                {
                    Transform3D t = chunk.ShapeOwnerGetTransform(o);
                    for (int b = 0; b < chunk.ShapeOwnerGetShapeCount(o); b++)
                    {
                        if (chunk.ShapeOwnerGetShape(o, b) is BoxShape3D bs)
                        {
                            detail.AppendLine($"  box at {t.Origin} size {bs.Size} disabled={chunk.IsShapeOwnerDisabled(o)}");
                        }
                    }
                }
                Report(detail.ToString());
                Report($"[sink] MISFIT {chunk.Name} ({voxels}v, {shapes} boxes) f{_frame}: voxels {vlow:F2}..{vtop:F2} boxes {lowest:F2}..{top:F2} sleep={chunk.Sleeping} frozen={chunk.Freeze}");
            }
        }
        string what = "";
        if (_last.TryGetValue(id, out var last))
        {
            if (voxels != last.voxels) what += $" voxels {last.voxels}->{voxels}";
            if (shapes != last.shapes) what += $" boxes {last.shapes}->{shapes}";
        }
        _last[id] = (voxels, shapes);

        if (!_history.TryGetValue(id, out Queue<string> lines))
        {
            lines = new Queue<string>();
            _history[id] = lines;
        }
        Vector3 v = chunk.LinearVelocity;
        lines.Enqueue($"  f{_frame} bottom={lowest:F2} centre={chunk.GlobalPosition.Y:F2} vy={v.Y:F2} |v|={v.Length():F1} " +
            $"mass={chunk.Mass:F0} frozen={chunk.Freeze} sleep={chunk.Sleeping} settled={chunk.IsSettled} " +
            $"crushing={chunk.CrushingThrough} ccd={chunk.ContinuousCd}{what}");
        while (lines.Count > History)
        {
            lines.Dequeue();
        }

        // Drawn, but out of the world: no boxes, collision switched off, or
        // frozen without being settled rubble. A piece swapped for its pieces
        // stays drawn so; one that stays so is stuck between the two.
        bool inSpace = PhysicsServer3D.BodyGetSpace(chunk.GetRid()).IsValid;
        bool outOfWorld = shapes == 0 || chunk.CollisionLayer == 0 || (chunk.Freeze && !chunk.IsSettled) || !inSpace;
        _hinged[id] = chunk.IsHinged ? _hinged.GetValueOrDefault(id) + 1 : 0;
        if (_hinged[id] == 60 * 8)
        {
            Report($"[sink] HINGE-STUCK {chunk.Name} ({voxels}v) on its hinge 8 s: bottom={lowest:F2} |v|={chunk.LinearVelocity.Length():F2} " +
                $"spin={chunk.AngularVelocity.Length():F3} sleep={chunk.Sleeping} inSpace={inSpace} f{_frame}");
        }
        _stuck[id] = outOfWorld && chunk.Visible && !chunk.IsRetired ? _stuck.GetValueOrDefault(id) + 1 : 0;
        if (_stuck[id] == 180)
        {
            var report = new StringBuilder();
            report.AppendLine($"[sink] STUCK {chunk.Name} ({voxels}v) drawn for 3 s out of the world: boxes={shapes} " +
                $"layer={chunk.CollisionLayer} frozen={chunk.Freeze} settled={chunk.IsSettled} breaking={chunk.IsBreaking} " +
                $"hinged={chunk.IsHinged} inSpace={inSpace} bottom={lowest:F2} f{_frame}");
            foreach (string line in _history[id])
            {
                report.AppendLine(line);
            }
            Report(report.ToString());
        }
                // Still, with nothing under it: resting on what has gone since.
        if (shapes > 0 && _frame % 30 == 0 && !chunk.IsHeld && chunk.LinearVelocity.Length() < 0.1f)
        {
            Vector3 from = chunk.GlobalPosition;
            from.Y = lowest + 0.05f;
            using var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 0.6f, uint.MaxValue,
                new Godot.Collections.Array<Rid> { chunk.GetRid() });
            bool under = chunk.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
            _airborne[id] = under ? 0 : _airborne.GetValueOrDefault(id) + 1;
            if (_airborne[id] == 4)
            {
                Report($"[sink] AIRBORNE {chunk.Name} ({voxels}v) still for 2 s, bottom {lowest:F2} m, nothing under it: " +
                    $"sleep={chunk.Sleeping} frozen={chunk.Freeze} settled={chunk.IsSettled} f{_frame}");
            }
        }
                _under[id] = shapes > 0 && top < _groundY ? _under.GetValueOrDefault(id) + 1 : 0;
        // Half a second wholly under: not a piece the sunk-piece catch saved.
        if (_under[id] >= 30 && _lost.Add(id))
        {
            Report($"[sink] LOST {chunk.Name} ({voxels}v): every box under the ground, top={top:F2} at f{_frame}");
        }
        if (voxels >= 1000 && _frame % 300 == 0)
        {
            Report($"[sink] big {chunk.Name} ({voxels}v) f{_frame}: origin y={chunk.GlobalPosition.Y:F2} boxes {lowest:F2}..{top:F2}");
        }
        if (shapes > 0 && lowest < _groundY - Depth && _reported.Add(id))
        {
            var report = new StringBuilder();
            report.AppendLine($"[sink] {chunk.Name} ({voxels}v) has a box {(_groundY - lowest):F2} m under the ground at f{_frame}:");
            foreach (string line in lines)
            {
                report.AppendLine(line);
            }
            if (chunk.Grid != null && VoxelDebris.SpawnedBy != null && VoxelDebris.SpawnedBy.TryGetValue(chunk.Grid, out string by))
            {
                report.AppendLine("  spawned " + by);
            }
            Report(report.ToString());
        }
    }

    /// <summary>
    /// Every loose piece and what the physics carries of it: whether it is
    /// awake, asleep or frozen, its boxes and shape owners, how fast it moves
    /// and spins, and where. A piece awake for long is in the solver every step
    /// with every box it has, so the totals by state are the step's load.
    /// </summary>
    private void Census()
    {
        var awake = new List<VoxelChunk>();
        int asleep = 0, frozen = 0, asleepBoxes = 0, frozenBoxes = 0, awakeBoxes = 0, owners = 0;
        foreach (Node child in VoxelDebris.Current.GetChildren())
        {
            if (child is not VoxelChunk chunk || chunk.IsRetired)
            {
                continue;
            }
            int boxes = EnabledBoxes(chunk);
            owners += chunk.GetShapeOwners().Length;
            if (chunk.Freeze)
            {
                frozen++;
                frozenBoxes += boxes;
            }
            else if (chunk.Sleeping)
            {
                asleep++;
                asleepBoxes += boxes;
            }
            else
            {
                awake.Add(chunk);
                awakeBoxes += boxes;
            }
        }
        var report = new StringBuilder();
        report.AppendLine($"[sink] CENSUS f{_frame}: awake {awake.Count} ({awakeBoxes} boxes)  asleep {asleep} ({asleepBoxes})  " +
            $"frozen {frozen} ({frozenBoxes})  owners {owners}");
        awake.Sort((a, b) => _awakeFor.GetValueOrDefault(b.GetInstanceId()).CompareTo(_awakeFor.GetValueOrDefault(a.GetInstanceId())));
        var seen = new HashSet<ulong>();
        foreach (VoxelChunk chunk in awake)
        {
            ulong id = chunk.GetInstanceId();
            seen.Add(id);
            int since = _awakeFor.GetValueOrDefault(id) + 1;
            _awakeFor[id] = since;
            Vector3 p = chunk.GlobalPosition;
            string moved = _censusAt.TryGetValue(id, out Vector3 was) ? $"{p.DistanceTo(was):F1}m" : "-";
            _censusAt[id] = p;
            report.AppendLine($"  {chunk.Name} {chunk.VoxelCount}v boxes={EnabledBoxes(chunk)} owners={chunk.GetShapeOwners().Length} " +
                $"awake {since * 5}s moved {moved} on {Under(chunk)} |v|={chunk.LinearVelocity.Length():F2} spin={chunk.AngularVelocity.Length():F2} " +
                $"still={chunk.StillFor:F1} hinged={chunk.IsHinged} held={chunk.IsHeld} refused={chunk.BakeRefused} " +
                $"bottom={chunk.LowestBox(Vector3.Up):F2} at ({p.X:F0},{p.Y:F1},{p.Z:F0})");
        }
        foreach (ulong id in new List<ulong>(_awakeFor.Keys))
        {
            if (!seen.Contains(id))
            {
                _awakeFor.Remove(id);
            }
        }
        // Everything else in the physics world, by kind: what a step carries
        // when no piece is moving.
        // Walks the whole tree (30-45 ms at 30 000 nodes): every sixth census.
        var kinds = new SortedDictionary<string, (int bodies, int shapes, int inSpace)>();
        if (_frame % 1800 == 0)
        {
            CountBodies(GetTree().Root, kinds);
        }
        foreach ((string kind, (int bodies, int shapes, int inSpace) n) in kinds)
        {
            report.AppendLine($"  world {kind}: {n.bodies} bodies ({n.inSpace} in space), {n.shapes} shapes");
        }
        Report(report.ToString());
    }

    private static void CountBodies(Node node, SortedDictionary<string, (int, int, int)> kinds)
    {
        if (node is CollisionObject3D body)
        {
            Rid rid = body.GetRid();
            bool area = body is Area3D;
            int shapes = area ? PhysicsServer3D.AreaGetShapeCount(rid) : PhysicsServer3D.BodyGetShapeCount(rid);
            bool inSpace = (area ? PhysicsServer3D.AreaGetSpace(rid) : PhysicsServer3D.BodyGetSpace(rid)).IsValid;
            string mode = body is RigidBody3D rb ? (rb.Freeze ? " frozen" : rb.Sleeping ? " asleep" : " awake") : "";
            string kind = $"{body.GetType().Name}{mode} under {node.GetParent()?.GetType().Name}";
            (int b, int s, int i) = kinds.GetValueOrDefault(kind);
            kinds[kind] = (b + 1, s + shapes, i + (inSpace ? 1 : 0));
        }
        foreach (Node child in node.GetChildren())
        {
            CountBodies(child, kinds);
        }
    }

    /// <summary>Census rounds a piece has been awake for in a row.</summary>
    private readonly Dictionary<ulong, int> _awakeFor = new();
    private readonly Dictionary<ulong, Vector3> _censusAt = new();

    /// <summary>What is straight under a piece's lowest box, by kind.</summary>
    private static string Under(VoxelChunk chunk)
    {
        float lowest = chunk.LowestBox(Vector3.Up);
        if (float.IsNaN(lowest))
        {
            return "-";
        }
        Vector3 from = chunk.GlobalPosition;
        from.Y = lowest + 0.3f;
        using var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 2.0f, uint.MaxValue,
            new Godot.Collections.Array<Rid> { chunk.GetRid() });
        Godot.Collections.Dictionary hit = chunk.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return "nothing";
        }
        float gap = from.Y - 0.3f - hit["position"].AsVector3().Y;
        return $"{hit["collider"].AsGodotObject()?.GetType().Name}({gap:F2})";
    }

    private static int EnabledBoxes(VoxelChunk chunk)
    {
        int boxes = 0;
        foreach (uint owner in chunk.GetShapeOwners())
        {
            if (!chunk.IsShapeOwnerDisabled(owner))
            {
                boxes += chunk.ShapeOwnerGetShapeCount(owner);
            }
        }
        return boxes;
    }

    private readonly Dictionary<ulong, int> _strays = new();

    private void WatchStrays()
    {
        foreach (VoxelPart part in VoxelPart.All)
        {
            if (!IsInstanceValid(part))
            {
                continue;
            }
            ulong id = part.GetInstanceId();
            int stray = part.StrayMeshes();
            _strays[id] = stray > 0 ? _strays.GetValueOrDefault(id) + 1 : 0;
            if (_strays[id] == 3)
            {
                Node owner = part.GetParent();
                Report($"[sink] STRAY {stray} meshes drawn under {owner?.Name}/{part.Name} for 3 s that the part no longer tracks, " +
                    $"at {part.GlobalPosition}, owner {owner?.GetType().Name} f{_frame}");
            }
        }
    }

    private string _file;

    /// <summary>Printed, and while U records written beside the recording
    /// (perf_<time>_probe.txt) with a tag in that frame's events column.</summary>
    private void Report(string text)
    {
        // A console line costs its frame 15-40 ms: while U records, the file only.
        if (_sink || !PerfLog.Recording)
        {
            GD.Print(text);
        }
        if (!PerfLog.Recording)
        {
            return;
        }
        string first = text.Split(System.Environment.NewLine[^1])[0];
        PerfLog.Event("probe " + (first.Length > 80 ? first.Substring(0, 80) : first));
        string csv = (GetParent() as PerfDriver)?.Recorder?.CurrentPath;
        if (string.IsNullOrEmpty(csv))
        {
            return;
        }
        _file = System.IO.Path.ChangeExtension(csv, null) + "_probe.txt";
        try
        {
            System.IO.File.AppendAllText(_file, text.TrimEnd() + System.Environment.NewLine);
        }
        catch (System.IO.IOException)
        {
        }
    }
}
