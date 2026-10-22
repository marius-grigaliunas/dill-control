using System.Diagnostics;
using DillControl.Voxel;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// A vine made in memory, with no files: the wrapper a baked vine's scene is
/// (<see cref="VineBaker"/>), around the model its .vox would import as. The
/// .vox bytes are made and read back in memory and built by the importer's
/// own <see cref="VoxModelBuilder"/>, with the chunk jitter seeded from the
/// path a bake would write, so the two are the same vine voxel for voxel and
/// chunk for chunk (<c>vinecheck.gd</c> checks it).
///
/// <see cref="BuildData"/> is the cost and may run on a worker;
/// <see cref="MakeNodes"/> is the main thread's part.
/// </summary>
public static class VineFactory
{
    /// <summary>The LODs' normal merge angle, the importer's default.</summary>
    private const float LodNormalMergeAngle = 60.0f;

    public sealed class Data
    {
        public string Name;
        public VineGrower.Grown Grown;
        public VineGenerator.Result Vine => Grown.Vine;
        /// <summary>Null when no valid vine grew: <see cref="Grown"/> says why.</summary>
        public VoxModelBuilder.Model Model;
        public VineLeafSet Leaves;
        public double GenerateMs;
        public double ModelMs;
    }

    /// <summary>Grows the vine the seed means (<see cref="VineGrower"/>) and
    /// builds its model: meshing and chunk graph.
    /// Pure; any thread.</summary>
    public static Data BuildData(VineKind kind, int seed, VineBakeStats stats, VineParams settings = null, bool changed = false)
    {
        var clock = Stopwatch.StartNew();
        VineGrower.Grown grown = settings != null ? VineGrower.FromSettings(settings, stats) : VineGrower.Grow(kind, seed, stats);
        double generateMs = clock.Elapsed.TotalMilliseconds;
        Data data = BuildData(grown, kind, stats, VineBaker.NameOf(kind, seed, changed));
        data.GenerateMs = generateMs;
        return data;
    }

    /// <summary>The model of a vine already grown, named <paramref name="name"/>
    /// (its chunk jitter is seeded from the path a bake of that name would
    /// write). Any thread.</summary>
    public static Data BuildData(VineGrower.Grown grown, VineKind kind, VineBakeStats stats, string name)
    {
        VineGenerator.Result vine = grown.Vine;
        var data = new Data { Name = name, Grown = grown };
        if (!grown.Ok)
            return data;

        var clock = Stopwatch.StartNew();
        VinePalette palette = VineBaker.PaletteFor(kind, stats, vine);
        VoxFile file = VoxFile.Load(VineBaker.Encode(vine, palette));
        data.Model = VoxModelBuilder.Build(file, new VoxModelBuilder.Options
        {
            VoxelSize = stats.VoxelSize,
            PivotIndex = 255,
            ChunkSize = VineBaker.ChunkSize,
            ChunkJitter = VineBaker.ChunkJitter,
            Path = $"{stats.VoxelFolder}/{data.Name}.vox",
        });
        data.Leaves = VineLeafSet.Of(vine, kind, palette);
        data.ModelMs = clock.Elapsed.TotalMilliseconds;
        return data;
    }

    /// <summary>The wrapper's nodes, on the main thread: <c>Voxels</c> (a static
    /// <see cref="VoxelBody"/> drawn whole until changed), <c>Model</c>,
    /// <c>Flex</c> and <c>Leaves</c>, named after the vine. Null if the vine failed.</summary>
    public static Node3D MakeNodes(Data data, VineBakeStats stats, bool lods = true)
    {
        if (data.Model == null)
            return null;
        var material = GD.Load<Material>(VineBaker.Material);
        Node3D model = VoxModelBuilder.Instantiate(data.Model, "Model", lods ? WithLods(material) : VoxModelBuilder.MeshInstances(material));

        var root = new Node3D { Name = data.Name };
        var voxels = new VoxelBody
        {
            Name = "Voxels",
            Stats = (VoxelDestructionStats)(stats.Destruction ?? GD.Load<Resource>(VineBaker.DestructionPath)),
            Static = true,
            WholeUntilChanged = true,
        };
        var flex = new PlantFlex
        {
            Name = "Flex",
            Stats = (FlexStats)(stats.Flex ?? GD.Load<Resource>(FlexStats.VinePath)),
        };
        root.AddChild(voxels);
        root.AddChild(model);
        root.AddChild(flex);
        voxels.Model = model;
        flex.Body = voxels;
        root.AddChild(new VineLeaves
        {
            Name = "Leaves",
            Set = data.Leaves,
            Body = voxels,
            Flex = flex,
            Model = model,
            Stats = GD.Load<LeafStats>(LeafStats.DefaultPath),
        });
        return root;
    }

    /// <summary>Both steps at once, on the main thread.</summary>
    public static Node3D Grow(VineKind kind, int seed, VineBakeStats stats, out Data data)
    {
        data = BuildData(kind, seed, stats);
        return MakeNodes(data, stats);
    }

    // As the importer leaves it: an ArrayMesh with LODs, so a vine far off
    // draws as few triangles as a baked one.
    private static VoxModelBuilder.MeshNodeMaker WithLods(Material material) => (name, arrays, transform) =>
    {
        var mesh = new ImporterMesh { ResourceName = name };
        mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays, material: material, name: "voxel");
        mesh.GenerateLods(LodNormalMergeAngle, 0.0f, new Godot.Collections.Array());
        return new MeshInstance3D { Name = name, Mesh = mesh.GetMesh(), Transform = transform };
    };
}
