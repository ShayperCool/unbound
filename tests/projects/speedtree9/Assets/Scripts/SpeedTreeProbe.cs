using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Records what importing each SpeedTree 9 tree of Assets/Trees gives: every object of the asset
/// (class, name, local file id, whether LoadAllAssetsAtPath lists it), the main object's hierarchy
/// with its components (transforms, LODGroup, renderers, Tree, colliders, Rigidbody), each mesh
/// (layout, sub-meshes, bounds, a hash of every vertex channel and of the indices) and each
/// material (shader, keywords, queue, every shader property). Writes values.jsonl into
/// SPEEDTREE_PROBE_OUT (floats as the hex of their bits) and exits.
/// </summary>
public sealed class SpeedTreeProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("SpeedTreeProbe").AddComponent<SpeedTreeProbe>();

    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value)).Append("\"}\n");

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
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
        string dir = Environment.GetEnvironmentVariable("SPEEDTREE_PROBE_OUT");
        try
        {
            Rec("pipeline", GraphicsSettings.currentRenderPipeline == null ? "builtin" : GraphicsSettings.currentRenderPipeline.GetType().Name);
            foreach (var path in Directory.GetFiles("Assets/Trees", "*.st9").OrderBy(p => p, StringComparer.Ordinal))
                DumpTree(path.Replace('\\', '/'));
            foreach (var path in Directory.GetFiles("Assets/Boxes", "*.crate").OrderBy(p => p, StringComparer.Ordinal))
                DumpObjects(path.Replace('\\', '/'));
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
            Debug.LogError("speedtree probe failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    void DumpTree(string path)
    {
        string t = Path.GetFileNameWithoutExtension(path);
        var main = AssetDatabase.LoadMainAssetAtPath(path);
        Rec(t + "/main", main == null ? "null" : main.GetType().Name + " " + main.name);
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
            rows[k] = id.ToString(CultureInfo.InvariantCulture);
            if (o is Mesh m) meshes.Add(m);
            if (o is Material mat) materials.Add(mat);
        }
        foreach (var row in rows) Rec(row.Key, row.Value);
        Rec(t + "/objects", all.Length.ToString(CultureInfo.InvariantCulture));
        if (main is GameObject root) Hierarchy(t, root.transform);
        foreach (var m in meshes.OrderBy(m => m.name, StringComparer.Ordinal)) DumpMesh(t, m);
        foreach (var m in materials.OrderBy(m => m.name, StringComparer.Ordinal)) DumpMaterial(t + "/material/" + m.name, m);
    }

    /// <summary>A scripted import's objects (class, name, local file id) and its main object's
    /// hierarchy.</summary>
    void DumpObjects(string path)
    {
        string t = Path.GetFileNameWithoutExtension(path);
        var main = AssetDatabase.LoadMainAssetAtPath(path);
        Rec(t + "/main", main == null ? "null" : main.GetType().Name + " " + main.name);
        var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o == null) continue;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long id);
            string name = o is Component c ? HierarchyPath(c.transform) + ":" + o.GetType().Name
                : o is GameObject g ? HierarchyPath(g.transform) : o.name;
            rows[t + "/object/" + o.GetType().Name + "/" + name] = id.ToString(CultureInfo.InvariantCulture);
        }
        foreach (var row in rows) Rec(row.Key, row.Value);
        if (main is GameObject root) Hierarchy(t, root.transform);
    }

    /// <summary>The scene's objects in Play mode: the instances of the trees, of the variant of a
    /// tree and of the crate, with what the instances override, and a scene object's references
    /// into them.</summary>
    void DumpScene()
    {
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
            .Where(g => g.name != "SpeedTreeProbe").OrderBy(g => g.name, StringComparer.Ordinal).ToArray();
        Rec("scene/roots", string.Join(",", roots.Select(g => g.name)));
        foreach (var root in roots) Hierarchy("scene", root.transform);
    }

    static string PathOf(Component c) => c == null ? "null" : HierarchyPath(c.transform) + ":" + c.GetType().Name;

    static string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;

    void Hierarchy(string t, Transform tr)
    {
        string p = t + "/go/" + HierarchyPath(tr);
        var go = tr.gameObject;
        Rec(p, (go.activeSelf ? "active" : "inactive") + " layer " + go.layer + " tag " + go.tag + " static " + go.isStatic);
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
                    Rec(p + "/Tree", (tree.windAsset == null ? "null" : tree.windAsset.name) + " data " + (tree.data == null ? "null" : tree.data.name));
                    break;
                case Rigidbody b:
                    Rec(p + "/Rigidbody", "gravity " + b.useGravity + " kinematic " + b.isKinematic + " mass " + F(b.mass)
                        + " interpolation " + b.interpolation + " detection " + b.collisionDetectionMode + " constraints " + b.constraints);
                    break;
                case SphereCollider s:
                    Rec(p + "/SphereCollider", F(s.radius) + " center " + V(s.center) + " trigger " + s.isTrigger);
                    break;
                case BoxCollider s:
                    Rec(p + "/BoxCollider", V(s.size) + " center " + V(s.center) + " trigger " + s.isTrigger + " enabled " + s.enabled);
                    break;
                case CrateMarker m:
                    Rec(p + "/CrateMarker", m.count + " label " + m.label + " lid " + PathOf(m.lid));
                    break;
                case Pointer m:
                    Rec(p + "/Pointer", PathOf(m.treeRenderer) + " lid " + PathOf(m.crateLid) + " root " + (m.variantRoot == null ? "null" : HierarchyPath(m.variantRoot.transform)));
                    break;
                case CapsuleCollider s:
                    Rec(p + "/CapsuleCollider", F(s.radius) + " height " + F(s.height) + " direction " + s.direction + " center " + V(s.center) + " trigger " + s.isTrigger);
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
        Rec(p + "/positions", Fnv(m.vertices.SelectMany(v => new[] { v.x, v.y, v.z })).ToString("x16") + " first " + (m.vertexCount > 0 ? V(m.vertices[0]) : ""));
        Rec(p + "/normals", Fnv(m.normals.SelectMany(v => new[] { v.x, v.y, v.z })).ToString("x16"));
        Rec(p + "/tangents", Fnv(m.tangents.SelectMany(v => new[] { v.x, v.y, v.z, v.w })).ToString("x16") + " first " + (m.tangents.Length > 0 ? V4(m.tangents[0]) : ""));
        Rec(p + "/colors32", FnvInts(m.colors32.Select(c => c.r | c.g << 8 | c.b << 16 | c.a << 24)).ToString("x16") + " first " + (m.colors32.Length > 0 ? m.colors32[0].ToString() : ""));
        for (int ch = 0; ch < 8; ch++)
        {
            var uvs = new List<Vector4>();
            m.GetUVs(ch, uvs);
            if (uvs.Count == 0) continue;
            Rec(p + "/uv" + ch, Fnv(uvs.SelectMany(v => new[] { v.x, v.y, v.z, v.w })).ToString("x16") + " first " + V4(uvs[0]));
        }
        Rec(p + "/uvDistribution", F(m.GetUVDistributionMetric(0)) + " " + F(m.GetUVDistributionMetric(1)));
    }

    void DumpMaterial(string p, Material m)
    {
        Rec(p, (m.shader == null ? "null" : m.shader.name) + " queue " + m.renderQueue + " instancing " + m.enableInstancing
            + " doubleSidedGI " + m.doubleSidedGI + " flags " + m.globalIlluminationFlags);
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
                    value = (tex == null ? "null" : tex.name) + " " + V(m.GetTextureScale(name)) + " " + V(m.GetTextureOffset(name));
                    break;
                default: value = "?"; break;
            }
            Rec(p + "/prop/" + name, value);
        }
    }
}
