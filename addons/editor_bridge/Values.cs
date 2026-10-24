#if TOOLS
using System;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// A property's value to JSON and back, for <c>get</c> and <c>set</c>.
/// Vectors are arrays, a colour is [r, g, b, a] or "#rrggbb", a resource is
/// its res:// path, a node its path from the scene's root, a rotation in
/// degrees. JSON back is read as the type the property already has.
/// </summary>
public static class Values
{
    private const int MaxItems = 64;

    public static JsonNode ToJson(Variant v, int depth = 0)
    {
        switch (v.VariantType)
        {
            case Variant.Type.Nil: return null;
            case Variant.Type.Bool: return v.AsBool();
            case Variant.Type.Int: return v.AsInt64();
            case Variant.Type.Float: return Math.Round(v.AsDouble(), 4);
            case Variant.Type.String: return v.AsString();
            case Variant.Type.StringName: return v.AsStringName().ToString();
            case Variant.Type.NodePath: return v.AsNodePath().ToString();
            case Variant.Type.Vector2: { Vector2 a = v.AsVector2(); return Arr(a.X, a.Y); }
            case Variant.Type.Vector2I: { Vector2I a = v.AsVector2I(); return new JsonArray(a.X, a.Y); }
            case Variant.Type.Vector3: return Arr(v.AsVector3());
            case Variant.Type.Vector3I: { Vector3I a = v.AsVector3I(); return new JsonArray(a.X, a.Y, a.Z); }
            case Variant.Type.Vector4: { Vector4 a = v.AsVector4(); return Arr(a.X, a.Y, a.Z, a.W); }
            case Variant.Type.Color: { Color c = v.AsColor(); return Arr(c.R, c.G, c.B, c.A); }
            case Variant.Type.Quaternion: return new JsonObject { ["rotation_deg"] = Arr(Degrees(new Basis(v.AsQuaternion()))) };
            case Variant.Type.Basis:
            {
                Basis b = v.AsBasis();
                return new JsonObject { ["rotation_deg"] = Arr(Degrees(b)), ["scale"] = Arr(b.Scale) };
            }
            case Variant.Type.Transform3D:
            {
                Transform3D t = v.AsTransform3D();
                return new JsonObject { ["origin"] = Arr(t.Origin), ["rotation_deg"] = Arr(Degrees(t.Basis)), ["scale"] = Arr(t.Basis.Scale) };
            }
            case Variant.Type.Aabb: { Aabb a = v.AsAabb(); return new JsonObject { ["position"] = Arr(a.Position), ["size"] = Arr(a.Size) }; }
            case Variant.Type.Object:
            {
                GodotObject o = v.AsGodotObject();
                if (o == null)
                    return null;
                if (o is Node n)
                    return n.IsInsideTree() && (n == Bridge.SceneRoot || Bridge.SceneRoot.IsAncestorOf(n)) ? "node:" + Bridge.PathOf(n) : "node:(outside the scene)";
                if (o is Resource r)
                    return !string.IsNullOrEmpty(r.ResourcePath) && !r.ResourcePath.Contains("::") ? r.ResourcePath : $"<{r.GetClass()} in the scene>";
                return $"<{o.GetClass()}>";
            }
            case Variant.Type.Array:
            {
                Godot.Collections.Array a = v.AsGodotArray();
                if (depth > 2)
                    return $"<array of {a.Count}>";
                var j = new JsonArray(a.Take(MaxItems).Select(e => ToJson(e, depth + 1)).ToArray());
                if (a.Count > MaxItems)
                    j.Add($"... {a.Count - MaxItems} more");
                return j;
            }
            case Variant.Type.Dictionary:
            {
                Godot.Collections.Dictionary d = v.AsGodotDictionary();
                if (depth > 2)
                    return $"<dictionary of {d.Count}>";
                var j = new JsonObject();
                foreach (Variant key in d.Keys.Take(MaxItems))
                    j[key.ToString()] = ToJson(d[key], depth + 1);
                return j;
            }
            default:
                return $"<{v.VariantType}>";
        }
    }

    /// <summary>JSON as a value for a property whose type is <paramref name="type"/> (its class in <paramref name="className"/> for an object).</summary>
    public static Variant FromJson(JsonNode j, Variant.Type type, string className, string key)
    {
        try
        {
            switch (type)
            {
                case Variant.Type.Bool: return j!.GetValue<bool>();
                case Variant.Type.Int: return (long)Math.Round(j!.GetValue<double>());
                case Variant.Type.Float: return j!.GetValue<double>();
                case Variant.Type.String: return j!.GetValue<string>();
                case Variant.Type.StringName: return new StringName(j!.GetValue<string>());
                case Variant.Type.NodePath: return new NodePath(j!.GetValue<string>());
                case Variant.Type.Vector2: { float[] a = Floats(j, 2); return new Vector2(a[0], a[1]); }
                case Variant.Type.Vector2I: { float[] a = Floats(j, 2); return new Vector2I((int)a[0], (int)a[1]); }
                case Variant.Type.Vector3: { float[] a = Floats(j, 3); return new Vector3(a[0], a[1], a[2]); }
                case Variant.Type.Vector3I: { float[] a = Floats(j, 3); return new Vector3I((int)a[0], (int)a[1], (int)a[2]); }
                case Variant.Type.Vector4: { float[] a = Floats(j, 4); return new Vector4(a[0], a[1], a[2], a[3]); }
                case Variant.Type.Color:
                {
                    if (j is JsonValue s && s.TryGetValue(out string hex))
                        return Color.FromHtml(hex);
                    float[] a = Floats(j, -1);
                    return new Color(a[0], a[1], a[2], a.Length > 3 ? a[3] : 1.0f);
                }
                case Variant.Type.Object:
                {
                    if (j == null)
                        return default;
                    string text = j.GetValue<string>();
                    if (text.StartsWith("node:"))
                        return Bridge.NodeAt(text["node:".Length..]);
                    if (text.StartsWith("res://"))
                        return ResourceLoader.Load(text) ?? throw new Bridge.Refused($"'{text}' did not load");
                    throw new Bridge.Refused($"'{key}' wants a res:// path or node:<path> ({className})");
                }
                default:
                    throw new Bridge.Refused($"'{key}' is a {type}, which set cannot write yet");
            }
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or NullReferenceException)
        {
            throw new Bridge.Refused($"'{key}' wants a {type}: {e.Message}");
        }
    }

    private static float[] Floats(JsonNode j, int count)
    {
        if (j is not JsonArray a || (count > 0 && a.Count != count) || (count < 0 && a.Count is < 3 or > 4))
            throw new FormatException(count > 0 ? $"an array of {count} numbers" : "[r, g, b] or [r, g, b, a] or \"#rrggbb\"");
        return a.Select(e => (float)e!.GetValue<double>()).ToArray();
    }

    private static Vector3 Degrees(Basis b)
    {
        Vector3 e = b.Orthonormalized().GetEuler();
        return new Vector3(Mathf.RadToDeg(e.X), Mathf.RadToDeg(e.Y), Mathf.RadToDeg(e.Z));
    }

    private static JsonArray Arr(Vector3 v) => Arr(v.X, v.Y, v.Z);

    private static JsonArray Arr(params float[] f) => new(f.Select(x => (JsonNode)Math.Round(x, 4)).ToArray());
}
#endif
