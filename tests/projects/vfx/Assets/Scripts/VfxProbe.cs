using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

/// <summary>
/// Records what the Visual Effect module answers about the probe's graphs (see README.md):
/// keyed values, one JSON line each, into VFX_PROBE_OUT/values.jsonl, then exits Play mode.
/// </summary>
public sealed class VfxProbe : MonoBehaviour
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string[] k_Graphs = { "Empty", "Basic", "Exposed", "Exposed2", "Events" };

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Time.captureFramerate = 30;
        new GameObject("VfxProbe").AddComponent<VfxProbe>();
    }

    static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.ToString();
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value ?? "<null>")).Append("\"}\n");
    void Rec(string key, bool value) => Rec(key, value ? "true" : "false");
    void Rec(string key, long value) => Rec(key, value.ToString(CultureInfo.InvariantCulture));
    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");
    static string V(Vector4 v) => F(v.x) + " " + F(v.y) + " " + F(v.z) + " " + F(v.w);

    /// <summary>Runs an action and records what it logged (type and text) and threw.</summary>
    void Logged(string key, Action action)
    {
        m_Logs.Clear();
        Application.LogCallback handler = (text, stack, type) => m_Logs.Add(type + ": " + text);
        Application.logMessageReceived += handler;
        string thrown = "";
        try { action(); }
        // The runtime words an argument exception's message its own way: its parameter is the fact.
        catch (ArgumentException e) { thrown = e.GetType().Name + " " + e.ParamName; }
        catch (Exception e) { thrown = e.GetType().Name + ": " + e.Message; }
        Application.logMessageReceived -= handler;
        Rec(key + "/logs", string.Join(" | ", m_Logs));
        Rec(key + "/thrown", thrown);
    }

    void Start()
    {
        try
        {
            Tables();
            foreach (var name in k_Graphs) Asset(name);
            foreach (var name in new[] { "Plain", "Overridden", "Events", "NoAsset" }) Component("scene/" + name, GameObject.Find(name).GetComponent<VisualEffect>());
            Scripted();
            Attributes();
            Rec("manager/fixedTimeStep", F(VFXManager.fixedTimeStep));
            Rec("manager/maxDeltaTime", F(VFXManager.maxDeltaTime));
        }
        catch (Exception e)
        {
            Rec("failed", e.ToString());
            Debug.LogError("vfx probe failed: " + e);
        }
        m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("VFX_PROBE_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.ExitPlaymode();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    /// <summary>The graph compiler's tables: the type every expression operation produces (for
    /// each type a data word can name), the size of every value type and its texture dimension.</summary>
    void Tables()
    {
        var module = typeof(VisualEffect).Assembly;
        var editor = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.VFX.VFXExpressionHelper")).First(t => t != null);
        var operation = module.GetType("UnityEngine.VFX.VFXExpressionOperation");
        var valueType = module.GetType("UnityEngine.VFX.VFXValueType");
        var typeOf = editor.GetMethod("GetTypeOfOperation", Any);
        var sizeOf = editor.GetMethod("GetSizeOfType", Any);
        var dimension = editor.GetMethod("GetTextureDimension", Any);
        var operations = Enum.GetValues(operation);
        var types = Enum.GetValues(valueType);
        foreach (var op in operations)
        {
            var row = new StringBuilder();
            foreach (var type in types)
            {
                var result = typeOf.Invoke(null, new object[] { op, 0, 0, 0, Convert.ToInt32(type) });
                row.Append(Convert.ToInt32(result)).Append(' ');
            }
            Rec("tables/type/" + op, row.ToString().TrimEnd());
        }
        foreach (var type in types)
        {
            Logged("tables/size/" + type, () => Rec("tables/size/" + type, Convert.ToInt32(sizeOf.Invoke(null, new object[] { type }))));
            Rec("tables/dimension/" + type, dimension.Invoke(null, new object[] { type }).ToString());
        }
        // Outside the enums.
        Rec("tables/type/outside", Convert.ToInt32(typeOf.Invoke(null, new object[] { Enum.ToObject(operation, 155), 0, 0, 0, 1 })));
        Logged("tables/size/outside", () => Rec("tables/size/outside", Convert.ToInt32(sizeOf.Invoke(null, new object[] { Enum.ToObject(valueType, 21) }))));
        Rec("tables/dimension/outside", dimension.Invoke(null, new object[] { Enum.ToObject(valueType, 21) }).ToString());
    }

    void Asset(string name)
    {
        var path = "Assets/Graphs/" + name + ".vfx";
        var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
        var key = "asset/" + name;
        Rec(key + "/loaded", asset != null);
        if (asset == null) return;
        Rec(key + "/name", asset.name);
        Rec(key + "/main", AssetDatabase.LoadMainAssetAtPath(path) == asset);
        var exposed = new List<VFXExposedProperty>();
        asset.GetExposedProperties(exposed);
        Rec(key + "/exposed", string.Join(", ", exposed.Select(p => p.name + ":" + (p.type == null ? "<null>" : p.type.Name))));
        foreach (var p in exposed)
        {
            Rec(key + "/dimension/" + p.name, asset.GetTextureDimension(p.name).ToString());
            Rec(key + "/space/" + p.name, asset.GetExposedSpace(p.name).ToString());
        }
        Rec(key + "/dimension/<missing>", asset.GetTextureDimension("NoSuchProperty").ToString());
        Rec(key + "/space/<missing>", asset.GetExposedSpace("NoSuchProperty").ToString());
        var events = new List<string>();
        asset.GetEvents(events);
        Rec(key + "/events", string.Join(", ", events));
        // The objects the import made, besides the graph's own models.
        var subs = new List<string>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o == null || o is ScriptableObject) continue;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long id);
            subs.Add(o.GetType().Name + " '" + o.name + "' " + id + (o is Material m ? " shader '" + m.shader.name + "'" : ""));
        }
        subs.Sort(StringComparer.Ordinal);
        Rec(key + "/objects", string.Join(" | ", subs));
        Rec(key + "/objects/scripts", AssetDatabase.LoadAllAssetsAtPath(path).Count(o => o is ScriptableObject));
        Rec(key + "/objects/representations", string.Join(" | ", AssetDatabase.LoadAllAssetRepresentationsAtPath(path).Select(o => o.GetType().Name).OrderBy(n => n, StringComparer.Ordinal)));
        foreach (var kind in new[] { "GetSystemNames", "GetParticleSystemNames", "GetSpawnSystemNames", "GetOutputEventNames" })
        {
            var names = new List<string>();
            typeof(VisualEffectAsset).GetMethod(kind, Any).Invoke(asset, new object[] { names });
            Rec(key + "/" + kind, string.Join(", ", names));
        }
    }

    static VisualEffectAsset Graph(string name) => AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Graphs/" + name + ".vfx");

    static string M(Matrix4x4 m)
    {
        var b = new StringBuilder();
        for (int i = 0; i < 16; i++) b.Append(F(m[i])).Append(i < 15 ? " " : "");
        return b.ToString();
    }

    static string Keys(AnimationCurve c) => c == null ? "<null>" : string.Join(";", c.keys.Select(k => F(k.time) + ":" + F(k.value)));

    static string Keys(Gradient g) => g == null ? "<null>" :
        string.Join(";", g.colorKeys.Select(k => F(k.time) + ":" + V(k.color))) + " / " + string.Join(";", g.alphaKeys.Select(k => F(k.time) + ":" + F(k.alpha)));

    static string Name(UnityEngine.Object o) => o == null ? "<null>" : o.GetType().Name + " " + o.name;

    /// <summary>Which of the typed Has* a name answers true to, as a string of 0 and 1 in the
    /// order: Bool Int UInt Float Vector2 Vector3 Vector4 Matrix4x4 Texture AnimationCurve
    /// Gradient Mesh SkinnedMeshRenderer GraphicsBuffer.</summary>
    static string Has(VisualEffect e, string n)
    {
        var bits = new[]
        {
            e.HasBool(n), e.HasInt(n), e.HasUInt(n), e.HasFloat(n), e.HasVector2(n), e.HasVector3(n), e.HasVector4(n), e.HasMatrix4x4(n),
            e.HasTexture(n), e.HasAnimationCurve(n), e.HasGradient(n), e.HasMesh(n), e.HasSkinnedMeshRenderer(n), e.HasGraphicsBuffer(n),
        };
        return string.Concat(bits.Select(b => b ? '1' : '0'));
    }

    /// <summary>The value of a property read with the getter of its listed type.</summary>
    static string Value(VisualEffect e, string n, Type type)
    {
        if (type == typeof(bool)) return e.GetBool(n) ? "true" : "false";
        if (type == typeof(int)) return e.GetInt(n).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(uint)) return e.GetUInt(n).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(float)) return F(e.GetFloat(n));
        if (type == typeof(Vector2)) return V(e.GetVector2(n));
        if (type == typeof(Vector3)) return V(e.GetVector3(n));
        if (type == typeof(Vector4)) return V(e.GetVector4(n));
        if (type == typeof(Matrix4x4)) return M(e.GetMatrix4x4(n));
        if (type == typeof(Texture)) return Name(e.GetTexture(n));
        if (type == typeof(AnimationCurve)) return Keys(e.GetAnimationCurve(n));
        if (type == typeof(Gradient)) return Keys(e.GetGradient(n));
        if (type == typeof(Mesh)) return Name(e.GetMesh(n));
        if (type == typeof(SkinnedMeshRenderer)) return Name(e.GetSkinnedMeshRenderer(n));
        return "<" + (type == null ? "null" : type.Name) + ">";
    }

    void Component(string key, VisualEffect e)
    {
        Rec(key + "/asset", e.visualEffectAsset == null ? "<null>" : e.visualEffectAsset.name);
        Rec(key + "/components", string.Join(", ", e.GetComponents<Component>().Select(c => c.GetType().Name)));
        Rec(key + "/pause", e.pause);
        Rec(key + "/playRate", F(e.playRate));
        Rec(key + "/startSeed", e.startSeed);
        Rec(key + "/resetSeedOnPlay", e.resetSeedOnPlay);
        Rec(key + "/initialEventName", e.initialEventName);
        Rec(key + "/initialEventID", e.initialEventID == Shader.PropertyToID(e.initialEventName ?? ""));
        var names = new List<string>();
        e.GetSystemNames(names);
        Rec(key + "/GetSystemNames", string.Join(", ", names));
        e.GetParticleSystemNames(names);
        Rec(key + "/GetParticleSystemNames", string.Join(", ", names));
        e.GetSpawnSystemNames(names);
        Rec(key + "/GetSpawnSystemNames", string.Join(", ", names));
        e.GetOutputEventNames(names);
        Rec(key + "/GetOutputEventNames", string.Join(", ", names));
        Rec(key + "/HasSystem/System", e.HasSystem("System"));
        Rec(key + "/HasSystem/System (1)", e.HasSystem("System (1)"));
        Rec(key + "/HasSystem/Nope", e.HasSystem("Nope"));
        var exposed = new List<VFXExposedProperty>();
        if (e.visualEffectAsset != null) e.visualEffectAsset.GetExposedProperties(exposed);
        foreach (var p in exposed)
        {
            Rec(key + "/has/" + p.name, Has(e, p.name));
            Logged(key + "/get/" + p.name, () => Rec(key + "/get/" + p.name, Value(e, p.name, p.type)));
            Rec(key + "/dimension/" + p.name, e.GetTextureDimension(p.name).ToString());
        }
        Rec(key + "/has/<missing>", Has(e, "NoSuchProperty"));
        Rec(key + "/dimension/<missing>", e.GetTextureDimension("NoSuchProperty").ToString());
        Logged(key + "/get/<missing>/float", () => Rec(key + "/get/<missing>/float", F(e.GetFloat("NoSuchProperty"))));
        Logged(key + "/get/<missing>/texture", () => Rec(key + "/get/<missing>/texture", Name(e.GetTexture("NoSuchProperty"))));
        Logged(key + "/get/<missing>/curve", () => Rec(key + "/get/<missing>/curve", Keys(e.GetAnimationCurve("NoSuchProperty"))));
        Logged(key + "/get/<missing>/gradient", () => Rec(key + "/get/<missing>/gradient", Keys(e.GetGradient("NoSuchProperty"))));
        var attribute = e.CreateVFXEventAttribute();
        Rec(key + "/attribute", attribute == null ? "<null>" : "made");
    }

    /// <summary>Effects made and changed from script: every setter with the right and a wrong
    /// type, overrides reset, an asset swap, a clone.</summary>
    void Scripted()
    {
        var go = new GameObject("Scripted");
        var e = go.AddComponent<VisualEffect>();
        Component("scripted/new", e);
        e.visualEffectAsset = Graph("Exposed");
        Component("scripted/assigned", e);

        var image = new Texture2D(2, 2) { name = "ProbeImage" };
        var volume = new Texture3D(2, 2, 2, TextureFormat.RGBA32, false) { name = "ProbeVolume" };
        var mesh = new Mesh { name = "ProbeMesh" };
        const string k = "scripted/set";
        Logged(k + "/Rate", () => e.SetFloat("Rate", 20.5f));
        Logged(k + "/Count", () => e.SetInt("Count", 41));
        Logged(k + "/uCount", () => e.SetUInt("uCount", 4000000000u));
        Logged(k + "/Flag", () => e.SetBool("Flag", false));
        Logged(k + "/Pair", () => e.SetVector2("Pair", new Vector2(0.5f, 0.25f)));
        Logged(k + "/Triple", () => e.SetVector3("Triple", new Vector3(9f, 8f, 7f)));
        Logged(k + "/Quad", () => e.SetVector4("Quad", new Vector4(6f, 5f, 4f, 3f)));
        Logged(k + "/Tint", () => e.SetVector4("Tint", new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));
        Logged(k + "/Where_position", () => e.SetVector3("Where_position", new Vector3(-1f, -2f, -3f)));
        Logged(k + "/Curve", () => e.SetAnimationCurve("Curve", AnimationCurve.Linear(0f, 3f, 2f, 4f)));
        var ramp = new Gradient();
        ramp.SetKeys(new[] { new GradientColorKey(Color.green, 0.25f), new GradientColorKey(Color.yellow, 0.5f) },
            new[] { new GradientAlphaKey(0.5f, 0.125f), new GradientAlphaKey(0.25f, 0.75f) });
        Logged(k + "/Ramp", () => e.SetGradient("Ramp", ramp));
        Logged(k + "/Image", () => e.SetTexture("Image", image));
        // A texture of another dimension than the property's.
        Logged(k + "/Volume/2D", () => e.SetTexture("Volume", image));
        Logged(k + "/Sky/3D", () => e.SetTexture("Sky", volume));
        Logged(k + "/Shape", () => e.SetMesh("Shape", mesh));
        // Wrong types and unknown names.
        Logged(k + "/wrong/Count/float", () => e.SetFloat("Count", 1f));
        Logged(k + "/wrong/Rate/int", () => e.SetInt("Rate", 1));
        Logged(k + "/wrong/Tint/vector3", () => e.SetVector3("Tint", Vector3.one));
        Logged(k + "/wrong/Image/mesh", () => e.SetMesh("Image", mesh));
        Logged(k + "/wrong/Shape/texture", () => e.SetTexture("Shape", image));
        Logged(k + "/wrong/missing", () => e.SetFloat("NoSuchProperty", 1f));
        Logged(k + "/wrong/get/Count/float", () => Rec(k + "/wrong/get/Count/float", F(e.GetFloat("Count"))));
        Logged(k + "/wrong/get/Rate/vector2", () => Rec(k + "/wrong/get/Rate/vector2", V(e.GetVector2("Rate"))));
        Logged(k + "/wrong/get/Image/mesh", () => Rec(k + "/wrong/get/Image/mesh", Name(e.GetMesh("Image"))));
        Component("scripted/set/after", e);

        // The curve and gradient set are copies: later changes to ours do not reach the property.
        ramp.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        Rec("scripted/copy/Ramp", Keys(e.GetGradient("Ramp")));
        var read = e.GetAnimationCurve("Curve");
        read.AddKey(5f, 5f);
        Rec("scripted/copy/Curve", Keys(e.GetAnimationCurve("Curve")));

        // A clone carries the overrides.
        var clone = Instantiate(go).GetComponent<VisualEffect>();
        Rec("scripted/clone/Rate", F(clone.GetFloat("Rate")));
        Rec("scripted/clone/Count", clone.GetInt("Count"));
        Rec("scripted/clone/Image", Name(clone.GetTexture("Image")));
        e.SetFloat("Rate", 30f);
        Rec("scripted/clone/Rate/after source change", F(clone.GetFloat("Rate")));

        // Overrides reset: back to the asset's values; nothing for an unknown or untouched name.
        Logged("scripted/reset/Rate", () => e.ResetOverride("Rate"));
        Logged("scripted/reset/Curve", () => e.ResetOverride("Curve"));
        Logged("scripted/reset/Image", () => e.ResetOverride("Image"));
        Logged("scripted/reset/untouched", () => e.ResetOverride("Zeta"));
        Logged("scripted/reset/missing", () => e.ResetOverride("NoSuchProperty"));
        Rec("scripted/reset/Rate/value", F(e.GetFloat("Rate")));
        Rec("scripted/reset/Curve/value", Keys(e.GetAnimationCurve("Curve")));
        Rec("scripted/reset/Image/value", Name(e.GetTexture("Image")));
        Rec("scripted/reset/Count/kept", e.GetInt("Count"));

        // Another asset with some of the same names: do overrides follow?
        e.SetFloat("Rate", 50f);
        e.visualEffectAsset = Graph("Exposed2");
        Rec("scripted/swap/Rate", F(e.GetFloat("Rate")));
        Rec("scripted/swap/Count/has", Has(e, "Count"));
        Rec("scripted/swap/Count", F(e.GetFloat("Count")));
        Rec("scripted/swap/Extra", F(e.GetFloat("Extra")));
        e.SetFloat("Rate", 60f);
        e.visualEffectAsset = Graph("Exposed");
        Rec("scripted/swap/back/Rate", F(e.GetFloat("Rate")));
        Rec("scripted/swap/back/Count", e.GetInt("Count"));
        // The same asset assigned again.
        e.SetFloat("Rate", 70f);
        e.visualEffectAsset = Graph("Exposed");
        Rec("scripted/swap/same/Rate", F(e.GetFloat("Rate")));
        e.visualEffectAsset = null;
        Rec("scripted/swap/null/has", Has(e, "Rate"));
        Logged("scripted/swap/null/get", () => Rec("scripted/swap/null/get", F(e.GetFloat("Rate"))));

        // State set from script.
        e.pause = true;
        e.playRate = 2.5f;
        e.startSeed = 99;
        e.resetSeedOnPlay = false;
        e.initialEventName = "Fire";
        Component("scripted/state", e);
        e.initialEventID = Shader.PropertyToID("Cease");
        Rec("scripted/state/initialEventName/by id", e.initialEventName);
        e.initialEventName = "";
        Rec("scripted/state/initialEventName/empty", e.initialEventName);
        e.visualEffectAsset = Graph("Events");
        Rec("scripted/state/initialEventName/with asset", e.initialEventName);
    }

    static readonly string[] k_Attributes =
    {
        "position", "velocity", "color", "alpha", "size", "lifetime", "age", "spawnCount", "direction", "targetPosition", "seed",
        "particleId", "alive", "angle", "scale", "mass", "texIndex", "NoSuchAttribute",
    };

    static string Has(VFXEventAttribute a, string n)
    {
        var bits = new[] { a.HasBool(n), a.HasInt(n), a.HasUint(n), a.HasFloat(n), a.HasVector2(n), a.HasVector3(n), a.HasVector4(n), a.HasMatrix4x4(n) };
        return string.Concat(bits.Select(b => b ? '1' : '0'));
    }

    /// <summary>Event attributes: the layout each asset's attributes have, their starting values,
    /// writes and reads by the right and a wrong type, copies.</summary>
    void Attributes()
    {
        foreach (var graph in k_Graphs)
        {
            var e = new GameObject("Attributes " + graph).AddComponent<VisualEffect>();
            e.visualEffectAsset = Graph(graph);
            var a = e.CreateVFXEventAttribute();
            var key = "attributes/" + graph;
            Rec(key, a == null ? "<null>" : "made");
            if (a == null) continue;
            foreach (var n in k_Attributes)
            {
                Rec(key + "/has/" + n, Has(a, n));
                Rec(key + "/get/" + n, F(a.GetFloat(n)) + " | " + V(a.GetVector3(n)) + " | " + a.GetUint(n) + " | " + a.GetInt(n) + " | " + (a.GetBool(n) ? "true" : "false"));
            }
        }
        {
            var e = new GameObject("Attributes set").AddComponent<VisualEffect>();
            e.visualEffectAsset = Graph("Events");
            var a = e.CreateVFXEventAttribute();
            const string key = "attributes/set";
            Logged(key + "/spawnCount", () => a.SetFloat("spawnCount", 7.5f));
            Logged(key + "/color", () => a.SetVector3("color", new Vector3(0.25f, 0.5f, 0.75f)));
            Logged(key + "/wrong/spawnCount/int", () => a.SetInt("spawnCount", 3));
            Logged(key + "/wrong/color/float", () => a.SetFloat("color", 1f));
            Logged(key + "/wrong/missing", () => a.SetFloat("NoSuchAttribute", 1f));
            Rec(key + "/get/spawnCount", F(a.GetFloat("spawnCount")));
            Rec(key + "/get/color", V(a.GetVector3("color")));
            Rec(key + "/get/wrong/spawnCount/int", a.GetInt("spawnCount"));
            Rec(key + "/get/wrong/color/vector4", V(a.GetVector4("color")));
            var copy = new VFXEventAttribute(a);
            a.SetFloat("spawnCount", 1f);
            Rec(key + "/copy/spawnCount", F(copy.GetFloat("spawnCount")));
            Rec(key + "/copy/color", V(copy.GetVector3("color")));
            var other = e.CreateVFXEventAttribute();
            other.CopyValuesFrom(copy);
            Rec(key + "/copied/spawnCount", F(other.GetFloat("spawnCount")));
            Rec(key + "/distinct", !ReferenceEquals(other, e.CreateVFXEventAttribute()));
            Logged(key + "/copy/null", () => new VFXEventAttribute(null));
            Logged(key + "/copied/null", () => other.CopyValuesFrom(null));
            // Values copied between attributes of assets with other layouts.
            var basic = new GameObject("Attributes basic").AddComponent<VisualEffect>();
            basic.visualEffectAsset = Graph("Basic");
            var b = basic.CreateVFXEventAttribute();
            Logged(key + "/copied/other asset", () => b.CopyValuesFrom(copy));
            Rec(key + "/copied/other asset/spawnCount", F(b.GetFloat("spawnCount")));
            Rec(key + "/copied/other asset/color", Has(b, "color") + " " + V(b.GetVector3("color")));
            a.Dispose();
            Logged(key + "/disposed/get", () => Rec(key + "/disposed/get", F(a.GetFloat("spawnCount"))));
        }
    }
}
