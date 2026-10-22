using System;
using System.Collections.Generic;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// The cheapest real leaf: one sheet of 60-96 triangles, broad and
/// heart-shaped, shallowly lobed with a toothed edge, cupped, with veins drawn
/// darker in its vertex colour; and a stem, a thin box. A blade is built a
/// unit across its middle in its own frame (X across, Y its face, Z along
/// the midrib, the stem at -Z) and each leaf scales, turns and tilts it.
/// Drawn in <c>mat_leaf_toon</c>, both faces; see docs/handoffs/vine-tool.md.
/// </summary>
public static class LeafMesh
{
    /// <summary>One shape: lobes each side, teeth round the edge, how deep
    /// the notch where the stem joins, how far the edge rises.</summary>
    public readonly record struct Shape(int Lobes, int Teeth, float Notch, float Cup);

    private static readonly Dictionary<(Shape, Color, Color), ArrayMesh> Blades = new();
    private static readonly Dictionary<Color, ArrayMesh> Stems = new();

    /// <summary>The blade's edge at <paramref name="theta"/> (0 the tip, pi the
    /// stem), as a share of its half-length, before its teeth.</summary>
    public static float Edge(Shape shape, float theta)
    {
        float tip = theta > Mathf.Pi ? theta - Mathf.Tau : theta;
        float back = Mathf.Abs(tip) - Mathf.Pi;
        // A heart: broad, drawn out to a point at the tip, two round lobes
        // either side of a notch where the stem joins.
        float r = 0.74f + (0.26f * Mathf.Exp(-(tip * tip) / (2.0f * 0.3f * 0.3f)));
        r *= 1.0f - (shape.Notch * 1.6f * Mathf.Exp(-(back * back) / (2.0f * 0.2f * 0.2f)));
        if (shape.Lobes > 0)
        {
            // Shallow lobes, symmetric about the midrib, none at the tip.
            float lobe = 0.5f - (0.5f * Mathf.Cos(tip * ((2 * shape.Lobes) + 1)));
            r *= 1.0f - (0.14f * lobe * Mathf.Min(1.0f, Mathf.Abs(tip)));
        }
        return r;
    }

    /// <summary>Points round the edge: two a tooth, so every other one is a notch.</summary>
    private static int SpokesOf(Shape shape) => Math.Clamp(shape.Teeth * 2, 20, 32);

    /// <summary>The blade for <paramref name="shape"/>, shared by every leaf of it.</summary>
    public static ArrayMesh Blade(Shape shape, Color blade, Color vein)
    {
        if (Blades.TryGetValue((shape, blade, vein), out ArrayMesh mesh))
        {
            return mesh;
        }
        // A centre, a ring halfway out and the edge, on spokes round it. The
        // veins are the spokes every third one out from the midrib: their
        // inner points are dark, so the colour runs dark along them.
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Color>();
        var indices = new List<int>();
        void Add(float x, float z, Color c)
        {
            float y = shape.Cup * ((x * x) + (z * z));
            vertices.Add(new Vector3(x, y, z));
            normals.Add(new Vector3(-2.0f * shape.Cup * x, 1.0f, -2.0f * shape.Cup * z).Normalized());
            colours.Add(c);
        }
        int spokes = SpokesOf(shape);
        Color dark = vein.Lerp(Colors.Black, 0.35f);
        Add(0.0f, 0.0f, dark);
        for (int i = 0; i < spokes; i++)
        {
            float theta = Mathf.Tau * i / spokes;
            // Every other point round the edge sits back: the teeth.
            float edge = Edge(shape, theta) * (i % 2 == 1 ? 0.93f : 1.0f);
            bool isVein = i % 4 == 0;
            float x = Mathf.Sin(theta), z = Mathf.Cos(theta);
            Add(x * edge * 0.5f, z * edge * 0.5f, isVein ? dark : blade.Lerp(vein, 0.2f));
            Add(x * edge, z * edge, blade);
        }
        for (int i = 0; i < spokes; i++)
        {
            int j = (i + 1) % spokes;
            int innerI = 1 + (2 * i), outerI = innerI + 1;
            int innerJ = 1 + (2 * j), outerJ = innerJ + 1;
            indices.AddRange(new[] { 0, innerJ, innerI });
            indices.AddRange(new[] { innerI, innerJ, outerJ });
            indices.AddRange(new[] { innerI, outerJ, outerI });
        }
        mesh = Build(vertices, normals, colours, indices, $"leaf_{shape.Lobes}_{shape.Teeth}");
        Blades[(shape, blade, vein)] = mesh;
        return mesh;
    }

    /// <summary>A stem: a box a unit long along +Z from its origin, a unit wide.</summary>
    public static ArrayMesh Stem(Color colour)
    {
        if (Stems.TryGetValue(colour, out ArrayMesh mesh))
        {
            return mesh;
        }
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Color>();
        var indices = new List<int>();
        // Four long faces; the ends are hidden in the stalk and the blade.
        Vector3[] across = { Vector3.Right, Vector3.Up, Vector3.Left, Vector3.Down };
        for (int f = 0; f < 4; f++)
        {
            Vector3 n = across[f];
            Vector3 side = across[(f + 1) % 4];
            int start = vertices.Count;
            foreach ((float s, float z) in new[] { (-0.5f, 0.0f), (0.5f, 0.0f), (0.5f, 1.0f), (-0.5f, 1.0f) })
            {
                vertices.Add((n * 0.5f) + (side * s) + new Vector3(0, 0, z));
                normals.Add(n);
                colours.Add(colour);
            }
            indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }
        mesh = Build(vertices, normals, colours, indices, "leaf_stem");
        Stems[colour] = mesh;
        return mesh;
    }

    private static ArrayMesh Build(List<Vector3> vertices, List<Vector3> normals, List<Color> colours, List<int> indices, string name)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh { ResourceName = name };
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>A kind's leaf shapes, each rolled inside its ranges from the
    /// kind's name: the same kind, the same shapes, everywhere.</summary>
    public static Shape[] ShapesOf(VineKind kind)
    {
        var shapes = new Shape[VineGenerator.LeafShapes];
        for (int i = 0; i < shapes.Length; i++)
        {
            var rng = Core.SeededRandom.Stream(Core.SeededRandom.Hash(0, kind?.KindName ?? "Vine"), "leafshape:" + i);
            Vector2 lobes = kind?.LeafLobes ?? new Vector2(0, 1);
            Vector2 teeth = kind?.LeafTeeth ?? new Vector2(10, 16);
            Vector2 notch = kind?.LeafNotch ?? new Vector2(0.15f, 0.3f);
            Vector2 cup = kind?.LeafCup ?? new Vector2(0.08f, 0.16f);
            shapes[i] = new Shape(
                rng.RangeInt((int)Math.Round(lobes.X), (int)Math.Round(lobes.Y)),
                rng.RangeInt((int)Math.Round(teeth.X), (int)Math.Round(teeth.Y)),
                rng.Range(notch.X, notch.Y),
                rng.Range(cup.X, cup.Y));
        }
        return shapes;
    }
}
