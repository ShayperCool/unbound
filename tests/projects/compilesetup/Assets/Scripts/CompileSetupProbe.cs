using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
#endif

/// <summary>
/// Writes values.jsonl in $COMPILE_SETUP_OUT (tests/value_probe.py rows): which of the precompiled
/// plugins that share an assembly name the scripts compile and run against, which test, Editor-only
/// and package assemblies the Editor builds and references, what a package naming a moved type by
/// its old namespace compiles to and CodeDom from an Editor-only assembly.
/// </summary>
public class CompileSetupProbe : MonoBehaviour
{
    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<object> action)
    {
        string result;
        try
        {
            var value = action();
            result = value == null ? "null" : value.ToString();
        }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COMPILE_SETUP_OUT"))) return;
        new GameObject("CompileSetupProbe").AddComponent<CompileSetupProbe>();
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("COMPILE_SETUP_OUT");
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"));
        yield return null;
        Plugins();
        Assemblies();
        Calls();
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Close();
#if UNITY_EDITOR
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Which copy binds: by version where they differ; where they are the same version,
    /// one of them, the same at compile and at run time (which one the Editor picks then is not
    /// specified: it changes with the project's other plugins).</summary>
    static void Plugins()
    {
        Try("plugins.dup.built", () => ProbeDup.Marker.Built);
        Try("plugins.dup.runtime", () => ProbeDup.Marker.Runtime());
        Try("plugins.dup.version", () => typeof(ProbeDup.Marker).Assembly.GetName().Version);
        Try("plugins.same.consistent", () => ProbeSame.Marker.Built == ProbeSame.Marker.Runtime());
        Try("plugins.order.consistent", () => ProbeOrder.Marker.Built == ProbeOrder.Marker.Runtime());
        // One assembly under two file names, each named by one assembly: what each compiled
        // against and which copy runs.
        Try("plugins.alias.explicit", () => Probe.Explicit.ExplicitReferences.Alias());
        Try("plugins.alias.explicit_new", () => Probe.ExplicitNew.ExplicitReferences.Alias());
    }

    /// <summary>The probe's assemblies as the Editor builds them for Play mode.</summary>
    static void Assemblies()
    {
#if UNITY_EDITOR
        bool Ours(string name) => name.StartsWith("Probe.") || name.StartsWith("Unity.2D.PixelPerfect") || name.StartsWith("Assembly-CSharp");
        bool OurPlugin(string path) => Path.GetFileName(path).StartsWith("Probe");
        // A package's plugins are listed by absolute path, the project's own by relative path:
        // both as project paths here. Where the copies have the same version only the file name
        // (which one is not specified).
        var project = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/";
        string Plugin(string path)
        {
            var name = Path.GetFileName(path);
            return name == "ProbeDup.dll" || name.StartsWith("ProbeAlias") ? path.Replace('\\', '/').Replace(project, "") : name;
        }
        var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor).Where(a => Ours(a.name)).OrderBy(a => a.name, StringComparer.Ordinal).ToArray();
        Row("assemblies", string.Join(",", assemblies.Select(a => a.name)));
        foreach (var a in assemblies)
        {
            Row("assembly." + a.name + ".flags", a.flags.ToString());
            Row("assembly." + a.name + ".references", string.Join(",", a.assemblyReferences.Select(r => r.name).Where(Ours).OrderBy(n => n, StringComparer.Ordinal)));
            Row("assembly." + a.name + ".plugins", string.Join(",", a.compiledAssemblyReferences.Where(OurPlugin).Select(Plugin).OrderBy(n => n, StringComparer.Ordinal)));
        }
#endif
    }

    static void Calls()
    {
#if UNITY_EDITOR
        Try("call.editor_only", () => Probe.Setup.Editor.EditorOnlyApi.Value());
        Try("codedom.extension", () => Probe.Setup.Editor.EditorOnlyApi.CodeDomExtension());
        Try("codedom.type", () => Probe.Setup.Editor.EditorOnlyApi.CodeDomType());
#endif
        Try("call.tarball", () => Probe.Consumer.Consumer.Value());
        // A registry package that names a type URP moved by its old namespace (UnityEngine.Experimental.Rendering.Universal).
        Try("call.converter", () => Type.GetType("UnityEditor.Rendering.Universal.U2DToURPPixelPerfectConverter, Unity.2D.PixelPerfect.Editor")?.FullName ?? "missing");
    }
}
