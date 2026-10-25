#if TOOLS
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using DillControl.Voxel;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// What an asset is and what it costs, read from it at rest: its size,
/// meshes and surfaces, its voxel parts with their chunks, and for a mech
/// its damage map (docs/decisions/0047). Text for the tab, JSON for the
/// bridge.
/// </summary>
public sealed class AssetInfo
{
    public sealed class Part
    {
        public string Name;
        public int Voxels;
        public int Chunks;
        public DamageBake Bake;
    }

    public string Path;
    public string RootType;
    public Vector3 Size;
    public int Nodes;
    public int Meshes;
    public int Surfaces;
    public int Vertices;
    public float VoxelSize;
    public readonly List<Part> Parts = new();
    public readonly List<string> Resources = new();

    public static AssetInfo Of(string path, Node3D asset, Aabb bounds)
    {
        var info = new AssetInfo { Path = path, RootType = asset.GetClass(), Size = bounds.Size };
        foreach (Node node in new[] { asset }.Concat(asset.FindChildren("*", "", true, false)))
        {
            info.Nodes++;
            if (node is MeshInstance3D { Mesh: Mesh mesh })
            {
                info.Meshes++;
                info.Surfaces += mesh.GetSurfaceCount();
                if (mesh is ArrayMesh array)
                {
                    for (int s = 0; s < array.GetSurfaceCount(); s++)
                    {
                        info.Vertices += array.SurfaceGetArrayLen(s);
                    }
                }
            }
            if (node.HasMeta(VoxelMeta.Cells))
            {
                byte[] cells = node.GetMeta(VoxelMeta.Cells).AsByteArray();
                info.VoxelSize = LabPreview.VoxelSizeOf(node);
                info.Parts.Add(new Part
                {
                    Name = node.Name,
                    Voxels = cells.Count(c => c != 0),
                    Chunks = node.HasMeta(VoxelMeta.ChunkCount) ? node.GetMeta(VoxelMeta.ChunkCount).AsInt32() : 0,
                    Bake = VoxDamageMap.TryRead(node),
                });
            }
        }
        foreach (Resource resource in AssetCatalog.ResourcesOf(asset))
        {
            info.Resources.Add(resource.ResourcePath);
        }
        return info;
    }

    public int Voxels => Parts.Sum(p => p.Voxels);

    public string Text()
    {
        var text = new StringBuilder();
        text.AppendLine($"[b]{Path.GetFile()}[/b]  {RootType}");
        text.AppendLine($"{Size.X:0.0} x {Size.Y:0.0} x {Size.Z:0.0} m, {Nodes} nodes");
        text.AppendLine($"{Meshes} meshes, {Surfaces} surfaces, {Vertices:N0} vertices");
        if (Parts.Count > 0)
        {
            text.AppendLine($"{Voxels:N0} voxels of {VoxelSize:0.###} m in {Parts.Count} parts, {Parts.Sum(p => p.Chunks)} chunks");
            foreach (Part part in Parts)
            {
                // One line a part: its voxels, and what it hangs from.
                string from = part.Bake == null || part.Bake.Parent == "" ? "" : $"  [color=#8a8a8a]< {part.Bake.Parent}[/color]";
                text.AppendLine($"  {part.Name}  {part.Voxels:N0}{from}");
            }
        }

        return text.ToString();
    }

    public JsonObject Json()
    {
        var parts = new JsonArray();
        foreach (Part part in Parts)
        {
            var one = new JsonObject { ["name"] = part.Name, ["voxels"] = part.Voxels, ["chunks"] = part.Chunks };
            if (part.Bake != null)
            {
                one["parent"] = part.Bake.Parent;
                one["held_by"] = part.Bake.Hold.Length;
                one["centre"] = Bridge.Json(part.Bake.Centre);
                var contacts = new JsonArray();
                for (int k = 0; k < part.Bake.ContactKinds.Length; k++)
                {
                    contacts.Add(new JsonObject
                    {
                        ["kind"] = part.Bake.ContactKinds[k].ToString().ToLowerInvariant(),
                        ["centre"] = Bridge.Json(part.Bake.ContactCentre[k]),
                        ["size"] = Bridge.Json(part.Bake.ContactSize[k]),
                    });
                }
                one["contacts"] = contacts;
            }
            parts.Add(one);
        }
        return new JsonObject
        {
            ["path"] = Path,
            ["root"] = RootType,
            ["size_m"] = Bridge.Json(Size),
            ["nodes"] = Nodes,
            ["meshes"] = Meshes,
            ["surfaces"] = Surfaces,
            ["vertices"] = Vertices,
            ["voxels"] = Voxels,
            ["voxel_size"] = System.Math.Round(VoxelSize, 4),
            ["parts"] = parts,
            ["resources"] = new JsonArray(Resources.Select(r => (JsonNode)r).ToArray()),
        };
    }
}
#endif
