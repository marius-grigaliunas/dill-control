#if TOOLS
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DillControl.Foliage;
using DillControl.Mission;
using DillControl.Voxel;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// A planned nest made ready to look at in the editor: each vine's voxels
/// meshed as the importer meshes them, its leaves and its pods, stood on a
/// flat disc where <see cref="NestPlacer"/> stands it on the planet (the heart
/// at the middle, each vine at its distance and bearing, turned so its lean
/// points at the heart). The planet itself is the game's and is not drawn:
/// its classes are placeholders in the editor.
///
/// <see cref="Build"/> runs on worker threads; the Nest tab makes the nodes.
/// </summary>
public static class NestPreview
{
    public sealed class Vine
    {
        public NestLayout.Vine Planned;
        public Transform3D Place;
        /// <summary>Where the mesh sits in the vine's space: its foot on the origin.</summary>
        public Vector3 MeshOffset;
        public VoxMesher.Arrays Mesh;
        public VineLeafSet Leaves;
        /// <summary>Each pod, in the vine's space.</summary>
        public Vector3[] Pods;
    }

    public static List<Vine> Build(NestLayout layout, NestKit kit, VineBakeStats stats)
    {
        var made = new Vine[layout.Vines.Count];
        Parallel.For(0, made.Length, i =>
        {
            NestLayout.Vine planned = layout.Vines[i];
            VineGenerator.Result vine = planned.Grown?.Vine;
            if (vine == null || !planned.Grown.Ok)
                return;
            VinePalette palette = VineBaker.PaletteFor(planned.Kind, stats, vine);
            VoxFile file = VoxFile.Load(VineBaker.Encode(vine, palette));
            VoxFile.Model model = file.Models[0];
            float vs = (float)vine.VoxelSize;
            Vector3 centre = VoxSpace.Centre(model.Size);
            (int X, int Y, int Z) t = VineBaker.FootTranslation(vine);
            var translation = new Vector3(t.X, t.Y, t.Z);
            made[i] = new Vine
            {
                Planned = planned,
                Place = PlaceOf(planned, kit),
                MeshOffset = VoxSpace.ToGodot(translation) * vs,
                Mesh = VoxMesher.BuildArrays(model.Cells, model.Size, Vector3I.Zero, model.Size, file.Palette, file.Emission, centre, vs, 255),
                Leaves = VineLeafSet.Of(vine, planned.Kind, palette),
                Pods = vine.Markers.Select(m => VoxSpace.ToGodot(translation + new Vector3(m.X + 0.5f, m.Y + 0.5f, m.Z + 0.5f) - centre) * vs).ToArray(),
            };
        });
        return made.Where(v => v != null).ToList();
    }

    /// <summary>Its place on the disc: as <see cref="NestPlacer"/> places it,
    /// with north along -Z and the ground level.</summary>
    public static Transform3D PlaceOf(NestLayout.Vine vine, NestKit kit)
    {
        Vector3 at = Vector3.Forward.Rotated(Vector3.Up, vine.Bearing) * vine.RadiusM;
        Vector3 toHeart = -at;
        Basis facing = toHeart.LengthSquared() > 1e-6f ? Basis.LookingAt(toHeart.Normalized(), Vector3.Up) : Basis.Identity;
        Vector3 lean = vine.Lean;
        float turn = lean.LengthSquared() > 1e-6f ? Mathf.Pi - Mathf.Atan2(lean.X, lean.Z) : 0.0f;
        return new Transform3D(facing * new Basis(Vector3.Up, turn), at - (Vector3.Up * kit.SinkM));
    }
}
#endif
