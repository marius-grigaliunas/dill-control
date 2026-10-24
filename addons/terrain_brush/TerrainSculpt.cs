#if TOOLS
using System;
using Godot;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// What a brush does to a <see cref="SculptedTerrain"/>'s heights, one dab
/// at a time: the same for the user's brush in the 3D view and for the
/// bridge's <c>sculpt</c>. A dab changes the copy it is given; the stroke
/// hands the result to the terrain, and the old heights to the undo.
/// </summary>
public static class TerrainSculpt
{
    public enum Mode { Raise, Lower, Smooth, Flatten }

    public struct Brush
    {
        public Mode Mode;
        public float RadiusM;
        /// <summary>Raise and Lower: metres a dab at the centre. Smooth and Flatten: the share of the way a dab goes, 0 to 1.</summary>
        public float Strength;
        /// <summary>The share of the radius at full strength; the rest falls off smoothly to nothing at the edge.</summary>
        public float Hardness;
        /// <summary>Flatten's height, metres over the terrain's node.</summary>
        public float Target;
    }

    public static bool TryMode(string name, out Mode mode) => Enum.TryParse(name, true, out mode);

    /// <summary>One dab at world (x, z). Returns whether a sample changed.</summary>
    public static bool Dab(SculptedTerrain terrain, float[] heights, float x, float z, Brush brush)
    {
        int n = terrain.Count;
        float spacing = terrain.Spacing;
        Vector3 centre = terrain.GlobalPosition;
        float half = terrain.Size * 0.5f;
        // Sample coordinates of the dab's centre.
        float u = (x - centre.X + half) / spacing;
        float v = (z - centre.Z + half) / spacing;
        float r = Mathf.Max(brush.RadiusM, spacing * 0.5f) / spacing;
        int u0 = Math.Max(0, (int)Mathf.Floor(u - r)), u1 = Math.Min(n - 1, (int)Mathf.Ceil(u + r));
        int v0 = Math.Max(0, (int)Mathf.Floor(v - r)), v1 = Math.Min(n - 1, (int)Mathf.Ceil(v + r));
        if (u0 > u1 || v0 > v1)
            return false;

        // Smooth reads the heights as they were before this dab.
        float[] before = brush.Mode == Mode.Smooth ? (float[])heights.Clone() : null;
        float hard = Mathf.Clamp(brush.Hardness, 0.0f, 0.99f);
        bool changed = false;
        for (int j = v0; j <= v1; j++)
        {
            for (int i = u0; i <= u1; i++)
            {
                float d = new Vector2(i - u, j - v).Length() / r;
                if (d >= 1.0f)
                    continue;
                float t = Mathf.Clamp((d - hard) / (1.0f - hard), 0.0f, 1.0f);
                float w = 1.0f - (t * t * (3.0f - (2.0f * t)));
                int k = (j * n) + i;
                float h = heights[k];
                float next = brush.Mode switch
                {
                    Mode.Raise => h + (brush.Strength * w),
                    Mode.Lower => h - (brush.Strength * w),
                    Mode.Flatten => h + ((brush.Target - h) * Mathf.Clamp(brush.Strength, 0.0f, 1.0f) * w),
                    _ => h + ((Neighbours(before, n, i, j) - h) * Mathf.Clamp(brush.Strength, 0.0f, 1.0f) * w),
                };
                if (next != h)
                {
                    heights[k] = next;
                    changed = true;
                }
            }
        }
        return changed;
    }

    // The mean of the eight around a sample (those inside the map).
    private static float Neighbours(float[] h, int n, int i, int j)
    {
        float sum = 0.0f;
        int count = 0;
        for (int dj = -1; dj <= 1; dj++)
        {
            for (int di = -1; di <= 1; di++)
            {
                int a = i + di, b = j + dj;
                if ((di == 0 && dj == 0) || a < 0 || b < 0 || a >= n || b >= n)
                    continue;
                sum += h[(b * n) + a];
                count++;
            }
        }
        return count > 0 ? sum / count : h[(j * n) + i];
    }

    /// <summary>
    /// Records a finished stroke as one undo: the terrain already holds
    /// <paramref name="after"/>; undo gives it <paramref name="before"/>.
    /// </summary>
    public static void Record(EditorUndoRedoManager undo, SculptedTerrain terrain, float[] before, float[] after)
    {
        undo.AddDoMethod(terrain, SculptedTerrain.MethodName.SetHeights, after);
        undo.AddUndoMethod(terrain, SculptedTerrain.MethodName.SetHeights, before);
    }

    /// <summary>Writes the terrain's heights and chunk meshes to their files.</summary>
    public static int Save(SculptedTerrain terrain)
    {
        int saved = 0;
        if (terrain.Map != null && !string.IsNullOrEmpty(terrain.Map.ResourcePath) && ResourceSaver.Save(terrain.Map) == Error.Ok)
            saved++;
        foreach (MeshInstance3D chunk in terrain.Chunks())
        {
            if (chunk.Mesh != null && !string.IsNullOrEmpty(chunk.Mesh.ResourcePath) && ResourceSaver.Save(chunk.Mesh) == Error.Ok)
                saved++;
        }
        return saved;
    }
}
#endif
