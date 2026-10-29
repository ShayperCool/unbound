using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what the Material API reports: which names a material has (its shader's properties,
/// properties saved in the asset that the shader does not declare, ones set from script), the main
/// texture and colour of a shader that names them by attribute, what survives a shader change.
/// Writes <c>MATERIALS_PROBE_OUT</c> (one "key": "value" per check) and quits.
/// </summary>
public class MaterialsProbe : MonoBehaviour
{
    readonly List<(string key, string value)> m_Results = new List<(string, string)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("MaterialsProbe").AddComponent<MaterialsProbe>();

    static readonly string[] Names =
    {
        "_BaseMap", "_BaseColor", "_Gloss", "_Count", "_Dir", "_SecondTex", "_MainTex", "_Color",
        "_StaleTex", "_StaleInt", "_StaleFloat", "_StaleColor", "_Missing", "_New", "_Legacy", "_Hidden",
    };

    void Add(string key, object value) => m_Results.Add((key, Format(value)));

    static string Format(object value)
    {
        switch (value)
        {
            case null: return "null";
            case float f: return f.ToString("0.####", CultureInfo.InvariantCulture);
            case Color c: return $"({Format(c.r)}, {Format(c.g)}, {Format(c.b)}, {Format(c.a)})";
            case Vector4 v: return $"({Format(v.x)}, {Format(v.y)}, {Format(v.z)}, {Format(v.w)})";
            case Vector2 v: return $"({Format(v.x)}, {Format(v.y)})";
            case Matrix4x4 m: return string.Join(" ", Enumerable.Range(0, 16).Select(i => Format(m[i])));
            case Texture t: return t ? t.name : "null";
            case bool b: return b ? "true" : "false";
            case IEnumerable<string> s: return string.Join(",", s);
            default: return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    void Has(string prefix, Material m)
    {
        foreach (var n in Names)
        {
            Add($"{prefix}.HasProperty({n})", m.HasProperty(n));
            Add($"{prefix}.HasFloat({n})", m.HasFloat(n));
            Add($"{prefix}.HasInteger({n})", m.HasInteger(n));
            Add($"{prefix}.HasColor({n})", m.HasColor(n));
            Add($"{prefix}.HasVector({n})", m.HasVector(n));
            Add($"{prefix}.HasTexture({n})", m.HasTexture(n));
        }
    }

    void Values(string prefix, Material m, bool keywords = true)
    {
        Add($"{prefix}.mainTexture", m.mainTexture);
        Add($"{prefix}.mainTextureScale", m.mainTextureScale);
        Add($"{prefix}.mainTextureOffset", m.mainTextureOffset);
        Add($"{prefix}.color", m.color);
        foreach (var n in Names)
        {
            Add($"{prefix}.GetFloat({n})", m.GetFloat(n));
            Add($"{prefix}.GetInteger({n})", m.GetInteger(n));
            Add($"{prefix}.GetColor({n})", m.GetColor(n));
            Add($"{prefix}.GetTexture({n})", m.GetTexture(n));
            Add($"{prefix}.GetTextureScale({n})", m.GetTextureScale(n));
        }
        Add($"{prefix}.GetVector(_Missing)", m.GetVector("_Missing"));
        Add($"{prefix}.GetMatrix(_Missing)", m.GetMatrix("_Missing"));
        Add($"{prefix}.GetInt(_Legacy)", m.GetInt("_Legacy"));
        Add($"{prefix}.texturePropertyNames", m.GetTexturePropertyNames());
        foreach (MaterialPropertyType t in Enum.GetValues(typeof(MaterialPropertyType)))
            Add($"{prefix}.GetPropertyNames({t})", m.GetPropertyNames(t));
        if (keywords) Add($"{prefix}.shaderKeywords", m.shaderKeywords);
    }

    void Start()
    {
        int logged = 0;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (type != LogType.Log) Add($"log{logged++:000}", $"{type}: {message}");
        };
        var saved = Resources.Load<Material>("Saved");
        foreach (var shader in new[] { saved.shader, Shader.Find("Probe/Plain") })
            for (int i = 0; i < shader.GetPropertyCount(); i++)
                Add($"{shader.name}.property{i}", $"{shader.GetPropertyName(i)} {shader.GetPropertyType(i)} [{shader.GetPropertyFlags(i)}] " +
                    $"[{string.Join("|", shader.GetPropertyAttributes(i))}] '{shader.GetPropertyDescription(i)}'");
        Has("saved", saved);
        // The first material the Editor loads with a shader may get the keywords of its drawers'
        // defaults (seen, not modelled): no keywords checked for it.
        Values("saved", saved, keywords: false);

        var copy = new Material(saved);
        copy.SetFloat("_New", 2f);
        copy.SetColor("_StaleColor", Color.cyan);
        Add("copy.HasProperty(_New)", copy.HasProperty("_New"));
        Add("copy.GetFloat(_New)", copy.GetFloat("_New"));
        Add("copy.HasFloat(_New)", copy.HasFloat("_New"));
        Add("copy.HasColor(_New)", copy.HasColor("_New"));
        Add("copy.GetColor(_New)", copy.GetColor("_New"));
        Add("copy.HasProperty(_StaleColor)", copy.HasProperty("_StaleColor"));
        Add("copy.HasColor(_StaleColor)", copy.HasColor("_StaleColor"));
        Add("copy.GetColor(_StaleColor)", copy.GetColor("_StaleColor"));
        copy.mainTexture = Resources.Load<Texture2D>("TexB");
        copy.color = Color.yellow;
        copy.mainTextureScale = new Vector2(6f, 7f);
        Add("copy.set.GetTexture(_BaseMap)", copy.GetTexture("_BaseMap"));
        Add("copy.set.GetTexture(_MainTex)", copy.GetTexture("_MainTex"));
        Add("copy.set.GetColor(_BaseColor)", copy.GetColor("_BaseColor"));
        Add("copy.set.GetColor(_Color)", copy.GetColor("_Color"));
        Add("copy.set.GetTextureScale(_BaseMap)", copy.GetTextureScale("_BaseMap"));
        Add("copy.set.GetTextureScale(_MainTex)", copy.GetTextureScale("_MainTex"));

        // Switch to a shader with the conventional names: what the material keeps.
        var plain = Shader.Find("Probe/Plain");
        var switched = new Material(saved);
        switched.shader = plain;
        Has("switched", switched);
        Values("switched", switched, keywords: false);
        switched.shader = saved.shader;
        Add("back.GetFloat(_Gloss)", switched.GetFloat("_Gloss"));
        Add("back.GetColor(_BaseColor)", switched.GetColor("_BaseColor"));
        Add("back.GetTexture(_BaseMap)", switched.GetTexture("_BaseMap"));
        Add("back.HasProperty(_StaleFloat)", switched.HasProperty("_StaleFloat"));

        var fresh = new Material(saved.shader);
        Has("fresh", fresh);
        Values("fresh", fresh);
        var plainFresh = new Material(plain);
        Values("plainFresh", plainFresh);

        Add("conflict.shaderKeywords", Resources.Load<Material>("Conflict").shaderKeywords);
        Add("qualityHigh.shaderKeywords", Resources.Load<Material>("QualityHigh").shaderKeywords);
        Add("conflictInvalid.shaderKeywords", Resources.Load<Material>("ConflictInvalid").shaderKeywords);
        Add("copy.name", copy.name);
        Add("fresh.name", fresh.name);
        var json = new StringBuilder("{\n");
        for (int i = 0; i < m_Results.Count; i++)
            json.Append("  \"").Append(m_Results[i].key).Append("\": \"").Append(m_Results[i].value.Replace("\"", "\\\""))
                .Append(i + 1 < m_Results.Count ? "\",\n" : "\"\n");
        json.Append("}\n");
        var output = Environment.GetEnvironmentVariable("MATERIALS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) { Debug.Log(json.ToString()); return; }
        File.WriteAllText(output, json.ToString());
        Debug.Log("materials probe written to " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
