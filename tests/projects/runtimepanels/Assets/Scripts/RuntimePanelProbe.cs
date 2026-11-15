using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// A runtime panel through IRuntimePanel and UIDocument.runtimePanel: the interface on the panel,
// its settings and selectable object, and which panel a document reports after its settings change.
public class RuntimePanelProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("RuntimePanelProbe").AddComponent<RuntimePanelProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("RUNTIME_PANEL_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static PanelSettings Settings(string name)
    {
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.name = name;
        return settings;
    }

    static UIDocument Document(string name, PanelSettings settings, bool active = true)
    {
        var go = new GameObject(name);
        go.SetActive(active);
        var document = go.AddComponent<UIDocument>();
        document.panelSettings = settings;
        return document;
    }

    void Panel(string key, IRuntimePanel panel)
    {
        Put(key + ".null", panel == null);
        if (panel == null) return;
        Put(key + ".settings", panel.panelSettings == null ? "<null>" : panel.panelSettings.name);
        Put(key + ".selectable", panel.selectableGameObject == null ? "<null>" : panel.selectableGameObject.name);
        Put(key + ".contextType", panel.contextType);
        Put(key + ".visualTree", panel.visualTree != null);
        Put(key + ".disposable", panel is IDisposable);
    }

    void Record()
    {
        var settingsA = Settings("SettingsA");
        var settingsB = Settings("SettingsB");
        var first = Document("First", settingsA);
        IPanel panelA = first.rootVisualElement.panel;
        Put("panel.isRuntimePanel", panelA is IRuntimePanel);
        Put("panel.declares", Array.IndexOf(panelA.GetType().GetInterfaces(), typeof(IRuntimePanel)) >= 0);
        Panel("first", first.runtimePanel);
        Put("first.samePanel", ReferenceEquals(first.runtimePanel, panelA));

        // The selectable object is stored on the panel and read back through the interface.
        var runtime = (IRuntimePanel)panelA;
        var selectable = new GameObject("Selectable");
        runtime.selectableGameObject = selectable;
        Panel("selectableSet", runtime);
        var other = new GameObject("Other");
        runtime.selectableGameObject = other;
        Panel("selectableReplaced", first.runtimePanel);
        runtime.selectableGameObject = null;
        Panel("selectableCleared", runtime);

        // Documents on the same settings share one panel.
        var second = Document("Second", settingsA);
        Put("second.samePanel", ReferenceEquals(second.runtimePanel, panelA));

        // A document keeps the panel it first reported after its settings change.
        first.panelSettings = settingsB;
        IPanel panelB = first.rootVisualElement.panel;
        Put("changed.rootPanelIsB", panelB != null && !ReferenceEquals(panelB, panelA));
        Put("changed.reportsA", ReferenceEquals(first.runtimePanel, panelA));
        Panel("changed", first.runtimePanel);
        var third = Document("Third", settingsB);
        Put("third.reportsB", ReferenceEquals(third.runtimePanel, panelB));
        Panel("third", third.runtimePanel);

        // A document whose settings change before it is first asked reports the new panel.
        var fourth = Document("Fourth", settingsA);
        fourth.panelSettings = settingsB;
        Put("fourth.reportsB", ReferenceEquals(fourth.runtimePanel, panelB));

        // A document enabled on its settings keeps that panel after a later change, whether it was
        // activated or re-enabled, before it is asked.
        var fifth = Document("Fifth", settingsA, false);
        fifth.gameObject.SetActive(true);
        fifth.panelSettings = settingsB;
        Put("fifth.reportsA", ReferenceEquals(fifth.runtimePanel, panelA));
        var sixth = Document("Sixth", null);
        sixth.panelSettings = settingsA;
        sixth.enabled = false;
        sixth.enabled = true;
        sixth.panelSettings = settingsB;
        Put("sixth.reportsA", ReferenceEquals(sixth.runtimePanel, panelA));
        Put("sixth.rootPanelIsB", ReferenceEquals(sixth.rootVisualElement.panel, panelB));

        // An inactive document has no panel; once active it reports one.
        var inactive = Document("Inactive", settingsA, false);
        Put("inactive.root", inactive.rootVisualElement == null);
        Panel("inactive", inactive.runtimePanel);
        inactive.gameObject.SetActive(true);
        Put("activated.reportsA", ReferenceEquals(inactive.runtimePanel, panelA));
        Panel("activated", inactive.runtimePanel);

        // A document without settings has no panel.
        var bare = Document("Bare", null);
        Put("bare.root", bare.rootVisualElement == null);
        Panel("bare", bare.runtimePanel);
    }
}
