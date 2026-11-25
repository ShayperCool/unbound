using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Records what importing each SpeedTree 8 tree of Assets/Trees gives (see README.md): the
/// importer's public settings, every object of the asset (class, name, local file id, hide flags),
/// the main object's hierarchy with its components, each mesh (layout, sub-meshes, bounds, a hash
/// of every vertex channel and of the indices: no vertex values), each material (shader, keywords,
/// queue, every shader property), the SpeedTree callbacks of each tree's import (SpeedTree8Events),
/// the trees' textures' import settings and the scene's instances of two trees in Play mode. Writes values.jsonl into SPEEDTREE8_PROBE_OUT (floats
/// as the hex of their bits) and exits.
/// </summary>
public sealed class SpeedTree8Probe : MonoBehaviour
{
    const string Trees = "Assets/Trees/";
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("SpeedTree8Probe").AddComponent<SpeedTree8Probe>();

    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value)).Append("\"}\n");

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");
    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");
    static string V(Vector3 v) => F(v.x) + " " + F(v.y) + " " + F(v.z);
    static string V4(Vector4 v) => F(v.x) + " " + F(v.y) + " " + F(v.z) + " " + F(v.w);
    static string Q(Quaternion q) => F(q.x) + " " + F(q.y) + " " + F(q.z) + " " + F(q.w);
    static string B(Bounds b) => V(b.center) + " " + V(b.extents);

    static ulong Fnv(IEnumerable<float> values)
    {
        ulong h = 14695981039346656037UL;
        foreach (var f in values)
            foreach (var b in BitConverter.GetBytes(f)) { h ^= b; h *= 1099511628211UL; }
        return h;
    }

    static ulong FnvInts(IEnumerable<int> values)
    {
        ulong h = 14695981039346656037UL;
        foreach (var i in values)
            foreach (var b in BitConverter.GetBytes(i)) { h ^= b; h *= 1099511628211UL; }
        return h;
    }

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        string dir = Environment.GetEnvironmentVariable("SPEEDTREE8_PROBE_OUT");
        try
        {
            Rec("pipeline", GraphicsSettings.currentRenderPipeline == null ? "builtin" : GraphicsSettings.currentRenderPipeline.GetType().Name);
            var trees = Directory.GetFiles(Trees, "*.st", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/'))
                .Where(p => p.EndsWith(".st", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            foreach (var path in trees) DumpTree(path);
            foreach (var path in Directory.GetFiles(Trees, "*.png", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/'))
                         .OrderBy(p => p, StringComparer.Ordinal))
                DumpTexture(path);
            DumpCallbacks(dir, trees);
            // The trees a GameObject search finds.
            Rec("find/GameObject", string.Join(",", AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Trees" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal)));
            DumpScene();
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
            else
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("speedtree8 probe failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static string Value(object v)
    {
        switch (v)
        {
            case null: return "null";
            case float f: return F(f);
            case Color c: return V4(c);
            case UnityEngine.Object o: return o.GetType().Name + " " + o.name;
            case string s: return s;
            case Array a: return "[" + string.Join(",", a.Cast<object>().Select(Value)) + "]";
            case IFormattable x: return x.ToString(null, CultureInfo.InvariantCulture);
            default: return v.ToString();
        }
    }

    /// <summary>The importer's public settings the Editor's SpeedTreeImporter has.</summary>
    static readonly string[] Settings =
    {
        "LODHeights", "alphaTestRef", "animateCrossFading", "bestWindQuality", "billboardTransitionCrossFadeWidth", "castShadows",
        "castShadowsByDefault", "defaultBillboardShader", "defaultShader", "enableBump", "enableBumpByDefault", "enableHue",
        "enableHueByDefault", "enableSettingOverride", "enableSmoothLODTransition", "enableSubsurface", "enableSubsurfaceByDefault",
        "fadeOutWidth", "generateColliders", "generateRigidbody", "hasBillboard", "hasImported", "hueVariation", "isV8", "mainColor",
        "materialFolderPath", "materialLocation", "receiveShadows", "receiveShadowsByDefault", "reflectionProbeUsages",
        "reflectionProbeUsagesByDefault", "scaleFactor", "selectedWindQuality", "useLightProbes", "useLightProbesByDefault",
        "windQualities",
    };

    void DumpTree(string path)
    {
        string t = path.Substring(Trees.Length);
        // Loaded first: an importer that has imported the tree in this session says so.
        var main = AssetDatabase.LoadMainAssetAtPath(path);
        var importer = AssetImporter.GetAtPath(path);
        Rec(t + "/importer", importer == null ? "null" : importer.GetType().FullName);
        if (importer != null)
            foreach (var name in Settings)
            {
                var p = importer.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                string value;
                try { value = p == null ? "missing" : Value(p.GetValue(importer)); }
                catch (Exception e) { value = "throws " + (e.InnerException ?? e).GetType().Name; }
                Rec(t + "/importer/" + name, value);
            }
        Rec(t + "/main", main == null ? "null" : main.GetType().Name + " " + main.name);
        if (s_Fresh) Rec(t + "/meta", MetaText(path));
        Rec(t + "/mainType", Value(AssetDatabase.GetMainAssetTypeAtPath(path)));
        Rec(t + "/prefabType", main == null ? "null" : PrefabUtility.GetPrefabAssetType(main).ToString());
        var all = AssetDatabase.LoadAllAssetsAtPath(path);
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var meshes = new List<Mesh>();
        var materials = new List<Material>();
        foreach (var o in all)
        {
            if (o == null) continue;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long id);
            string name = o is Component c ? HierarchyPath(c.transform) + ":" + o.GetType().Name
                : o is GameObject g ? HierarchyPath(g.transform) : o.name;
            string key = t + "/object/" + o.GetType().Name + "/" + name;
            string k = key;
            for (int i = 1; rows.ContainsKey(k); i++) k = key + "#" + i;
            rows[k] = id.ToString(CultureInfo.InvariantCulture) + " hide " + o.hideFlags;
            if (o is Mesh m) meshes.Add(m);
            if (o is Material mat) materials.Add(mat);
        }
        foreach (var row in rows) Rec(row.Key, row.Value);
        Rec(t + "/objects", all.Length.ToString(CultureInfo.InvariantCulture));
        if (main is GameObject root)
        {
            Hierarchy(t, root.transform);
            // The materials the renderers draw with, imported or remapped to project files.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && !materials.Contains(m)) materials.Add(m);
        }
        foreach (var m in meshes.OrderBy(m => m.name, StringComparer.Ordinal)) DumpMesh(t, m);
        // An imported material by its name, a project material file by its path (the External
        // materials of each level share names).
        foreach (var m in materials.OrderBy(m => AssetDatabase.GetAssetPath(m) + "/" + m.name, StringComparer.Ordinal))
        {
            string file = AssetDatabase.GetAssetPath(m);
            DumpMaterial(t + "/material/" + (file == path ? m.name : file.StartsWith(Trees, StringComparison.Ordinal) ? file.Substring(Trees.Length) : file), m);
        }
    }

    /// <summary>The scene's instances of trees in Play mode (prefab instances of two .st files).</summary>
    void DumpScene()
    {
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
            .Where(g => g.name != "SpeedTree8Probe").OrderBy(g => g.name, StringComparer.Ordinal).ToArray();
        Rec("scene/roots", string.Join(",", roots.Select(g => g.name)));
        foreach (var root in roots)
        {
            Hierarchy("scene", root.transform);
            Rec("scene/" + root.name + "/source", PrefabUtility.GetCorrespondingObjectFromSource(root) is GameObject source ? AssetDatabase.GetAssetPath(source) : "none");
        }
    }

    static string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;

    void Hierarchy(string t, Transform tr)
    {
        string p = t + "/go/" + HierarchyPath(tr);
        var go = tr.gameObject;
        Rec(p, (go.activeSelf ? "active" : "inactive") + " layer " + go.layer + " tag " + go.tag + " static " + go.isStatic + " hide " + go.hideFlags);
        Rec(p + "/transform", V(tr.localPosition) + " " + Q(tr.localRotation) + " " + V(tr.localScale));
        Rec(p + "/components", string.Join(" ", go.GetComponents<Component>().Select(c => c == null ? "missing" : c.GetType().Name)));
        foreach (var c in go.GetComponents<Component>())
        {
            switch (c)
            {
                case LODGroup g:
                    Rec(p + "/LODGroup", g.fadeMode + " animate " + g.animateCrossFading + " billboard " + g.lastLODBillboard
                        + " size " + F(g.size) + " ref " + V(g.localReferencePoint) + " count " + g.lodCount + " enabled " + g.enabled);
                    var lods = g.GetLODs();
                    for (int i = 0; i < lods.Length; i++)
                        Rec(p + "/LODGroup/lod" + i, F(lods[i].screenRelativeTransitionHeight) + " fade " + F(lods[i].fadeTransitionWidth)
                            + " renderers " + string.Join(",", lods[i].renderers.Select(r => r == null ? "null" : r.name)));
                    break;
                case MeshRenderer r:
                    Rec(p + "/MeshRenderer", string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" : m.name))
                        + " cast " + r.shadowCastingMode + " receive " + r.receiveShadows + " probes " + r.lightProbeUsage
                        + " reflection " + r.reflectionProbeUsage + " motion " + r.motionVectorGenerationMode
                        + " layerMask " + r.renderingLayerMask + " enabled " + r.enabled + " bounds " + B(r.localBounds));
                    break;
                case MeshFilter f:
                    Rec(p + "/MeshFilter", f.sharedMesh == null ? "null" : f.sharedMesh.name);
                    break;
                case UnityEngine.Tree tree:
                    Rec(p + "/Tree", "wind " + (tree.windAsset == null ? "null" : tree.windAsset.GetType().Name + " " + tree.windAsset.name)
                        + " hasSpeedTreeWind " + tree.hasSpeedTreeWind);
                    break;
                case Rigidbody b:
                    Rec(p + "/Rigidbody", "gravity " + b.useGravity + " kinematic " + b.isKinematic + " mass " + F(b.mass)
                        + " interpolation " + b.interpolation + " detection " + b.collisionDetectionMode + " constraints " + b.constraints);
                    break;
                case SphereCollider s:
                    Rec(p + "/SphereCollider", F(s.radius) + " center " + V(s.center) + " trigger " + s.isTrigger + " enabled " + s.enabled);
                    break;
                case CapsuleCollider s:
                    Rec(p + "/CapsuleCollider", F(s.radius) + " height " + F(s.height) + " direction " + s.direction + " center " + V(s.center)
                        + " trigger " + s.isTrigger + " enabled " + s.enabled);
                    break;
                case Transform _:
                    break;
                default:
                    Rec(p + "/" + c.GetType().Name, "present");
                    break;
            }
        }
        for (int i = 0; i < tr.childCount; i++) Hierarchy(t, tr.GetChild(i));
    }

    void DumpMesh(string t, Mesh m)
    {
        string p = t + "/mesh/" + m.name;
        Rec(p, "vertices " + m.vertexCount + " submeshes " + m.subMeshCount + " index " + m.indexFormat + " readable " + m.isReadable
            + " bounds " + B(m.bounds) + " streams " + m.vertexBufferCount);
        Rec(p + "/layout", string.Join(" ", m.GetVertexAttributes().Select(a => a.attribute + ":" + a.format + "x" + a.dimension + "@" + a.stream)));
        for (int s = 0; s < m.subMeshCount; s++)
        {
            var d = m.GetSubMesh(s);
            Rec(p + "/submesh" + s, d.topology + " start " + d.indexStart + " count " + d.indexCount + " base " + d.baseVertex
                + " first " + d.firstVertex + " vcount " + d.vertexCount + " bounds " + B(d.bounds));
            Rec(p + "/submesh" + s + "/indices", FnvInts(m.GetIndices(s)).ToString("x16"));
        }
        Rec(p + "/positions", Fnv(m.vertices.SelectMany(v => new[] { v.x, v.y, v.z })).ToString("x16"));
        Rec(p + "/normals", Fnv(m.normals.SelectMany(v => new[] { v.x, v.y, v.z })).ToString("x16"));
        Rec(p + "/tangents", Fnv(m.tangents.SelectMany(v => new[] { v.x, v.y, v.z, v.w })).ToString("x16"));
        Rec(p + "/colors32", FnvInts(m.colors32.Select(c => c.r | c.g << 8 | c.b << 16 | c.a << 24)).ToString("x16"));
        for (int ch = 0; ch < 8; ch++)
        {
            var uvs = new List<Vector4>();
            m.GetUVs(ch, uvs);
            if (uvs.Count == 0) continue;
            Rec(p + "/uv" + ch, Fnv(uvs.SelectMany(v => new[] { v.x, v.y, v.z, v.w })).ToString("x16"));
        }
    }

    void DumpMaterial(string p, Material m)
    {
        Rec(p, (m.shader == null ? "null" : m.shader.name) + " queue " + m.renderQueue + " instancing " + m.enableInstancing
            + " doubleSidedGI " + m.doubleSidedGI + " flags " + m.globalIlluminationFlags + " path " + AssetDatabase.GetAssetPath(m)
            + " hide " + m.hideFlags);
        Rec(p + "/keywords", string.Join(" ", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)));
        var shader = m.shader;
        if (shader == null) return;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            string name = shader.GetPropertyName(i);
            string value;
            switch (shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Color: value = V4(m.GetColor(name)); break;
                case ShaderPropertyType.Vector: value = V4(m.GetVector(name)); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: value = F(m.GetFloat(name)); break;
                case ShaderPropertyType.Int: value = m.GetInteger(name).ToString(CultureInfo.InvariantCulture); break;
                case ShaderPropertyType.Texture:
                    var tex = m.GetTexture(name);
                    value = (tex == null ? "null" : tex.name + " " + AssetDatabase.GetAssetPath(tex)) + " " + V(m.GetTextureScale(name)) + " " + V(m.GetTextureOffset(name));
                    break;
                default: value = "?"; break;
            }
            Rec(p + "/prop/" + name, value);
        }
    }

    /// <summary>Whether the run imports the samples without .meta files (the fresh profile), whose
    /// written .meta files it records.</summary>
    static readonly bool s_Fresh = File.Exists("SpeedTree8Profile.txt");

    /// <summary>An asset's .meta as written, its random GUID left out.</summary>
    static string MetaText(string path) =>
        File.Exists(path + ".meta")
            ? string.Join("\n", File.ReadAllLines(path + ".meta").Select(l => l.StartsWith("guid: ", StringComparison.Ordinal) ? "guid: (new)" : l))
            : "none";

    void DumpTexture(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        string t = "texture/" + path.Substring(Trees.Length);
        if (importer == null) { Rec(t, "no TextureImporter"); return; }
        Rec(t, importer.textureType + " srgb " + importer.sRGBTexture + " alpha " + importer.alphaSource
            + " transparency " + importer.alphaIsTransparency + " mips " + importer.mipmapEnabled + " wrap " + importer.wrapMode
            + " filter " + importer.filterMode + " max " + importer.maxTextureSize + " readable " + importer.isReadable);
        // What the .meta file says of the settings the trees' import changes.
        if (s_Fresh)
            Rec(t + "/metafile", string.Join(" ", File.ReadAllLines(path + ".meta").Select(l => l.Trim())
                .Where(l => l.StartsWith("alphaIsTransparency:", StringComparison.Ordinal) || l.StartsWith("sRGBTexture:", StringComparison.Ordinal))));
    }

    /// <summary>Each tree's SpeedTree callbacks in the order its import made them.</summary>
    void DumpCallbacks(string dir, string[] trees)
    {
        var lines = string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "events.txt"))
            ? Array.Empty<string>() : File.ReadAllLines(Path.Combine(dir, "events.txt"));
        foreach (var path in trees)
        {
            var mine = lines.Select(l => l.Split('\t')).Where(f => f.Length == 3 && f[1] == path).ToArray();
            string t = path.Substring(Trees.Length);
            Rec(t + "/callbacks", string.Join(",", mine.Select(f => f[0])));
            for (int i = 0; i < mine.Length; i++) Rec(t + "/callbacks/" + i, mine[i][0] + " " + mine[i][2]);
        }
    }
}
