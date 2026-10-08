using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// A MagicaVoxel <c>.vox</c> file, read whole: its models, its palette, which
/// palette indices glow, and the scene graph that places the models.
///
/// Everything here is in MagicaVoxel's own space (Z up, integer voxels).
/// <see cref="VoxSpace"/> converts to Godot's. Plain C# with no editor types,
/// so the importer and anything at runtime (a destructible body built from a
/// .vox) read files the same way.
///
/// The format is RIFF-like chunks: "VOX " and a version, then MAIN, whose
/// children are SIZE/XYZI pairs (one per model), the palette (RGBA), materials
/// (MATL) and the scene graph (nTRN, nGRP, nSHP, LAYR). Unknown chunks are
/// skipped, so newer files still read.
/// </summary>
public sealed class VoxFile
{
    public sealed class Model
    {
        public Vector3I Size;

        /// <summary>One palette index per cell, 0 for empty. Index
        /// <c>x + Size.X * (y + Size.Y * z)</c>.</summary>
        public byte[] Cells;

        public int Index(int x, int y, int z) => x + Size.X * (y + Size.Y * z);

        public byte At(int x, int y, int z) =>
            x < 0 || y < 0 || z < 0 || x >= Size.X || y >= Size.Y || z >= Size.Z
                ? (byte)0
                : Cells[Index(x, y, z)];
    }

    /// <summary>A transform node: a rotation that only permutes and flips axes,
    /// and a translation in voxels. Its one child is a group or a shape.</summary>
    public sealed class TransformNode
    {
        public int Id;
        public string Name = "";
        public bool Hidden;
        public int Child;
        public int Layer = -1;
        public VoxRotation Rotation = VoxRotation.Identity;
        public Vector3I Translation;
    }

    public sealed class GroupNode
    {
        public int Id;
        public int[] Children = Array.Empty<int>();
    }

    public sealed class ShapeNode
    {
        public int Id;
        public int Model;
    }

    public readonly List<Model> Models = new();

    /// <summary>Linear colour per palette index; index 0 is empty and unused.</summary>
    public readonly Color[] Palette = new Color[256];

    /// <summary>Emission strength per palette index, 0 for ordinary voxels.
    /// MagicaVoxel's emit weight times one plus its power ("flux", 0-4), so a
    /// weight-1 voxel at power 2 is 3.</summary>
    public readonly float[] Emission = new float[256];

    public readonly Dictionary<int, TransformNode> Transforms = new();
    public readonly Dictionary<int, GroupNode> Groups = new();
    public readonly Dictionary<int, ShapeNode> Shapes = new();
    public readonly HashSet<int> HiddenLayers = new();

    public int Version { get; private set; }

    /// <summary>True when the file has a scene graph. Very old files have only
    /// models, which then sit at the origin.</summary>
    public bool HasSceneGraph => Transforms.ContainsKey(0);

    public static VoxFile Load(byte[] data)
    {
        var file = new VoxFile();
        file.Read(data);
        return file;
    }

    private void Read(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "VOX ")
            throw new InvalidDataException("Not a MagicaVoxel file: no \"VOX \" header.");
        Version = reader.ReadInt32();

        DefaultPalette(Palette);
        Vector3I pendingSize = Vector3I.Zero;

        // MAIN has no content of its own; every chunk that matters is one of
        // its children, so reading chunks flat from here reads them all.
        while (reader.BaseStream.Position + 12 <= reader.BaseStream.Length)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            int contentSize = reader.ReadInt32();
            int childrenSize = reader.ReadInt32();
            if (id == "MAIN")
                continue;

            long end = reader.BaseStream.Position + contentSize;
            switch (id)
            {
                case "SIZE":
                    pendingSize = new Vector3I(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                    break;
                case "XYZI":
                    Models.Add(ReadVoxels(reader, pendingSize));
                    break;
                case "RGBA":
                    // Entry i is palette index i + 1; the 256th is unused.
                    for (int i = 0; i < 255; i++)
                        Palette[i + 1] = Color.Color8(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()).SrgbToLinear();
                    break;
                case "MATL":
                    ReadMaterial(reader);
                    break;
                case "nTRN":
                    ReadTransform(reader);
                    break;
                case "nGRP":
                    ReadGroup(reader);
                    break;
                case "nSHP":
                    ReadShape(reader);
                    break;
                case "LAYR":
                    ReadLayer(reader);
                    break;
            }

            reader.BaseStream.Position = end + childrenSize;
        }
    }

    private static Model ReadVoxels(BinaryReader reader, Vector3I size)
    {
        var model = new Model { Size = size, Cells = new byte[size.X * size.Y * size.Z] };
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            int x = reader.ReadByte(), y = reader.ReadByte(), z = reader.ReadByte();
            byte colour = reader.ReadByte();
            if (x < size.X && y < size.Y && z < size.Z)
                model.Cells[model.Index(x, y, z)] = colour;
        }
        return model;
    }

    private void ReadMaterial(BinaryReader reader)
    {
        int index = reader.ReadInt32();
        Dictionary<string, string> props = ReadDict(reader);
        if (index <= 0 || index > 255 || !props.TryGetValue("_type", out string type) || type != "_emit")
            return;

        // Older files call the weight _emit; newer ones _weight.
        float weight = Float(props, "_weight", Float(props, "_emit", 1.0f));
        float power = Float(props, "_flux", 0.0f);
        Emission[index] = weight * (1.0f + power);
    }

    private void ReadTransform(BinaryReader reader)
    {
        var node = new TransformNode { Id = reader.ReadInt32() };
        Dictionary<string, string> attributes = ReadDict(reader);
        node.Name = attributes.GetValueOrDefault("_name", "");
        node.Hidden = attributes.GetValueOrDefault("_hidden", "0") == "1";
        node.Child = reader.ReadInt32();
        reader.ReadInt32(); // reserved
        node.Layer = reader.ReadInt32();

        // Only the first animation frame is placed; MagicaVoxel's keyframes
        // are not how anything here moves.
        int frames = reader.ReadInt32();
        for (int f = 0; f < frames; f++)
        {
            Dictionary<string, string> frame = ReadDict(reader);
            if (f > 0)
                continue;
            if (frame.TryGetValue("_r", out string r))
                node.Rotation = VoxRotation.FromByte(byte.Parse(r));
            if (frame.TryGetValue("_t", out string t))
            {
                string[] parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                node.Translation = new Vector3I(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
            }
        }
        Transforms[node.Id] = node;
    }

    private void ReadGroup(BinaryReader reader)
    {
        var node = new GroupNode { Id = reader.ReadInt32() };
        ReadDict(reader);
        node.Children = new int[reader.ReadInt32()];
        for (int i = 0; i < node.Children.Length; i++)
            node.Children[i] = reader.ReadInt32();
        Groups[node.Id] = node;
    }

    private void ReadShape(BinaryReader reader)
    {
        var node = new ShapeNode { Id = reader.ReadInt32() };
        ReadDict(reader);
        int models = reader.ReadInt32();
        // A shape can list one model per animation frame; the first is placed.
        node.Model = models > 0 ? reader.ReadInt32() : -1;
        Shapes[node.Id] = node;
    }

    private void ReadLayer(BinaryReader reader)
    {
        int id = reader.ReadInt32();
        if (ReadDict(reader).GetValueOrDefault("_hidden", "0") == "1")
            HiddenLayers.Add(id);
    }

    private static Dictionary<string, string> ReadDict(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        var dict = new Dictionary<string, string>(count);
        for (int i = 0; i < count; i++)
            dict[ReadString(reader)] = ReadString(reader);
        return dict;
    }

    private static string ReadString(BinaryReader reader) =>
        Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));

    private static float Float(Dictionary<string, string> props, string key, float fallback) =>
        props.TryGetValue(key, out string s)
        && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)
            ? f
            : fallback;

    /// <summary>MagicaVoxel's built-in palette, for files saved without an
    /// RGBA chunk: a 6-level colour cube from white down, then ten-step ramps
    /// of red, green, blue and grey.</summary>
    private static void DefaultPalette(Color[] palette)
    {
        int index = 1;
        int[] cube = { 0xff, 0xcc, 0x99, 0x66, 0x33, 0x00 };
        foreach (int r in cube)
            foreach (int g in cube)
                foreach (int b in cube)
                    if ((r | g | b) != 0 && index < 256)
                        palette[index++] = Color.Color8((byte)r, (byte)g, (byte)b).SrgbToLinear();

        int[] ramp = { 0xee, 0xdd, 0xbb, 0xaa, 0x88, 0x77, 0x55, 0x44, 0x22, 0x11 };
        for (int channel = 0; channel < 4; channel++)
            foreach (int v in ramp)
            {
                if (index >= 256)
                    break;
                byte c = (byte)v;
                palette[index++] = (channel switch
                {
                    0 => Color.Color8(c, 0, 0),
                    1 => Color.Color8(0, c, 0),
                    2 => Color.Color8(0, 0, c),
                    _ => Color.Color8(c, c, c),
                }).SrgbToLinear();
            }
    }
}
