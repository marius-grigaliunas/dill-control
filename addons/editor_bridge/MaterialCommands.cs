#if TOOLS
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// A shader material's parameters, as its inspector shows and sets them:
/// <c>material</c> reads them, and with values changes them as one undo, and
/// with save writes the <c>.tres</c>. A material file open in the editor is
/// changed here, never on disk, or the editor writes its own copy back over it.
/// </summary>
public static class MaterialCommands
{
    public static void Register()
    {
        Bridge.Register("material",
            "Read a shader material's parameters (every uniform the shader has, with its value), or with 'values' set them, " +
            "e.g. {\"billow\":0.8}, as one undo; the change shows at once. The material is 'path' (res:// .tres) or the " +
            "material_override of 'node'. 'save': true writes the .tres, which every user of it shares.",
            """{"type":"object","properties":{"path":{"type":"string"},"node":{"type":"string"},"values":{"type":"object"},"save":{"type":"boolean"}}}""",
            Material);
    }

    private static JsonNode Material(JsonObject args)
    {
        ShaderMaterial material = Find(args);
        Shader shader = material.Shader ?? throw new Bridge.Refused($"'{material.ResourcePath}' has no shader");
        var uniforms = shader.GetShaderUniformList().Select(u => (Godot.Collections.Dictionary)u).ToList();

        if (args["values"] is JsonObject values && values.Count > 0)
        {
            var changes = new System.Collections.Generic.List<(string Key, Variant Old, Variant New)>();
            foreach ((string name, JsonNode value) in values)
            {
                Godot.Collections.Dictionary info = uniforms.FirstOrDefault(u => u["name"].AsString() == name)
                    ?? throw new Bridge.Refused($"'{shader.ResourcePath}' has no uniform '{name}'");
                var type = (Variant.Type)info["type"].AsInt32();
                changes.Add(("shader_parameter/" + name, Current(material, name), Values.FromJson(value, type, info["hint_string"].AsString(), name)));
            }
            EditorUndoRedoManager undo = Bridge.BeginAction($"material {string.Join(", ", values.Select(v => v.Key))}");
            foreach ((string key, Variant old, Variant value) in changes)
            {
                material.Set(key, value);
                undo.AddDoProperty(material, key, value);
                undo.AddUndoProperty(material, key, old);
            }
            Bridge.EndAction(undo);
        }

        var result = new JsonObject { ["material"] = material.ResourcePath, ["shader"] = shader.ResourcePath };
        if (Bridge.Flag(args, "save", false))
        {
            if (string.IsNullOrEmpty(material.ResourcePath) || material.ResourcePath.Contains("::"))
                throw new Bridge.Refused("the material is not a file of its own; save the scene instead");
            Error error = ResourceSaver.Save(material);
            if (error != Error.Ok)
                throw new Bridge.Refused($"not saved: {error}");
            result["saved"] = true;
        }
        var parameters = new JsonObject();
        foreach (Godot.Collections.Dictionary u in uniforms)
        {
            string name = u["name"].AsString();
            parameters[name] = Values.ToJson(Current(material, name));
        }
        result["parameters"] = parameters;
        return result;
    }

    // What the material holds, or the shader's default where it holds nothing.
    private static Variant Current(ShaderMaterial material, string name)
    {
        Variant value = material.GetShaderParameter(name);
        return value.VariantType != Variant.Type.Nil
            ? value
            : RenderingServer.ShaderGetParameterDefault(material.Shader.GetRid(), name);
    }

    private static ShaderMaterial Find(JsonObject args)
    {
        if (Bridge.Text(args, "path") is string path)
        {
            return ResourceLoader.Load(path) as ShaderMaterial
                ?? throw new Bridge.Refused($"'{path}' is not a shader material");
        }
        if (Bridge.Text(args, "node") is string at)
        {
            Node node = Bridge.SceneRoot.GetNodeOrNull(at) ?? throw new Bridge.Refused($"no node '{at}'");
            return (node as GeometryInstance3D)?.MaterialOverride as ShaderMaterial
                ?? throw new Bridge.Refused($"'{at}' has no shader material_override");
        }
        throw new Bridge.Refused("give 'path' or 'node'");
    }
}
#endif
