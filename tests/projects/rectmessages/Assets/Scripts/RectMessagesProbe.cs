using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class RectMessagesProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("RectMessagesProbe").AddComponent<RectMessagesProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    static readonly List<string> messages = new List<string>();
    static string phase;
    StreamWriter output;
    RectTransform grand, parent, child, leaf, other;
    public static void Hit(string name, string message) { if (phase != null) messages.Add(phase+":"+name+":"+message); }
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    static RectTransform Rect(string name, Transform parent = null, bool stretch = false)
    {
        var go = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false); rect.sizeDelta = new Vector2(100, 80);
        if (stretch) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; }
        go.AddComponent<RectMessageWatch>(); return rect;
    }
    IEnumerator Check(string key, Action change)
    {
        messages.Clear(); phase = "call"; change(); Put(key+".immediate", string.Join(",", messages));
        phase = "read"; var ignored = leaf.rect; Put(key+".read", string.Join(",", messages));
        phase = "force"; Canvas.ForceUpdateCanvases(); Put(key+".force", string.Join(",", messages));
        phase = "next"; yield return null; Put(key+".next", string.Join(",", messages)); phase = null;
    }
    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("RECT_MESSAGES_OUT"); if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        grand = Rect("grand"); parent = Rect("parent", grand); child = Rect("child", parent); leaf = Rect("leaf", child, true); other = Rect("other", grand);
        Put("flag.default", parent.sendChildDimensionsChange);
        yield return Check("off.child-size", () => child.sizeDelta += new Vector2(13, 7));
        grand.sendChildDimensionsChange = true;
        yield return Check("grand-only.child-size", () => child.sizeDelta += new Vector2(11, 3));
        yield return Check("flag.enable-parent", () => parent.sendChildDimensionsChange = true);
        yield return Check("parent.child-size", () => child.sizeDelta += new Vector2(9, 5));
        child.sendChildDimensionsChange = true;
        yield return Check("all.child-size", () => child.sizeDelta += new Vector2(7, 2));
        yield return Check("all.child-same", () => child.sizeDelta = child.sizeDelta);
        yield return Check("all.child-twice", () => { child.sizeDelta += Vector2.one; child.sizeDelta += Vector2.one; });
        yield return Check("all.child-position", () => child.anchoredPosition += new Vector2(10, 20));
        yield return Check("all.child-pivot", () => child.pivot = new Vector2(.25f, .75f));
        yield return Check("all.child-anchor", () => child.anchorMax = Vector2.one);
        yield return Check("all.parent-size", () => parent.sizeDelta += new Vector2(8, 6));
        yield return Check("all.leaf-size", () => leaf.sizeDelta += new Vector2(3, 4));
        yield return Check("flag.disable-parent", () => parent.sendChildDimensionsChange = false);
        yield return Check("child-flag.child-size", () => child.sizeDelta += new Vector2(6, 4));
        parent.sendChildDimensionsChange = true;
        parent.GetComponent<RectMessageWatch>().enabled = false;
        yield return Check("disabled-listener.child-size", () => child.sizeDelta += new Vector2(6, 4));
        parent.GetComponent<RectMessageWatch>().enabled = true;
        parent.gameObject.SetActive(false);
        yield return Check("inactive.child-size", () => child.sizeDelta += new Vector2(6, 4));
        parent.gameObject.SetActive(true);
        other.sendChildDimensionsChange = true;
        yield return Check("reparent.child", () => child.SetParent(other, false));
        child.SetParent(parent, false);
        var watch = child.GetComponent<RectMessageWatch>(); watch.destination = other; watch.moveOnRect = true;
        yield return Check("callback.reparent-child", () => child.sizeDelta += new Vector2(5, 3));
        child.SetParent(parent, false);
        var canvas = grand.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var group = parent.gameObject.AddComponent<HorizontalLayoutGroup>(); group.childControlWidth = false; group.childControlHeight = false; group.childForceExpandWidth = false; group.childForceExpandHeight = false;
        var fit = parent.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        Canvas.ForceUpdateCanvases();
        Put("layout.flag", parent.sendChildDimensionsChange);
        yield return Check("layout.child-size", () => child.sizeDelta += new Vector2(17, 9));
        Put("layout.parent-size", parent.rect.size); Put("layout.child-size", child.rect.size);
        yield return Check("layout.off", () => parent.sendChildDimensionsChange = false);
        yield return Check("layout.off.child-size", () => child.sizeDelta += new Vector2(13, 7));
        Put("layout.off.parent-size", parent.rect.size); Put("layout.off.child-size", child.rect.size);
        yield return Check("layout.zero-size", () => child.sizeDelta = Vector2.zero);
        yield return Check("layout.negative-size", () => child.sizeDelta = new Vector2(-10, -20));
        var clone = Instantiate(child.gameObject); Put("flag.clone", clone.GetComponent<RectTransform>().sendChildDimensionsChange); DestroyImmediate(clone);
        group.enabled = false; Put("flag.layout-disabled", parent.sendChildDimensionsChange);
        var fixedParent = Rect("fixed-parent"); var fixedChild = Rect("fixed-child", fixedParent);
        fixedParent.sendChildDimensionsChange = true;
        var oldSize = fixedChild.rect.size;
        yield return Check("fixed.parent-size", () => fixedParent.sizeDelta += new Vector2(25, 13));
        Put("fixed.child-size-unchanged", fixedChild.rect.size == oldSize);
        DestroyImmediate(fixedParent.gameObject);
        foreach (bool hasRect in new[] { true, false })
        {
            string key = hasRect ? "life.rect" : "life.plain";
            messages.Clear(); phase = "create";
            var owner = new GameObject(key); owner.SetActive(false);
            var target = hasRect ? owner.AddComponent<RectTransform>() : null;
            if (target != null) target.sizeDelta = new Vector2(233, 71);
            owner.AddComponent<RectLifecycleWatch>(); Put(key+".created", string.Join(",", messages));
            messages.Clear(); phase = "resize"; if (target != null) target.sizeDelta = new Vector2(244, 80);
            Put(key+".resized", string.Join(",", messages));
            messages.Clear(); phase = "activate"; owner.SetActive(true); Put(key+".active", string.Join(",", messages));
            phase = "next"; yield return null; Put(key+".next", string.Join(",", messages));
            phase = null; DestroyImmediate(owner);
        }
        var matrixParent = Rect("matrix-parent"); var matrixChild = Rect("matrix-child", matrixParent, true);
        var matrixLeaf = Rect("matrix-leaf", matrixChild); matrixLeaf.anchorMin = matrixLeaf.anchorMax = Vector2.one;
        var matrixSibling = Rect("matrix-sibling", matrixParent); matrixSibling.anchorMin = matrixSibling.anchorMax = Vector2.zero;
        var watcher = matrixChild.gameObject.AddComponent<RectWorldWatch>(); watcher.leaf = matrixLeaf; watcher.sibling = matrixSibling;
        var oldLeafWorld = matrixLeaf.localToWorldMatrix; var oldSiblingWorld = matrixSibling.localToWorldMatrix;
        watcher.capture = true;
        yield return Check("matrix.parent-size", () => matrixParent.sizeDelta += new Vector2(50, 20));
        Put("matrix.leaf-world-final", matrixLeaf.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero));
        Put("matrix.sibling-world-final", matrixSibling.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero));
        DestroyImmediate(matrixParent.gameObject);
        foreach (bool write in new[] { false, true })
        {
            string result = "no exception";
            var thread = new System.Threading.Thread(() =>
            {
                try { if (write) parent.sendChildDimensionsChange = false; else { var ignoredFlag = parent.sendChildDimensionsChange; } }
                catch (Exception error) { result = error.GetType().FullName+":"+error.Message; }
            });
            thread.Start(); thread.Join(); Put(write ? "thread.set-flag" : "thread.get-flag", result);
        }
        Put("complete", 1); output.Dispose(); output = null; EditorApplication.Exit(0);
    }
}
