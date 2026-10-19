using System.Collections.Generic;
using System.Text;
using Godot;
using DillControl.Voxel;

namespace DillControl.Debug;

/// <summary>
/// Development views for watching destruction, switched by words after
/// <c>--</c> on the command line (any order, any bench mode):
/// <c>sink</c> follows every loose piece's collision boxes and prints the
/// frames before one goes more than <see cref="Depth"/> under the ground;
/// <c>nodust</c> hides the dust (collision boxes are DustLab's <c>boxes</c>).
/// Not part of the game: <see cref="PerfDriver"/> adds it only when asked.
/// </summary>
public partial class SinkProbe : Node
{
    /// <summary>How far under the ground plane a box corner may go before it counts.</summary>
    private const float Depth = 1.0f;
    private const int History = 40;

    private bool _sink, _noDust;
    private float _groundY;
    private int _frame;
    private readonly Dictionary<ulong, Queue<string>> _history = new();
    private readonly HashSet<ulong> _reported = new();
    private readonly HashSet<ulong> _lost = new();
    private readonly Dictionary<ulong, int> _airborne = new();
    private readonly HashSet<ulong> _misfit = new();
    private readonly Dictionary<ulong, int> _misfitFrames = new();
    private readonly Dictionary<ulong, int> _under = new();
    private readonly Dictionary<ulong, (int voxels, int shapes)> _last = new();

    public static bool Wanted(string[] args)
    {
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
        if (!_sink || VoxelDebris.Current == null)
        {
            return;
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
                GD.Print(detail.ToString());
                GD.Print($"[sink] MISFIT {chunk.Name} ({voxels}v, {shapes} boxes) f{_frame}: voxels {vlow:F2}..{vtop:F2} boxes {lowest:F2}..{top:F2} sleep={chunk.Sleeping} frozen={chunk.Freeze}");
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
                GD.Print($"[sink] AIRBORNE {chunk.Name} ({voxels}v) still for 2 s, bottom {lowest:F2} m, nothing under it: " +
                    $"sleep={chunk.Sleeping} frozen={chunk.Freeze} settled={chunk.IsSettled} f{_frame}");
            }
        }
                _under[id] = shapes > 0 && top < _groundY ? _under.GetValueOrDefault(id) + 1 : 0;
        // Half a second wholly under: not a piece the sunk-piece catch saved.
        if (_under[id] >= 30 && _lost.Add(id))
        {
            GD.Print($"[sink] LOST {chunk.Name} ({voxels}v): every box under the ground, top={top:F2} at f{_frame}");
        }
        if (voxels >= 1000 && _frame % 300 == 0)
        {
            GD.Print($"[sink] big {chunk.Name} ({voxels}v) f{_frame}: origin y={chunk.GlobalPosition.Y:F2} boxes {lowest:F2}..{top:F2}");
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
            GD.Print(report.ToString());
        }
    }
}
