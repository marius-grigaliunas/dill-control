#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// The Water brush (docs/decisions/0045): paints a pond's wet cells on, or
/// off with Shift, at the pond's own level. A stroke works on the pond it
/// starts in, or the nearest within three radii (20 m at least); started on
/// dry ground with none near, it begins a new pond at Depth over the ground clicked. Water
/// is only painted where the ground is below the level, so it never floats.
/// A stroke is one undo.
/// </summary>
public sealed class WaterPaint
{
    private readonly SculptedTerrain _terrain;
    public readonly WaterBody Body;
    private readonly bool _made;
    private readonly Vector2 _base;
    private readonly float _cell;
    private readonly HashSet<(int, int)> _wet = new();
    private readonly (Vector2 Corner, int Width, int Length, byte[] Cells) _was;

    public WaterPaint(SculptedTerrain terrain, Vector3 at, float radius, float depth)
    {
        _terrain = terrain;
        // Started on dry ground near a pond, the stroke belongs to that pond: a
        // second pond at another level overlapping it is never what was meant.
        WaterBody.DepthAt(at, out WaterBody body);
        body ??= Nearest(at, Mathf.Max(radius * 3.0f, 20.0f));
        if (body == null)
        {
            // A new pond with no cells yet, its grid on the terrain's samples.
            ArenaHeights h = terrain.Heights;
            float s = h.Spacing;
            float minX = h.Min.X + (Mathf.Floor((at.X - h.Min.X) / s) * s);
            float minZ = h.Min.Y + (Mathf.Floor((at.Z - h.Min.Y) / s) * s);
            body = WaterFlood.Create(at.Y + depth, s, minX, minZ, 0, 0, System.Array.Empty<byte>());
            _made = true;
        }
        Body = body;
        _cell = body.CellSize;
        Vector3 o = body.GlobalPosition;
        _base = new Vector2(o.X + body.Corner.X, o.Z + body.Corner.Y);
        _was = (body.Corner, body.Width, body.Length, body.Cells);
        for (int z = 0; z < body.Length; z++)
        {
            for (int x = 0; x < body.Width; x++)
            {
                if (body.Cells[(z * body.Width) + x] != 0)
                    _wet.Add((x, z));
            }
        }
    }

    // The pond whose water comes nearest the ring, if any is in it.
    private static WaterBody Nearest(Vector3 at, float radius)
    {
        WaterBody best = null;
        float bestDistance = radius;
        foreach (WaterBody body in EditorInterface.Singleton.GetEditedSceneRoot()
                     .FindChildren("*", nameof(Node3D), true, true).OfType<WaterBody>())
        {
            Vector3 o = body.GlobalPosition;
            for (int z = 0; z < body.Length; z++)
            {
                for (int x = 0; x < body.Width; x++)
                {
                    if (body.Cells[(z * body.Width) + x] == 0)
                        continue;
                    var middle = new Vector2(o.X + body.Corner.X + ((x + 0.5f) * body.CellSize), o.Z + body.Corner.Y + ((z + 0.5f) * body.CellSize));
                    float d = middle.DistanceTo(new Vector2(at.X, at.Z));
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = body;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>One dab: cells whose middles are inside the radius go wet (where the ground is below the level) or dry.</summary>
    public void Dab(float x, float z, float radius, bool erase)
    {
        ArenaHeights h = _terrain.Heights;
        float level = Body.GlobalPosition.Y;
        int i0 = Mathf.FloorToInt((x - radius - _base.X) / _cell), i1 = Mathf.FloorToInt((x + radius - _base.X) / _cell);
        int j0 = Mathf.FloorToInt((z - radius - _base.Y) / _cell), j1 = Mathf.FloorToInt((z + radius - _base.Y) / _cell);
        bool changed = false;
        for (int j = j0; j <= j1; j++)
        {
            for (int i = i0; i <= i1; i++)
            {
                float cx = _base.X + ((i + 0.5f) * _cell), cz = _base.Y + ((j + 0.5f) * _cell);
                if (new Vector2(cx - x, cz - z).Length() > radius)
                    continue;
                if (erase)
                {
                    changed |= _wet.Remove((i, j));
                    continue;
                }
                float ax = _base.X + (i * _cell), az = _base.Y + (j * _cell);
                if (!h.Contains(ax, az) || !h.Contains(ax + _cell, az + _cell))
                    continue;
                float ground = Mathf.Min(Mathf.Min(h.HeightAt(ax, az), h.HeightAt(ax + _cell, az)),
                    Mathf.Min(h.HeightAt(ax, az + _cell), h.HeightAt(ax + _cell, az + _cell)));
                if (ground < level)
                    changed |= _wet.Add((i, j));
            }
        }
        if (changed)
        {
            (Vector2 corner, int width, int length, byte[] cells) = Shape();
            Body.Reshape(corner, width, length, cells);
        }
    }

    // The wet cells as the body keeps them: the grid round them, relative to the body.
    private (Vector2 Corner, int Width, int Length, byte[] Cells) Shape()
    {
        if (_wet.Count == 0)
            return (Body.Corner, 0, 0, System.Array.Empty<byte>());
        int i0 = _wet.Min(c => c.Item1), i1 = _wet.Max(c => c.Item1);
        int j0 = _wet.Min(c => c.Item2), j1 = _wet.Max(c => c.Item2);
        int width = i1 - i0 + 1, length = j1 - j0 + 1;
        var cells = new byte[width * length];
        foreach ((int i, int j) in _wet)
            cells[((j - j0) * width) + (i - i0)] = 1;
        Vector3 o = Body.GlobalPosition;
        var corner = new Vector2(_base.X + (i0 * _cell) - o.X, _base.Y + (j0 * _cell) - o.Z);
        return (corner, width, length, cells);
    }

    public int WetCells => _wet.Count;

    /// <summary>The stroke as one undo, the new pond's making first if it made one.</summary>
    public void Record(EditorUndoRedoManager undo)
    {
        if (_made)
            WaterFlood.RecordCreate(undo, Body);
        (Vector2 corner, int width, int length, byte[] cells) = Shape();
        undo.AddDoMethod(Body, WaterBody.MethodName.Reshape, corner, width, length, cells);
        undo.AddUndoMethod(Body, WaterBody.MethodName.Reshape, _was.Corner, _was.Width, _was.Length, _was.Cells);
    }

    public bool Made => _made;
}
#endif
