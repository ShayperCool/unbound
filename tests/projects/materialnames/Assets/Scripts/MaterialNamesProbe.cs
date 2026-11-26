using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $MATERIALNAMES_OUT (tests/value_probe.py rows): which property names a
/// material lists and has after values are set from script for names its shader does not declare
/// (every kind, in an order that is neither declaration nor name order), in a copy, after a shader
/// change, and around the saves of a material asset (AssetDatabase.SaveAssetIfDirty, SaveAssets,
/// CreateAsset): what the material has after each, and the file written.
/// Values never contain instance ids.
/// </summary>
public class MaterialNamesProbe : MonoBehaviour
{
    const string Variable = "MATERIALNAMES_OUT";

    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    /// <summary>A value of an action, or what it throws, and (as key.logs) the logs it writes.</summary>
    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    static string F(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    static string C(Color c) => F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a);
    static string V(Vector4 v) => F(v.x) + " " + F(v.y) + " " + F(v.z) + " " + F(v.w);
    static string T(Texture t) => ReferenceEquals(t, null) ? "null" : t == null ? "destroyed" : t.name;

    static readonly MaterialPropertyType[] Kinds = (MaterialPropertyType[])Enum.GetValues(typeof(MaterialPropertyType));

    /// <summary>GetPropertyNames of every kind and GetTexturePropertyNames, one row each.</summary>
    static void Names(string key, Material m)
    {
        foreach (var kind in Kinds)
            Try(key + "/names/" + kind, () => string.Join(" ", m.GetPropertyNames(kind)));
        Try(key + "/texture_names", () => string.Join(" ", m.GetTexturePropertyNames()));
    }

    /// <summary>The Has* answers of a name.</summary>
    static string Has(Material m, string n) =>
        "property " + m.HasProperty(n) + " float " + m.HasFloat(n) + " integer " + m.HasInteger(n) + " color " + m.HasColor(n)
        + " texture " + m.HasTexture(n) + " matrix " + m.HasMatrix(n);

    /// <summary>The values the save rows set: a float saved for a name the shader lacks
    /// (_Saved), names neither saved nor declared of each kind, and a declared float.</summary>
    static readonly string[] SaveNames = { "_Saved", "_Fresh", "_FreshColor", "_FreshTex", "_FreshM", "_FreshA", "_Glossiness" };

    static void SetForSave(Material m)
    {
        m.SetFloat("_Saved", 2);
        m.SetFloat("_Fresh", 3);
        m.SetColor("_FreshColor", new Color(0.25f, 0.5f, 0.75f, 1f));
        m.SetTexture("_FreshTex", Texture2D.whiteTexture);
        m.SetMatrix("_FreshM", Matrix4x4.Scale(new Vector3(2, 3, 4)));
        m.SetFloatArray("_FreshA", new[] { 4f, 5f });
        m.SetFloat("_Glossiness", 0.75f);
    }

    /// <summary>What the material has of the save rows' names: Has*, then the getter of the kind
    /// each was set as, one row each, then the property names.</summary>
    static void State(string key, Material m)
    {
        foreach (var n in SaveNames)
            Try(key + "/has/" + n, () => Has(m, n));
        Try(key + "/get/_Saved", () => F(m.GetFloat("_Saved")));
        Try(key + "/get/_Fresh", () => F(m.GetFloat("_Fresh")));
        Try(key + "/get/_FreshColor", () => C(m.GetColor("_FreshColor")));
        Try(key + "/get/_FreshTex", () => T(m.GetTexture("_FreshTex")));
        Try(key + "/get/_FreshM", () => F(m.GetMatrix("_FreshM").m11));
        Try(key + "/get/_FreshA", () => F(m.GetFloat("_FreshA")) + " array " + (m.GetFloatArray("_FreshA")?.Length ?? -1));
        Try(key + "/get/_Glossiness", () => F(m.GetFloat("_Glossiness")));
        Names(key, m);
    }

    /// <summary>After a save: the same names under Probe/Other, which declares _Saved, _Fresh,
    /// _FreshColor and _FreshTex, and back under Probe/Names.</summary>
    static void Switched(string key, Material m, Shader names, Shader other)
    {
        m.shader = other;
        Try(key + "/other", () => "saved " + F(m.GetFloat("_Saved")) + " fresh " + F(m.GetFloat("_Fresh"))
            + " color " + C(m.GetColor("_FreshColor")) + " tex " + T(m.GetTexture("_FreshTex")));
        m.shader = names;
        Try(key + "/back", () => "has saved " + m.HasProperty("_Saved") + " fresh " + m.HasProperty("_Fresh")
            + " glossiness " + F(m.GetFloat("_Glossiness")));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("MaterialNamesProbe").AddComponent<MaterialNamesProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static void Run()
    {
        var names = Shader.Find("Probe/Names");
        var other = Shader.Find("Probe/Other");
        var kinds = Shader.Find("Probe/Kinds");
        Try("shaders", () => (names != null) + " " + (other != null) + " " + (kinds != null));

        // Whether names are listed in the order they were set or by property id: _Pre gets its
        // id before _Post, and is set after it.
        int pre = Shader.PropertyToID("_Pre");
        int preColor = Shader.PropertyToID("_PreColor");
        var order = new Material(names) { name = "Order" };
        order.SetFloat("_Post", 1);
        order.SetFloat("_Pre", 2);
        order.SetColor("_PostColor", Color.red);
        order.SetColor("_PreColor", Color.blue);
        // Names that may have ids below the shader's own (used by built-in shaders): listed
        // before or after the declared ones.
        order.SetInteger("_Stencil", 1);
        order.SetFloat("_Cutoff", 0.5f);
        order.SetColor("_TintColor", Color.green);
        Try("order/early_ids", () => "stencil<count " + (Shader.PropertyToID("_Stencil") < Shader.PropertyToID("_Count"))
            + " cutoff<glossiness " + (Shader.PropertyToID("_Cutoff") < Shader.PropertyToID("_Glossiness"))
            + " tintColor<color " + (Shader.PropertyToID("_TintColor") < Shader.PropertyToID("_Color"))
            + " tintColor<mainTexST " + (Shader.PropertyToID("_TintColor") < Shader.PropertyToID("_MainTex_ST")));
        Try("order/ids", () => "pre<post " + (pre < Shader.PropertyToID("_Post")) + " preColor<postColor " + (preColor < Shader.PropertyToID("_PostColor"))
            + " mainTexST<color " + (Shader.PropertyToID("_MainTex_ST") < Shader.PropertyToID("_Color"))
            + " mainTex<color " + (Shader.PropertyToID("_MainTex") < Shader.PropertyToID("_Color"))
            + " color<glossiness " + (Shader.PropertyToID("_Color") < Shader.PropertyToID("_Glossiness"))
            + " glossiness<count " + (Shader.PropertyToID("_Glossiness") < Shader.PropertyToID("_Count"))
            + " saved<fresh " + (Shader.PropertyToID("_Saved") < Shader.PropertyToID("_Fresh"))
            + " freshColor<freshTexST " + (Shader.PropertyToID("_FreshColor") < Shader.PropertyToID("_FreshTex_ST")));
        Names("order", order);

        // Names set from script, in an order neither by name nor by kind: declared and undeclared
        // floats, a float set again, an integer, a legacy int (a float), a colour, a vector,
        // textures, a matrix, a float array, and one name set as a float and then as a colour.
        var m = new Material(names) { name = "Scripted" };
        Names("fresh", m);
        m.SetFloat("_Zf", 1);
        m.SetFloat("_Af", 2);
        m.SetFloat("_Glossiness", 0.5f);
        m.SetInteger("_Zi", 3);
        m.SetInt("_Li", 4);
        m.SetColor("_Zc", new Color(0.25f, 0.5f, 0.75f, 1f));
        m.SetVector("_Av", new Vector4(1, 2, 3, 4));
        m.SetTexture("_Zt", Texture2D.whiteTexture);
        m.SetTexture("_At", null);
        m.SetMatrix("_Zm", Matrix4x4.identity);
        m.SetFloatArray("_Fa", new[] { 1f, 2f });
        m.SetFloat("_Zf", 6);
        m.SetFloat("_Mix", 1);
        m.SetColor("_Mix", new Color(0.5f, 0.5f, 0.5f, 1f));
        m.SetTexture("_MainTex", Texture2D.whiteTexture);
        m.SetTextureScale("_Sc", new Vector2(2, 3));
        m.SetTextureOffset("_Of", new Vector2(0.5f, 0.25f));
        m.SetVectorArray("_Va", new[] { new Vector4(5, 6, 7, 8), Vector4.one });
        m.SetMatrixArray("_Ma", new[] { Matrix4x4.Scale(new Vector3(9, 9, 9)), Matrix4x4.identity });
        Names("set", m);
        foreach (var n in new[] { "_Zf", "_Zi", "_Li", "_Zc", "_Av", "_Zt", "_At", "_Zm", "_Fa", "_Mix", "_Sc", "_Of", "_Va", "_Ma" })
            Try("set/has/" + n, () => Has(m, n));
        Try("set/get/_Mix", () => "float " + F(m.GetFloat("_Mix")) + " color " + C(m.GetColor("_Mix")));
        Try("set/get/_Av", () => V(m.GetVector("_Av")));
        Try("set/get/_Zt", () => T(m.GetTexture("_Zt")));
        Try("set/get/_Zt_scale", () => F(m.GetTextureScale("_Zt").x));
        Try("set/get/_MainTex", () => T(m.GetTexture("_MainTex")));
        Try("set/get/_Sc", () => F(m.GetTextureScale("_Sc").y) + " " + T(m.GetTexture("_Sc")));
        Try("set/get/_Of", () => F(m.GetTextureOffset("_Of").x) + " " + F(m.GetTextureScale("_Of").x) + " " + T(m.GetTexture("_Of")));
        Try("set/get/_Fa", () => F(m.GetFloat("_Fa")) + " array " + (m.GetFloatArray("_Fa")?.Length ?? -1));
        Try("set/get/_Zm", () => F(m.GetMatrix("_Zm").m00));
        Try("set/get/_Va", () => V(m.GetVector("_Va")) + " array " + (m.GetVectorArray("_Va")?.Length ?? -1));
        Try("set/get/_Ma", () => F(m.GetMatrix("_Ma").m00) + " array " + (m.GetMatrixArray("_Ma")?.Length ?? -1));

        // A copy, and a material given the scripted one's properties.
        var copy = new Material(m);
        Names("copy", copy);
        foreach (var n in new[] { "_Zf", "_Zc", "_Zt", "_Zm", "_Fa", "_Of" })
            Try("copy/has/" + n, () => Has(copy, n));
        Try("copy/get", () => "zf " + F(copy.GetFloat("_Zf")) + " zc " + C(copy.GetColor("_Zc")) + " zm " + F(copy.GetMatrix("_Zm").m00)
            + " fa " + (copy.GetFloatArray("_Fa")?.Length ?? -1));
        var instance = Instantiate(m);
        Names("instantiate", instance);
        Try("instantiate/has/_Zf", () => Has(instance, "_Zf"));
        var holder = new GameObject("Holder").AddComponent<MeshRenderer>();
        holder.sharedMaterial = m;
        var own = holder.material;
        Names("renderer_material", own);
        Try("renderer_material/has/_Zf", () => Has(own, "_Zf"));
        var given = new Material(names) { name = "Given" };
        given.CopyPropertiesFromMaterial(m);
        Names("copy_properties", given);
        Try("copy_properties/has/_Zf", () => Has(given, "_Zf"));

        var switched = new Material(m);
        switched.shader = other;
        Names("switched", switched);
        switched.shader = names;
        Names("switched_back", switched);

        // A name the shader declares as a float, given a texture.
        {
            var declared = new Material(names) { name = "Declared" };
            declared.SetTexture("_Glossiness", Texture2D.whiteTexture);
            Try("declared_float_texture", () => "has " + declared.HasTexture("_Glossiness") + " get " + T(declared.GetTexture("_Glossiness"))
                + " names " + string.Join(" ", declared.GetPropertyNames(MaterialPropertyType.Texture))
                + " texture_names " + string.Join(" ", declared.GetTexturePropertyNames()));
        }

        // A SerializedObject over a material given a matrix, an array and a float from script,
        // applied after a change of its queue.
        {
            var applied = new Material(names) { name = "Applied" };
            applied.SetMatrix("_Sm", Matrix4x4.Scale(new Vector3(2, 2, 2)));
            applied.SetFloatArray("_Sa", new[] { 3f, 4f });
            applied.SetFloat("_Sf", 5);
            var so = new SerializedObject(applied);
            so.FindProperty("m_CustomRenderQueue").intValue = 2001;
            Try("serialized/apply", () => so.ApplyModifiedPropertiesWithoutUndo().ToString());
            Try("serialized/after", () => "queue " + applied.renderQueue + " matrix " + applied.HasMatrix("_Sm") + " " + F(applied.GetMatrix("_Sm").m00)
                + " array " + applied.HasFloat("_Sa") + " " + (applied.GetFloatArray("_Sa")?.Length ?? -1)
                + " float " + applied.HasFloat("_Sf") + " names " + string.Join(" ", applied.GetPropertyNames(MaterialPropertyType.Float))
                + " | " + string.Join(" ", applied.GetPropertyNames(MaterialPropertyType.Matrix)));
        }

        // Saves of a material asset (copies of Base.mat, which saves _Saved: 7 beside its shader's
        // properties).
        AssetDatabase.CreateFolder("Assets", "Out");
        var saves = new (string Name, Action<Material> Save)[]
        {
            ("save_if_dirty", mat => { EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); }),
            ("save_assets", mat => { EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets(); }),
            ("not_dirty", mat => AssetDatabase.SaveAssetIfDirty(mat)),
        };
        foreach (var (saveName, save) in saves)
        {
            string path = "Assets/Out/" + saveName + ".mat";
            if (!AssetDatabase.CopyAsset("Assets/Materials/Base.mat", path)) { Row(saveName, "not copied"); continue; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Try(saveName + "/dirty_loaded", () => EditorUtility.IsDirty(mat).ToString());
            SetForSave(mat);
            Try(saveName + "/dirty_set", () => EditorUtility.IsDirty(mat).ToString());
            State(saveName + "/before", mat);
            Try(saveName + "/save", () => { save(mat); return "saved dirty " + EditorUtility.IsDirty(mat); });
            State(saveName + "/after", mat);
            Try(saveName + "/file", () => File.ReadAllText(path));
            Switched(saveName, mat, names, other);
        }

        // A colour set for a name the material's data saves as a float, saved: what a shader
        // declaring it as a colour, and one declaring it as a float, read.
        {
            string path = "Assets/Out/kinds.mat";
            if (AssetDatabase.CopyAsset("Assets/Materials/Base.mat", path))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                mat.SetColor("_Saved", new Color(1f, 0.5f, 0.25f, 1f));
                Try("kinds/save", () => { EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); return "saved"; });
                Try("kinds/file", () => File.ReadAllText(path));
                mat.shader = kinds;
                Try("kinds/color", () => C(mat.GetColor("_Saved")));
                mat.shader = other;
                Try("kinds/float", () => F(mat.GetFloat("_Saved")));
            }
            else Row("kinds", "not copied");
        }

        // A new material given the values, then made an asset.
        {
            string path = "Assets/Out/create.mat";
            var mat = new Material(names) { name = "create" };
            SetForSave(mat);
            Try("create/save", () => { AssetDatabase.CreateAsset(mat, path); return "created"; });
            State("create/after", mat);
            Try("create/file", () => File.ReadAllText(path));
            // A second save of the asset after a new value for a name the shader lacks.
            mat.SetFloat("_Later", 5);
            Try("create/resave", () =>
            {
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
                return "has " + mat.HasProperty("_Later") + " get " + F(mat.GetFloat("_Later"))
                    + " names " + string.Join(" ", mat.GetPropertyNames(MaterialPropertyType.Float));
            });
            Try("create/refile", () => File.ReadAllText(path));
            Switched("create", mat, names, other);
        }
    }
}
