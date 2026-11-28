using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

static class DisplayMenuProbe
{
    [Serializable]
    struct Row
    {
        public string k;
        public string v;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("DISPLAY_CUSTOM_MENU_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var output = new List<string>();

        string Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { return error.GetType().Name; }
            return "ok";
        }

        void Add(string key, string value) => output.Add(JsonUtility.ToJson(new Row { k = key, v = value }));

        var position = new Rect(4, 8, 120, 16);
        var options = new[] { new GUIContent("Alpha"), new GUIContent("Beta"), new GUIContent("Gamma") };
        var mouseButton = typeof(Tools).GetField("s_ButtonDown", BindingFlags.Static | BindingFlags.NonPublic);
        var initialMouseButton = Convert.ToString(mouseButton.GetValue(null));
        int calls = 0;
        EditorUtility.SelectMenuItemFunction select = (_, _, _) => calls++;

        Add("menu.nullArray", Attempt(() => EditorUtility.DisplayCustomMenu(position, null, -1, select, null)));
        Add("menu.nullContent", Attempt(() => EditorUtility.DisplayCustomMenu(position, new GUIContent[] { null }, 0, select, null, true)));
        Add("menu.empty", Attempt(() => EditorUtility.DisplayCustomMenu(position, Array.Empty<GUIContent>(), -1, select, null)));
        Add("menu.valid", Attempt(() => EditorUtility.DisplayCustomMenu(position, options, 1, select, null)));
        Add("menu.hotControl", Attempt(() =>
        {
            GUIUtility.hotControl = 247;
            Add("menu.hotControlBefore", GUIUtility.hotControl.ToString());
            EditorUtility.DisplayCustomMenu(position, options, 1, select, null);
            Add("menu.hotControlAfter", GUIUtility.hotControl.ToString());
        }));
        Add("menu.hotkey", Attempt(() => EditorUtility.DisplayCustomMenu(position, options, 0, select, null, true)));

        var visited = new List<int>();
        bool Enabled(int index)
        {
            visited.Add(index);
            return index != 1;
        }
        Add("menu.enabled", Attempt(() => EditorUtility.DisplayCustomMenu(position, options, Enabled, 2, select, null)));
        Add("menu.enabledOrder", string.Join(",", visited));

        visited.Clear();
        Add("menu.validatesBeforeEnabled", Attempt(() => EditorUtility.DisplayCustomMenu(position,
            new[] { options[0], null, options[1] }, Enabled, 1, select, null)));
        Add("menu.invalidEnabledOrder", string.Join(",", visited));
        Add("menu.noSelectHandler", Attempt(() => EditorUtility.DisplayCustomMenu(position,
            new[] { new GUIContent((string)null) }, 0, null, null)));

        visited.Clear();
        bool Failing(int index)
        {
            visited.Add(index);
            if (index == 1) throw new FormatException();
            return true;
        }
        GUIUtility.hotControl = 419;
        Add("menu.enabledThrows", Attempt(() => EditorUtility.DisplayCustomMenu(position, options, Failing, 2, select, null)));
        Add("menu.stopsOnFailure", string.Join(",", visited));
        Add("menu.failedHotControl", GUIUtility.hotControl.ToString());
        Add("menu.separators", Attempt(() => EditorUtility.DisplayCustomMenuWithSeparators(position,
            new[] { "Alpha", "", "Beta" }, new[] { true, false, true }, new[] { false, true, false },
            new[] { 0, 2 }, select, null)));
        Add("menu.separatorHotControl", GUIUtility.hotControl.ToString());
        Add("menu.selectCalls", calls.ToString());

        Add("menu.initialSceneButton", initialMouseButton);
        mouseButton.SetValue(null, 1);
        Add("menu.sceneButtonSuccessful", Attempt(() => EditorUtility.DisplayCustomMenu(position, options, 1, select, null)));
        Add("menu.sceneButtonAfterSuccess", Convert.ToString(mouseButton.GetValue(null)));
        mouseButton.SetValue(null, 2);
        Add("menu.sceneButtonFailed", Attempt(() => EditorUtility.DisplayCustomMenu(position, options, Failing, 1, select, null)));
        Add("menu.sceneButtonAfterFailure", Convert.ToString(mouseButton.GetValue(null)));
        Add("menu.separatorNullArrays", Attempt(() => EditorUtility.DisplayCustomMenuWithSeparators(position,
            null, null, null, null, null, null)));
        Add("menu.sceneButtonAfterEmpty", Convert.ToString(mouseButton.GetValue(null)));

        output.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(directory, "values.jsonl"), output);
        EditorApplication.Exit(0);
    }
}
