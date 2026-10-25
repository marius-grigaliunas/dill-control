#if TOOLS
using System.Collections.Generic;
using System.Linq;
using DillControl.Voxel;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// What the asset lab can show (docs/decisions/0047): every wrapper scene
/// in the asset folders and every bare model, and for one of them the
/// resources it is tuned by and who else shares each.
/// </summary>
public static class AssetCatalog
{
    public sealed record Asset(string Path, string Group, string Name);

    private static readonly (string Folder, string Group, string Extension)[] Roots =
    {
        ("res://scenes/props", "Props", ".tscn"),
        ("res://scenes/vehicles", "Vehicles", ".tscn"),
        ("res://scenes/enemies", "Enemies", ".tscn"),
        ("res://scenes/weapons", "Weapons", ".tscn"),
        ("res://art/voxels", "Voxels", ".vox"),
        ("res://art/models", "Models", ".glb"),
    };

    public static List<Asset> All()
    {
        var found = new List<Asset>();
        foreach ((string folder, string group, string extension) in Roots)
        {
            Collect(folder, group, extension, found);
        }
        return found;
    }

    private static void Collect(string folder, string group, string extension, List<Asset> into)
    {
        if (!DirAccess.DirExistsAbsolute(folder))
        {
            return;
        }
        foreach (string file in DirAccess.GetFilesAt(folder).OrderBy(f => f))
        {
            if (file.EndsWith(extension))
            {
                into.Add(new Asset($"{folder}/{file}", group, file.GetBaseName()));
            }
        }
        foreach (string sub in DirAccess.GetDirectoriesAt(folder).OrderBy(d => d))
        {
            Collect($"{folder}/{sub}", group, extension, into);
        }
    }

    /// <summary>One by its path, its file name, or its name without the extension.</summary>
    public static Asset Find(string name)
    {
        List<Asset> all = All();
        return all.FirstOrDefault(a => a.Path == name)
            ?? all.FirstOrDefault(a => a.Path.GetFile() == name)
            ?? all.FirstOrDefault(a => a.Name == name);
    }

    /// <summary>
    /// The saved resources (<c>.tres</c>) the nodes under <paramref name="root"/>
    /// are tuned by, each once, in the order met. A <see cref="VoxelBody"/>
    /// with no stats is tuned by the default ones, and they are listed.
    /// </summary>
    public static List<Resource> ResourcesOf(Node root)
    {
        var found = new List<Resource>();
        var seen = new HashSet<string>();
        void Add(Resource resource)
        {
            if (resource == null || !resource.ResourcePath.StartsWith("res://") || !resource.ResourcePath.EndsWith(".tres")
                || resource.ResourcePath.Contains("::") || !seen.Add(resource.ResourcePath))
            {
                return;
            }
            found.Add(resource);
        }
        foreach (Node node in new[] { root }.Concat(root.FindChildren("*", "", true, false)))
        {
            foreach (Godot.Collections.Dictionary property in node.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                if ((Variant.Type)property["type"].AsInt64() != Variant.Type.Object || (usage & PropertyUsageFlags.Storage) == 0)
                {
                    continue;
                }
                Add(node.Get(property["name"].AsStringName()).AsGodotObject() as Resource);
            }
            if (node.GetScript().AsGodotObject() is Script script && script.ResourcePath.EndsWith("/VoxelBody.cs")
                && node.Get("Stats").AsGodotObject() == null)
            {
                Add(GD.Load<Resource>(VoxelDestructionStats.DefaultPath));
            }
        }
        return found;
    }

    /// <summary>Every scene and resource that names <paramref name="path"/>,
    /// by its path or its uid, other than itself.</summary>
    public static List<string> UsersOf(string path)
    {
        long id = ResourceLoader.GetResourceUid(path);
        string uid = id != ResourceUid.InvalidId ? ResourceUid.IdToText(id) : null;
        var users = new List<string>();
        foreach (string folder in new[] { "res://scenes", "res://resources" })
        {
            Users(folder, path, uid, users);
        }
        return users;
    }

    private static void Users(string folder, string path, string uid, List<string> into)
    {
        foreach (string file in DirAccess.GetFilesAt(folder))
        {
            string full = $"{folder}/{file}";
            if ((!file.EndsWith(".tscn") && !file.EndsWith(".tres")) || full == path)
            {
                continue;
            }
            string text = FileAccess.GetFileAsString(full);
            if (text.Contains($"\"{path}\"") || (uid != null && text.Contains($"\"{uid}\"")))
            {
                into.Add(full);
            }
        }
        foreach (string sub in DirAccess.GetDirectoriesAt(folder))
        {
            Users($"{folder}/{sub}", path, uid, into);
        }
    }
}
#endif
