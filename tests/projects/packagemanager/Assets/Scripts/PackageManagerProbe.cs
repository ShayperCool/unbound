using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>
/// Writes values.jsonl in $PACKAGEMANAGER_OUT (tests/value_probe.py rows): PackageInfo.versions
/// and errors, Request.Error, PackageCollection.error, Client.Search/Add/Remove/AddAndRemove/Embed
/// argument validation, and offline failure requests on Unity 6000.0.71f1.
/// </summary>
public class PackageManagerProbe : MonoBehaviour
{
    const string Variable = "PACKAGEMANAGER_OUT";

    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static string S(string value) => value == null ? "null" : "\"" + value + "\"";

    static string Arr(string[] values) => values == null ? "null" : "[" + string.Join(",", values.Select(S)) + "]";

#pragma warning disable CS0618
    static string VersionsText(VersionsInfo v) => v == null
        ? "null"
        : "all=" + Arr(v.all)
          + " compatible=" + Arr(v.compatible)
          + " recommended=" + S(v.recommended)
          + " verified=" + S(v.verified)
          + " deprecated=" + Arr(v.deprecated)
          + " latest=" + S(v.latest)
          + " latestCompatible=" + S(v.latestCompatible);
#pragma warning restore CS0618

    static string ErrorsText(Error[] errors) => errors == null
        ? "null"
        : "count=" + errors.Length;

    static string BaseArgMessage(ArgumentException e)
    {
        var msg = e.Message ?? "";
        int nl = msg.IndexOf('\n');
        if (nl >= 0) msg = msg.Substring(0, nl);
        int paren = msg.IndexOf(" (Parameter '", StringComparison.Ordinal);
        if (paren >= 0) msg = msg.Substring(0, paren);
        return msg.TrimEnd('\r');
    }

    static void Try(string key, Func<string> action)
    {
        string result;
        try { result = action(); }
        catch (ArgumentException e) { result = "throws " + e.GetType().FullName + " param=" + S(e.ParamName) + " msg=" + S(BaseArgMessage(e)); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + " msg=" + S(e.Message); }
        Row(key, result);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("PackageManagerProbe").AddComponent<PackageManagerProbe>();
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        yield return Run();
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static IEnumerator RecordSearch(string prefix, string idOrName)
    {
        var req = Client.Search(idOrName, offlineMode: true);
        while (!req.IsCompleted) yield return null;
        Row(prefix + ".packageIdOrName", S(req.PackageIdOrName));
        Row(prefix + ".status", req.Status.ToString());
        Row(prefix + ".hasError", (req.Error != null).ToString());
        Row(prefix + ".errorCode", req.Error == null ? "null" : req.Error.errorCode.ToString());
        Row(prefix + ".hasMessage", (req.Error != null && !string.IsNullOrEmpty(req.Error.message)).ToString());
        Row(prefix + ".resultCount", req.Result == null ? "null" : req.Result.Length.ToString());
    }

    static IEnumerator Run()
    {

        const string payload = "\"m_Error\":{\"m_ErrorCode\":0,\"m_Message\":\"probe error\"}";
        string ErrorText(UnityEditor.PackageManager.Error error) =>
            error == null ? "null" : error.errorCode + ":" + error.message;
        Row("json/request.empty.error", ErrorText(JsonUtility.FromJson<RemoveRequest>("{}").Error));
        Row("json/request.unfetched.error", ErrorText(JsonUtility.FromJson<RemoveRequest>("{" + payload + "}").Error));
        Row("json/request.fetched.error", ErrorText(JsonUtility.FromJson<RemoveRequest>("{\"m_ErrorFetched\":true," + payload + "}").Error));
        Row("json/collection.empty.error", ErrorText(JsonUtility.FromJson<PackageCollection>("{}").error));
        Row("json/collection.unset.error", ErrorText(JsonUtility.FromJson<PackageCollection>("{" + payload + "}").error));
        Row("json/collection.set.error", ErrorText(JsonUtility.FromJson<PackageCollection>("{\"m_HasError\":true," + payload + "}").error));

        // 1. PackageInfo.versions & errors on embedded, local and built-in packages.
        foreach (var entry in new[] { ("embedded", "com.probe.pm"), ("local", "com.probe.local"), ("builtin", "com.unity.modules.audio") })
        {
            var info = PackageInfo.FindForPackageName(entry.Item2);
            Row("info/" + entry.Item1 + ".found", (info != null).ToString());
            if (info != null)
            {
                Row("info/" + entry.Item1 + ".source", info.source.ToString());
                Row("info/" + entry.Item1 + ".versions", VersionsText(info.versions));
                Row("info/" + entry.Item1 + ".errors", ErrorsText(info.errors));
            }
        }

        // 2. Client.List(offlineMode: true): Request.Error and PackageCollection.error + listed PackageInfo.versions/errors.
        var listReq = Client.List(offlineMode: true, includeIndirectDependencies: true);
        while (!listReq.IsCompleted) yield return null;
        Row("list/status", listReq.Status.ToString());
        Row("list/error", listReq.Error == null ? "null" : listReq.Error.errorCode + ":" + !string.IsNullOrEmpty(listReq.Error.message));
        Row("list/collection.error", listReq.Result?.error == null ? "null" : listReq.Result.error.errorCode.ToString());
        var listedEmbedded = listReq.Result?.FirstOrDefault(p => p.name == "com.probe.pm");
        Row("list/embedded.versions", listedEmbedded == null ? "missing" : VersionsText(listedEmbedded.versions));
        Row("list/embedded.errors", listedEmbedded == null ? "missing" : ErrorsText(listedEmbedded.errors));

        // 3. Client.Search offline: validation, builtin (exact/versioned/mismatched), embedded, local, missing.
        Try("validate/search.null", () => { Client.Search(null, true); return "ok"; });
        Try("validate/search.empty", () => { Client.Search("", true); return "ok"; });
        Try("validate/search.ws", () => { Client.Search("   ", true); return "ok"; });

        yield return RecordSearch("search/builtin", "com.unity.modules.audio");
        yield return RecordSearch("search/builtinVersionMatch", "com.unity.modules.audio@1.0.0");
        yield return RecordSearch("search/builtinVersionMiss", "com.unity.modules.audio@9.9.9");
        yield return RecordSearch("search/embedded", "com.probe.pm");
        yield return RecordSearch("search/local", "com.probe.local");
        yield return RecordSearch("search/missing", "com.probe.does.not.exist");

        // 4. Synchronous argument validation on Add, Remove, AddAndRemove, Embed.
        Try("validate/add.null", () => { Client.Add(null); return "ok"; });
        Try("validate/add.empty", () => { Client.Add(""); return "ok"; });
        Try("validate/add.ws", () => { Client.Add("   "); return "ok"; });

        Try("validate/remove.null", () => { Client.Remove(null); return "ok"; });
        Try("validate/remove.empty", () => { Client.Remove(""); return "ok"; });
        Try("validate/remove.ws", () => { Client.Remove("   "); return "ok"; });

        Try("validate/addAndRemove.bothNull", () => { Client.AddAndRemove(null, null); return "ok"; });
        Try("validate/addAndRemove.bothEmpty", () => { Client.AddAndRemove(Array.Empty<string>(), Array.Empty<string>()); return "ok"; });
        Try("validate/addAndRemove.addWs", () => { Client.AddAndRemove(new[] { " " }, null); return "ok"; });
        Try("validate/addAndRemove.removeEmpty", () => { Client.AddAndRemove(null, new[] { "" }); return "ok"; });

        Try("validate/embed.null", () => { Client.Embed(null); return "ok"; });
        Try("validate/embed.empty", () => { Client.Embed(""); return "ok"; });
        Try("validate/embed.ws", () => { Client.Embed("   "); return "ok"; });
        Try("validate/embed.notRegistered", () => { Client.Embed("com.probe.not.registered"); return "ok"; });

        // 5. RemoveRequest for an uninstalled package: PackageIdOrName + failure status + Error object.
        var remReq = Client.Remove("com.probe.not.installed");
        Row("remove/notInstalled.packageIdOrName", S(remReq.PackageIdOrName));
        while (!remReq.IsCompleted) yield return null;
        Row("remove/notInstalled.status", remReq.Status.ToString());
        Row("remove/notInstalled.hasError", (remReq.Error != null).ToString());
        Row("remove/notInstalled.errorCode", remReq.Error == null ? "null" : remReq.Error.errorCode.ToString());
        Row("remove/notInstalled.hasMessage", (remReq.Error != null && !string.IsNullOrEmpty(remReq.Error.message)).ToString());

        // 6. EmbedRequest for an already-embedded package: fails without mutating disk.
        var embedEmbedded = Client.Embed("com.probe.pm");
        while (!embedEmbedded.IsCompleted) yield return null;
        Row("embed/alreadyEmbedded.status", embedEmbedded.Status.ToString());
        Row("embed/alreadyEmbedded.hasError", (embedEmbedded.Error != null).ToString());
        Row("embed/alreadyEmbedded.errorCode", embedEmbedded.Error == null ? "null" : embedEmbedded.Error.errorCode.ToString());
        Row("embed/alreadyEmbedded.hasMessage", (embedEmbedded.Error != null && !string.IsNullOrEmpty(embedEmbedded.Error.message)).ToString());
        Row("embed/alreadyEmbedded.resultNull", (embedEmbedded.Result == null).ToString());
    }
}
