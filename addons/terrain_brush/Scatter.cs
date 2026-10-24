#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DillControl.Core;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// Painting things onto a <see cref="SculptedTerrain"/> (docs/decisions/0044):
/// spots on the ground, kept apart from everything standing in Foliage and
/// Props, off steep ground and out of water, thinning toward the brush's
/// edge; and real instances of scenes made on them, each the node the user
/// would drag in, stood on the ground with a random turn. Scatter, Forest
/// and Vines all paint through one of these a stroke; a stroke is one undo.
/// </summary>
public sealed class Scatter
{
    public struct Settings
    {
        public float RadiusM;
        /// <summary>Instances per 1,000 m² the brush fills up to: it never adds where there are this many already.</summary>
        public float Density;
        /// <summary>No closer than this to anything already standing.</summary>
        public float SpacingM;
        public float MaxSlopeDeg;
        /// <summary>The share of the radius at full density; outside it thins to nothing at the edge. 1 is even all over.</summary>
        public float Hardness;
    }

    private readonly SculptedTerrain _terrain;
    private readonly Node _root;
    private readonly Dictionary<string, PackedScene> _scenes = new();
    public readonly SeededRandom Random;

    // Everything standing, so nothing lands in another. A spot taken but not
    // yet built (a vine still baking) has no node; its scene names what it will be.
    private readonly List<Standing> _standing = new();
    public readonly List<Node3D> Added = new();
    public readonly List<(Node3D Node, Node Parent, int Index)> Removed = new();

    private sealed class Standing
    {
        public Vector3 At;
        public Node3D Node;
        public string Scene;
    }

    public Scatter(SculptedTerrain terrain)
    {
        _terrain = terrain;
        _root = EditorInterface.Singleton.GetEditedSceneRoot();
        // The brush's own stream, from the clock: where a stroke puts things is the user's doing, not a contract's.
        Random = SeededRandom.Stream(Time.GetTicksUsec(), "editor:scatter");
        foreach (Node3D n in Branches(_root).SelectMany(b => b.FindChildren("*", nameof(Node3D), true, true).OfType<Node3D>())
                     .Where(n => !string.IsNullOrEmpty(n.SceneFilePath)))
            _standing.Add(new Standing { At = n.GlobalPosition, Node = n, Scene = n.SceneFilePath });
    }

    private static IEnumerable<Node> Branches(Node root) =>
        new[] { "Foliage", "Props" }.Select(b => root.GetNodeOrNull(b)).Where(b => b != null);

    /// <summary>Instances in Foliage and Props whose scene <paramref name="mine"/> says yes to.</summary>
    public static int Count(Node root, Func<string, bool> mine) =>
        Branches(root).Sum(b => b.FindChildren("*", "", true, true).Count(n => !string.IsNullOrEmpty(n.SceneFilePath) && mine(n.SceneFilePath)));

    public static int Count(Node root, ICollection<string> palette) => Count(root, palette.Contains);

    /// <summary>
    /// New spots in one dab at world (x, z), filling up to the density of
    /// what <paramref name="mine"/> counts. Each is taken at once, as
    /// <paramref name="scene"/>, so later spots keep away from it.
    /// </summary>
    public List<Vector3> Spots(float x, float z, Settings s, Func<string, bool> mine, string scene)
    {
        var spots = new List<Vector3>();
        ArenaHeights h = _terrain.Heights;
        float area = Mathf.Pi * s.RadiusM * s.RadiusM;
        int want = Mathf.Max(1, Mathf.RoundToInt(s.Density * area / 1000.0f));
        int here = _standing.Count(t => new Vector2(t.At.X - x, t.At.Z - z).Length() < s.RadiusM && mine(t.Scene));
        float maxSlope = Mathf.DegToRad(s.MaxSlopeDeg);
        float hard = Mathf.Clamp(s.Hardness <= 0.0f ? 1.0f : s.Hardness, 0.0f, 1.0f);
        // A few tries for every one still missing; a try too near, too steep, wet or past the falloff is dropped.
        for (int attempt = 0; attempt < (want - here) * 4 && here < want; attempt++)
        {
            float a = Random.Range(0.0f, Mathf.Tau);
            float d = Mathf.Sqrt(Random.NextFloat());
            float px = x + (Mathf.Cos(a) * d * s.RadiusM), pz = z + (Mathf.Sin(a) * d * s.RadiusM);
            if (hard < 1.0f && d > hard)
            {
                float t = (d - hard) / (1.0f - hard);
                if (Random.NextFloat() > 1.0f - (t * t * (3.0f - (2.0f * t))))
                    continue;
            }
            if (!h.Contains(px, pz) || h.SlopeAt(px, pz, _terrain.Spacing) > maxSlope)
                continue;
            if (_standing.Any(t => new Vector2(t.At.X - px, t.At.Z - pz).Length() < s.SpacingM))
                continue;
            var at = new Vector3(px, h.HeightAt(px, pz), pz);
            if (WaterBody.DepthAt(at) > 0.0f)
                continue;
            _standing.Add(new Standing { At = at, Scene = scene });
            spots.Add(at);
            here++;
        }
        return spots;
    }

    /// <summary>One dab of instances of the palette's scenes.</summary>
    public void Paint(float x, float z, Settings s, IList<string> palette)
    {
        if (palette.Count == 0)
            return;
        foreach (Vector3 at in Spots(x, z, s, palette.Contains, palette[0]))
        {
            Node3D node = Make(palette[Random.RangeInt(0, palette.Count - 1)], at, Random.Range(0.0f, Mathf.Tau));
            Standing spot = _standing.Last(t => t.At == at);
            spot.Node = node;
            if (node != null)
                spot.Scene = node.SceneFilePath;
        }
    }

    /// <summary>One dab of taking away what <paramref name="mine"/> says yes to, within the radius.</summary>
    public void Erase(float x, float z, Settings s, Func<string, bool> mine)
    {
        for (int i = _standing.Count - 1; i >= 0; i--)
        {
            Standing t = _standing[i];
            if (t.Node == null || new Vector2(t.At.X - x, t.At.Z - z).Length() >= s.RadiusM || !mine(t.Scene) || !t.Node.IsInsideTree())
                continue;
            _standing.RemoveAt(i);
            Node3D node = t.Node;
            if (Added.Remove(node))
            {
                node.GetParent().RemoveChild(node);
                node.QueueFree();
                continue;
            }
            Node parent = node.GetParent();
            Removed.Add((node, parent, node.GetIndex()));
            parent.RemoveChild(node);
        }
    }

    /// <summary>An instance of a scene stood at a spot, as the user would drag it in.</summary>
    public Node3D Make(string path, Vector3 at, float yaw)
    {
        if (!_scenes.TryGetValue(path, out PackedScene packed))
            _scenes[path] = packed = ResourceLoader.Load<PackedScene>(path);
        if (packed?.Instantiate(PackedScene.GenEditState.Instance) is not Node3D node)
            return null;
        Node parent = PlaceCommands.DefaultParent(_root, path);
        node.Name = PlaceCommands.NameOf(path);
        parent.AddChild(node, true);
        node.Owner = _root;
        node.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), at);
        Added.Add(node);
        return node;
    }

    /// <summary>The stroke as one undo; everything it did is already done.</summary>
    public void Record(EditorUndoRedoManager undo)
    {
        foreach (Node3D node in Added)
        {
            Node parent = node.GetParent();
            undo.AddDoMethod(parent, Node.MethodName.AddChild, node, true);
            undo.AddDoProperty(node, Node.PropertyName.Owner, _root);
            undo.AddDoReference(node);
            undo.AddUndoMethod(parent, Node.MethodName.RemoveChild, node);
        }
        foreach ((Node3D node, Node parent, int index) in Removed)
        {
            undo.AddDoMethod(parent, Node.MethodName.RemoveChild, node);
            undo.AddUndoMethod(parent, Node.MethodName.AddChild, node, true);
            undo.AddUndoMethod(parent, Node.MethodName.MoveChild, node, index);
            undo.AddUndoProperty(node, Node.PropertyName.Owner, _root);
            undo.AddUndoReference(node);
        }
    }

    public bool Changed => Added.Count > 0 || Removed.Count > 0;

    /// <summary>The scenes in scenes/props whose names start with any of <paramref name="prefixes"/>.</summary>
    public static List<string> ScenesStarting(IEnumerable<string> prefixes)
    {
        string[] p = prefixes.ToArray();
        return DirAccess.GetFilesAt("res://scenes/props").Where(f => f.EndsWith(".tscn") && p.Any(f.StartsWith))
            .OrderBy(f => f).Select(f => $"res://scenes/props/{f}").ToList();
    }
}
#endif
