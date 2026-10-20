using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DillControl.Voxel;

/// <summary>
/// Writes a MagicaVoxel .vox (version 150) of one named model and any number
/// of one-voxel <c>@Name</c> markers, the layout of
/// <c>tools/vox-tools/make_graybox.py</c> that <see cref="VoxFile"/> reads:
/// SIZE/XYZI pairs, the scene graph, RGBA, MATL. Plain C#, nothing of
/// Godot's, so the editor and a headless check both call it.
/// </summary>
public static class VoxWriter
{
    public struct Cell
    {
        public int X, Y, Z;
        public byte C;
    }

    public struct Marker
    {
        public string Name;
        public int X, Y, Z;
    }

    /// <summary>
    /// <paramref name="translation"/> places the model's node: MagicaVoxel
    /// centres a model on <c>floor(size / 2)</c>, so a voxel corner
    /// <c>p</c> lands at <c>translation + p - floor(size / 2)</c>. Markers are
    /// in the model's voxel coordinates and land on the same centres as its
    /// voxels would. <paramref name="rgba"/> holds index 1 at offset 0, as the
    /// RGBA chunk does; <paramref name="materials"/> maps an index to its
    /// MATL <c>_type</c> (<c>_diffuse</c>, <c>_glass</c>...).
    /// </summary>
    public static byte[] Write(string modelName, int sx, int sy, int sz, IReadOnlyList<Cell> cells,
        (int X, int Y, int Z) translation, IReadOnlyList<Marker> markers, byte[] rgba,
        IReadOnlyList<(int Index, string Type)> materials, byte markerColour)
    {
        var parts = new MemoryStream();
        Chunk(parts, "SIZE", Ints(sx, sy, sz));
        Chunk(parts, "XYZI", Xyzi(cells));
        if (markers.Count > 0)
        {
            Chunk(parts, "SIZE", Ints(1, 1, 1));
            Chunk(parts, "XYZI", Xyzi(new[] { new Cell { C = markerColour } }));
        }

        var childIds = new List<int> { 2 };
        for (int i = 0; i < markers.Count; i++)
            childIds.Add(4 + i * 2);

        Transform(parts, 0, "", 1, -1, null);
        var group = new MemoryStream();
        group.Write(Ints(1));
        group.Write(Dict());
        group.Write(Ints(childIds.Count));
        group.Write(Ints(childIds.ToArray()));
        Chunk(parts, "nGRP", group.ToArray());
        Transform(parts, 2, modelName, 3, 0, $"{translation.X} {translation.Y} {translation.Z}");
        Shape(parts, 3, 0);

        int halfX = sx >> 1, halfY = sy >> 1, halfZ = sz >> 1;
        for (int i = 0; i < markers.Count; i++)
        {
            Marker m = markers[i];
            int id = 4 + i * 2;
            // A 1x1x1 model's centre is its voxel's corner and the importer
            // puts a marker on its voxel's centre: the same centre the model's
            // voxel at (m.X, m.Y, m.Z) has.
            int tx = translation.X + m.X - halfX;
            int ty = translation.Y + m.Y - halfY;
            int tz = translation.Z + m.Z - halfZ;
            Transform(parts, id, m.Name, id + 1, 0, $"{tx} {ty} {tz}");
            Shape(parts, id + 1, 1);
        }

        Chunk(parts, "RGBA", rgba);
        foreach ((int index, string type) in materials)
        {
            var matl = new MemoryStream();
            matl.Write(Ints(index));
            matl.Write(Dict(("_type", type)));
            Chunk(parts, "MATL", matl.ToArray());
        }

        var file = new MemoryStream();
        file.Write(Encoding.ASCII.GetBytes("VOX "));
        file.Write(Ints(150));
        Chunk(file, "MAIN", System.Array.Empty<byte>(), parts.ToArray());
        return file.ToArray();
    }

    private static byte[] Ints(params int[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            int v = values[i];
            bytes[i * 4] = (byte)v;
            bytes[i * 4 + 1] = (byte)(v >> 8);
            bytes[i * 4 + 2] = (byte)(v >> 16);
            bytes[i * 4 + 3] = (byte)(v >> 24);
        }
        return bytes;
    }

    private static byte[] Str(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var buffer = new MemoryStream();
        buffer.Write(Ints(bytes.Length));
        buffer.Write(bytes);
        return buffer.ToArray();
    }

    private static byte[] Dict(params (string Key, string Value)[] entries)
    {
        var buffer = new MemoryStream();
        buffer.Write(Ints(entries.Length));
        foreach ((string key, string value) in entries)
        {
            buffer.Write(Str(key));
            buffer.Write(Str(value));
        }
        return buffer.ToArray();
    }

    private static void Chunk(Stream into, string id, byte[] content, byte[] children = null)
    {
        children ??= System.Array.Empty<byte>();
        into.Write(Encoding.ASCII.GetBytes(id));
        into.Write(Ints(content.Length, children.Length));
        into.Write(content);
        into.Write(children);
    }

    private static byte[] Xyzi(IReadOnlyList<Cell> cells)
    {
        var body = new byte[4 + cells.Count * 4];
        Ints(cells.Count).CopyTo(body, 0);
        for (int i = 0; i < cells.Count; i++)
        {
            int o = 4 + i * 4;
            body[o] = (byte)cells[i].X;
            body[o + 1] = (byte)cells[i].Y;
            body[o + 2] = (byte)cells[i].Z;
            body[o + 3] = cells[i].C;
        }
        return body;
    }

    private static void Transform(Stream into, int id, string name, int child, int layer, string translation)
    {
        var body = new MemoryStream();
        body.Write(Ints(id));
        body.Write(name.Length > 0 ? Dict(("_name", name)) : Dict());
        body.Write(Ints(child, -1, layer, 1));
        body.Write(translation != null ? Dict(("_t", translation)) : Dict());
        Chunk(into, "nTRN", body.ToArray());
    }

    private static void Shape(Stream into, int id, int model)
    {
        var body = new MemoryStream();
        body.Write(Ints(id));
        body.Write(Dict());
        body.Write(Ints(1, model));
        body.Write(Dict());
        Chunk(into, "nSHP", body.ToArray());
    }
}
