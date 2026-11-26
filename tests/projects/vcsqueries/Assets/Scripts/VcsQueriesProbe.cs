using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $VCSQUERIES_OUT (tests/value_probe.py rows): what UnityEditor.VersionControl
/// answers in a project without a version control provider (the default "Visible Meta Files"
/// mode): the provider's state, assets by path, checkouts, statuses and the tasks they return,
/// asset lists, and which paths are versioned. Values never contain instance ids.
/// </summary>
public class VcsQueriesProbe : MonoBehaviour
{
    const string Variable = "VCSQUERIES_OUT";

    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static string Relative(string text) => text.Replace(Path.GetFullPath("."), "<project>");

    /// <summary>A value of an action, or what it throws, and (as key.logs) the logs it writes.</summary>
    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, Relative(result));
        if (logs.Count > 0) Row(key + ".logs", Relative(string.Join("\n---\n", logs)));
    }

    static string S(string value) => value == null ? "null" : "\"" + value + "\"";

    static string A(Asset asset) => asset == null ? "null"
        : "path " + S(asset.path) + " name " + S(asset.name) + " fullName " + S(asset.fullName) + " assetPath " + S(asset.assetPath)
          + " isFolder " + asset.isFolder + " isMeta " + asset.isMeta + " state " + asset.state + " locked " + asset.locked
          + " readOnly " + asset.readOnly + " isInCurrentProject " + asset.isInCurrentProject;

    static string T(Task task)
    {
        if (task == null) return "null";
        task.Wait();
        var messages = task.messages;
        var assets = task.assetList;
        return "success " + task.success + " resultCode " + task.resultCode + " text " + S(task.text) + " description " + S(task.description)
            + " messages " + (messages == null ? "null" : "[" + string.Join(" | ", messages.Select(m => m.severity + ": " + m.message)) + "]")
            + " assets " + (assets == null ? "null" : "[" + string.Join(" | ", assets.Select(A)) + "]");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("VcsQueriesProbe").AddComponent<VcsQueriesProbe>();
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
        Try("settings/mode", () => S(VersionControlSettings.mode));
        Try("provider/enabled", () => Provider.enabled.ToString());
        Try("provider/isActive", () => Provider.isActive.ToString());
        Try("provider/onlineState", () => Provider.onlineState.ToString());
        Try("provider/offlineReason", () => S(Provider.offlineReason));
        Try("provider/requiresNetwork", () => Provider.requiresNetwork.ToString());
        Try("provider/hasCheckoutSupport", () => Provider.hasCheckoutSupport.ToString());
        Try("provider/hasLockingSupport", () => Provider.hasLockingSupport.ToString());
        Try("provider/isVersioningFolders", () => Provider.isVersioningFolders.ToString());
        Try("provider/activeTask", () => Provider.activeTask == null ? "null" : "a task");
        Try("provider/plugin", () => Provider.GetActivePlugin() == null ? "null" : Provider.GetActivePlugin().name);
        Try("provider/preCheckoutCallback", () => Provider.preCheckoutCallback == null ? "null" : "set");

        foreach (var (name, path) in new[] { ("a", "Assets/Data/A.txt"), ("data", "Assets/Data"), ("missing", "Assets/Data/Missing.txt"),
                     ("meta", "Assets/Data/A.txt.meta"), ("settings", "ProjectSettings/ProjectSettings.asset"), ("empty", ""), ("null", null) })
        {
            Try("get/" + name, () => A(Provider.GetAssetByPath(path)));
            Try("new/" + name, () => A(new Asset(path)));
            Try("versioned/" + name, () => VersionControlUtils.IsPathVersioned(path).ToString());
        }
        foreach (var (name, path) in new[] { ("packages", "Packages/manifest.json"), ("library", "Library/ArtifactDB"), ("user", "UserSettings/x"),
                     ("other", "Other/A.txt"), ("absolute", Path.GetFullPath("Assets/Data/A.txt")),
                     ("backslash", "Assets\\Data\\A.txt"), ("lower", "assets/Data/A.txt"), ("assets", "Assets"), ("assetsx", "AssetsX/A.txt"),
                     ("packagemeta", "Packages/manifest.json.meta"), ("dotdot", "Assets/../Other/A.txt") })
            Try("versioned/" + name, () => VersionControlUtils.IsPathVersioned(path).ToString());

        Try("isopen/asset", () => Provider.IsOpenForEdit(Provider.GetAssetByPath("Assets/Data/A.txt") ?? new Asset("Assets/Data/A.txt")).ToString());
        Try("isopen/new", () => Provider.IsOpenForEdit(new Asset("Assets/Data/A.txt")).ToString());

        Try("checkout/path", () => T(Provider.Checkout("Assets/Data/A.txt", CheckoutMode.Both)));
        Try("checkout/asset", () => T(Provider.Checkout(new Asset("Assets/Data/A.txt"), CheckoutMode.Asset)));
        Try("checkout/object", () => T(Provider.Checkout(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/A.txt"), CheckoutMode.Both)));
        Try("checkout/list", () =>
        {
            var list = new AssetList();
            list.Add(new Asset("Assets/Data/A.txt"));
            list.Add(new Asset("Assets/Data/B.txt"));
            return "count " + list.Count + " " + T(Provider.Checkout(list, CheckoutMode.Both));
        });
        Try("checkout/valid", () => Provider.CheckoutIsValid(new Asset("Assets/Data/A.txt")).ToString());
        Try("status/path", () => T(Provider.Status("Assets/Data/A.txt")));
        Try("status/asset", () => T(Provider.Status(new Asset("Assets/Data/A.txt"))));
        Try("getlatest", () =>
        {
            var list = new AssetList();
            list.Add(new Asset("Assets/Data/A.txt"));
            return T(Provider.GetLatest(list));
        });
        Try("clearcache", () => { Provider.ClearCache(); return "done"; });
        Try("precheckout/set", () =>
        {
            Provider.preCheckoutCallback = (AssetList list, ref string changesetID, ref string changesetDescription) => true;
            var set = Provider.preCheckoutCallback == null ? "null" : "set";
            Provider.preCheckoutCallback = null;
            return set;
        });
        Try("assetlist/empty", () => "count " + new AssetList().Count);
        Try("assetlist/filter", () =>
        {
            var list = new AssetList();
            list.Add(new Asset("Assets/Data/A.txt"));
            list.Add(new Asset("Assets/Data"));
            return "folders " + list.FilterChildren().Count + " " + string.Join(",", list.Select(a => a.isFolder.ToString()));
        });
        Try("files", () => File.ReadAllText("Assets/Data/A.txt"));
    }
}
