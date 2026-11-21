using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
#endif

public class ToolbarControlsProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("ToolbarControlsProbe");
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        go.AddComponent<ToolbarControlsProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("TOOLBAR_CONTROLS_OUT");
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Put("error", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }
        m_Logs.Sort(StringComparer.Ordinal);
        m_Out.Append("{\"k\":\"logs\",\"v\":[").Append(string.Join(",", m_Logs.Select(Json))).Append("]}\n");
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    static Type RuntimeUtil => typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

    static void Frames(int count = 3)
    {
        var update = RuntimeUtil.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var repaint = RuntimeUtil.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < count; i++)
        {
            update.Invoke(null, null);
            repaint.Invoke(null, new object[] { false });
            System.Threading.Thread.Sleep(20);
        }
    }

    void Put(string key, string value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");

    static string Json(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string B(bool v) => v ? "1" : "0";

    static string Classes(VisualElement e) => string.Join(" ", e.GetClasses());

    static string Sheets(VisualElement e)
    {
        var names = new List<string>();
        for (int i = 0; i < e.styleSheets.count; i++) names.Add(e.styleSheets[i] == null ? "null" : e.styleSheets[i].name);
        return "[" + string.Join(",", names) + "]";
    }

    // An element and its hierarchy children: type, name, classes and, for text elements, text.
    static string Tree(VisualElement e)
    {
        var sb = new StringBuilder();
        sb.Append(e.GetType().Name).Append('#').Append(e.name).Append('{').Append(Classes(e)).Append('}');
        if (e is TextElement t && !(e is TextField)) sb.Append("'").Append(t.text).Append("'");
        if (e.hierarchy.childCount > 0)
        {
            sb.Append('(');
            for (int i = 0; i < e.hierarchy.childCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Tree(e.hierarchy[i]));
            }
            sb.Append(')');
        }
        return sb.ToString();
    }

    static string Focus(VisualElement e) => $"focusable={B(e.focusable)}|tabIndex={e.tabIndex}|delegates={B(e.delegatesFocus)}";

    static void Submit(VisualElement target)
    {
        using var evt = NavigationSubmitEvent.GetPooled();
        evt.target = target;
        target.SendEvent(evt);
    }

#if UNITY_EDITOR
    void Run()
    {
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        m_Root.style.width = 640f;
        m_Root.style.height = 480f;
        Application.logMessageReceived += (msg, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{msg}");
        };

        var fontProperty = typeof(Editor).Assembly.GetType("UnityEditor.Experimental.EditorResources")
            ?.GetProperty("currentFontName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Put("skin", $"pro={B(EditorGUIUtility.isProSkin)}|font={fontProperty?.GetValue(null)}");

        // 1. Each control as constructed.
        var toolbar = new Toolbar();
        var button = new ToolbarButton { text = "Run" };
        var toggle = new ToolbarToggle { text = "Grid" };
        var menu = new ToolbarMenu();
        var spacer = new ToolbarSpacer();
        var search = new ToolbarSearchField();
        var popupSearch = new ToolbarPopupSearchField();
        var crumbs = new ToolbarBreadcrumbs();
        m_Root.Add(toolbar);
        foreach (var e in new VisualElement[] { button, toggle, menu, spacer, search, popupSearch, crumbs }) toolbar.Add(e);
        Frames();
        foreach (var e in new VisualElement[] { toolbar, button, toggle, menu, spacer, search, popupSearch, crumbs })
            Put("new." + e.GetType().Name, $"{Tree(e)}|sheets={Sheets(e)}|{Focus(e)}");

        // 2. ToolbarButton: a click through submit.
        int clicks = 0;
        button.clicked += () => clicks++;
        Submit(button);
        var clickable = new ToolbarButton(() => clicks += 10);
        toolbar.Add(clickable);
        Submit(clickable);
        Put("button.clicks", clicks.ToString());

        // 3. ToolbarToggle: value and change events.
        var toggleEvents = new List<string>();
        toggle.RegisterValueChangedCallback(evt => toggleEvents.Add($"{B(evt.previousValue)}>{B(evt.newValue)}"));
        toggle.value = true;
        Submit(toggle);
        Put("toggle", $"value={B(toggle.value)}|events={string.Join(",", toggleEvents)}");

        // 4. ToolbarMenu: text, variant and menu items.
        menu.text = "File";
        menu.variant = ToolbarMenu.Variant.Popup;
        var afterPopup = Classes(menu);
        menu.variant = ToolbarMenu.Variant.Default;
        menu.menu.AppendAction("Open", _ => { });
        menu.menu.AppendAction("Save", _ => { }, DropdownMenuAction.AlwaysDisabled);
        menu.menu.AppendSeparator();
        Put("menu", $"{Tree(menu)}|text={menu.text}|popup={afterPopup}|default={Classes(menu)}|items={menu.menu.MenuItems().Count}");

        // 5. ToolbarSpacer: flex.
        spacer.flex = true;
        var flexOn = Classes(spacer);
        spacer.flex = false;
        Put("spacer", $"flexOn={flexOn}|flexOff={Classes(spacer)}|flex={B(spacer.flex)}");

        // 6. ToolbarSearchField: value, placeholder, cancel button, Escape, change events.
        var searchEvents = new List<string>();
        search.RegisterValueChangedCallback(evt => searchEvents.Add($"{evt.previousValue}>{evt.newValue}"));
        var cancel = search.Q<Button>("unity-cancel");
        var steps = new List<string> { $"empty:cancel={Classes(cancel)}" };
        search.value = "abc";
        steps.Add($"abc:cancel={Classes(cancel)}|value={search.value}");
        search.placeholderText = "Find";
        steps.Add($"placeholder={search.placeholderText}");
        using (var click = NavigationSubmitEvent.GetPooled())
        {
            click.target = cancel;
            cancel.SendEvent(click);
        }
        steps.Add($"cancelClick:value='{search.value}'|cancel={Classes(cancel)}");
        search.SetValueWithoutNotify("quiet");
        steps.Add($"quiet:cancel={Classes(cancel)}|value={search.value}");
        var input = search.Q(TextInputBaseField<string>.textInputUssName);
        using (var escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
        {
            escape.target = input;
            input.SendEvent(escape);
        }
        steps.Add($"escape:value='{search.value}'|cancel={Classes(cancel)}");
        steps.Add("events=" + string.Join(",", searchEvents));
        Put("search", string.Join("###", steps));

        // 7. ToolbarPopupSearchField: its menu.
        popupSearch.menu.AppendAction("Name", _ => { });
        Put("popupSearch", $"items={popupSearch.menu.MenuItems().Count}|{Classes(popupSearch)}");

        // 8. ToolbarBreadcrumbs: push, pop, and a pop of something else.
        int crumbClicks = 0;
        crumbs.PushItem("Root", () => crumbClicks++);
        crumbs.PushItem("Child");
        var pushed = Tree(crumbs);
        Submit(crumbs[0]);
        crumbs.PopItem();
        var popped = Tree(crumbs);
        crumbs.Add(new Label("other"));
        string popOther;
        try
        {
            crumbs.PopItem();
            popOther = "no exception";
        }
        catch (Exception e)
        {
            popOther = e.GetType().Name + ":" + e.Message;
        }
        Put("crumbs", $"pushed={pushed}###popped={popped}###clicks={crumbClicks}###popOther={popOther}");

        // 9. The same controls from UXML.
        var asset = Resources.Load<VisualTreeAsset>("ToolbarControls");
        var clone = asset.Instantiate();
        m_Root.Add(clone);
        Frames();
        var bar = clone.Q<Toolbar>("bar");
        var parts = new List<string> { Tree(bar) };
        var uxmlToggle = clone.Q<ToolbarToggle>("tgl");
        var uxmlMenu = clone.Q<ToolbarMenu>("menu");
        var uxmlSpacer = clone.Q<ToolbarSpacer>("spacer");
        var uxmlSearch = clone.Q<ToolbarSearchField>("search");
        parts.Add($"toggle={B(uxmlToggle.value)}|menu={uxmlMenu.text}/{uxmlMenu.variant}|flex={B(uxmlSpacer.flex)}|search={uxmlSearch.value}/{uxmlSearch.placeholderText}");
        Put("uxml", string.Join("###", parts));
    }
#else
    void Run() { }
#endif
}
