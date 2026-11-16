using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Warms up a ShaderVariantCollection asset that lists built-in shaders, among them two the Editor
/// ships without published source (Hidden/Internal-UIRAtlasBlitCopy, Hidden/UIElements/EditorUIE),
/// and records its counts and warm-up state after each progressive step and a full WarmUp, which
/// variants it contains and whether Shader.Find finds the shaders. Writes values.jsonl into
/// WARMUP_BUILTIN_OUT and exits.
/// </summary>
public sealed class WarmupBuiltinProbe : MonoBehaviour
{
    static readonly string[] Shaders = { "Hidden/Internal-UIRAtlasBlitCopy", "UI/Default", "Hidden/UIElements/EditorUIE", "Sprites/Default" };

    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("WarmupBuiltinProbe").AddComponent<WarmupBuiltinProbe>();

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }

    static string State(ShaderVariantCollection c) =>
        $"shaders {c.shaderCount} variants {c.variantCount} warm {c.isWarmedUp} warmed {c.warmedUpVariantCount}";

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        try
        {
            foreach (var name in Shaders)
                Rec("find/" + name, Try(() => Shader.Find(name) != null ? "found" : "null"));
            var collection = Resources.Load<ShaderVariantCollection>("Prewarm");
            Rec("loaded", collection != null ? "true" : "false");
            Rec("state/cold", State(collection));
            foreach (var name in Shaders)
                Rec("contains/" + name, Try(() => collection.Contains(new ShaderVariantCollection.ShaderVariant { shader = Shader.Find(name), passType = PassType.Normal, keywords = new string[0] }) ? "true" : "false"));
            for (int step = 1; step <= 3; step++)
            {
                Rec("progressive/" + step, Try(() => collection.WarmUpProgressively(1).ToString()));
                Rec("state/progressive/" + step, State(collection));
            }
            Rec("full", Try(() => { collection.WarmUp(); return "done"; }));
            Rec("state/full", State(collection));
            Rec("progressive/after", Try(() => collection.WarmUpProgressively(1).ToString()));
            Rec("global", Try(() => { Shader.WarmupAllShaders(); return "done"; }));
            Rec("state/global", State(collection));
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        }
        catch (Exception e) { Debug.LogError("warm-up probe failed: " + e); code = 1; }
        var dir = Environment.GetEnvironmentVariable("WARMUP_BUILTIN_OUT");
        if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
        else { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString()); }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
