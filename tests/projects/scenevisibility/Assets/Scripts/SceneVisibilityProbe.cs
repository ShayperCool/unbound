using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode, what SceneVisibilityManager answers and raises: on a small hierarchy
/// (A with A1 and A2, A2 with A2a; B) it runs one operation per Editor update (hiding, showing,
/// toggling, scene-wide calls, isolation, picking, null arguments, objects added under a hidden
/// parent and destroyed while hidden) and records, for each operation, the visibilityChanged and
/// pickingChanged events raised during the call and before the next update, any exception, and
/// then every object's and the scene's answers.
/// </summary>
static class SceneVisibilityProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static SceneVisibilityManager Manager => SceneVisibilityManager.instance;
    static readonly Dictionary<string, GameObject> s_Objects = new Dictionary<string, GameObject>();
    static int s_Visibility, s_Picking, s_Step = -1;
    static string s_Pending;

    static GameObject O(string name) => s_Objects[name];

    static readonly (string Name, Action Run)[] s_Steps =
    {
        ("initial", () => { }),
        ("hideA", () => Manager.Hide(O("A"), false)),
        ("hideAWithDescendants", () => Manager.Hide(O("A"), true)),
        ("showA1", () => Manager.Show(O("A1"), false)),
        ("toggleA2WithDescendants", () => Manager.ToggleVisibility(O("A2"), true)),
        ("toggleB", () => Manager.ToggleVisibility(O("B"), false)),
        ("hideBAgain", () => Manager.Hide(O("B"), false)),
        ("showScene", () => Manager.Show(SceneManager.GetActiveScene())),
        ("hideScene", () => Manager.Hide(SceneManager.GetActiveScene())),
        ("showAll", () => Manager.ShowAll()),
        ("hideArray", () => Manager.Hide(new[] { O("A1"), O("B") }, false)),
        ("showArrayWithDescendants", () => Manager.Show(new[] { O("A1"), O("A") }, true)),
        ("hideAll", () => Manager.HideAll()),
        ("showAllAgain", () => Manager.ShowAll()),
        ("isolateA1", () => Manager.Isolate(O("A1"), false)),
        ("showBIsolated", () => Manager.Show(O("B"), false)),
        ("exitIsolation", () => Manager.ExitIsolation()),
        ("showAllAfterIsolation", () => Manager.ShowAll()),
        ("disablePickingAWithDescendants", () => Manager.DisablePicking(O("A"), true)),
        ("enablePickingA2", () => Manager.EnablePicking(O("A2"), false)),
        ("togglePickingB", () => Manager.TogglePicking(O("B"), false)),
        ("disablePickingScene", () => Manager.DisablePicking(SceneManager.GetActiveScene())),
        ("enablePickingScene", () => Manager.EnablePicking(SceneManager.GetActiveScene())),
        ("disableAllPicking", () => Manager.DisableAllPicking()),
        ("enableAllPicking", () => Manager.EnableAllPicking()),
        ("disablePickingArray", () => Manager.DisablePicking(new[] { O("A1"), O("B") }, false)),
        ("enablePickingArrayWithDescendants", () => Manager.EnablePicking(new[] { O("A1"), O("B") }, true)),
        ("hideNull", () => Manager.Hide((GameObject)null, false)),
        ("hideNullArray", () => Manager.Hide((GameObject[])null, false)),
        ("hideAThenAddA3", () =>
        {
            Manager.Hide(O("A"), true);
            var a3 = new GameObject("A3");
            a3.transform.SetParent(O("A").transform, false);
            s_Objects["A3"] = a3;
        }),
        ("destroyA2a", () => Object.DestroyImmediate(O("A2a"))),
        ("showAllAtEnd", () => Manager.ShowAll()),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCENE_VISIBILITY_OUT"))) return;
        GameObject Make(string name, string parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(O(parent).transform, false);
            return s_Objects[name] = go;
        }
        Make("A", null);
        Make("A1", "A");
        Make("A2", "A");
        Make("A2a", "A2");
        Make("B", null);
        SceneVisibilityManager.visibilityChanged += () => s_Visibility++;
        SceneVisibilityManager.pickingChanged += () => s_Picking++;
        EditorApplication.update += Update;
    }

    static void Update()
    {
        if (s_Step >= 0)
        {
            // The step's events before this update, then everyone's answers.
            Put(s_Steps[s_Step].Name, s_Pending + "|later=" + s_Visibility + "/" + s_Picking + "|" + Answers());
        }
        s_Step++;
        if (s_Step == s_Steps.Length)
        {
            EditorApplication.update -= Update;
            s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
            var directory = Environment.GetEnvironmentVariable("SCENE_VISIBILITY_OUT");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
            EditorApplication.Exit(0);
            return;
        }
        s_Visibility = s_Picking = 0;
        string thrown = "<none>";
        try { s_Steps[s_Step].Run(); }
        catch (Exception e) { thrown = e.GetType().Name; }
        s_Pending = "throws=" + thrown + "|during=" + s_Visibility + "/" + s_Picking;
        s_Visibility = s_Picking = 0;
    }

    static string Bit(bool value) => value ? "1" : "0";

    /// <summary>Each object as IsHidden, IsHidden with descendants, AreAllDescendantsHidden,
    /// AreAllDescendantsVisible, IsPickingDisabled, IsPickingDisabled with descendants,
    /// IsPickingDisabledOnAllDescendants, IsPickingEnabledOnAllDescendants; then the scene's
    /// IsHidden, AreAllDescendantsHidden, AreAnyDescendantsHidden, IsPickingDisabled,
    /// IsPickingDisabledOnAllDescendants, IsPickingDisabledOnAnyDescendant, and isolation.</summary>
    static string Answers()
    {
        var parts = new List<string>();
        foreach (var name in new[] { "A", "A1", "A2", "A2a", "A3", "B" })
        {
            if (!s_Objects.TryGetValue(name, out var go)) continue;
            if (go == null) { parts.Add(name + "=destroyed"); continue; }
            parts.Add(name + "=" + Bit(Manager.IsHidden(go)) + Bit(Manager.IsHidden(go, true)) + Bit(Manager.AreAllDescendantsHidden(go))
                + Bit(Manager.AreAllDescendantsVisible(go)) + Bit(Manager.IsPickingDisabled(go)) + Bit(Manager.IsPickingDisabled(go, true))
                + Bit(Manager.IsPickingDisabledOnAllDescendants(go)) + Bit(Manager.IsPickingEnabledOnAllDescendants(go)));
        }
        var scene = SceneManager.GetActiveScene();
        parts.Add("scene=" + Bit(Manager.IsHidden(scene)) + Bit(Manager.AreAllDescendantsHidden(scene)) + Bit(Manager.AreAnyDescendantsHidden(scene))
            + Bit(Manager.IsPickingDisabled(scene)) + Bit(Manager.IsPickingDisabledOnAllDescendants(scene)) + Bit(Manager.IsPickingDisabledOnAnyDescendant(scene)));
        parts.Add("isolated=" + Bit(Manager.IsCurrentStageIsolated()));
        return string.Join(" ", parts);
    }

    static void Put(string key, string value) =>
        s_Output.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
}
