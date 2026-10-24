#if TOOLS
using System;
using System.Collections.Generic;
using Godot;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// Floods a hollow of a <see cref="SculptedTerrain"/> (docs/decisions/0045):
/// from a cell, every cell that connects to it and has ground lower than the
/// level gets water, flat at that level. The result is a real
/// <see cref="WaterBody"/> under the level's Water branch, its surface a mesh
/// saved beside the scene. Used by the brush and the bridge's <c>flood</c>.
/// </summary>
public static class WaterFlood
{
    private const string MaterialPath = "res://art/materials/mat_water_toon.tres";

    /// <summary>The most cells one flood may cover (about 800 m square at 4 m).</summary>
    public const int MaxCells = 40000;

    public sealed class Refused(string message) : Exception(message);

    /// <summary>Floods from world (x, z) up to world height <paramref name="level"/> and adds the body, as one undo.</summary>
    public static WaterBody Flood(SculptedTerrain terrain, float x, float z, float level, EditorUndoRedoManager undo)
    {
        ArenaHeights h = terrain.Heights ?? throw new Refused("the terrain has no heights");
        int n = h.Count - 1;
        float s = h.Spacing;
        int cx = Mathf.FloorToInt((x - h.Min.X) / s), cz = Mathf.FloorToInt((z - h.Min.Y) / s);
        if (cx < 0 || cz < 0 || cx >= n || cz >= n)
            throw new Refused("that point is off the terrain");

        // A cell is wet where any of its corners is below the level.
        bool Wet(int i, int j) =>
            Math.Min(Math.Min(Ground(h, i, j), Ground(h, i + 1, j)), Math.Min(Ground(h, i, j + 1), Ground(h, i + 1, j + 1))) < level;
        if (!Wet(cx, cz))
            throw new Refused($"the ground there is above {level:0.##} m: nothing to flood");

        var wet = new HashSet<(int, int)>();
        var open = new Queue<(int, int)>();
        wet.Add((cx, cz));
        open.Enqueue((cx, cz));
        int x0 = cx, x1 = cx, z0 = cz, z1 = cz;
        while (open.Count > 0)
        {
            (int i, int j) = open.Dequeue();
            if (i == 0 || j == 0 || i == n - 1 || j == n - 1)
                throw new Refused($"at {level:0.##} m the water runs off the terrain's edge: a lower level, or a higher rim");
            foreach ((int di, int dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                (int a, int b) = (i + di, j + dj);
                if (wet.Contains((a, b)) || !Wet(a, b))
                    continue;
                wet.Add((a, b));
                if (wet.Count > MaxCells)
                    throw new Refused($"at {level:0.##} m the water covers over {MaxCells} cells: a lower level, or a smaller hollow");
                open.Enqueue((a, b));
                x0 = Math.Min(x0, a);
                x1 = Math.Max(x1, a);
                z0 = Math.Min(z0, b);
                z1 = Math.Max(z1, b);
            }
        }

        int width = x1 - x0 + 1, length = z1 - z0 + 1;
        var cells = new byte[width * length];
        foreach ((int i, int j) in wet)
            cells[((j - z0) * width) + (i - x0)] = 1;

        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        Node parent = root.GetNodeOrNull("Water") ?? root;
        float minX = h.Min.X + (x0 * s), minZ = h.Min.Y + (z0 * s);
        var middle = new Vector3(minX + (width * s * 0.5f), level, minZ + (length * s * 0.5f));
        var body = new WaterBody
        {
            Name = "Pond",
            Stats = ResourceLoader.Load<WaterStats>(WaterStats.DefaultPath),
            CellSize = s,
            Width = width,
            Length = length,
            Corner = new Vector2(minX - middle.X, minZ - middle.Z),
            Cells = cells,
        };
        parent.AddChild(body, true);
        body.Owner = root;
        body.GlobalPosition = middle;

        var mesh = new ArrayMesh();
        body.FillSurface(mesh, ResourceLoader.Load<Material>(MaterialPath));
        string scene = root.SceneFilePath;
        string folder = $"{scene.GetBaseDir()}/{scene.GetFile().GetBaseName()}_water";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(folder));
        string path = $"{folder}/{body.Name}_{Time.GetUnixTimeFromSystem():0}.res";
        if (ResourceSaver.Save(mesh, path) == Error.Ok)
            mesh.TakeOverPath(path);
        var surface = new MeshInstance3D { Name = "Surface", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        body.AddChild(surface);
        surface.Owner = root;

        undo.AddDoMethod(parent, Node.MethodName.AddChild, body, true);
        undo.AddDoProperty(body, Node.PropertyName.Owner, root);
        undo.AddDoProperty(surface, Node.PropertyName.Owner, root);
        undo.AddDoReference(body);
        undo.AddUndoMethod(parent, Node.MethodName.RemoveChild, body);
        return body;
    }

    private static float Ground(ArenaHeights h, int i, int j) => h.Base + h.Heights[(j * h.Count) + i];
}
#endif
