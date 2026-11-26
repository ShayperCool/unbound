using UnityEngine;

/// <summary>An OnGUI behaviour of the guidepth probe: logs the depth its OnGUI reads first and
/// last, per frame and event, and sets GUI.depth on given frames (A: -3 in every call of frames 3
/// to 5, B: 5 in its Repaint of frame 7 only, B: -3 in every call of frames 9 to 11).</summary>
public class GuiDepthBehaviour : MonoBehaviour
{
    void Update() => GuiDepthProbe.Updates.Append(name);

    void OnGUI()
    {
        var e = Event.current.type;
        if (e != EventType.Layout && e != EventType.Repaint) return;
        int frame = GuiDepthProbe.Frame;
        string key = "f" + frame + "." + name + "." + (e == EventType.Layout ? "layout" : "repaint");
        GuiDepthProbe.Add(key + ".entry", GUI.depth);
        if (name == "A" && frame >= 3 && frame <= 5) GUI.depth = -3;
        if (name == "B" && frame == 7 && e == EventType.Repaint) GUI.depth = 5;
        if (name == "B" && frame >= 9 && frame <= 11) GUI.depth = -3;
        GuiDepthProbe.Add(key + ".exit", GUI.depth);
        if (e == EventType.Layout)
        {
            GuiDepthProbe.Layouts.Append(name);
            GuiDepthProbe.LayoutDepth[name == "A" ? 0 : 1] = GUI.depth;
        }
        else GuiDepthProbe.Repaints.Append(name);
    }
}
