using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

static class PropertyEditorOpenProbe
{
    [Serializable]
    struct Row
    {
        public string k;
        public string v;
    }

    static EditorWindow[] Inspectors() => Resources.FindObjectsOfTypeAll<EditorWindow>()
        .Where(window => window.GetType().FullName == "UnityEditor.PropertyEditor").ToArray();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("PROPERTY_EDITOR_OPEN_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var output = new List<string>();
        var existing = Inspectors();
        var first = new GameObject("First target");
        var second = new GameObject("Second target");
        var destroyed = new GameObject("Destroyed target");
        Object.DestroyImmediate(destroyed);

        void Capture(string key, Func<string> read)
        {
            string value;
            try { value = read(); }
            catch (Exception error) { value = "EX:" + error.GetType().Name; }
            output.Add(JsonUtility.ToJson(new Row { k = key, v = value }));
        }

        try
        {
            EditorWindow[] Added() => Inspectors().Where(window => !existing.Contains(window)).ToArray();
            Capture("open.null", () =>
            {
                EditorUtility.OpenPropertyEditor(null);
                return Added().Length.ToString();
            });
            Capture("open.destroyed", () =>
            {
                EditorUtility.OpenPropertyEditor(destroyed);
                return Added().Length.ToString();
            });
            Capture("open.first", () =>
            {
                EditorUtility.OpenPropertyEditor(first);
                return string.Join(",", Added().Select(window => window.titleContent.text));
            });
            Capture("open.second", () =>
            {
                EditorUtility.OpenPropertyEditor(second);
                return string.Join(",", Added().Select(window => window.titleContent.text).OrderBy(title => title, StringComparer.Ordinal));
            });
            Capture("open.tooltips", () => string.Join("|", Added()
                .OrderBy(window => window.titleContent.text, StringComparer.Ordinal)
                .Select(window => window.titleContent.tooltip)));
            Capture("open.relativePosition", () =>
            {
                var windows = Added().OrderBy(window => window.titleContent.text, StringComparer.Ordinal).ToArray();
                return (windows[1].position.position - windows[0].position.position).ToString();
            });
            Capture("open.targets", () =>
            {
                return string.Join(",", Added().Select(window => (Object)window.GetType()
                    .GetField("m_InspectedObject", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window))
                    .Select(target => target == first ? "first" : target == second ? "second" : "other")
                    .OrderBy(value => value, StringComparer.Ordinal));
            });
            var priorSelection = Selection.activeObject;
            var child = new GameObject("Nested target");
            child.transform.SetParent(first.transform, false);
            var component = child.transform;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Fixture.txt");
            try
            {
                Selection.activeObject = first;
                Capture("open.component", () => DescribeNew(component));
                Capture("open.asset", () => DescribeNew(asset));
                Capture("open.keepsSelection", () => (Selection.activeObject == first).ToString());
                Capture("open.duplicate", () => DescribeNew(first));
                var custom = child.AddComponent<FocusedInspectorFixture>();
                var before = Added();
                var enablesBefore = FocusedInspectorProbeEditor.Enabled;
                var disablesBefore = FocusedInspectorProbeEditor.Disabled;
                Capture("open.customTarget", () => DescribeNew(custom));
                Capture("open.customEnableOnReturn", () => (FocusedInspectorProbeEditor.Enabled - enablesBefore).ToString());
                Added().SingleOrDefault(window => !before.Contains(window))?.Close();
                Capture("open.customDisableOnClose", () => (FocusedInspectorProbeEditor.Disabled - disablesBefore).ToString());

                Capture("open.tracker", () =>
                {
                    var secondWindow = Added().Single(window => window.titleContent.text == "Second target");
                    var tracker = (ActiveEditorTracker)secondWindow.GetType().GetProperty("tracker").GetValue(secondWindow);
                    var locked = new List<Object>();
                    typeof(ActiveEditorTracker).GetMethod("GetObjectsLockedByThisTracker", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(tracker, new object[] { locked });
                    return $"locked={tracker.isLocked}|targets={locked.Count}|target={locked.SingleOrDefault() == second}|editors={tracker.activeEditors.Length}";
                });
                Capture("open.trackerCollectionAndUnlock", () =>
                {
                    var secondWindow = Added().Single(window => window.titleContent.text == "Second target");
                    var tracker = (ActiveEditorTracker)secondWindow.GetType().GetProperty("tracker").GetValue(secondWindow);
                    var getter = typeof(ActiveEditorTracker).GetMethod("GetObjectsLockedByThisTracker", BindingFlags.Instance | BindingFlags.NonPublic);
                    var locked = new List<Object> { first };
                    getter.Invoke(tracker, new object[] { locked });
                    var before = string.Join(",", locked.Select(obj => obj.name));
                    tracker.isLocked = false;
                    getter.Invoke(tracker, new object[] { locked });
                    var unlocked = $"locked={tracker.isLocked}|targets={string.Join(",", locked.Select(obj => obj.name))}";
                    tracker.isLocked = true;
                    getter.Invoke(tracker, new object[] { locked });
                    return $"previousList={before}|{unlocked}|restored={tracker.isLocked}|restoredTargets={string.Join(",", locked.Select(obj => obj.name))}";
                });

                var focusBefore = EditorWindow.focusedWindow;
                int focusEvents = 0;
                bool focusKeepsPrevious = true;
                Action onFocus = () =>
                {
                    focusEvents++;
                    focusKeepsPrevious &= EditorWindow.focusedWindow == focusBefore;
                };
                EditorWindow.windowFocusChanged += onFocus;
                try
                {
                    Capture("open.focus", () =>
                    {
                        var beforeFocus = Added();
                        EditorUtility.OpenPropertyEditor(second);
                        var created = Added().SingleOrDefault(window => !beforeFocus.Contains(window));
                        return $"created={created != null}|newHasFocus={created != null && EditorWindow.focusedWindow == created}|keepsFocus={EditorWindow.focusedWindow == focusBefore}|events={focusEvents}";
                    });
                    Capture("open.focusEventAndPanel", () =>
                    {
                        var opened = Added().Where(window => window.titleContent.text == "Second target").ToArray();
                        return $"keepsFocusDuringEvent={focusKeepsPrevious}|panels={opened.Count(window => window.rootVisualElement.panel != null)}";
                    });
                    Capture("open.closeFocus", () =>
                    {
                        var window = Added().First(candidate => candidate.titleContent.text == "Second target");
                        int beforeClose = focusEvents;
                        window.Close();
                        return $"closed={window == null}|focusKept={EditorWindow.focusedWindow == focusBefore}|events={focusEvents - beforeClose}";
                    });

                }
                finally
                {
                    EditorWindow.windowFocusChanged -= onFocus;
                }
                Action badFocus = () => throw new FormatException("focused window notification");
                EditorWindow.windowFocusChanged += badFocus;
                try
                {
                    Capture("open.focusThrowingSubscriber", () =>
                    {
                        int countBefore = Added().Length;
                        try
                        {
                            EditorUtility.OpenPropertyEditor(first);
                            return $"returned|newWindows={Added().Length - countBefore}";
                        }
                        catch (FormatException)
                        {
                            return $"EX:FormatException|newWindows={Added().Length - countBefore}";
                        }
                    });
                }
                finally
                {
                    EditorWindow.windowFocusChanged -= badFocus;
                }
            }
            finally
            {
                Selection.activeObject = priorSelection;
            }

            string DescribeNew(Object target)
            {
                var before = Added();
                EditorUtility.OpenPropertyEditor(target);
                var window = Added().SingleOrDefault(candidate => !before.Contains(candidate));
                if (window == null) return "missing";
                var field = window.GetType().GetField("m_InspectedObject", BindingFlags.Instance | BindingFlags.NonPublic);
                return window.titleContent.text + "|" + window.titleContent.tooltip + "|target=" + ((Object)field.GetValue(window) == target);
            }

        }
        finally
        {
            foreach (var window in Inspectors().Where(window => !existing.Contains(window))) window.Close();
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }

        output.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(directory, "values.jsonl"), output);
        EditorApplication.Exit(0);
    }
}

[CustomEditor(typeof(FocusedInspectorFixture))]
public sealed class FocusedInspectorProbeEditor : Editor
{
    internal static int Enabled;
    internal static int Disabled;

    void OnEnable() => Enabled++;
    void OnDisable() => Disabled++;
}
