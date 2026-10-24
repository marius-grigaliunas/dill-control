#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DillControl.Core;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// The scatter brush (docs/decisions/0044): paints real instances of the
/// scenes in a palette onto a <see cref="SculptedTerrain"/>, each the node
/// the user would drag in, stood on the ground with a random turn, kept
/// apart and off steep ground; or takes them away. The same for the brush in
/// the 3D view and for the bridge's <c>scatter</c>. A stroke is one undo.
/// </summary>
public sealed class Scatter
{
    public struct Settings
    {
        public float RadiusM;
        /// <summary>Instances per 1,000 m² the brush fills up to: it never adds where there are this many already.</summary>
        public float Density;
        /// <summary>No closer than this to anything already standing in Foliage or Props.</summary>
        public float SpacingM;
        public float MaxSlopeDeg;
    }

    private readonly SculptedTerrain _terrain;
    private readonly Node _root;
    private readonly List<string> _palette;
    private readonly Dictionary<string, PackedScene> _scenes = new();
    private readonly SeededRandom _random;
    // Everything standing, so nothing lands in another; what this stroke made or took.
    private readonly List<(Vector3 At, Node3D Node)> _standing = new();
    public readonly List<Node3D> Added = new();
    public readonly List<(Node3D Node, Node Parent, int Index)> Removed = new();

    public Scatter(SculptedTerrain terrain, IEnumerable<string> palette)
    {
        _terrain = terrain;
        _root = EditorInterface.Singleton.GetEditedSceneRoot();
        _palette = palette.ToList();
        foreach (string path in _palette)
            _scenes[path] = ResourceLoader.Load<PackedScene>(path);
        // The brush's own stream, from the clock: where a stroke puts things is the user's doing, not a contract's.
        _random = SeededRandom.Stream(Time.GetTicksUsec(), "editor:scatter");
        foreach (string branch in new[] { "Foliage", "Props" })
        {
            Node b = _root.GetNodeOrNull(branch);
            if (b == null)
                continue;
            foreach (Node3D n in b.FindChildren("*", nameof(Node3D), true, true).OfType<Node3D>().Where(n => !string.IsNullOrEmpty(n.SceneFilePath)))
                _standing.Add((n.GlobalPosition, n));
        }
    }

    /// <summary>Instances of the palette's scenes in Foliage and Props.</summary>
    public static int Count(Node root, ICollection<string> palette) =>
        new[] { "Foliage", "Props" }.Select(b => root.GetNodeOrNull(b)).Where(b => b != null)
            .Sum(b => b.FindChildren("*", "", true, true).Count(n => palette.Contains(n.SceneFilePath)));

    /// <summary>One dab of adding at world (x, z).</summary>
    public void Paint(float x, float z, Settings s)
    {
        if (_palette.Count == 0)
            return;
        ArenaHeights h = _terrain.Heights;
        float area = Mathf.Pi * s.RadiusM * s.RadiusM;
        int want = Mathf.Max(1, Mathf.RoundToInt(s.Density * area / 1000.0f));
        int here = _standing.Count(t => new Vector2(t.At.X - x, t.At.Z - z).Length() < s.RadiusM && _palette.Contains(t.Node.SceneFilePath));
        float maxSlope = Mathf.DegToRad(s.MaxSlopeDeg);
        // A few tries for every one still missing; a try that lands too near or too steep is dropped.
        for (int attempt = 0; attempt < (want - here) * 4 && here < want; attempt++)
        {
            float a = _random.Range(0.0f, Mathf.Tau);
            float r = s.RadiusM * Mathf.Sqrt(_random.NextFloat());
            float px = x + (Mathf.Cos(a) * r), pz = z + (Mathf.Sin(a) * r);
            if (!h.Contains(px, pz) || h.SlopeAt(px, pz, _terrain.Spacing) > maxSlope)
                continue;
            if (_standing.Any(t => new Vector2(t.At.X - px, t.At.Z - pz).Length() < s.SpacingM))
                continue;
            if (WaterBody.DepthAt(new Vector3(px, h.HeightAt(px, pz), pz)) > 0.0f)
                continue;
            string path = _palette[_random.RangeInt(0, _palette.Count - 1)];
            Node3D node = Make(path, new Vector3(px, h.HeightAt(px, pz), pz), _random.Range(0.0f, Mathf.Tau));
            if (node == null)
                continue;
            _standing.Add((node.GlobalPosition, node));
            Added.Add(node);
            here++;
        }
    }

    /// <summary>One dab of taking away: the palette's instances within the radius.</summary>
    public void Erase(float x, float z, Settings s)
    {
        for (int i = _standing.Count - 1; i >= 0; i--)
        {
            (Vector3 at, Node3D node) = _standing[i];
            if (new Vector2(at.X - x, at.Z - z).Length() >= s.RadiusM || !_palette.Contains(node.SceneFilePath) || !node.IsInsideTree())
                continue;
            _standing.RemoveAt(i);
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

    private Node3D Make(string path, Vector3 at, float yaw)
    {
        if (_scenes[path]?.Instantiate(PackedScene.GenEditState.Instance) is not Node3D node)
            return null;
        Node parent = PlaceCommands.DefaultParent(_root, path);
        node.Name = PlaceCommands.NameOf(path);
        parent.AddChild(node, true);
        node.Owner = _root;
        node.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), at);
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
}
#endif
