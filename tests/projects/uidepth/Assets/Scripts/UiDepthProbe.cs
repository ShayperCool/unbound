using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// CanvasRenderer.absoluteDepth, CanvasRenderer.relativeDepth and Graphic.depth over several root
// canvases (overlay at equal and different sorting orders, camera, world, a camera canvas without
// a camera, another display), nested canvases with and without overrideSorting, renderers that
// are culled, transparent, disabled, inactive or without a Graphic, and how the numbers change
// after reparenting, enabling and disabling in Play mode; with the order and depths of
// EventSystem.RaycastAll at the screen's centre for each GraphicRaycaster. Writes values.jsonl
// to UI_DEPTH_OUT.
public class UiDepthProbe : MonoBehaviour
{
    readonly StringBuilder m_Records = new StringBuilder();
    readonly List<CanvasRenderer> m_Renderers = new List<CanvasRenderer>();
    readonly Dictionary<string, Transform> m_Objects = new Dictionary<string, Transform>();
    Camera m_Camera;
    Font m_Font;

    void Value(string key, string value) => m_Records.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    T Get<T>(string name) where T : Component => m_Objects[name].GetComponent<T>();

    RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        // Every node fills its parent, so every graphic covers the screen's centre.
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        m_Objects[name] = rt;
        return rt;
    }

    Image Img(string name, Transform parent)
    {
        var image = Node(name, parent).gameObject.AddComponent<Image>();
        m_Renderers.Add(image.canvasRenderer);
        return image;
    }

    Canvas Root(string name, RenderMode mode, int order)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        m_Objects[name] = go.transform;
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = mode;
        canvas.sortingOrder = order;
        return canvas;
    }

    Canvas Nested(string name, Transform parent, bool overrideSorting, int order, bool raycaster)
    {
        var rt = Node(name, parent);
        var canvas = rt.gameObject.AddComponent<Canvas>();
        if (raycaster) rt.gameObject.AddComponent<GraphicRaycaster>();
        canvas.overrideSorting = overrideSorting;
        canvas.sortingOrder = order;
        return canvas;
    }

    void Build()
    {
        m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        m_Camera = new GameObject("Camera").AddComponent<Camera>();
        m_Camera.transform.position = new Vector3(0, 0, -10);
        m_Camera.cullingMask = ~(1 << 31);
        new GameObject("EventSystem", typeof(EventSystem));

        // Overlay root, order 0: plain renderers, nested canvases between them, renderers that
        // draw nothing.
        var oa = Root("OA", RenderMode.ScreenSpaceOverlay, 0).transform;
        Img("a0", oa);
        var a1 = Img("a1", oa);
        Img("a1c", a1.transform);
        var nestN = Nested("nestN", oa, false, 0, false).transform;
        Img("n0", nestN);
        var n1 = Img("n1", nestN);
        Img("n1c", n1.transform);
        Img("a2", oa);
        var nestO = Nested("nestO", oa, true, 0, true).transform;
        Img("o0", nestO);
        var nestON = Nested("nestON", nestO, false, 0, false).transform;
        Img("on0", nestON);
        Img("o1", nestO);
        Img("a3", oa);
        var nestO5 = Nested("nestO5", oa, true, 5, true).transform;
        Img("p0", nestO5);
        Img("cull", oa).canvasRenderer.cull = true;
        var mask = Node("rectMask", oa);
        mask.gameObject.AddComponent<RectMask2D>();
        var outside = Img("maskedOut", mask);
        var outsideRect = (RectTransform)outside.transform;
        outsideRect.anchorMin = outsideRect.anchorMax = Vector2.zero;
        outsideRect.sizeDelta = new Vector2(10, 10);
        outsideRect.anchoredPosition = new Vector2(-1000, -1000);
        var alpha0 = Img("colorAlpha0", oa);
        alpha0.color = new Color(1, 1, 1, 0);
        alpha0.canvasRenderer.cullTransparentMesh = false;
        var alpha0Cull = Img("colorAlpha0Cull", oa);
        alpha0Cull.color = new Color(1, 1, 1, 0);
        alpha0Cull.canvasRenderer.cullTransparentMesh = true;
        Img("setAlpha0", oa);
        var group = Node("group0", oa);
        group.gameObject.AddComponent<CanvasGroup>().alpha = 0;
        Img("g0", group);
        Img("graphicOff", oa).enabled = false;
        Img("inactive", oa).gameObject.SetActive(false);
        var bare = Node("bare", oa);
        m_Renderers.Add(bare.gameObject.AddComponent<CanvasRenderer>());
        var text = Node("emptyText", oa).gameObject.AddComponent<Text>();
        text.font = m_Font;
        text.text = "";
        m_Renderers.Add(text.canvasRenderer);
        var nestD = Nested("nestD", oa, false, 0, false);
        Img("d0", nestD.transform);
        nestD.enabled = false;
        var nestDO = Nested("nestDO", oa, true, 2, true);
        Img("do0", nestDO.transform);
        nestDO.enabled = false;
        Img("a4", oa);

        // Further overlay roots: the same order, made after OA; a lower order; another display;
        // a disabled root.
        var ob = Root("OB", RenderMode.ScreenSpaceOverlay, 0).transform;
        Img("b0", ob);
        Img("b1", ob);
        var oc = Root("OC", RenderMode.ScreenSpaceOverlay, -3).transform;
        Img("c0", oc);
        var oe = Root("OE", RenderMode.ScreenSpaceOverlay, 1);
        oe.targetDisplay = 1;
        Img("e0", oe.transform);
        var oq = Root("OQ", RenderMode.ScreenSpaceOverlay, 0);
        Img("q0", oq.transform);
        oq.enabled = false;

        // Camera and world roots.
        var cc = Root("CC", RenderMode.ScreenSpaceCamera, 0);
        cc.worldCamera = m_Camera;
        cc.planeDistance = 5;
        Img("k0", cc.transform);
        Img("k1", cc.transform);
        var cz = Root("CZ", RenderMode.ScreenSpaceCamera, 0);
        Img("z0", cz.transform);
        var ww = World("WW", new Vector3(0, 0, 0), 0);
        Img("w0", ww.transform);
        Img("w1", ww.transform);
        var wx = World("WX", new Vector3(0, 0, 1), 31);
        Img("x0", wx.transform).gameObject.layer = 31;
        var wy = World("WY", new Vector3(0, 0, -20), 0);
        Img("y0", wy.transform);
        var wn = World("WN", new Vector3(0, 0, 2), 0);
        wn.worldCamera = null;
        Img("v0", wn.transform);

        // Renderers on canvas objects themselves, an empty nested canvas, an overriding canvas
        // under a disabled one, an inactive nested canvas.
        var os = Root("OS", RenderMode.ScreenSpaceOverlay, 0);
        m_Renderers.Add(os.gameObject.AddComponent<Image>().canvasRenderer);
        Img("s0", os.transform);
        Nested("nestE", os.transform, false, 0, false);
        Img("s1", os.transform);
        NestedImg("nestI", os.transform, false, 0);
        Img("ni0", m_Objects["nestI"]);
        Img("s2", os.transform);
        NestedImg("nestOI", os.transform, true, 0);
        Img("oi0", m_Objects["nestOI"]);
        Img("s3", os.transform);
        var nestX = Nested("nestX", os.transform, false, 0, false);
        Nested("nestXO", nestX.transform, true, 0, true);
        Img("xo0", m_Objects["nestXO"]);
        nestX.enabled = false;
        Img("s4", os.transform);
        Nested("nestIn", os.transform, false, 0, false);
        Img("in0", m_Objects["nestIn"]);
        m_Objects["nestIn"].gameObject.SetActive(false);
        Img("s5", os.transform);
        var wrap = Node("wrap", os.transform);
        Img("wr0", wrap);
        Img("wr1", wrap);
        Img("s6", os.transform);
        // Nested canvases holding only an overriding canvas, or renderers only one canvas deeper.
        var nestG = Nested("nestG", os.transform, false, 0, false);
        Nested("nestGO", nestG.transform, true, 0, true);
        Img("go0", m_Objects["nestGO"]);
        Img("s7", os.transform);
        var nestH = Nested("nestH", os.transform, false, 0, false);
        Nested("nestHN", nestH.transform, false, 0, false);
        Img("hn0", m_Objects["nestHN"]);
        Img("s8", os.transform);
        // Nested canvases whose only child holds no renderer, is a disabled canvas, an inactive
        // renderer or a disabled overriding canvas.
        Node("emptyChild", Nested("nestK", os.transform, false, 0, false).transform);
        Img("s9", os.transform);
        var nestLD = Nested("nestLD", Nested("nestL", os.transform, false, 0, false).transform, false, 0, false);
        Img("ld0", nestLD.transform);
        nestLD.enabled = false;
        Img("s10", os.transform);
        Img("mi0", Nested("nestM", os.transform, false, 0, false).transform).gameObject.SetActive(false);
        Img("s11", os.transform);
        var nestPO = Nested("nestPO", Nested("nestP", os.transform, false, 0, false).transform, true, 0, true);
        Img("po0", nestPO.transform);
        nestPO.enabled = false;
        Img("s12", os.transform);
        // Nested canvases whose only child is an empty enabled canvas, not overriding or overriding.
        Nested("nestRE", Nested("nestR", os.transform, false, 0, false).transform, false, 0, false);
        Img("s13", os.transform);
        Nested("nestSO", Nested("nestS", os.transform, false, 0, false).transform, true, 0, true);
        Img("s14", os.transform);

        // An overriding canvas under a disabled root.
        var ox = Root("OX", RenderMode.ScreenSpaceOverlay, 0);
        Img("x1", ox.transform);
        Nested("nestOX", ox.transform, true, 0, true);
        Img("xr0", m_Objects["nestOX"]);
        Nested("nestNX", ox.transform, false, 0, false);
        Img("nx0", m_Objects["nestNX"]);
        ox.enabled = false;
        Nested("nestXN", m_Objects["nestX"], false, 0, false);
        Img("xn0", m_Objects["nestXN"]);

        // Canvas objects deactivated in Play: a root's and a nested one's.
        var oy = Root("OY", RenderMode.ScreenSpaceOverlay, 0);
        Img("oy0", oy.transform);
        var oz = Root("OZ", RenderMode.ScreenSpaceOverlay, 0);
        Img("oz0", oz.transform);
        Nested("nestZN", oz.transform, false, 0, false);
        Img("zn0", m_Objects["nestZN"]);
        Img("oz1", oz.transform);

        // A root and an overriding canvas at the same sorting order, each with its raycaster:
        // RaycastAll orders their hits by depth, so the overriding canvas's hit at depth 0 comes
        // after the root's at 1 and 2 (rt0 is no raycast target).
        m_EventRoot = Root("OR", RenderMode.ScreenSpaceOverlay, 0).GetComponent<GraphicRaycaster>();
        Img("rt0", m_EventRoot.transform).raycastTarget = false;
        Img("rt1", m_EventRoot.transform);
        Nested("nestRT", m_EventRoot.transform, true, 0, true);
        Img("rq0", m_Objects["nestRT"]);
        Img("rt2", m_EventRoot.transform);
    }

    void NestedImg(string name, Transform parent, bool overrideSorting, int order)
    {
        var image = Img(name, parent);
        var canvas = image.gameObject.AddComponent<Canvas>();
        image.gameObject.AddComponent<GraphicRaycaster>();
        canvas.overrideSorting = overrideSorting;
        canvas.sortingOrder = order;
    }

    Canvas World(string name, Vector3 position, int layer)
    {
        var canvas = Root(name, RenderMode.WorldSpace, 0);
        canvas.worldCamera = m_Camera;
        canvas.gameObject.layer = layer;
        var rt = (RectTransform)canvas.transform;
        rt.position = position;
        rt.sizeDelta = new Vector2(100, 100);
        rt.localScale = Vector3.one * 0.1f;
        return canvas;
    }

    void Record(string phase)
    {
        foreach (var renderer in m_Renderers)
        {
            var graphic = renderer.GetComponent<Graphic>();
            Value(phase + "." + renderer.name, "[" + renderer.absoluteDepth + "," + renderer.relativeDepth + "," +
                (graphic != null ? graphic.depth.ToString() : "null") + "]");
        }
    }

    readonly List<RaycastResult> m_Hits = new List<RaycastResult>();
    GraphicRaycaster m_EventRoot;

    readonly SortedDictionary<string, StringBuilder> m_Groups = new SortedDictionary<string, StringBuilder>(StringComparer.Ordinal);

    // RaycastAll at the screen's centre, as one list per GraphicRaycaster in RaycastAll's order:
    // a raycaster's own hits sort by depth. The order between raycasters (their canvases'
    // sortingOrder and renderOrder) is not this probe's question and is not recorded, except
    // under one root raycaster (raycastRoot.OR), where the depths decide it.
    void Raycasts(string phase)
    {
        var data = new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width / 2f, Screen.height / 2f) };
        m_Hits.Clear();
        EventSystem.current.RaycastAll(data, m_Hits);
        m_Groups.Clear();
        foreach (var hit in m_Hits)
        {
            var root = hit.module.gameObject.name;
            if (!m_Groups.TryGetValue(root, out var list)) m_Groups[root] = list = new StringBuilder();
            list.Append(list.Length == 0 ? "" : ",").Append('"').Append(hit.gameObject.name).Append(':').Append(hit.depth).Append('"');
        }
        foreach (var group in m_Groups)
            Value(phase + ".raycast." + group.Key, "[" + group.Value + "]");
        // The hits under one root raycaster, across its raycasters, where depth decides the order.
        var under = new StringBuilder();
        foreach (var hit in m_Hits)
            if (hit.module.rootRaycaster == m_EventRoot)
                under.Append(under.Length == 0 ? "" : ",").Append('"').Append(hit.gameObject.name).Append(':').Append(hit.depth).Append('"');
        Value(phase + ".raycastRoot.OR", "[" + under + "]");
    }

    IEnumerator Start()
    {
        yield return null;
        var directory = Environment.GetEnvironmentVariable("UI_DEPTH_OUT") ?? "/tmp/ui-depth";
        Directory.CreateDirectory(directory);
        Build();
        Record("made");
        // A renderer's alpha set after its Graphic has built its mesh.
        yield return null;
        Get<CanvasRenderer>("setAlpha0").SetAlpha(0);
        yield return null;
        yield return null;
        Record("initial");
        Raycasts("initial");

        // Reparenting and sibling order.
        m_Objects["a0"].SetAsLastSibling();
        m_Objects["b1"].SetParent(m_Objects["a1"], false);
        m_Objects["c0"].SetParent(m_Objects["nestN"], false);
        m_Objects["o0"].SetParent(m_Objects["OA"], false);
        m_Objects["o0"].SetSiblingIndex(0);
        Record("reparent.now");
        yield return null;
        Record("reparent.next");
        yield return null;
        Record("reparent");
        Raycasts("reparent");

        // Disabling and enabling.
        m_Objects["a2"].gameObject.SetActive(false);
        Get<Canvas>("nestN").enabled = false;
        Get<Image>("graphicOff").enabled = true;
        m_Objects["inactive"].gameObject.SetActive(true);
        Get<Canvas>("OB").enabled = false;
        Get<Canvas>("nestD").enabled = true;
        Get<Canvas>("nestDO").enabled = true;
        Get<Image>("a3").enabled = false;
        Get<CanvasRenderer>("cull").cull = false;
        m_Objects["OY"].gameObject.SetActive(false);
        m_Objects["nestZN"].gameObject.SetActive(false);
        Record("toggle.now");
        yield return null;
        yield return null;
        Record("toggle");
        Raycasts("toggle");

        // Sorting: a nested canvas starts and stops overriding; a root's order changes.
        Get<Canvas>("nestN").enabled = true;
        Get<Canvas>("nestN").overrideSorting = true;
        Get<Canvas>("nestO").overrideSorting = false;
        Get<Canvas>("OC").sortingOrder = 10;
        Get<Canvas>("CC").renderMode = RenderMode.ScreenSpaceOverlay;
        Record("sorting.now");
        yield return null;
        yield return null;
        Record("sorting");
        Raycasts("sorting");

        // Back on, and a root made last.
        Get<Canvas>("OB").enabled = true;
        m_Objects["a2"].gameObject.SetActive(true);
        Get<Image>("a3").enabled = true;
        var late = Root("OL", RenderMode.ScreenSpaceOverlay, 0).transform;
        Img("l0", late);
        Img("l1", late);
        Record("late.now");
        yield return null;
        yield return null;
        Record("late");
        Raycasts("late");

        // A nested canvas made a root, and a root made nested.
        m_Objects["nestO5"].SetParent(null, false);
        m_Objects["OL"].SetParent(m_Objects["k0"], false);
        yield return null;
        yield return null;
        Record("roots");
        Raycasts("roots");

        // A canvas added above renderers and one removed from under them.
        m_Objects["wrap"].gameObject.AddComponent<Canvas>();
        DestroyImmediate(Get<GraphicRaycaster>("nestI"));
        DestroyImmediate(Get<Canvas>("nestI"));
        Record("canvases.now");
        yield return null;
        yield return null;
        Record("canvases");
        Raycasts("canvases");

        // Canvas.ForceUpdateCanvases after a change.
        m_Objects["a0"].SetSiblingIndex(0);
        Img("f0", m_Objects["OS"]);
        Canvas.ForceUpdateCanvases();
        Record("forced.now");
        yield return null;
        yield return null;
        Record("forced");

        Value("complete", "true");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), m_Records.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
