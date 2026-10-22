using Godot;

namespace DillControl.Foliage;

/// <summary>
/// One vine's leaves, as data: where each is and how big, and the shapes and
/// colours they are drawn in. In the vine's model space (Godot axes, metres,
/// the stalk's foot on the origin). A bake writes it beside the vine's .vox
/// (<c>prp_vine_kind_seed_leaves.tres</c>); a vine made in memory makes its
/// own (<see cref="VineFactory"/>). <see cref="VineLeaves"/> draws and runs it.
/// </summary>
[GlobalClass, Tool]
public partial class VineLeafSet : Resource
{
    /// <summary>Where each stem leaves the stalk or an arm.</summary>
    [Export] public Vector3[] Attach { get; set; } = System.Array.Empty<Vector3>();
    /// <summary>Each blade's middle.</summary>
    [Export] public Vector3[] Centre { get; set; } = System.Array.Empty<Vector3>();
    /// <summary>Along each midrib, base to tip.</summary>
    [Export] public Vector3[] Midrib { get; set; } = System.Array.Empty<Vector3>();
    /// <summary>Each blade's face, upward.</summary>
    [Export] public Vector3[] Face { get; set; } = System.Array.Empty<Vector3>();
    /// <summary>Across each blade, metres.</summary>
    [Export] public float[] Size { get; set; } = System.Array.Empty<float>();
    [Export] public int[] Shape { get; set; } = System.Array.Empty<int>();
    /// <summary>Each leaf's colour against <see cref="Blade"/>, a multiplier near white.</summary>
    [Export] public Color[] Tint { get; set; } = System.Array.Empty<Color>();

    /// <summary>The shapes, <see cref="LeafMesh.Shape"/> by index: lobes, teeth, notch, cup.</summary>
    [Export] public int[] ShapeLobes { get; set; } = System.Array.Empty<int>();
    [Export] public int[] ShapeTeeth { get; set; } = System.Array.Empty<int>();
    [Export] public float[] ShapeNotch { get; set; } = System.Array.Empty<float>();
    [Export] public float[] ShapeCup { get; set; } = System.Array.Empty<float>();

    [Export] public Color Blade { get; set; }
    [Export] public Color Vein { get; set; }
    [Export] public Color Stem { get; set; }

    public int Count => Centre.Length;

    public LeafMesh.Shape ShapeAt(int index) =>
        new(ShapeLobes[index], ShapeTeeth[index], ShapeNotch[index], ShapeCup[index]);

    /// <summary>A grown vine's leaves in <paramref name="palette"/> (its shade,
    /// <see cref="VineBaker.PaletteFor"/>), placed as its model is: the voxel point
    /// q lands at (t + q - floor(size / 2)) times the voxel size, in Godot's
    /// axes, t the translation that stands the foot on the origin.</summary>
    public static VineLeafSet Of(VineGenerator.Result vine, VineKind kind, VinePalette palette)
    {
        palette ??= new VinePalette();
        float vs = (float)vine.VoxelSize;
        (int tx, int ty, int tz) = VineBaker.FootTranslation(vine);
        var shift = new Vector3(tx - (vine.BoundsX / 2), ty - (vine.BoundsY / 2), tz - (vine.BoundsZ / 2));
        Vector3 Point(double x, double y, double z) => Voxel.VoxSpace.ToGodot(new Vector3((float)x, (float)y, (float)z) + shift) * vs;
        Vector3 Direction(double x, double y, double z) => Voxel.VoxSpace.ToGodot(new Vector3((float)x, (float)y, (float)z)).Normalized();

        int n = vine.Leaves.Count;
        var set = new VineLeafSet
        {
            Attach = new Vector3[n], Centre = new Vector3[n], Midrib = new Vector3[n], Face = new Vector3[n],
            Size = new float[n], Shape = new int[n], Tint = new Color[n],
        };
        for (int i = 0; i < n; i++)
        {
            VineGenerator.LeafPlace l = vine.Leaves[i];
            set.Attach[i] = Point(l.AX, l.AY, l.AZ);
            set.Centre[i] = Point(l.CX, l.CY, l.CZ);
            set.Midrib[i] = Direction(l.MX, l.MY, l.MZ);
            set.Face[i] = Direction(l.NX, l.NY, l.NZ);
            set.Size[i] = (float)l.SizeM;
            set.Shape[i] = l.Shape;
            set.Tint[i] = palette.LeafTint((int)vine.Params.Seed, i);
        }
        LeafMesh.Shape[] shapes = LeafMesh.ShapesOf(kind);
        set.ShapeLobes = new int[shapes.Length];
        set.ShapeTeeth = new int[shapes.Length];
        set.ShapeNotch = new float[shapes.Length];
        set.ShapeCup = new float[shapes.Length];
        for (int s = 0; s < shapes.Length; s++)
        {
            set.ShapeLobes[s] = shapes[s].Lobes;
            set.ShapeTeeth[s] = shapes[s].Teeth;
            set.ShapeNotch[s] = shapes[s].Notch;
            set.ShapeCup[s] = shapes[s].Cup;
        }
        // Linear, as the importer writes voxel colours for voxel_toon.
        set.Blade = palette.Leaf.SrgbToLinear();
        set.Vein = palette.Knuckle.SrgbToLinear();
        set.Stem = palette.Petiole.SrgbToLinear();
        return set;
    }
}
