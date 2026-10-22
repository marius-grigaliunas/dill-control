using System;
using System.Collections.Generic;
using DillControl.Core;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// Every bending plant, stepped together, and the one texture the shader
/// bends them by. Made by the first <see cref="PlantFlex"/> that needs it, so
/// a plant dropped into any scene bends with nothing else set up.
///
/// The texture is a row per plant: its foot and the length of a segment, its
/// up and how many points, then each point's move from rest in world space.
/// voxel_toon reads the row its material's <c>flex_row</c> names and moves
/// each vertex by its height. One upload a frame however many plants there
/// are; each plant draws with its own copy of the material, made once.
/// </summary>
public partial class FlexField : Node
{
    private static readonly int SectionStep = FrameProfiler.Section("plant flex");

    public static readonly StringName RowParameter = "flex_row";
    private static readonly StringName TextureParameter = "flex_rods";

    private const int Width = 2 + Rod.MaxSegments + 1;

    private static FlexField _node;
    private static readonly List<PlantFlex> Plants = new();
    private static readonly Stack<int> FreeRows = new();
    private static int _rowsUsed;

    private static Image _image;
    private static ImageTexture _texture;
    private static int _capacity;
    private static float[] _data;
    private static byte[] _bytes;
    private static readonly HashSet<ShaderMaterial> Materials = new();

    /// <summary>What every plant leans in. The default unless a level sets its own.</summary>
    public static WindStats Wind { get; set; }

    private double _time;

    internal static void Join(PlantFlex plant)
    {
        Wind ??= GD.Load<WindStats>(WindStats.DefaultPath);
        if (_node == null || !IsInstanceValid(_node))
        {
            _node = new FlexField { Name = "FlexField" };
            plant.GetTree().Root.CallDeferred(Node.MethodName.AddChild, _node);
        }

        plant.Row = FreeRows.Count > 0 ? FreeRows.Pop() : _rowsUsed++;
        Plants.Add(plant);
        if (_rowsUsed > _capacity || _texture == null)
        {
            Grow(Math.Max(64, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)_rowsUsed)));
        }
        Write(plant);
    }

    /// <summary>A plant's own copy of its material, bending by its row.</summary>
    internal static ShaderMaterial MaterialFor(PlantFlex plant, Material from)
    {
        if (from is not ShaderMaterial shader)
        {
            return null;
        }
        var own = (ShaderMaterial)shader.Duplicate();
        own.SetShaderParameter(RowParameter, plant.Row);
        own.SetMeta(RowMeta, plant.Row);
        own.SetShaderParameter(TextureParameter, _texture);
        Materials.Add(own);
        return own;
    }

    internal static void Leave(PlantFlex plant)
    {
        if (!Plants.Remove(plant) || plant.Row < 0)
        {
            return;
        }
        Array.Clear(_data, plant.Row * Width * 4, Width * 4);
        foreach (ShaderMaterial material in plant.OwnMaterials)
        {
            Materials.Remove(material);
        }
        FreeRows.Push(plant.Row);
        plant.Row = -1;
        if (Plants.Count == 0)
        {
            FreeRows.Clear();
            _rowsUsed = 0;
        }
    }

    private static void Grow(int rows)
    {
        float[] old = _data;
        _capacity = rows;
        _data = new float[Width * rows * 4];
        _bytes = new byte[_data.Length * sizeof(float)];
        if (old != null)
        {
            Array.Copy(old, _data, Math.Min(old.Length, _data.Length));
        }
        _image = Image.CreateFromData(Width, rows, false, Image.Format.Rgbaf, _bytes);
        _texture = ImageTexture.CreateFromImage(_image);
        foreach (ShaderMaterial material in Materials)
        {
            material.SetShaderParameter(TextureParameter, _texture);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionStep);
        _time += delta;
        float dt = (float)delta;
        foreach (PlantFlex plant in Plants)
        {
            plant.Step(dt, Wind, _time);
            Write(plant);
        }
    }

    public override void _Process(double delta)
    {
        if (_texture == null || Plants.Count == 0)
        {
            return;
        }
        Buffer.BlockCopy(_data, 0, _bytes, 0, _rowsUsed * Width * 4 * sizeof(float));
        _image.SetData(Width, _capacity, false, Image.Format.Rgbaf, _bytes);
        _texture.Update(_image);
    }

    /// <summary>For <c>bench.gd</c>: every plant drawn unbent, or bent again (an A/B of what bending costs to draw).</summary>
    public void BenchStill()
    {
        _still = !_still;
        foreach (ShaderMaterial material in Materials)
        {
            material.SetShaderParameter(RowParameter, _still ? -1 : material.GetMeta(RowMeta, -1).AsInt32());
        }
        GD.Print($"[flex] bending {(_still ? "off" : "on")}");
    }

    private static bool _still;
    private static readonly StringName RowMeta = "flex_row";

    /// <summary>For <c>bench.gd</c>: how many plants, how far their tips have moved, what pushed them.</summary>
    public void PrintBenchSummary()
    {
        int count = 0, hit = 0, pulled = 0;
        float most = 0.0f, sum = 0.0f;
        foreach (PlantFlex plant in Plants)
        {
            if (plant.Rod == null)
            {
                continue;
            }
            float tip = plant.Rod.Offset(plant.Rod.Segments).Length();
            most = Mathf.Max(most, tip);
            sum += tip;
            count++;
            hit += plant.Hits > 0 ? 1 : 0;
            pulled += plant.Pulls > 0 ? 1 : 0;
        }
        GD.Print(System.FormattableString.Invariant(
            $"[flex] {count} plants, tips {(count > 0 ? sum / count : 0):F2} m on average, {most:F2} m at most; {hit} hit, {pulled} pulled on"));
    }

    /// <summary>For <c>bench.gd</c>: each plant's tip, how far it has moved, and what has pushed it.</summary>
    public void PrintBenchStatus()
    {
        var line = new System.Text.StringBuilder($"[flex] {Plants.Count} plants");
        foreach (PlantFlex plant in Plants)
        {
            if (plant.Rod == null)
            {
                continue;
            }
            Rod rod = plant.Rod;
            Vector2 tip = rod.Offset(rod.Segments);
            line.Append(System.FormattableString.Invariant(
                $"\n[flex]   {plant.GetParent()?.Name,-22} h={rod.Height,5:F1} tip={tip.Length(),5:F2} m peak={plant.PeakTip,5:F2} m drop={rod.Drop(rod.Segments):F2} hits={plant.Hits} shaken={plant.Kicks} pulls={plant.Pulls} recounts={plant.Recounts} gave way={plant.GaveWay} released={plant.Released} stiffness left={rod.Softest(out int joint):P0} at joint {joint} kinked {Mathf.RadToDeg(rod.Kink(joint)):F1} deg substeps={rod.Substeps}"));
        }
        GD.Print(line.ToString());
        foreach (PlantFlex plant in Plants)
        {
            plant.PeakTip = 0.0f;
        }
    }

    private static void Write(PlantFlex plant)
    {
        Rod rod = plant.Rod;
        if (rod == null || plant.Row < 0)
        {
            return;
        }
        int at = plant.Row * Width * 4;
        Put(at, plant.Foot, 1.0f / rod.SegmentLength);
        Put(at + 4, plant.Up, rod.Segments + 1);
        for (int p = 0; p <= rod.Segments; p++)
        {
            Put(at + ((2 + p) * 4), plant.World(rod.Offset(p), rod.Drop(p)), 0.0f);
        }
    }

    private static void Put(int at, Vector3 v, float w)
    {
        _data[at] = v.X;
        _data[at + 1] = v.Y;
        _data[at + 2] = v.Z;
        _data[at + 3] = w;
    }
}
