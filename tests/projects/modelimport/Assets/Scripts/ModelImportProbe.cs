using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what model import gives a game: each model's hierarchy and components, every
/// renderer's material slots (shader, render queue, tags, keywords, GI flags and the value of every
/// shader property, textures by name, size, offset and scale), which slots share a Material, and
/// each LODGroup. Writes <c>MODEL_IMPORT_PROBE_OUT</c> (one {"k","v"} JSON line per value) and quits.
/// </summary>
public class ModelImportProbe : MonoBehaviour
{
    public GameObject[] models;

    readonly StringBuilder m_Out = new StringBuilder();

    void Add(string key, object value)
    {
        m_Out.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");
    }

    static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static string Number(float f)
    {
        if (float.IsNaN(f) || float.IsInfinity(f)) return Quote(f.ToString(CultureInfo.InvariantCulture));
        return f.ToString("R", CultureInfo.InvariantCulture);
    }

    static string Json(object value)
    {
        switch (value)
        {
            case null: return "null";
            case string s: return Quote(s);
            case bool b: return b ? "true" : "false";
            case int i: return i.ToString(CultureInfo.InvariantCulture);
            case float f: return Number(f);
            case Color c: return $"[{Number(c.r)},{Number(c.g)},{Number(c.b)},{Number(c.a)}]";
            case Vector4 v: return $"[{Number(v.x)},{Number(v.y)},{Number(v.z)},{Number(v.w)}]";
            case Vector3 v: return $"[{Number(v.x)},{Number(v.y)},{Number(v.z)}]";
            case Vector2 v: return $"[{Number(v.x)},{Number(v.y)}]";
            case IEnumerable<string> list: return "[" + string.Join(",", list.Select(Quote)) + "]";
            default: return Quote(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }

    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
        parts.Reverse();
        return parts.Count == 0 ? "." : string.Join("/", parts);
    }

    void Start()
    {
        foreach (var model in models)
        {
            var instance = Instantiate(model);
            instance.name = model.name;
            Model(model.name, instance.transform);
            DestroyImmediate(instance);
        }
        var path = Environment.GetEnvironmentVariable("MODEL_IMPORT_PROBE_OUT");
        if (!string.IsNullOrEmpty(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, m_Out.ToString());
        }
        else
        {
            Debug.Log(m_Out.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void Model(string name, Transform root)
    {
        var materials = new List<Material>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var key = name + "/" + PathOf(t, root);
            Add(key + ".components", t.GetComponents<Component>().Select(c => c.GetType().Name).ToList());
            var renderer = t.GetComponent<Renderer>();
            if (renderer != null)
            {
                var slots = renderer.sharedMaterials;
                Add(key + ".slots", slots.Length);
                for (int i = 0; i < slots.Length; i++)
                    Material(key + ".mat" + i, slots[i], materials);
            }
            var animator = t.GetComponent<Animator>();
            if (animator != null)
            {
                Add(key + ".avatar", animator.avatar != null ? animator.avatar.name : null);
                Add(key + ".avatarValid", animator.avatar != null && animator.avatar.isValid);
                Add(key + ".isHuman", animator.isHuman);
            }
            var group = t.GetComponent<LODGroup>();
            if (group != null) Lods(key + ".lod", group, root);
        }
    }

    void Material(string key, Material m, List<Material> seen)
    {
        if (m == null)
        {
            Add(key, null);
            return;
        }
        int index = seen.IndexOf(m);
        if (index >= 0)
        {
            Add(key + ".same", index);
            return;
        }
        seen.Add(m);
        Add(key + ".index", seen.Count - 1);
        Add(key + ".name", m.name);
        var shader = m.shader;
        Add(key + ".shader", shader != null ? shader.name : null);
#if UNITY_EDITOR
        var asset = UnityEditor.AssetDatabase.GetAssetPath(m);
        Add(key + ".asset", asset);
        // Unity's own Default-Material: not made by the import (and the Editor's copy changes as it
        // imports), so only which one it is.
        if (asset.StartsWith("Resources/") || asset.StartsWith("Library/")) return;
#endif
        Add(key + ".renderQueue", m.renderQueue);
        Add(key + ".rawRenderQueue", m.rawRenderQueue);
        Add(key + ".RenderType", m.GetTag("RenderType", false));
        Add(key + ".keywords", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal).ToList());
        Add(key + ".gi", m.globalIlluminationFlags.ToString());
        Add(key + ".doubleSided", m.doubleSidedGI);
        if (shader == null) return;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            var prop = shader.GetPropertyName(i);
            var p = key + "." + prop;
            switch (shader.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    Add(p, m.GetColor(prop));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    Add(p, m.GetVector(prop));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    Add(p, m.GetFloat(prop));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Int:
                    Add(p, m.GetInteger(prop));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    var tex = m.GetTexture(prop);
                    Add(p, tex != null ? $"{tex.name} {tex.width}x{tex.height}" : null);
                    Add(p + ".offset", m.GetTextureOffset(prop));
                    Add(p + ".scale", m.GetTextureScale(prop));
                    break;
            }
        }
    }

    void Lods(string key, LODGroup group, Transform root)
    {
        Add(key + ".count", group.lodCount);
        Add(key + ".fadeMode", group.fadeMode.ToString());
        Add(key + ".animateCrossFading", group.animateCrossFading);
        Add(key + ".size", group.size);
        Add(key + ".localReferencePoint", group.localReferencePoint);
        Add(key + ".enabled", group.enabled);
        var lods = group.GetLODs();
        for (int i = 0; i < lods.Length; i++)
        {
            Add(key + i + ".height", lods[i].screenRelativeTransitionHeight);
            Add(key + i + ".fadeWidth", lods[i].fadeTransitionWidth);
            Add(key + i + ".renderers", lods[i].renderers.Select(r => r != null ? PathOf(r.transform, root) : "null").ToList());
        }
    }
}
