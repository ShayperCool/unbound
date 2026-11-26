using UnityEngine;

/// <summary>An OnGUI behaviour of the guidepth probe: logs the depth its OnGUI reads first, per
/// frame and event, and sets GUI.depth on given frames (A: -3 in every event of frames 3 to 5,
/// B: 5 in its Repaint of frame 7 only, B: -3 in every event of frames 9 to 11).</summary>
public class GuiDepthBehaviour : MonoBehaviour
{
    void OnGUI()
    {
        var e = Event.current.type;
        if (e != EventType.Layout && e != EventType.Repaint) return;
        int frame = GuiDepthProbe.Frame;
        string key = "f" + frame + "." + name + "." + e;
        GuiDepthProbe.Add(key + ".entry", GUI.depth);
        GuiDepthProbe.Order.Append(name).Append(e == EventType.Layout ? "L" : "R").Append(' ');
        if (name == "A" && frame >= 3 && frame <= 5) GUI.depth = -3;
        if (name == "B" && frame == 7 && e == EventType.Repaint) GUI.depth = 5;
        if (name == "B" && frame >= 9 && frame <= 11) GUI.depth = -3;
        GuiDepthProbe.Add(key + ".exit", GUI.depth);
    }
}
